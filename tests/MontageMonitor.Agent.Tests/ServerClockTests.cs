using System.Text.Json;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ServerClockTests
{
    private static readonly DateTimeOffset ServerNow = new(2026, 9, 11, 8, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-5)]
    [InlineData(-120)]
    [InlineData(172800)]
    public void NewShift_UsesServerTimeEvenWhenWindowsClockIsWrong(int localOffsetSeconds)
    {
        var time = new ManualTimeProvider(ServerNow.AddSeconds(localOffsetSeconds));
        var settings = Settings(time);
        var session = Session();

        AgentSettingsStore.SaveOperatorSessionValues(settings, session);
        time.Advance(TimeSpan.FromSeconds(1));
        var eventTime = settings.Clock.GetUtcNow();

        Assert.Equal(ServerNow.AddSeconds(1), eventTime);
        Assert.True(eventTime >= session.StartedAtUtc && eventTime < session.ExpiresAtUtc);
        Assert.True(settings.HasValidOperatorSession(eventTime));
    }

    [Fact]
    public void WindowsClockJump_DoesNotChangeEventTimeOrExtendTheShift()
    {
        var time = new ManualTimeProvider(ServerNow);
        var settings = Settings(time);
        var session = Session();
        AgentSettingsStore.SaveOperatorSessionValues(settings, session);

        time.ShiftWallClock(TimeSpan.FromDays(-2));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(ServerNow.AddMinutes(30), settings.Clock.GetUtcNow());
        Assert.True(settings.HasValidOperatorSession(settings.Clock.GetUtcNow()));

        time.ShiftWallClock(TimeSpan.FromDays(10));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(session.ExpiresAtUtc, settings.Clock.GetUtcNow());
        Assert.False(settings.HasValidOperatorSession(settings.Clock.GetUtcNow()));
    }

    [Fact]
    public void SavedOffset_CanBeUsedForOfflineRestart()
    {
        var time = new ManualTimeProvider(ServerNow.AddMinutes(-2));
        var settings = Settings(time);
        AgentSettingsStore.SaveOperatorSessionValues(settings, Session());

        var json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("\"Clock\"", json);
        var restored = JsonSerializer.Deserialize<AgentSettings>(json)!;
        time.Advance(TimeSpan.FromMinutes(5));
        restored.Clock = new ServerClock(time);
        restored.Clock.RestoreOffset(restored.ServerClockOffsetTicks);

        Assert.Equal(ServerNow.AddMinutes(5), restored.Clock.GetUtcNow());
    }

    private static AgentSettings Settings(TimeProvider time) => new()
    {
        ProtectedDeviceAccessToken = "unused",
        Clock = new ServerClock(time),
    };

    private static AgentOperatorSessionResponse Session() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Test", ServerNow, ServerNow.AddHours(1));

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;
        public void ShiftWallClock(TimeSpan offset) => _now += offset;

        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            _timestamp += elapsed.Ticks;
        }
    }
}
