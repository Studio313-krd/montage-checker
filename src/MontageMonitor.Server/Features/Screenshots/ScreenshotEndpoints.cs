using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Agents;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Screenshots;

public static class ScreenshotEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEndpointRouteBuilder MapScreenshotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/agent/screenshots", UploadAsync)
            .RequireAuthorization(AgentAuthenticationDefaults.Policy)
            .RequireRateLimiting(SecurityPolicies.ScreenshotUploadRateLimit)
            .DisableAntiforgery()
            .WithTags("Скриншоты Agent")
            .WithName("UploadAgentScreenshot")
            .WithSummary("Загрузить сжатый JPEG-скриншот");
        endpoints.MapGet("/api/screenshots/{screenshotId:guid}/content", GetContentAsync)
            .RequireAuthorization(SecurityPolicies.ViewScreenshots)
            .WithTags("Скриншоты")
            .WithName("GetScreenshotContent")
            .WithSummary("Получить изображение скриншота");
        return endpoints;
    }

    private static async Task<IResult> GetContentAsync(
        Guid screenshotId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        AuditWriter auditWriter,
        IOptions<ScreenshotStorageOptions> storageOptions,
        CancellationToken cancellationToken)
    {
        var screenshot = await dbContext.Screenshots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == screenshotId, cancellationToken);
        if (screenshot is null || screenshot.FileDeletedAtUtc is not null ||
            !await employeeAccessService.CanViewAsync(
                screenshot.EmployeeId,
                httpContext.User,
                cancellationToken))
        {
            return Results.NotFound();
        }

        var root = Path.GetFullPath(storageOptions.Value.RootPath);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(
                root,
                screenshot.StoragePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Results.NotFound();
        }
        if (!IsInsideRoot(root, fullPath) || !File.Exists(fullPath))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "private, max-age=60";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        auditWriter.Add(
            httpContext,
            "screenshot.view",
            nameof(Screenshot),
            screenshot.Id.ToString(),
            httpContext.User.GetRequiredUserId(),
            new { screenshot.EmployeeId, screenshot.TimestampUtc });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.File(
            fullPath,
            screenshot.MimeType,
            lastModified: screenshot.TimestampUtc,
            enableRangeProcessing: false);
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        IOptions<ScreenshotStorageOptions> storageOptions,
        OperatorSessionService operatorSessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(
                "Ожидается multipart/form-data.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.Problem(
                "Размер multipart-запроса превышает допустимый предел.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var file = form.Files.GetFile("file");
        var metadataJson = form["metadata"].ToString();
        if (file is null || string.IsNullOrWhiteSpace(metadataJson))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["upload"] = ["Требуются JPEG-файл и metadata."],
            });
        }

        ScreenshotUploadMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<ScreenshotUploadMetadata>(metadataJson, JsonOptions);
        }
        catch (JsonException)
        {
            metadata = null;
        }

        if (metadata is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metadata"] = ["Некорректная metadata скриншота."],
            });
        }

        var validation = ValidateMetadata(metadata, httpContext, timeProvider.GetUtcNow());
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var agentId = httpContext.User.GetRequiredAgentId();
        var computerId = httpContext.User.GetRequiredComputerId();
        if (metadata.OperatorSessionId.HasValue)
        {
            var operatorSession = await operatorSessions.FindValidSessionAsync(
                metadata.OperatorSessionId.Value,
                agentId,
                computerId,
                metadata.TimestampUtc,
                cancellationToken);
            if (operatorSession is null || operatorSession.EmployeeId != metadata.EmployeeId)
            {
                return Results.Json(
                    new { message = "Требуется выбрать монтажёра и подтвердить PIN-код." },
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }
        }
        else if (metadata.EmployeeId != httpContext.User.GetRequiredEmployeeId())
        {
            return Results.Json(
                new { message = "Сотрудник скриншота не соответствует старому device token." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!await dbContext.Employees.AsNoTracking().AnyAsync(
                item => item.Id == metadata.EmployeeId && item.IsActive,
                cancellationToken))
        {
            return metadata.OperatorSessionId.HasValue
                ? Results.Json(
                    new { message = "Требуется выбрать активного монтажёра и подтвердить PIN-код." },
                    statusCode: StatusCodes.Status428PreconditionRequired)
                : Results.Json(
                    new { message = "Сотрудник старого device token деактивирован." },
                    statusCode: StatusCodes.Status403Forbidden);
        }

        var screenshotEnabled = await dbContext.Employees.AsNoTracking()
            .Where(item => item.Id == metadata.EmployeeId)
            .Select(item => item.ScreenshotEnabled)
            .SingleOrDefaultAsync(cancellationToken);
        if (!screenshotEnabled)
        {
            return Results.Problem(
                "Создание скриншотов отключено для этого сотрудника.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var options = storageOptions.Value;
        var maxUploadBytes = Math.Clamp(options.MaxUploadBytes, 256 * 1_024, 20 * 1_024 * 1_024);
        if (file.Length is <= 0 || file.Length > maxUploadBytes)
        {
            return Results.Problem(
                $"Размер JPEG должен быть от 1 байта до {maxUploadBytes} байт.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!string.Equals(file.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Разрешён только image/jpeg.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var existing = await dbContext.Screenshots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EventId == metadata.EventId, cancellationToken);
        if (existing is not null)
        {
            return existing.EmployeeId == metadata.EmployeeId && existing.ComputerId == metadata.ComputerId
                ? Results.Ok(new ScreenshotUploadResponse(metadata.EventId, existing.Id, timeProvider.GetUtcNow()))
                : Results.Conflict(new { message = "UUID скриншота уже принадлежит другому компьютеру." });
        }

        await using var memory = new MemoryStream(checked((int)file.Length));
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (!JpegInspector.TryGetDimensions(bytes, out var width, out var height) ||
            width != metadata.Width || height != metadata.Height)
        {
            return Results.Problem(
                "Содержимое не является допустимым JPEG или размеры не совпадают с metadata.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var root = Path.GetFullPath(options.RootPath);
        var date = metadata.TimestampUtc.UtcDateTime;
        var relativePath = Path.Combine(
            metadata.EmployeeId.ToString("N"),
            date.Year.ToString("0000"),
            date.Month.ToString("00"),
            date.Day.ToString("00"),
            metadata.EventId.ToString("N") + ".jpg");
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsInsideRoot(root, fullPath))
        {
            return Results.Problem("Некорректный путь хранения.", statusCode: StatusCodes.Status400BadRequest);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: false);
            var now = timeProvider.GetUtcNow();
            var screenshot = new Screenshot
            {
                EventId = metadata.EventId,
                EmployeeId = metadata.EmployeeId,
                ComputerId = metadata.ComputerId,
                TimestampUtc = metadata.TimestampUtc,
                StoragePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                MimeType = "image/jpeg",
                FileSizeBytes = bytes.LongLength,
                Width = width,
                Height = height,
                ScreenIndex = metadata.ScreenIndex,
                ForegroundProcess = Normalize(metadata.ForegroundProcess),
                ForegroundWindowTitle = Normalize(metadata.ForegroundWindowTitle),
                HumanState = metadata.HumanState,
                MachineState = metadata.MachineState,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Screenshots.Add(screenshot);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created(
                $"/api/screenshots/{screenshot.Id}",
                new ScreenshotUploadResponse(metadata.EventId, screenshot.Id, now));
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            var duplicate = await dbContext.Screenshots.AsNoTracking()
                .SingleOrDefaultAsync(item => item.EventId == metadata.EventId, cancellationToken);
            return duplicate is not null && duplicate.ComputerId == metadata.ComputerId
                ? Results.Ok(new ScreenshotUploadResponse(metadata.EventId, duplicate.Id, timeProvider.GetUtcNow()))
                : Results.Conflict(new { message = "Файл скриншота с этим UUID уже существует." });
        }
        catch (DbUpdateException)
        {
            TryDelete(fullPath);
            return Results.Conflict(new { message = "Не удалось сохранить повторный UUID скриншота." });
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static Dictionary<string, string[]>? ValidateMetadata(
        ScreenshotUploadMetadata metadata,
        HttpContext httpContext,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (metadata.EventId == Guid.Empty || metadata.AgentId == Guid.Empty ||
            metadata.EmployeeId == Guid.Empty || metadata.ComputerId == Guid.Empty)
        {
            errors["identifiers"] = ["Идентификаторы скриншота не могут быть пустыми."];
        }

        if (metadata.AgentId != httpContext.User.GetRequiredAgentId() ||
            metadata.ComputerId != httpContext.User.GetRequiredComputerId())
        {
            errors["device"] = ["Metadata скриншота не соответствует device token."];
        }

        if (metadata.TimestampUtc > now.AddMinutes(5))
        {
            errors[nameof(metadata.TimestampUtc)] = ["Время скриншота находится более чем на 5 минут в будущем."];
        }

        if (metadata.ScreenIndex < 0 || metadata.Width is < 1 or > 10_000 ||
            metadata.Height is < 1 or > 10_000 || (long)metadata.Width * metadata.Height > 50_000_000)
        {
            errors["dimensions"] = ["Некорректный индекс экрана или размеры изображения."];
        }

        if (metadata.ForegroundProcess?.Length > 255 || metadata.ForegroundWindowTitle?.Length > 2_000)
        {
            errors["foreground"] = ["Metadata активного окна слишком длинная."];
        }

        if (metadata.HumanState == HumanState.Locked)
        {
            errors[nameof(metadata.HumanState)] = ["Нельзя загружать скриншот заблокированной сессии."];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal static class JpegInspector
{
    private static readonly HashSet<byte> StartOfFrameMarkers =
        [0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF];

    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8 ||
            data[^2] != 0xFF || data[^1] != 0xD9)
        {
            return false;
        }

        var position = 2;
        while (position + 3 < data.Length)
        {
            if (data[position++] != 0xFF)
            {
                return false;
            }

            while (position < data.Length && data[position] == 0xFF)
            {
                position++;
            }

            if (position >= data.Length)
            {
                return false;
            }

            var marker = data[position++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7 || marker == 0x01)
            {
                continue;
            }

            if (marker == 0xDA || position + 1 >= data.Length)
            {
                break;
            }

            var segmentLength = (data[position] << 8) | data[position + 1];
            if (segmentLength < 2 || position + segmentLength > data.Length)
            {
                return false;
            }

            if (StartOfFrameMarkers.Contains(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                height = (data[position + 3] << 8) | data[position + 4];
                width = (data[position + 5] << 8) | data[position + 6];
                return width > 0 && height > 0;
            }

            position += segmentLength;
        }

        return false;
    }
}
