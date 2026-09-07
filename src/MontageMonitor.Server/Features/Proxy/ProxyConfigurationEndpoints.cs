using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Proxy;

public static class ProxyConfigurationEndpoints
{
    private static readonly string[] DefaultVideoExtensions =
        [".mp4", ".mov", ".mxf", ".avi", ".webm", ".mkv", ".m4v"];

    public static IEndpointRouteBuilder MapProxyConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithTags("Настройка Proxy");

        group.MapGet("/proxy-rules", GetRulesAsync)
            .WithName("GetProxyRules")
            .WithSummary("Получить правила определения Proxy");
        group.MapPost("/proxy-rules", CreateRuleAsync)
            .WithName("CreateProxyRule")
            .WithSummary("Создать правило определения Proxy");
        group.MapPut("/proxy-rules/{ruleId:guid}", UpdateRuleAsync)
            .WithName("UpdateProxyRule")
            .WithSummary("Изменить правило определения Proxy");

        group.MapGet("/computers/{computerId:guid}/proxy-folders", GetFoldersAsync)
            .WithName("GetProxyFolders")
            .WithSummary("Получить Proxy-папки компьютера");
        group.MapPost("/computers/{computerId:guid}/proxy-folders", CreateFolderAsync)
            .WithName("CreateProxyFolder")
            .WithSummary("Добавить Proxy-папку");
        group.MapPut("/computers/{computerId:guid}/proxy-folders/{folderId:guid}", UpdateFolderAsync)
            .WithName("UpdateProxyFolder")
            .WithSummary("Изменить Proxy-папку");
        group.MapDelete("/computers/{computerId:guid}/proxy-folders/{folderId:guid}", DisableFolderAsync)
            .WithName("DisableProxyFolder")
            .WithSummary("Отключить Proxy-папку");

        return endpoints;
    }

    private static async Task<IResult> GetRulesAsync(
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.RenderRules.AsNoTracking()
            .Where(item => item.Type == ProcessingType.Proxy)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(entities.Select(ToRuleResponse));
    }

    private static async Task<IResult> CreateRuleAsync(
        SaveProxyRuleRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRule(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var processName = NormalizeProcessName(request.ProcessName);
        if (await dbContext.RenderRules.AnyAsync(
                item => item.Type == ProcessingType.Proxy && item.ProcessName.ToUpper() == processName.ToUpper(),
                cancellationToken))
        {
            return Results.Conflict(new { message = "Правило Proxy для этого процесса уже существует." });
        }

        var now = timeProvider.GetUtcNow();
        var rule = new RenderRule
        {
            Name = request.Name.Trim(),
            Type = ProcessingType.Proxy,
            ProcessName = processName,
            CpuThresholdPercent = request.CpuThresholdPercent,
            DiskWriteThresholdBytesPerSecond = request.DiskWriteThresholdBytesPerSecond,
            ConfirmationSeconds = request.ConfirmationSeconds,
            FinishTimeoutSeconds = request.FinishTimeoutSeconds,
            MinimumConfidence = request.MinimumConfidence,
            FileNamePatterns = NormalizePatterns(request.FileNamePatterns),
            IsEnabled = request.IsEnabled,
            UpdatedByUserId = httpContext.User.GetRequiredUserId(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.RenderRules.Add(rule);
        auditWriter.Add(httpContext, "proxy_rule.created", "RenderRule", rule.Id.ToString(), rule.UpdatedByUserId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/admin/proxy-rules/{rule.Id}", ToRuleResponse(rule));
    }

    private static async Task<IResult> UpdateRuleAsync(
        Guid ruleId,
        SaveProxyRuleRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRule(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var rule = await dbContext.RenderRules.SingleOrDefaultAsync(
            item => item.Id == ruleId && item.Type == ProcessingType.Proxy,
            cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        var processName = NormalizeProcessName(request.ProcessName);
        if (await dbContext.RenderRules.AnyAsync(
                item => item.Id != ruleId && item.Type == ProcessingType.Proxy &&
                        item.ProcessName.ToUpper() == processName.ToUpper(),
                cancellationToken))
        {
            return Results.Conflict(new { message = "Правило Proxy для этого процесса уже существует." });
        }

        rule.Name = request.Name.Trim();
        rule.ProcessName = processName;
        rule.CpuThresholdPercent = request.CpuThresholdPercent;
        rule.DiskWriteThresholdBytesPerSecond = request.DiskWriteThresholdBytesPerSecond;
        rule.ConfirmationSeconds = request.ConfirmationSeconds;
        rule.FinishTimeoutSeconds = request.FinishTimeoutSeconds;
        rule.MinimumConfidence = request.MinimumConfidence;
        rule.FileNamePatterns = NormalizePatterns(request.FileNamePatterns);
        rule.IsEnabled = request.IsEnabled;
        rule.UpdatedByUserId = httpContext.User.GetRequiredUserId();
        rule.UpdatedAtUtc = timeProvider.GetUtcNow();
        auditWriter.Add(httpContext, "proxy_rule.updated", "RenderRule", rule.Id.ToString(), rule.UpdatedByUserId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToRuleResponse(rule));
    }

    private static async Task<IResult> GetFoldersAsync(
        Guid computerId,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Computers.AnyAsync(item => item.Id == computerId, cancellationToken))
        {
            return Results.NotFound();
        }

        var entities = await dbContext.WatchedFolders.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.Type == WatchedFolderType.Proxy)
            .OrderBy(item => item.PathPattern)
            .ToListAsync(cancellationToken);
        return Results.Ok(entities.Select(ToFolderResponse));
    }

    private static async Task<IResult> CreateFolderAsync(
        Guid computerId,
        SaveProxyFolderRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateFolder(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        if (!await dbContext.Computers.AnyAsync(item => item.Id == computerId, cancellationToken))
        {
            return Results.NotFound();
        }

        var pathPattern = request.PathPattern.Trim();
        if (await FolderExistsAsync(dbContext, computerId, pathPattern, null, cancellationToken))
        {
            return Results.Conflict(new { message = "Эта Proxy-папка уже настроена для компьютера." });
        }

        var now = timeProvider.GetUtcNow();
        var folder = new WatchedFolder
        {
            ComputerId = computerId,
            Type = WatchedFolderType.Proxy,
            PathPattern = pathPattern,
            Extensions = NormalizeExtensions(request.Extensions),
            FileNamePatterns = NormalizePatterns(request.FileNamePatterns),
            IsEnabled = request.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.WatchedFolders.Add(folder);
        auditWriter.Add(
            httpContext,
            "proxy_folder.created",
            "WatchedFolder",
            folder.Id.ToString(),
            httpContext.User.GetRequiredUserId(),
            new { computerId, folder.PathPattern });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created(
            $"/api/admin/computers/{computerId}/proxy-folders/{folder.Id}",
            ToFolderResponse(folder));
    }

    private static async Task<IResult> UpdateFolderAsync(
        Guid computerId,
        Guid folderId,
        SaveProxyFolderRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateFolder(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var folder = await dbContext.WatchedFolders.SingleOrDefaultAsync(
            item => item.Id == folderId && item.ComputerId == computerId && item.Type == WatchedFolderType.Proxy,
            cancellationToken);
        if (folder is null)
        {
            return Results.NotFound();
        }

        var pathPattern = request.PathPattern.Trim();
        if (await FolderExistsAsync(dbContext, computerId, pathPattern, folderId, cancellationToken))
        {
            return Results.Conflict(new { message = "Эта Proxy-папка уже настроена для компьютера." });
        }

        folder.PathPattern = pathPattern;
        folder.Extensions = NormalizeExtensions(request.Extensions);
        folder.FileNamePatterns = NormalizePatterns(request.FileNamePatterns);
        folder.IsEnabled = request.IsEnabled;
        folder.UpdatedAtUtc = timeProvider.GetUtcNow();
        auditWriter.Add(
            httpContext,
            "proxy_folder.updated",
            "WatchedFolder",
            folder.Id.ToString(),
            httpContext.User.GetRequiredUserId(),
            new { computerId, folder.PathPattern });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToFolderResponse(folder));
    }

    private static async Task<IResult> DisableFolderAsync(
        Guid computerId,
        Guid folderId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var folder = await dbContext.WatchedFolders.SingleOrDefaultAsync(
            item => item.Id == folderId && item.ComputerId == computerId && item.Type == WatchedFolderType.Proxy,
            cancellationToken);
        if (folder is null)
        {
            return Results.NotFound();
        }

        folder.IsEnabled = false;
        folder.UpdatedAtUtc = timeProvider.GetUtcNow();
        auditWriter.Add(
            httpContext,
            "proxy_folder.disabled",
            "WatchedFolder",
            folder.Id.ToString(),
            httpContext.User.GetRequiredUserId(),
            new { computerId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<bool> FolderExistsAsync(
        MonitoringDbContext dbContext,
        Guid computerId,
        string pathPattern,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.WatchedFolders.AnyAsync(
            item => item.Id != exceptId && item.ComputerId == computerId &&
                    item.Type == WatchedFolderType.Proxy && item.PathPattern.ToUpper() == pathPattern.ToUpper(),
            cancellationToken);

    private static Dictionary<string, string[]>? ValidateRule(SaveProxyRuleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Required(request.Name, 255, nameof(request.Name), "Название", errors);
        Required(request.ProcessName, 255, nameof(request.ProcessName), "Encoder", errors);
        if (request.CpuThresholdPercent is < 0 or > 100)
        {
            errors[nameof(request.CpuThresholdPercent)] = ["Порог CPU должен быть от 0 до 100 процентов."];
        }

        if (request.DiskWriteThresholdBytesPerSecond < 0)
        {
            errors[nameof(request.DiskWriteThresholdBytesPerSecond)] = ["Порог записи не может быть отрицательным."];
        }

        if (request.ConfirmationSeconds is < 5 or > 300 || request.FinishTimeoutSeconds is < 5 or > 600)
        {
            errors["timing"] = ["Подтверждение должно быть 5–300 секунд, завершение — 5–600 секунд."];
        }

        if (request.MinimumConfidence is < 1 or > 100)
        {
            errors[nameof(request.MinimumConfidence)] = ["Минимальная уверенность должна быть от 1 до 100."];
        }

        ValidatePatterns(request.FileNamePatterns, nameof(request.FileNamePatterns), errors);
        return errors.Count == 0 ? null : errors;
    }

    private static Dictionary<string, string[]>? ValidateFolder(SaveProxyFolderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Required(request.PathPattern, 2_000, nameof(request.PathPattern), "Путь", errors);
        if (!string.IsNullOrWhiteSpace(request.PathPattern) && !LooksLikeAbsoluteWindowsPath(request.PathPattern))
        {
            errors[nameof(request.PathPattern)] =
                ["Укажите абсолютный Windows-путь, например D:\\Proxy или E:\\Projects\\*\\Proxy."];
        }

        ValidatePatterns(request.FileNamePatterns, nameof(request.FileNamePatterns), errors);
        if (request.Extensions is { Count: > 50 } || request.Extensions?.Any(item => item.Length > 32) == true)
        {
            errors[nameof(request.Extensions)] = ["Допускается до 50 расширений длиной не более 32 символов."];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static void ValidatePatterns(
        IReadOnlyList<string>? patterns,
        string key,
        IDictionary<string, string[]> errors)
    {
        if (patterns is { Count: > 50 } || patterns?.Any(item => item.Length > 255) == true)
        {
            errors[key] = ["Допускается до 50 шаблонов длиной не более 255 символов."];
        }
    }

    private static void Required(
        string? value,
        int maximumLength,
        string key,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            errors[key] = [$"{displayName}: обязательное поле, максимум {maximumLength} символов."];
        }
    }

    private static string NormalizeProcessName(string value)
    {
        var processName = value.Trim();
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";
    }

    private static string[] NormalizeExtensions(IReadOnlyList<string>? extensions) =>
        (extensions is { Count: > 0 } ? extensions : DefaultVideoExtensions)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim().StartsWith('.') ? item.Trim().ToLowerInvariant() : "." + item.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string[] NormalizePatterns(IReadOnlyList<string>? patterns) =>
        (patterns ?? [])
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool LooksLikeAbsoluteWindowsPath(string path)
    {
        var value = path.Trim();
        return value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' &&
               (value[2] == '\\' || value[2] == '/') || value.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static ProxyRuleResponse ToRuleResponse(RenderRule rule) => new(
        rule.Id,
        rule.Name,
        rule.ProcessName,
        rule.CpuThresholdPercent,
        rule.DiskWriteThresholdBytesPerSecond,
        rule.ConfirmationSeconds,
        rule.FinishTimeoutSeconds,
        rule.MinimumConfidence,
        rule.FileNamePatterns,
        rule.IsEnabled,
        rule.UpdatedAtUtc);

    private static ProxyFolderResponse ToFolderResponse(WatchedFolder folder) => new(
        folder.Id,
        folder.ComputerId,
        folder.PathPattern,
        folder.Extensions,
        folder.FileNamePatterns,
        folder.IsEnabled,
        folder.UpdatedAtUtc);
}
