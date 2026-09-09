using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class AgentRuntimeShutdownTests
{
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

    private static AgentRuntime CreateRuntime()
    {
        var settings = new AgentSettings
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

        return new AgentRuntime(
            settings,
            new AgentSettingsStore(),
            new StubActivityMonitor(),
            new StubIdleMonitor(),
            new StubMetricsProvider(),
            new StubRenderDetector(),
            new StubProxyDetector(),
            new StubScreenshotCapture(),
            new StubEventQueue(),
            new StubScreenshotQueue(),
            new StubApiClient());
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
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(HeartbeatRequest heartbeat, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<QueuedHeartbeat>> GetReadyAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueuedHeartbeat>>([]);

        public Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFailedAsync(
            Guid eventId,
            int previousAttemptCount,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubScreenshotQueue : ILocalScreenshotQueue
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnqueueAsync(
            ScreenshotUploadMetadata metadata,
            ReadOnlyMemory<byte> jpegData,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<QueuedScreenshot>> GetReadyAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueuedScreenshot>>([]);

        public Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFailedAsync(
            Guid eventId,
            int previousAttemptCount,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubApiClient : IAgentApiClient
    {
        public Task<HeartbeatSendResult> SendHeartbeatAsync(
            HeartbeatRequest heartbeat,
            CancellationToken cancellationToken) => Task.FromResult(HeartbeatSendResult.Sent);

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
            CancellationToken cancellationToken) => Task.FromResult(HeartbeatSendResult.Sent);

        public void Dispose()
        {
        }
    }
}
