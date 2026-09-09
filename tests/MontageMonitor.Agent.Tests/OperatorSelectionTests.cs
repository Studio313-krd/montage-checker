using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Features.Agents;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class OperatorSelectionTests
{
    [Fact]
    public void NextBoundary_BeforeSixMoscow_IsSameDayAtSix()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);
        var nowUtc = new DateTimeOffset(2026, 9, 9, 2, 30, 0, TimeSpan.Zero);

        var boundary = service.GetNextSelectionBoundaryUtc(nowUtc);

        Assert.Equal(new DateTimeOffset(2026, 9, 9, 3, 0, 0, TimeSpan.Zero), boundary);
    }

    [Fact]
    public void NextBoundary_AtSixMoscow_IsNextDayAtSix()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);
        var nowUtc = new DateTimeOffset(2026, 9, 9, 3, 0, 0, TimeSpan.Zero);

        var boundary = service.GetNextSelectionBoundaryUtc(nowUtc);

        Assert.Equal(new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero), boundary);
    }

    [Theory]
    [InlineData("0.8.0", false)]
    [InlineData("0.9.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("unknown", false)]
    public void RequiresOperatorSession_IsBackwardCompatible(string version, bool expected)
    {
        Assert.Equal(expected, OperatorSessionService.RequiresOperatorSession(version));
    }

    [Fact]
    public void Pin_IsFourDigitsAndCanBeProtectedAndVerified()
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "montage-monitor-pin-tests-" + Guid.NewGuid().ToString("N")));
        try
        {
            var service = new EmployeeOperatorPinService(DataProtectionProvider.Create(directory));
            var pins = Enumerable.Range(0, 100).Select(_ => service.Generate()).ToList();

            Assert.All(pins, pin =>
            {
                Assert.Equal(4, pin.Length);
                Assert.All(pin, character => Assert.True(char.IsAsciiDigit(character)));
            });

            var pin = pins[0];
            var protectedPin = service.Protect(pin);
            Assert.NotEqual(pin, protectedPin);
            Assert.Equal(pin, service.Reveal(protectedPin));
            Assert.True(service.Verify(protectedPin, pin));
            Assert.False(service.Verify(protectedPin, pin == "0000" ? "0001" : "0000"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static MonitoringDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql("Host=localhost;Database=montage_monitor_tests;Username=tests")
            .Options;
        return new MonitoringDbContext(options);
    }

    private static OperatorSessionService CreateService(MonitoringDbContext context) => new(
        context,
        Options.Create(new OperatorSelectionOptions
        {
            TimeZone = "Europe/Moscow",
            DailySelectionHour = 6,
        }));
}
