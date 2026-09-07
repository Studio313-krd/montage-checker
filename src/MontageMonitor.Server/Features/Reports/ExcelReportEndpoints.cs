using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Reports;

internal static class ExcelReportEndpoints
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static async Task<IResult> ExportAsync(
        string? employeeIds,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? timeZone,
        HttpContext httpContext,
        ExcelReportDataBuilder dataBuilder,
        ExcelReportWorkbookWriter workbookWriter,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var employeeResult = ParseEmployeeIds(employeeIds);
        if (employeeResult.Error is not null)
        {
            return Results.ValidationProblem(employeeResult.Error);
        }

        var rangeResult = ReportRangeResolver.Resolve(fromUtc, toUtc, timeZone, timeProvider.GetUtcNow());
        if (rangeResult.Error is not null)
        {
            return Results.ValidationProblem(rangeResult.Error);
        }

        var range = rangeResult.Range!.Value;
        var report = await dataBuilder.BuildAsync(
            range, employeeResult.EmployeeIds, httpContext.User, cancellationToken);
        if (report.NotFound || report.Data is null)
        {
            return Results.NotFound();
        }

        var bytes = workbookWriter.Write(report.Data);
        auditWriter.Add(
            httpContext,
            "report.excel.downloaded",
            "ExcelReport",
            userId: httpContext.User.GetRequiredUserId(),
            details: new
            {
                fromUtc = range.StartUtc,
                toUtc = range.EndUtc,
                timeZone = range.TimeZone.Id,
                employeeIds = employeeResult.EmployeeIds,
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        var startDate = TimeZoneInfo.ConvertTime(range.StartUtc, range.TimeZone).Date;
        var endDate = TimeZoneInfo.ConvertTime(range.EndUtc.AddTicks(-1), range.TimeZone).Date;
        var fileName = $"MontageMonitor_{startDate:yyyy-MM-dd}_{endDate:yyyy-MM-dd}.xlsx";
        return Results.File(bytes, ExcelContentType, fileName);
    }

    private static EmployeeIdParseResult ParseEmployeeIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new EmployeeIdParseResult([], null);
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 50 || parts.Any(part => !Guid.TryParse(part, out _)))
        {
            return InvalidEmployeeIds();
        }

        var ids = parts.Select(Guid.Parse).Distinct().ToArray();
        return ids.Length > 50 ? InvalidEmployeeIds() : new EmployeeIdParseResult(ids, null);
    }

    private static EmployeeIdParseResult InvalidEmployeeIds() => new(null!, new Dictionary<string, string[]>
    {
        ["employeeIds"] = ["Передайте не более 50 корректных идентификаторов сотрудников через запятую."],
    });

    private sealed record EmployeeIdParseResult(
        IReadOnlyCollection<Guid> EmployeeIds,
        Dictionary<string, string[]>? Error);
}
