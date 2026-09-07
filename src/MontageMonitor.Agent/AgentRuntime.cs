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

    public event EventHandler<AgentRuntimeStatus>? StatusChanged;
    public event EventHandler<Exception>? FatalError;

    public void Start()
    {
        if (_worker is null)
        {
            _worker = RunAndReportFailureAsync(_cancellation.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        if (_worker is not null)
        {
            try
            {
                await _worker;
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
            var now = DateTimeOffset.UtcNow;
            if (now >= nextConfigurationRefresh)
            {
                var configuration = await apiClient.GetConfigurationAsync(cancellationToken);
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
            settings.EmployeeId,
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
            screenshotEvent);
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
                settings.EmployeeId,
                settings.ComputerId,
                timestampUtc,
                screenshot.ScreenIndex,
                screenshot.Width,
                screenshot.Height,
                activity.ProcessName,
                activity.WindowTitle,
                idle.HumanState,
                _machineDetection.MachineState);
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
                    _lastSyncAtUtc = DateTimeOffset.UtcNow;
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
                        null,
                        await CountQueuedAsync(cancellationToken),
                        "Сервер отклонил один heartbeat.");
                    break;
                case HeartbeatSendResult.Unauthorized:
                    Publish(
                        AgentConnectionState.EnrollmentRequired,
                        null,
                        await CountQueuedAsync(cancellationToken));
                    return;
                default:
                    await eventQueue.MarkFailedAsync(
                        queued.EventId,
                        queued.AttemptCount,
                        cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        null,
                        await CountQueuedAsync(cancellationToken));
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
                    _lastSyncAtUtc = DateTimeOffset.UtcNow;
                    break;
                case HeartbeatSendResult.Rejected:
                    await screenshotQueue.MarkSentAsync(screenshot.EventId, cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        null,
                        await CountQueuedAsync(cancellationToken),
                        "Сервер отклонил один скриншот.");
                    break;
                case HeartbeatSendResult.Unauthorized:
                    Publish(
                        AgentConnectionState.EnrollmentRequired,
                        null,
                        await CountQueuedAsync(cancellationToken));
                    return;
                default:
                    await screenshotQueue.MarkFailedAsync(
                        screenshot.EventId,
                        screenshot.AttemptCount,
                        cancellationToken);
                    Publish(
                        AgentConnectionState.Offline,
                        null,
                        await CountQueuedAsync(cancellationToken));
                    return;
            }
        }

        Publish(
            AgentConnectionState.Connected,
            _lastSyncAtUtc,
            await CountQueuedAsync(cancellationToken));
    }

    private async Task<int> CountQueuedAsync(CancellationToken cancellationToken) =>
        await eventQueue.CountAsync(cancellationToken) +
        await screenshotQueue.CountAsync(cancellationToken);

    private void Publish(
        AgentConnectionState state,
        DateTimeOffset? lastSyncAtUtc,
        int queuedCount,
        string? details = null) =>
        StatusChanged?.Invoke(this, new AgentRuntimeStatus(
            state,
            lastSyncAtUtc,
            queuedCount,
            _machineDetection.MachineState,
            details));
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
    EnrollmentRequired,
}
