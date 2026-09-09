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
    public void AgentPassword_IsFourDigitsAndCanBeProtectedAndVerified()
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "montage-monitor-password-tests-" + Guid.NewGuid().ToString("N")));
        try
        {
            var service = new EmployeeAgentPasswordService(DataProtectionProvider.Create(directory));
            var passwords = Enumerable.Range(0, 100).Select(_ => service.Generate()).ToList();

            Assert.All(passwords, password =>
            {
                Assert.Equal(4, password.Length);
                Assert.All(password, character => Assert.True(char.IsAsciiDigit(character)));
            });

            var password = passwords[0];
            var protectedPassword = service.Protect(password);
            Assert.NotEqual(password, protectedPassword);
            Assert.Equal(password, service.Reveal(protectedPassword));
            Assert.True(service.Verify(protectedPassword, password));
            Assert.False(service.Verify(protectedPassword, password == "0000" ? "0001" : "0000"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "1234", true)]
    [InlineData(true, "0000", true)]
    [InlineData(false, "1234", false)]
    [InlineData(true, "123", false)]
    [InlineData(true, "12a4", false)]
    public void AgentLoginInput_RequiresEmployeeAndFourDigitPassword(
        bool hasEmployee,
        string password,
        bool expected)
    {
        Assert.Equal(expected, AgentLoginInput.IsValid(hasEmployee ? Guid.NewGuid() : null, password));
    }

    [Theory]
    [InlineData("IDDQD", true)]
    [InlineData("iddqd", false)]
    [InlineData("IDDQ", false)]
    [InlineData("1234", false)]
    public void ShutdownPassword_AcceptsOnlyExpectedValue(string password, bool expected)
    {
        Assert.Equal(expected, ShutdownDialog.PasswordMatches(password));
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
