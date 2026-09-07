using MontageMonitor.Server.Features.Reports;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ReportRangeResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_RejectsRangeLongerThanOneYear()
    {
        var result = ReportRangeResolver.Resolve(Now.AddDays(-367), Now, "UTC", Now);

        Assert.Null(result.Range);
        Assert.Contains("range", result.Error!);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe/<Moscow>")]
    public void Resolve_RejectsUnsafeTimeZone(string timeZone)
    {
        var result = ReportRangeResolver.Resolve(Now.AddDays(-1), Now, timeZone, Now);

        Assert.Null(result.Range);
        Assert.Contains("timeZone", result.Error!);
    }

    [Fact]
    public void Resolve_ClampsFutureEndForOpenSessions()
    {
        var result = ReportRangeResolver.Resolve(Now.AddHours(-1), Now.AddHours(1), "UTC", Now);

        Assert.Equal(Now, result.Range!.Value.EffectiveEndUtc);
    }
}
