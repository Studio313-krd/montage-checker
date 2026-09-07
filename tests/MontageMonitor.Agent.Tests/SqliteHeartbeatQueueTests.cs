using MontageMonitor.Agent.Storage;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class SqliteHeartbeatQueueTests
{
    [Fact]
    public async Task Queue_RecoversPendingHeartbeatAfterProcessRestart()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "montage-monitor-queue-test-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "queue.db");
        var heartbeat = CreateHeartbeat();

        try
        {
            var firstProcess = new SqliteHeartbeatQueue(databasePath);
            await firstProcess.InitializeAsync(TestContext.Current.CancellationToken);
            await firstProcess.EnqueueAsync(heartbeat, TestContext.Current.CancellationToken);
            await firstProcess.EnqueueAsync(heartbeat, TestContext.Current.CancellationToken);
            Assert.Equal(1, await firstProcess.CountAsync(TestContext.Current.CancellationToken));

            var recoveredProcess = new SqliteHeartbeatQueue(databasePath);
            await recoveredProcess.InitializeAsync(TestContext.Current.CancellationToken);
            var queued = await recoveredProcess.GetReadyAsync(10, TestContext.Current.CancellationToken);

            var item = Assert.Single(queued);
            Assert.Equal(heartbeat.EventId, item.EventId);
            Assert.Equal(heartbeat, item.Payload);
            Assert.Equal(0, item.AttemptCount);

            await recoveredProcess.MarkSentAsync(item.EventId, TestContext.Current.CancellationToken);
            Assert.Equal(0, await recoveredProcess.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static HeartbeatRequest CreateHeartbeat() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "0.13.0",
        new DateTimeOffset(2026, 9, 7, 10, 30, 0, TimeSpan.Zero),
        "montazher",
        "EDIT-01",
        HumanState.Active,
        MachineState.Normal,
        "Adobe Premiere Pro.exe",
        @"C:\Program Files\Adobe\Premiere.exe",
        "Монтаж ролика",
        12,
        21.5,
        46.2);
}
