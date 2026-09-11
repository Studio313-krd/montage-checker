using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Agent.Screenshots;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent;

internal sealed class AgentRuntime(
    AgentSettings settings,
    AgentSettingsStore settingsStore,
    IActivityMonitor activityMonitor,
    IIdleMonitor idleMonitor,
    ISystemMetricsProvider metricsProvider,
    IRenderDetector renderDetector,
    IProxyDetector proxyDetector,
    IScreenshotCapture screenshotCapture,
    ILocalEventQueue eventQueue,
    ILocalScreenshotQueue screenshotQueue,
    IAgentApiClient apiClient) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _worker;
    private DateTimeOffset? _lastSyncAtUtc;
    private RenderDetectionResult _renderDetection = new(MachineState.Normal, null);
    private ProxyDetectionResult _proxyDetection = new(MachineState.Normal, null);
    private MachineWorkDetection _machineDetection = MachineWorkDetection.Normal;
    private MachineState? _lastQueuedMachineState;
    private bool _operatorSelectionRequired;
    private AgentConnectionState _connectionState = AgentConnectionState.Starting;
    private string? _connectionDetails;

    public event EventHandler<AgentRuntimeStatus>? StatusChanged;
    public event EventHandler<Exception>? FatalError;

    public void Start()
    {
        if (_worker is null)
        {
            // The runtime must never inherit the WinForms synchronization context. Otherwise
            // synchronously tearing down the application can wait for a continuation that is
            // itself waiting for the UI thread, leaving the tray process frozen.
            _worker = Task.Run(() => RunAndReportFailureAsync(_cancellation.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        apiClient.Dispose();
        idleMonitor.Dispose();
        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await eventQueue.InitializeAsync(cancellationToken);
        await screenshotQueue.InitializeAsync(cancellationToken);
        var nextHeartbeat = DateTimeOffset.MinValue;
        var nextScreenshot = DateTimeOffset.MinValue;
        var nextConfigurationRefresh = DateTimeOffset.MinValue;
        Publish(AgentConnectionState.Starting, null, await CountQueuedAsync(cancellationToken));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = settings.Clock.GetUtcNow();
            if (!settings.HasValidOperatorSession(now))
            {
                Publish(
                    AgentConnectionState.OperatorSelectionRequired,
                    _lastSyncAtUtc,
                    await CountQueuedAsync(cancellationToken));
                return;
            }

            if (now >= nextConfigurationRefresh)
            {
                var configuration = await apiClient.GetConfigurationAsync(
                    settings.OperatorSessionId,
                    cancellationToken);
                if (configuration is not null)
                {
                    // Сохраняем весь backward-compatible snapshot: после обновления EXE в нём могут
                    // появиться новые секции даже при неизменившемся числовом ConfigVersion.
                    settingsStore.SaveRemoteConfiguration(settings, configuration);
                }

                nextConfigurationRefresh = now.AddMinutes(5);
            }

            _renderDetection = await renderDetector.EvaluateAsync(
                now,
                settings.RenderRules,
                settings.RenderFolders,
                cancellationToken);
            _proxyDetection = await proxyDetector.EvaluateAsync(
                now,
                settings.ProxyRules,
                settings.ProxyFolders,
                cancellationToken);
            _machineDetection = MachineWorkDetection.Select(_renderDetection, _proxyDetection);
            ScreenshotSkippedEvent? screenshotEvent = null;
            if (now >= nextScreenshot)
            {
                screenshotEvent = await CaptureScreenshotsAsync(now, cancellationToken);
                nextScreenshot = now.AddMinutes(Math.Clamp(settings.Screenshots.IntervalMinutes, 1, 60));
            }

            var machineStateChanged = _lastQueuedMachineState.HasValue &&
                                      _lastQueuedMachineState.Value != _machineDetection.MachineState;
            if (now >= nextHeartbeat || machineStateChanged || screenshotEvent is not null)
            {
                await eventQueue.EnqueueAsync(CreateHeartbeat(now, screenshotEvent), cancellationToken);
                _lastQueuedMachineState = _machineDetection.MachineState;
                nextHeartbeat = now.AddSeconds(Math.Clamp(settings.HeartbeatIntervalSeconds, 15, 300));
            }

            await FlushQueueAsync(cancellationToken);
            if (_operatorSelectionRequired)
            {
                return;
            }

            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private async Task RunAndReportFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FatalError?.Invoke(this, exception);
        }
    }

    private HeartbeatRequest CreateHeartbeat(
        DateTimeOffset timestampUtc,
        ScreenshotSkippedEvent? screenshotEvent = null)
    {
        var activity = activityMonitor.Capture();
        var idle = idleMonitor.Capture(settings.IdleThresholdSeconds);
        var metrics = metricsProvider.Capture();
        return new HeartbeatRequest(
            Guid.NewGuid(),
            settings.AgentId,
            settings.SelectedEmployeeId!.Value,
            settings.ComputerId,
            AgentEnvironment.Version,
            timestampUtc,
            AgentEnvironment.WindowsUser,
            Environment.MachineName,
            idle.HumanState,
            _machineDetection.MachineState,
            activity.ProcessName,
            activity.ExecutablePath,
            activity.WindowTitle,
            idle.IdleSeconds,
            metrics.CpuLoadPercent,
            metrics.MemoryLoadPercent,
            _machineDetection.RenderTelemetry,
            _machineDetection.ProxyTelemetry,
            screenshotEvent,
            settings.OperatorSessionId);
    }

    private async Task<ScreenshotSkippedEvent?> CaptureScreenshotsAsync(
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken)
    {
        var configuration = settings.Screenshots;
        var idle = idleMonitor.Capture(settings.IdleThresholdSeconds);
        if (!ScreenshotCapturePolicy.CanCapture(configuration.Enabled, idle.HumanState))
        {
            return null;
        }

        var activity = activityMonitor.Capture();
        if (ScreenshotCapturePolicy.IsPrivacyExcluded(activity.ProcessName, configuration.ExcludedProcesses))
        {
            return new ScreenshotSkippedEvent(
                Guid.NewGuid(),
                timestampUtc,
                "SCREENSHOT_SKIPPED_PRIVACY",
                activity.ProcessName,
                activity.WindowTitle);
        }

        var screenshots = screenshotCapture.Capture(
            configuration.CaptureMode,
            configuration.MaxWidth,
            configuration.JpegQuality);
        foreach (var screenshot in screenshots)
        {
            var metadata = new ScreenshotUploadMetadata(
                Guid.NewGuid(),
                settings.AgentId,
                settings.SelectedEmployeeId!.Value,
                settings.ComputerId,
                timestampUtc,
                screenshot.ScreenIndex,
                screenshot.Width,
                screenshot.Height,
                activity.ProcessName,
                activity.WindowTitle,
                idle.HumanState,
                _machineDetection.MachineState,
                settings.OperatorSessionId);
            await screenshotQueue.EnqueueAsync(metadata, screenshot.JpegData, cancellationToken);
        }

        return null;
    }

    private async Task FlushQueueAsync(CancellationToken cancellationToken)
    {
        var pending = await eventQueue.GetReadyAsync(20, cancellationToken);
        foreach (var queued in pending)
        {
            var result = await apiClient.SendHeartbeatAsync(queued.Payload, cancellationToken);
            switch (result)
            {
                case HeartbeatSendResult.Sent:
                    await eventQueue.MarkSentAsync(queued.EventId, cancellationToken);
                    _lastSyncAtUtc = settings.Clock.GetUtcNow();
                    Publish(
                        AgentConnectionState.Connected,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken));
                    break;
                case HeartbeatSendResult.Rejected:
                    // Некорректное локальное событие не должно навсегда блокировать очередь.
                    await eventQueue.MarkSentAsync(queued.EventId, cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails ?? "Сервер отклонил один heartbeat.");
                    break;
                case HeartbeatSendResult.Unauthorized:
                    Publish(
                        AgentConnectionState.AuthenticationRequired,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails);
                    return;
                case HeartbeatSendResult.OperatorSelectionRequired:
                    await eventQueue.MarkFailedAsync(queued.EventId, queued.AttemptCount, cancellationToken);
                    if (queued.Payload.OperatorSessionId != settings.OperatorSessionId)
                    {
                        // Keep historical data for diagnosis/retry, but let the current shift upload.
                        continue;
                    }

                    await RequireOperatorSelectionAsync(cancellationToken);
                    return;
                default:
                    await eventQueue.MarkFailedAsync(
                        queued.EventId,
                        queued.AttemptCount,
                        cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails ?? "Не удалось отправить heartbeat. Повторим автоматически.");
                    return;
            }
        }

        var screenshots = await screenshotQueue.GetReadyAsync(3, cancellationToken);
        foreach (var screenshot in screenshots)
        {
            var result = await apiClient.UploadScreenshotAsync(screenshot, cancellationToken);
            switch (result)
            {
                case HeartbeatSendResult.Sent:
                    await screenshotQueue.MarkSentAsync(screenshot.EventId, cancellationToken);
                    _lastSyncAtUtc = settings.Clock.GetUtcNow();
                    break;
                case HeartbeatSendResult.Rejected:
                    await screenshotQueue.MarkSentAsync(screenshot.EventId, cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails ?? "Сервер отклонил один скриншот.");
                    break;
                case HeartbeatSendResult.Unauthorized:
                    Publish(
                        AgentConnectionState.AuthenticationRequired,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails);
                    return;
                case HeartbeatSendResult.OperatorSelectionRequired:
                    await screenshotQueue.MarkFailedAsync(screenshot.EventId, screenshot.AttemptCount, cancellationToken);
                    if (screenshot.Metadata.OperatorSessionId != settings.OperatorSessionId)
                    {
                        continue;
                    }

                    await RequireOperatorSelectionAsync(cancellationToken);
                    return;
                default:
                    await screenshotQueue.MarkFailedAsync(
                        screenshot.EventId,
                        screenshot.AttemptCount,
                        cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        _lastSyncAtUtc,
                        await CountQueuedAsync(cancellationToken),
                        apiClient.LastErrorDetails ?? "Не удалось отправить скриншот. Повторим автоматически.");
                    return;
            }
        }

        Publish(
            _connectionState,
            _lastSyncAtUtc,
            await CountQueuedAsync(cancellationToken),
            _connectionDetails);
    }

    private async Task RequireOperatorSelectionAsync(CancellationToken cancellationToken)
    {
        _operatorSelectionRequired = true;
        // The tray checks these same settings before opening the selection dialog.
        // Invalidate first: a server rejection overrides the locally cached expiry.
        settings.OperatorSessionExpiresAtUtc = null;
        Publish(
            AgentConnectionState.OperatorSelectionRequired,
            _lastSyncAtUtc,
            await CountQueuedAsync(cancellationToken),
            apiClient.LastErrorDetails ?? "Сервер отклонил текущую смену. Выберите сотрудника заново.");
    }

    private async Task<int> CountQueuedAsync(CancellationToken cancellationToken) =>
        await eventQueue.CountAsync(cancellationToken) +
        await screenshotQueue.CountAsync(cancellationToken);

    private void Publish(
        AgentConnectionState state,
        DateTimeOffset? lastSyncAtUtc,
        int queuedCount,
        string? details = null)
    {
        _connectionState = state;
        _connectionDetails = details;
        StatusChanged?.Invoke(this, new AgentRuntimeStatus(
            state,
            lastSyncAtUtc,
            queuedCount,
            _machineDetection.MachineState,
            details));
    }
}

internal sealed record MachineWorkDetection(
    MachineState MachineState,
    RenderTelemetrySnapshot? RenderTelemetry,
    ProxyTelemetrySnapshot? ProxyTelemetry)
{
    public static readonly MachineWorkDetection Normal = new(MachineState.Normal, null, null);

    public static MachineWorkDetection Select(
        RenderDetectionResult render,
        ProxyDetectionResult proxy)
    {
        // Явная Render-папка имеет приоритет при ошибочно пересекающихся настройках папок.
        if (render.MachineState == MachineState.Render && render.Telemetry?.OutputFile is not null)
        {
            return new MachineWorkDetection(MachineState.Render, render.Telemetry, proxy.Telemetry);
        }

        if (proxy.MachineState == MachineState.Proxy)
        {
            return new MachineWorkDetection(MachineState.Proxy, render.Telemetry, proxy.Telemetry);
        }

        if (render.MachineState == MachineState.Render)
        {
            return new MachineWorkDetection(MachineState.Render, render.Telemetry, proxy.Telemetry);
        }

        return new MachineWorkDetection(MachineState.Normal, render.Telemetry, proxy.Telemetry);
    }
}

internal sealed record AgentRuntimeStatus(
    AgentConnectionState State,
    DateTimeOffset? LastSyncAtUtc,
    int QueuedCount,
    MachineState MachineState,
    string? Details = null);

internal enum AgentConnectionState
{
    Starting,
    Connected,
    Offline,
    AuthenticationRequired,
    OperatorSelectionRequired,
}
