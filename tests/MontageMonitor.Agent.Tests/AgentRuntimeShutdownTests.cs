using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class AgentRuntimeShutdownTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(-172800)]
    [InlineData(172800)]
    public async Task FirstHeartbeat_AfterLoginUsesTheServerClock(int serverOffsetSeconds)
    {
        var settings = CreateSettings();
        var serverNow = DateTimeOffset.UtcNow.AddSeconds(serverOffsetSeconds);
        var session = new AgentOperatorSessionResponse(
            Guid.NewGuid(), settings.SelectedEmployeeId!.Value, "Test", serverNow, serverNow.AddHours(1));
        AgentSettingsStore.SaveOperatorSessionValues(settings, session);
        var api = new StubApiClient(validSession: session);
        await using var runtime = CreateRuntime(settings, apiClient: api);
        var result = new TaskCompletionSource<AgentRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) =>
        {
            if (status.State is AgentConnectionState.Connected or AgentConnectionState.OperatorSelectionRequired)
            {
                result.TrySetResult(status);
            }
        };
        runtime.FatalError += (_, error) => result.TrySetException(error);

        runtime.Start();
        var status = await result.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(AgentConnectionState.Connected, status.State);
        Assert.NotNull(api.LastHeartbeat);
        Assert.InRange(api.LastHeartbeat.TimestampUtc, session.StartedAtUtc, session.StartedAtUtc.AddSeconds(5));
    }

    [Theory]
    [InlineData((int)HeartbeatSendResult.RetryLater)]
    [InlineData((int)HeartbeatSendResult.Rejected)]
    public async Task FailedHeartbeat_StaysOfflineWhileNoRetryIsReady(int result)
    {
        var queue = new StubEventQueue();
        await using var runtime = CreateRuntime(eventQueue: queue, apiClient: new StubApiClient((HeartbeatSendResult)result));
        var nextCycle = new TaskCompletionSource<AgentRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) =>
        {
            if (queue.ReadCount >= 2)
            {
                nextCycle.TrySetResult(status);
            }
        };
        runtime.FatalError += (_, exception) => nextCycle.TrySetException(exception);

        runtime.Start();
        var status = await nextCycle.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(AgentConnectionState.Offline, status.State);
        Assert.Null(status.LastSyncAtUtc);
    }

    [Fact]
    public async Task RejectedCurrentOperatorSession_InvalidatesLocalSelectionBeforeNotifyingUi()
    {
        var settings = CreateSettings();
        await using var runtime = CreateRuntime(settings, apiClient: new StubApiClient(HeartbeatSendResult.OperatorSelectionRequired));
        var selectionRequired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) =>
        {
            if (status.State == AgentConnectionState.OperatorSelectionRequired)
            {
                selectionRequired.TrySetResult(settings.HasValidOperatorSession(DateTimeOffset.UtcNow));
            }
        };
        runtime.FatalError += (_, exception) => selectionRequired.TrySetException(exception);

        runtime.Start();
        var stillValid = await selectionRequired.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(stillValid, "Отказ сервера должен открыть выбор сотрудника, даже если локальный срок смены ещё не истёк.");
    }

    [Fact]
    public async Task RejectedPreviousOperatorSession_PreservesHistoryAndSendsCurrentHeartbeat()
    {
        var settings = CreateSettings();
        var queue = new StubEventQueue { PrependPreviousSession = true };
        await using var runtime = CreateRuntime(settings, queue, new StubApiClient(validSessionId: settings.OperatorSessionId));
        var connected = new TaskCompletionSource<AgentRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) =>
        {
            if (status.State is AgentConnectionState.Connected or AgentConnectionState.OperatorSelectionRequired)
            {
                connected.TrySetResult(status);
            }
        };
        runtime.FatalError += (_, exception) => connected.TrySetException(exception);

        runtime.Start();
        var status = await connected.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(AgentConnectionState.Connected, status.State);
        Assert.NotNull(status.LastSyncAtUtc);
        Assert.Equal(1, status.QueuedCount);
        Assert.True(settings.HasValidOperatorSession(DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedScreenshot_RequiresSelectionOnlyForCurrentShift(bool previousShift)
    {
        var settings = CreateSettings();
        var metadata = new ScreenshotUploadMetadata(
            Guid.NewGuid(), settings.AgentId, settings.SelectedEmployeeId!.Value, settings.ComputerId,
            DateTimeOffset.UtcNow, 0, 800, 600, null, null, HumanState.Active, MachineState.Normal,
            previousShift ? Guid.NewGuid() : settings.OperatorSessionId);
        var screenshots = new StubScreenshotQueue(new QueuedScreenshot(metadata.EventId, metadata, "unused", 0));
        var api = new StubApiClient(screenshotResult: HeartbeatSendResult.OperatorSelectionRequired);
        await using var runtime = CreateRuntime(settings, apiClient: api, screenshotQueue: screenshots);
        var uploaded = new TaskCompletionSource<AgentRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) =>
        {
            if (api.ScreenshotAttempts > 0)
            {
                uploaded.TrySetResult(status);
            }
        };
        runtime.FatalError += (_, exception) => uploaded.TrySetException(exception);

        runtime.Start();
        var status = await uploaded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(previousShift ? AgentConnectionState.Connected : AgentConnectionState.OperatorSelectionRequired, status.State);
        Assert.Equal(previousShift, settings.HasValidOperatorSession(DateTimeOffset.UtcNow));
        Assert.True(screenshots.Deferred);
        Assert.Equal(1, status.QueuedCount);
    }

#pragma warning disable xUnit1031 // The blocking wait deliberately emulates WinForms ExitThreadCore.
    [Fact]
    public void DisposeAsync_CompletesWithoutPumpingCapturedUiContext()
    {
        var originalContext = SynchronizationContext.Current;
        using var reachedRunningState = new ManualResetEventSlim();
        var runtime = CreateRuntime();
        runtime.StatusChanged += (_, status) =>
        {
            if (status.State == AgentConnectionState.Connected)
            {
                reachedRunningState.Set();
            }
        };

        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            runtime.Start();
            Assert.True(reachedRunningState.Wait(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));

            var disposal = runtime.DisposeAsync().AsTask();

            Assert.True(
                disposal.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
                "Остановка Agent не должна ждать обработки сообщений UI-потока.");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
#pragma warning restore xUnit1031

    private static AgentSettings CreateSettings() => new()
    {
        AgentId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        ComputerId = Guid.NewGuid(),
        ProtectedDeviceAccessToken = "unused-in-test",
        CredentialExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        OperatorSessionId = Guid.NewGuid(),
        SelectedEmployeeId = Guid.NewGuid(),
        SelectedEmployeeName = "Тестовый сотрудник",
        OperatorSessionExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        HeartbeatIntervalSeconds = 15,
    };

    private static AgentRuntime CreateRuntime(
        AgentSettings? settings = null,
        ILocalEventQueue? eventQueue = null,
        IAgentApiClient? apiClient = null,
        ILocalScreenshotQueue? screenshotQueue = null)
    {
        return new AgentRuntime(
            settings ?? CreateSettings(),
            new AgentSettingsStore(),
            new StubActivityMonitor(),
            new StubIdleMonitor(),
            new StubMetricsProvider(),
            new StubRenderDetector(),
            new StubProxyDetector(),
            new StubScreenshotCapture(),
            eventQueue ?? new StubEventQueue(),
            screenshotQueue ?? new StubScreenshotQueue(),
            apiClient ?? new StubApiClient());
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }

    private sealed class StubActivityMonitor : IActivityMonitor
    {
        public ActivitySnapshot Capture() => new(null, null, null);
    }

    private sealed class StubIdleMonitor : IIdleMonitor
    {
        public IdleSnapshot Capture(int idleThresholdSeconds) => new(0, HumanState.Active);

        public void Dispose()
        {
        }
    }

    private sealed class StubMetricsProvider : ISystemMetricsProvider
    {
        public SystemMetricsSnapshot Capture() => new(0, 0);
    }

    private sealed class StubRenderDetector : IRenderDetector
    {
        public Task<RenderDetectionResult> EvaluateAsync(
            DateTimeOffset timestampUtc,
            IReadOnlyList<AgentRenderRule> rules,
            IReadOnlyList<AgentWatchedFolder> folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RenderDetectionResult(MachineState.Normal, null));
    }

    private sealed class StubProxyDetector : IProxyDetector
    {
        public Task<ProxyDetectionResult> EvaluateAsync(
            DateTimeOffset timestampUtc,
            IReadOnlyList<AgentProxyRule> rules,
            IReadOnlyList<AgentWatchedFolder> folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProxyDetectionResult(MachineState.Normal, null));
    }

    private sealed class StubScreenshotCapture : IScreenshotCapture
    {
        public IReadOnlyList<CapturedScreenshot> Capture(
            ScreenshotCaptureMode captureMode,
            int maxWidth,
            int jpegQuality) => [];
    }

    private sealed class StubEventQueue : ILocalEventQueue
    {
        private readonly List<QueuedHeartbeat> _pending = [];
        private bool _deferred;
        public int ReadCount { get; private set; }
        public bool PrependPreviousSession { get; init; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(HeartbeatRequest heartbeat, CancellationToken cancellationToken)
        {
            if (PrependPreviousSession && _pending.Count == 0)
            {
                var previous = heartbeat with { EventId = Guid.NewGuid(), OperatorSessionId = Guid.NewGuid() };
                _pending.Add(new QueuedHeartbeat(previous.EventId, previous, 0));
            }
            _pending.Add(new QueuedHeartbeat(heartbeat.EventId, heartbeat, 0));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QueuedHeartbeat>> GetReadyAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<QueuedHeartbeat>>(_deferred ? [] : _pending.ToArray());
        }

        public Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken)
        {
            _pending.RemoveAll(item => item.EventId == eventId);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid eventId,
            int previousAttemptCount,
            CancellationToken cancellationToken)
        {
            _deferred = true;
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(_pending.Count);

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubScreenshotQueue(QueuedScreenshot? pending = null) : ILocalScreenshotQueue
    {
        public bool Deferred { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(
            ScreenshotUploadMetadata metadata,
            ReadOnlyMemory<byte> jpegData,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<QueuedScreenshot>> GetReadyAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueuedScreenshot>>(pending is not null && !Deferred ? [pending] : []);

        public Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFailedAsync(
            Guid eventId,
            int previousAttemptCount,
            CancellationToken cancellationToken)
        {
            Deferred = true;
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(pending is null ? 0 : 1);

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubApiClient(
        HeartbeatSendResult result = HeartbeatSendResult.Sent,
        Guid? validSessionId = null,
        HeartbeatSendResult screenshotResult = HeartbeatSendResult.Sent,
        AgentOperatorSessionResponse? validSession = null) : IAgentApiClient
    {
        public int ScreenshotAttempts { get; private set; }
        public HeartbeatRequest? LastHeartbeat { get; private set; }

        public Task<HeartbeatSendResult> SendHeartbeatAsync(
            HeartbeatRequest heartbeat,
            CancellationToken cancellationToken)
        {
            LastHeartbeat = heartbeat;
            if (validSession is not null &&
                (heartbeat.TimestampUtc < validSession.StartedAtUtc || heartbeat.TimestampUtc >= validSession.ExpiresAtUtc))
            {
                return Task.FromResult(HeartbeatSendResult.OperatorSelectionRequired);
            }

            return Task.FromResult(
                validSessionId.HasValue && heartbeat.OperatorSessionId != validSessionId
                    ? HeartbeatSendResult.OperatorSelectionRequired
                    : result);
        }

        public Task<AgentConfigurationResponse?> GetConfigurationAsync(
            Guid? operatorSessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AgentConfigurationResponse?>(null);

        public Task<AgentOperatorOptionsResponse> GetOperatorOptionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<AgentOperatorOptionsResponse>(new NotSupportedException());

        public Task<AgentOperatorSessionResponse> StartOperatorSessionAsync(
            Guid employeeId,
            string password,
            CancellationToken cancellationToken) =>
            Task.FromException<AgentOperatorSessionResponse>(new NotSupportedException());

        public Task<HeartbeatSendResult> UploadScreenshotAsync(
            QueuedScreenshot screenshot,
            CancellationToken cancellationToken)
        {
            ScreenshotAttempts++;
            return Task.FromResult(screenshotResult);
        }

        public void Dispose()
        {
        }
    }
}
