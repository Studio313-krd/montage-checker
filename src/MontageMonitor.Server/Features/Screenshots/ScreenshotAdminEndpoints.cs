using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Screenshots;

public static class ScreenshotAdminEndpoints
{
    private static readonly string[] SettingKeys =
    [
        "screenshots.captureMode",
        "screenshots.maxWidth",
        "screenshots.jpegQuality",
        "screenshots.retentionDays",
    ];

    public static IEndpointRouteBuilder MapScreenshotAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/screenshots")
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithTags("Настройка скриншотов");

        group.MapGet("/settings", GetSettingsAsync)
            .WithSummary("Получить глобальные настройки скриншотов");
        group.MapPut("/settings", UpdateSettingsAsync)
            .WithSummary("Изменить глобальные настройки скриншотов");
        group.MapGet("/privacy-processes", GetPrivacyProcessesAsync)
            .WithSummary("Получить процессы, исключённые из скриншотов");
        group.MapPost("/privacy-processes", AddPrivacyProcessAsync)
            .WithSummary("Добавить privacy-исключение");
        group.MapDelete("/privacy-processes/{ruleId:guid}", RemovePrivacyProcessAsync)
            .WithSummary("Отключить privacy-исключение");
        return endpoints;
    }

    private static async Task<IResult> GetSettingsAsync(
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var values = await dbContext.SystemSettings.AsNoTracking()
            .Where(item => SettingKeys.Contains(item.Key))
            .ToDictionaryAsync(item => item.Key, item => item.JsonValue, cancellationToken);
        return Results.Ok(ToSettings(values));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateScreenshotSettingsRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.MaxWidth is < 320 or > 7_680)
        {
            errors[nameof(request.MaxWidth)] = ["Максимальная ширина должна быть от 320 до 7680 пикселей."];
        }

        if (request.JpegQuality is < 55 or > 65)
        {
            errors[nameof(request.JpegQuality)] = ["Качество JPEG должно быть от 55 до 65."];
        }

        if (request.RetentionDays is < 1 or > 3_650)
        {
            errors[nameof(request.RetentionDays)] = ["Срок хранения должен быть от 1 до 3650 дней."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var settings = await dbContext.SystemSettings
            .Where(item => SettingKeys.Contains(item.Key))
            .ToDictionaryAsync(item => item.Key, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var userId = httpContext.User.GetRequiredUserId();
        Set("screenshots.captureMode", JsonSerializer.Serialize(request.CaptureMode.ToString()));
        Set("screenshots.maxWidth", request.MaxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("screenshots.jpegQuality", request.JpegQuality.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("screenshots.retentionDays", request.RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        auditWriter.Add(
            httpContext,
            "screenshot.settings.updated",
            "SystemSetting",
            details: new { request.CaptureMode, request.MaxWidth, request.JpegQuality, request.RetentionDays });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ScreenshotSettingsResponse(
            request.CaptureMode,
            request.MaxWidth,
            request.JpegQuality,
            request.RetentionDays));

        void Set(string key, string jsonValue)
        {
            var setting = settings[key];
            setting.JsonValue = jsonValue;
            setting.Version++;
            setting.UpdatedByUserId = userId;
            setting.UpdatedAtUtc = now;
        }
    }

    private static async Task<IResult> GetPrivacyProcessesAsync(
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rules = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsScreenshotExcluded)
            .OrderBy(item => item.DisplayName)
            .Select(item => new ScreenshotPrivacyProcessResponse(item.Id, item.ProcessName, item.DisplayName))
            .ToListAsync(cancellationToken);
        return Results.Ok(rules);
    }

    private static async Task<IResult> AddPrivacyProcessAsync(
        AddScreenshotPrivacyProcessRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProcessName) || request.ProcessName.Trim().Length > 255 ||
            string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 255)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["process"] = ["Процесс и название обязательны, максимум 255 символов."],
            });
        }

        var processName = NormalizeProcessName(request.ProcessName);
        var normalized = processName.ToUpperInvariant();
        var rule = await dbContext.ApplicationRules.SingleOrDefaultAsync(
            item => item.NormalizedProcessName == normalized,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var userId = httpContext.User.GetRequiredUserId();
        if (rule is null)
        {
            rule = new ApplicationRule
            {
                ProcessName = processName,
                NormalizedProcessName = normalized,
                DisplayName = request.DisplayName.Trim(),
                Classification = ApplicationClassification.Ignored,
                IsScreenshotExcluded = true,
                IsEnabled = true,
                UpdatedByUserId = userId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.ApplicationRules.Add(rule);
        }
        else
        {
            rule.DisplayName = request.DisplayName.Trim();
            rule.IsScreenshotExcluded = true;
            rule.UpdatedByUserId = userId;
            rule.UpdatedAtUtc = now;
        }

        auditWriter.Add(httpContext, "screenshot.privacy_process.added", "ApplicationRule", rule.Id.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ScreenshotPrivacyProcessResponse(rule.Id, rule.ProcessName, rule.DisplayName));
    }

    private static async Task<IResult> RemovePrivacyProcessAsync(
        Guid ruleId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rule = await dbContext.ApplicationRules.SingleOrDefaultAsync(
            item => item.Id == ruleId && item.IsScreenshotExcluded,
            cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        rule.IsScreenshotExcluded = false;
        rule.UpdatedByUserId = httpContext.User.GetRequiredUserId();
        rule.UpdatedAtUtc = timeProvider.GetUtcNow();
        auditWriter.Add(httpContext, "screenshot.privacy_process.removed", "ApplicationRule", rule.Id.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static ScreenshotSettingsResponse ToSettings(IReadOnlyDictionary<string, string> values)
    {
        var modeJson = values.GetValueOrDefault("screenshots.captureMode", "\"PrimaryMonitor\"");
        var modeText = JsonSerializer.Deserialize<string>(modeJson);
        var mode = Enum.TryParse<ScreenshotCaptureMode>(modeText, true, out var parsedMode)
            ? parsedMode
            : ScreenshotCaptureMode.PrimaryMonitor;
        return new ScreenshotSettingsResponse(
            mode,
            ParseInt(values, "screenshots.maxWidth", 1_600),
            ParseInt(values, "screenshots.jpegQuality", 60),
            ParseInt(values, "screenshots.retentionDays", 30));
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        int.TryParse(values.GetValueOrDefault(key), out var parsed) ? parsed : fallback;

    private static string NormalizeProcessName(string value)
    {
        var processName = Path.GetFileName(value.Trim());
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";
    }
}

public sealed record ScreenshotSettingsResponse(
    ScreenshotCaptureMode CaptureMode,
    int MaxWidth,
    int JpegQuality,
    int RetentionDays);

public sealed record UpdateScreenshotSettingsRequest(
    ScreenshotCaptureMode CaptureMode,
    int MaxWidth,
    int JpegQuality,
    int RetentionDays);

public sealed record ScreenshotPrivacyProcessResponse(Guid Id, string ProcessName, string DisplayName);

public sealed record AddScreenshotPrivacyProcessRequest(string ProcessName, string DisplayName);
