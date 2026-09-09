using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Activity;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Agents;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/agent")
            .WithTags("Windows Agent");

        group.MapPost("/enroll", EnrollAsync)
            .AllowAnonymous()
            .RequireRateLimiting(SecurityPolicies.EnrollmentRateLimit)
            .WithName("EnrollAgent")
            .WithSummary("Зарегистрировать Windows Agent одноразовым кодом");
        group.MapPost("/heartbeat", HeartbeatAsync)
            .RequireAuthorization(AgentAuthenticationDefaults.Policy)
            .WithName("AgentHeartbeat")
            .WithSummary("Принять heartbeat от Agent");
        group.MapGet("/operators", GetOperatorsAsync)
            .RequireAuthorization(AgentAuthenticationDefaults.Policy)
            .WithName("GetAgentOperators")
            .WithSummary("Получить список монтажёров для выбора");
        group.MapPost("/operator-session", StartOperatorSessionAsync)
            .RequireAuthorization(AgentAuthenticationDefaults.Policy)
            .RequireRateLimiting(SecurityPolicies.OperatorPinRateLimit)
            .WithName("StartAgentOperatorSession")
            .WithSummary("Подтвердить монтажёра PIN-кодом до следующего 06:00");
        group.MapGet("/config", GetConfigurationAsync)
            .RequireAuthorization(AgentAuthenticationDefaults.Policy)
            .WithName("GetAgentConfiguration")
            .WithSummary("Получить конфигурацию Agent");

        return endpoints;
    }

    private static async Task<IResult> EnrollAsync(
        AgentEnrollmentRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AgentCredentialService credentialService,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateEnrollment(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var tokenHash = AgentCredentialService.Hash(request.EnrollmentToken.Trim());
        var enrollmentToken = await dbContext.AgentEnrollmentTokens
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (enrollmentToken is null || enrollmentToken.UsedAtUtc is not null ||
            enrollmentToken.ExpiresAtUtc <= now)
        {
            return InvalidEnrollmentToken();
        }

        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            item => item.Id == enrollmentToken.EmployeeId,
            cancellationToken);
        if (employee is null || !employee.IsActive)
        {
            return InvalidEnrollmentToken();
        }

        var normalizedMachineName = request.MachineName.Trim().ToUpperInvariant();
        var computer = await dbContext.Computers.SingleOrDefaultAsync(
            item => item.EmployeeId == employee.Id && item.NormalizedName == normalizedMachineName,
            cancellationToken);
        if (computer is null)
        {
            computer = new Computer
            {
                EmployeeId = employee.Id,
                Name = request.MachineName.Trim(),
                NormalizedName = normalizedMachineName,
                OperatingSystem = request.OperatingSystem.Trim(),
                AgentVersion = request.AgentVersion.Trim(),
                LastIpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                LastOnlineAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Computers.Add(computer);
        }
        else
        {
            computer.Name = request.MachineName.Trim();
            computer.OperatingSystem = request.OperatingSystem.Trim();
            computer.AgentVersion = request.AgentVersion.Trim();
            computer.LastIpAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            computer.LastOnlineAtUtc = now;
            computer.IsRevoked = false;
            computer.UpdatedAtUtc = now;
        }

        var agent = await dbContext.Agents.SingleOrDefaultAsync(
            item => item.ComputerId == computer.Id,
            cancellationToken);
        if (agent is null)
        {
            agent = new AgentDevice
            {
                ComputerId = computer.Id,
                Status = AgentStatus.Online,
                InstalledVersion = request.AgentVersion.Trim(),
                EnrolledAtUtc = now,
                LastSeenAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Agents.Add(agent);
        }
        else
        {
            agent.Status = AgentStatus.Online;
            agent.InstalledVersion = request.AgentVersion.Trim();
            agent.EnrolledAtUtc = now;
            agent.LastSeenAtUtc = now;
            agent.RevokedAtUtc = null;
            agent.UpdatedAtUtc = now;

            var oldCredentials = await dbContext.AgentCredentials
                .Where(item => item.AgentId == agent.Id && item.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var oldCredential in oldCredentials)
            {
                oldCredential.RevokedAtUtc = now;
                oldCredential.UpdatedAtUtc = now;
            }
        }

        var credential = credentialService.Create(agent.Id);
        dbContext.AgentCredentials.Add(credential.Entity);
        enrollmentToken.UsedAtUtc = now;
        enrollmentToken.ConsumedByAgentId = agent.Id;
        enrollmentToken.UpdatedAtUtc = now;
        auditWriter.Add(
            httpContext,
            "agent.enrolled",
            "Agent",
            agent.Id.ToString(),
            details: new { employeeId = employee.Id, computerId = computer.Id, computer.Name });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return InvalidEnrollmentToken();
        }

        return Results.Ok(new AgentEnrollmentResponse(
            agent.Id,
            employee.Id,
            computer.Id,
            credential.AccessToken,
            credential.Entity.ExpiresAtUtc!.Value));
    }

    private static async Task<IResult> GetOperatorsAsync(
        Guid? operatorSessionId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        OperatorSessionService operatorSessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var agentId = httpContext.User.GetRequiredAgentId();
        var computerId = httpContext.User.GetRequiredComputerId();
        var now = timeProvider.GetUtcNow();
        AgentOperatorSessionResponse? current = null;
        if (operatorSessionId.HasValue)
        {
            var session = await operatorSessions.FindValidSessionAsync(
                operatorSessionId.Value,
                agentId,
                computerId,
                now,
                cancellationToken);
            if (session is not null)
            {
                var employee = await dbContext.Employees.AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == session.EmployeeId && item.IsActive,
                        cancellationToken);
                if (employee is not null)
                {
                    current = new AgentOperatorSessionResponse(
                        session.Id,
                        employee.Id,
                        employee.Name,
                        session.StartedAtUtc,
                        session.ExpiresAtUtc);
                }
            }
        }

        var employees = await dbContext.Employees.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new AgentOperatorOption(item.Id, item.Name))
            .ToListAsync(cancellationToken);
        return Results.Ok(new AgentOperatorOptionsResponse(employees, current, now));
    }

    private static async Task<IResult> StartOperatorSessionAsync(
        StartAgentOperatorSessionRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeOperatorPinService pinService,
        OperatorSessionService operatorSessions,
        ActivityAggregationService activityAggregator,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty || string.IsNullOrEmpty(request.Pin) || request.Pin.Length != 4 ||
            request.Pin.Any(character => character is < '0' or > '9'))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Pin)] = ["Выберите сотрудника и введите PIN из четырёх цифр."],
            });
        }

        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            item => item.Id == request.EmployeeId && item.IsActive,
            cancellationToken);
        if (employee is null || !pinService.Verify(employee.OperatorPinProtected, request.Pin))
        {
            return Results.Json(
                new { message = "Неверный сотрудник или PIN-код." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var agentId = httpContext.User.GetRequiredAgentId();
        var computerId = httpContext.User.GetRequiredComputerId();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var session = await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await activityAggregator.AcquireComputerLockAsync(computerId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var openSessions = await dbContext.AgentOperatorSessions
                .Where(item => item.AgentId == agentId && item.EndedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var openSession in openSessions)
            {
                openSession.EndedAtUtc = openSession.ExpiresAtUtc < now
                    ? openSession.ExpiresAtUtc
                    : now;
                openSession.UpdatedAtUtc = now;
            }

            if (openSessions.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var createdSession = new AgentOperatorSession
            {
                AgentId = agentId,
                ComputerId = computerId,
                EmployeeId = employee.Id,
                StartedAtUtc = now,
                ExpiresAtUtc = operatorSessions.GetNextSelectionBoundaryUtc(now),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.AgentOperatorSessions.Add(createdSession);
            auditWriter.Add(
                httpContext,
                "agent.operator.selected",
                "AgentOperatorSession",
                createdSession.Id.ToString(),
                details: new { agentId, computerId, employeeId = employee.Id, createdSession.ExpiresAtUtc });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return createdSession;
        });
        return Results.Ok(new AgentOperatorSessionResponse(
            session.Id,
            employee.Id,
            employee.Name,
            session.StartedAtUtc,
            session.ExpiresAtUtc));
    }

    private static async Task<IResult> HeartbeatAsync(
        HeartbeatRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        ActivityAggregationService activityAggregator,
        OperatorSessionService operatorSessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateHeartbeat(request);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var agentId = httpContext.User.GetRequiredAgentId();
        var legacyEmployeeId = httpContext.User.GetRequiredEmployeeId();
        var computerId = httpContext.User.GetRequiredComputerId();
        if (request.AgentId != agentId || request.ComputerId != computerId)
        {
            return Results.Json(
                new { message = "Идентификаторы heartbeat не соответствуют device token." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.OperatorSessionId.HasValue)
        {
            var operatorSession = await operatorSessions.FindValidSessionAsync(
                request.OperatorSessionId.Value,
                agentId,
                computerId,
                request.TimestampUtc,
                cancellationToken);
            if (operatorSession is null || operatorSession.EmployeeId != request.EmployeeId)
            {
                return OperatorSelectionRequired();
            }
        }
        else if (OperatorSessionService.RequiresOperatorSession(request.AgentVersion))
        {
            return OperatorSelectionRequired();
        }
        else if (request.EmployeeId != legacyEmployeeId)
        {
            return Results.Json(
                new { message = "Сотрудник heartbeat не соответствует старому device token." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!await dbContext.Employees.AsNoTracking().AnyAsync(
                item => item.Id == request.EmployeeId && item.IsActive,
                cancellationToken))
        {
            return request.OperatorSessionId.HasValue
                ? OperatorSelectionRequired()
                : Results.Json(
                    new { message = "Сотрудник старого device token деактивирован." },
                    statusCode: StatusCodes.Status403Forbidden);
        }

        var employeeId = request.EmployeeId;

        var now = timeProvider.GetUtcNow();
        if (request.TimestampUtc > now.AddMinutes(5))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TimestampUtc)] = ["Время heartbeat не может быть более чем на 5 минут в будущем."],
            });
        }

        var render = request.RenderTelemetry;
        var proxy = request.ProxyTelemetry;
        var useProxyTelemetry = request.MachineState == MachineState.Proxy;
        var processingProgram = useProxyTelemetry ? proxy?.Program : render?.Program;
        var processingStartedAtUtc = useProxyTelemetry ? proxy?.ProcessStartedAtUtc : render?.ProcessStartedAtUtc;
        var processingCpu = useProxyTelemetry ? proxy?.ProcessCpuPercent : render?.ProcessCpuPercent;
        var processingWorkingSet = useProxyTelemetry ? proxy?.ProcessWorkingSetBytes : render?.ProcessWorkingSetBytes;
        var processingIoRead = useProxyTelemetry
            ? proxy?.ProcessIoReadBytesPerSecond
            : render?.ProcessIoReadBytesPerSecond;
        var processingIoWrite = useProxyTelemetry
            ? proxy?.ProcessIoWriteBytesPerSecond
            : render?.ProcessIoWriteBytesPerSecond;
        var processingChildren = useProxyTelemetry ? proxy?.ChildProcessCount : render?.ChildProcessCount;
        var processingGpu = useProxyTelemetry ? null : render?.GpuLoadPercent;
        var processingFolder = useProxyTelemetry ? proxy?.OutputFolder : render?.OutputFolder;
        var processingFile = useProxyTelemetry ? proxy?.OutputFile : render?.OutputFile;
        var processingFileSize = useProxyTelemetry ? proxy?.OutputFileSizeBytes : render?.OutputFileSizeBytes;
        var processingConfidence = useProxyTelemetry ? proxy?.DetectionConfidence : render?.DetectionConfidence;
        var processingReason = useProxyTelemetry ? proxy?.DetectionReason : render?.DetectionReason;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await activityAggregator.AcquireComputerLockAsync(computerId, cancellationToken);
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO heartbeats (
                    id, event_id, agent_id, employee_id, computer_id,
                    agent_version, windows_user, machine_name, timestamp_utc,
                    human_state, machine_state, foreground_process,
                    foreground_executable_path, foreground_window_title,
                    idle_seconds, cpu_load_percent, memory_load_percent,
                    render_program, render_process_started_at_utc,
                    render_process_cpu_percent, render_process_working_set_bytes,
                    render_process_io_read_bytes_per_second, render_process_io_write_bytes_per_second,
                    render_child_process_count, render_gpu_load_percent,
                    render_output_folder, render_output_file, render_output_file_size_bytes,
                    render_detection_confidence, render_detection_reason,
                    created_at_utc, updated_at_utc)
                VALUES (
                    {Guid.NewGuid()}, {request.EventId}, {agentId}, {employeeId}, {computerId},
                    {request.AgentVersion.Trim()}, {request.WindowsUser.Trim()}, {request.MachineName.Trim()}, {request.TimestampUtc},
                    {request.HumanState.ToString()}, {request.MachineState.ToString()},
                    CAST({NormalizeOptional(request.ForegroundProcess)} AS varchar),
                    CAST({NormalizeOptional(request.ForegroundExecutablePath)} AS varchar),
                    CAST({NormalizeOptional(request.ForegroundWindowTitle)} AS varchar),
                    {request.IdleSeconds}, {request.CpuLoadPercent}, {request.MemoryLoadPercent},
                    CAST({NormalizeOptional(processingProgram)} AS varchar),
                    CAST({processingStartedAtUtc} AS timestamptz),
                    CAST({processingCpu} AS double precision),
                    CAST({processingWorkingSet} AS bigint),
                    CAST({processingIoRead} AS double precision),
                    CAST({processingIoWrite} AS double precision),
                    CAST({processingChildren} AS integer),
                    CAST({processingGpu} AS double precision),
                    CAST({NormalizeOptional(processingFolder)} AS varchar),
                    CAST({NormalizeOptional(processingFile)} AS varchar),
                    CAST({processingFileSize} AS bigint),
                    CAST({processingConfidence} AS smallint),
                    CAST({NormalizeOptional(processingReason)} AS varchar),
                    {now}, {now})
                ON CONFLICT (event_id) DO NOTHING;
                """, cancellationToken);

            if (inserted == 0)
            {
                var existingOwner = await dbContext.Heartbeats.AsNoTracking()
                    .Where(item => item.EventId == request.EventId)
                    .Select(item => new { item.AgentId, item.EmployeeId, item.ComputerId })
                    .SingleAsync(cancellationToken);
                if (existingOwner.AgentId != agentId || existingOwner.EmployeeId != employeeId ||
                    existingOwner.ComputerId != computerId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (Accepted: false, ConfigVersion: 0L);
                }
            }
            else
            {
                if (request.ScreenshotEvent is { } screenshotEvent)
                {
                    var eventPayload = JsonSerializer.Serialize(new
                    {
                        screenshotEvent.ForegroundProcess,
                        screenshotEvent.ForegroundWindowTitle,
                        reason = "privacy_exclusion",
                    });
                    await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO activity_events (
                            event_id, agent_id, employee_id, computer_id,
                            occurred_at_utc, received_at_utc, event_type, schema_version, payload)
                        VALUES (
                            {screenshotEvent.EventId}, {agentId}, {employeeId}, {computerId},
                            {screenshotEvent.TimestampUtc}, {now}, {screenshotEvent.EventType}, 1,
                            CAST({eventPayload} AS jsonb))
                        ON CONFLICT (event_id) DO NOTHING;
                        """, cancellationToken);
                }

                var timing = await activityAggregator.GetHeartbeatTimingAsync(cancellationToken);
                await activityAggregator.RebuildFromHeartbeatAsync(
                    employeeId,
                    computerId,
                    request.TimestampUtc,
                    timing,
                    cancellationToken);
            }

            var agent = await dbContext.Agents.SingleAsync(item => item.Id == agentId, cancellationToken);
            var computer = await dbContext.Computers.SingleAsync(item => item.Id == computerId, cancellationToken);
            agent.Status = AgentStatus.Online;
            agent.InstalledVersion = request.AgentVersion.Trim();
            agent.LastSeenAtUtc = now;
            agent.UpdatedAtUtc = now;
            computer.AgentVersion = request.AgentVersion.Trim();
            computer.LastHeartbeatAtUtc = now;
            computer.LastOnlineAtUtc = now;
            computer.LastIpAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            computer.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (Accepted: true, agent.ConfigVersion);
        });

        return outcome.Accepted
            ? Results.Ok(new HeartbeatResponse(request.EventId, now, outcome.ConfigVersion))
            : Results.Conflict(new { message = "UUID heartbeat уже принадлежит другому устройству." });
    }

    private static async Task<IResult> GetConfigurationAsync(
        Guid? operatorSessionId,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        OperatorSessionService operatorSessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var agentId = httpContext.User.GetRequiredAgentId();
        var employeeId = httpContext.User.GetRequiredEmployeeId();
        var computerId = httpContext.User.GetRequiredComputerId();
        if (operatorSessionId.HasValue)
        {
            var session = await operatorSessions.FindValidSessionAsync(
                operatorSessionId.Value,
                agentId,
                computerId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (session is null)
            {
                return OperatorSelectionRequired();
            }

            employeeId = session.EmployeeId;
        }
        var agent = await dbContext.Agents.AsNoTracking()
            .SingleAsync(item => item.Id == agentId, cancellationToken);
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleAsync(item => item.Id == employeeId, cancellationToken);
        var heartbeatSetting = await dbContext.SystemSettings.AsNoTracking()
            .SingleAsync(item => item.Key == "agent.heartbeatIntervalSeconds", cancellationToken);
        var heartbeatInterval = int.TryParse(heartbeatSetting.JsonValue, out var configuredInterval)
            ? Math.Clamp(configuredInterval, 15, 300)
            : 30;
        var screenshotSettingKeys = new[]
        {
            "screenshots.captureMode",
            "screenshots.maxWidth",
            "screenshots.jpegQuality",
        };
        var screenshotSettings = await dbContext.SystemSettings.AsNoTracking()
            .Where(item => screenshotSettingKeys.Contains(item.Key))
            .ToDictionaryAsync(item => item.Key, item => item.JsonValue, cancellationToken);
        var excludedProcesses = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsScreenshotExcluded)
            .OrderBy(item => item.ProcessName)
            .Select(item => item.ProcessName)
            .ToListAsync(cancellationToken);
        var screenshotConfiguration = new AgentScreenshotConfiguration(
            employee.ScreenshotEnabled,
            employee.ScreenshotIntervalMinutes,
            ParseCaptureMode(screenshotSettings.GetValueOrDefault("screenshots.captureMode")),
            ParseInteger(screenshotSettings.GetValueOrDefault("screenshots.maxWidth"), 1_600, 320, 7_680),
            ParseInteger(screenshotSettings.GetValueOrDefault("screenshots.jpegQuality"), 60, 55, 65),
            excludedProcesses);
        var renderRules = await dbContext.RenderRules.AsNoTracking()
            .Where(item => item.Type == ProcessingType.Render && item.IsEnabled)
            .OrderBy(item => item.Name)
            .Select(item => new AgentRenderRule(
                item.Id,
                item.Name,
                item.ProcessName,
                item.CpuThresholdPercent,
                item.DiskWriteThresholdBytesPerSecond,
                item.ConfirmationSeconds,
                item.FinishTimeoutSeconds,
                item.MinimumConfidence,
                item.FileNamePatterns))
            .ToListAsync(cancellationToken);
        var renderFolders = await dbContext.WatchedFolders.AsNoTracking()
            .Where(item => item.ComputerId == agent.ComputerId && item.Type == WatchedFolderType.Render && item.IsEnabled)
            .OrderBy(item => item.PathPattern)
            .Select(item => new AgentWatchedFolder(
                item.Id,
                item.PathPattern,
                item.Extensions,
                item.FileNamePatterns))
            .ToListAsync(cancellationToken);
        var proxyRules = await dbContext.RenderRules.AsNoTracking()
            .Where(item => item.Type == ProcessingType.Proxy && item.IsEnabled)
            .OrderBy(item => item.Name)
            .Select(item => new AgentProxyRule(
                item.Id,
                item.Name,
                item.ProcessName,
                item.CpuThresholdPercent,
                item.DiskWriteThresholdBytesPerSecond,
                item.ConfirmationSeconds,
                item.FinishTimeoutSeconds,
                item.MinimumConfidence,
                item.FileNamePatterns))
            .ToListAsync(cancellationToken);
        var proxyFolders = await dbContext.WatchedFolders.AsNoTracking()
            .Where(item => item.ComputerId == agent.ComputerId && item.Type == WatchedFolderType.Proxy && item.IsEnabled)
            .OrderBy(item => item.PathPattern)
            .Select(item => new AgentWatchedFolder(
                item.Id,
                item.PathPattern,
                item.Extensions,
                item.FileNamePatterns))
            .ToListAsync(cancellationToken);
        var latestRenderRuleUpdate = await dbContext.RenderRules.AsNoTracking()
            .Where(item => item.Type == ProcessingType.Render)
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var latestRenderFolderUpdate = await dbContext.WatchedFolders.AsNoTracking()
            .Where(item => item.ComputerId == agent.ComputerId && item.Type == WatchedFolderType.Render)
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var latestProxyRuleUpdate = await dbContext.RenderRules.AsNoTracking()
            .Where(item => item.Type == ProcessingType.Proxy)
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var latestProxyFolderUpdate = await dbContext.WatchedFolders.AsNoTracking()
            .Where(item => item.ComputerId == agent.ComputerId && item.Type == WatchedFolderType.Proxy)
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var latestScreenshotSettingUpdate = await dbContext.SystemSettings.AsNoTracking()
            .Where(item => screenshotSettingKeys.Contains(item.Key))
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var latestPrivacyRuleUpdate = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsScreenshotExcluded)
            .MaxAsync(item => (DateTimeOffset?)item.UpdatedAtUtc, cancellationToken);
        var configVersion = Math.Max(
            Math.Max(agent.ConfigVersion, heartbeatSetting.Version),
            employee.UpdatedAtUtc.ToUnixTimeMilliseconds());
        if (latestRenderRuleUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestRenderRuleUpdate.Value.ToUnixTimeMilliseconds());
        }

        if (latestRenderFolderUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestRenderFolderUpdate.Value.ToUnixTimeMilliseconds());
        }

        if (latestProxyRuleUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestProxyRuleUpdate.Value.ToUnixTimeMilliseconds());
        }

        if (latestProxyFolderUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestProxyFolderUpdate.Value.ToUnixTimeMilliseconds());
        }

        if (latestScreenshotSettingUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestScreenshotSettingUpdate.Value.ToUnixTimeMilliseconds());
        }

        if (latestPrivacyRuleUpdate.HasValue)
        {
            configVersion = Math.Max(configVersion, latestPrivacyRuleUpdate.Value.ToUnixTimeMilliseconds());
        }

        return Results.Ok(new AgentConfigurationResponse(
            configVersion,
            heartbeatInterval,
            employee.IdleThresholdSeconds,
            renderRules,
            renderFolders,
            proxyRules,
            proxyFolders,
            screenshotConfiguration));
    }

    private static Dictionary<string, string[]>? ValidateEnrollment(AgentEnrollmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRequired(request.EnrollmentToken, 512, nameof(request.EnrollmentToken), "Код регистрации", errors);
        ValidateRequired(request.MachineName, 255, nameof(request.MachineName), "Имя компьютера", errors);
        ValidateRequired(request.WindowsUser, 255, nameof(request.WindowsUser), "Пользователь Windows", errors);
        ValidateRequired(request.OperatingSystem, 255, nameof(request.OperatingSystem), "Версия Windows", errors);
        ValidateRequired(request.AgentVersion, 50, nameof(request.AgentVersion), "Версия Agent", errors);
        return errors.Count == 0 ? null : errors;
    }

    private static Dictionary<string, string[]>? ValidateHeartbeat(HeartbeatRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EventId == Guid.Empty || request.AgentId == Guid.Empty ||
            request.EmployeeId == Guid.Empty || request.ComputerId == Guid.Empty)
        {
            errors["identifiers"] = ["Идентификаторы heartbeat не могут быть пустыми."];
        }

        ValidateRequired(request.AgentVersion, 50, nameof(request.AgentVersion), "Версия Agent", errors);
        ValidateRequired(request.WindowsUser, 255, nameof(request.WindowsUser), "Пользователь Windows", errors);
        ValidateRequired(request.MachineName, 255, nameof(request.MachineName), "Имя компьютера", errors);
        ValidateOptional(request.ForegroundProcess, 255, nameof(request.ForegroundProcess), errors);
        ValidateOptional(request.ForegroundExecutablePath, 2_000, nameof(request.ForegroundExecutablePath), errors);
        ValidateOptional(request.ForegroundWindowTitle, 2_000, nameof(request.ForegroundWindowTitle), errors);

        if (request.IdleSeconds < 0)
        {
            errors[nameof(request.IdleSeconds)] = ["Время простоя не может быть отрицательным."];
        }

        if (request.CpuLoadPercent is < 0 or > 100 || request.MemoryLoadPercent is < 0 or > 100)
        {
            errors["systemLoad"] = ["Загрузка CPU и памяти должна быть от 0 до 100 процентов."];
        }

        var render = request.RenderTelemetry;
        var proxy = request.ProxyTelemetry;
        if (request.MachineState == MachineState.Render && render is null)
        {
            errors[nameof(request.RenderTelemetry)] = ["Для состояния Render обязательна диагностическая телеметрия."];
        }

        if (request.MachineState == MachineState.Proxy && proxy is null)
        {
            errors[nameof(request.ProxyTelemetry)] = ["Для состояния Proxy обязательна диагностическая телеметрия."];
        }

        if (render is not null)
        {
            ValidateRequired(render.Program, 255, nameof(render.Program), "Render process", errors);
            ValidateRequired(
                render.DetectionReason,
                2_000,
                nameof(render.DetectionReason),
                "Причина определения Render",
                errors);
            ValidateOptional(render.OutputFolder, 2_000, nameof(render.OutputFolder), errors);
            ValidateOptional(render.OutputFile, 2_000, nameof(render.OutputFile), errors);
            if (render.ProcessCpuPercent is < 0 or > 100 ||
                render.GpuLoadPercent is < 0 or > 100)
            {
                errors["renderLoad"] = ["Загрузка Render CPU/GPU должна быть от 0 до 100 процентов."];
            }

            if (render.ProcessWorkingSetBytes < 0 || render.ProcessIoReadBytesPerSecond < 0 ||
                render.ProcessIoWriteBytesPerSecond < 0 || render.ChildProcessCount < 0 ||
                render.OutputFileSizeBytes < 0)
            {
                errors["renderMetrics"] = ["Счётчики процесса и файла не могут быть отрицательными."];
            }
        }


        if (proxy is not null)
        {
            ValidateRequired(proxy.Program, 255, nameof(proxy.Program), "Proxy process", errors);
            ValidateRequired(
                proxy.DetectionReason,
                2_000,
                nameof(proxy.DetectionReason),
                "Причина определения Proxy",
                errors);
            ValidateOptional(proxy.OutputFolder, 2_000, nameof(proxy.OutputFolder), errors);
            ValidateOptional(proxy.OutputFile, 2_000, nameof(proxy.OutputFile), errors);
            if (proxy.ProcessCpuPercent is < 0 or > 100)
            {
                errors["proxyLoad"] = ["Загрузка Proxy CPU должна быть от 0 до 100 процентов."];
            }

            if (proxy.ProcessWorkingSetBytes < 0 || proxy.ProcessIoReadBytesPerSecond < 0 ||
                proxy.ProcessIoWriteBytesPerSecond < 0 || proxy.ChildProcessCount < 0 ||
                proxy.OutputFileSizeBytes < 0)
            {
                errors["proxyMetrics"] = ["Счётчики Proxy-процесса и файла не могут быть отрицательными."];
            }
        }

        if (request.ScreenshotEvent is { } screenshotEvent)
        {
            if (screenshotEvent.EventId == Guid.Empty)
            {
                errors[nameof(screenshotEvent.EventId)] = ["UUID события пропуска скриншота не может быть пустым."];
            }

            if (!string.Equals(
                    screenshotEvent.EventType,
                    "SCREENSHOT_SKIPPED_PRIVACY",
                    StringComparison.Ordinal))
            {
                errors[nameof(screenshotEvent.EventType)] = ["Неизвестный тип события скриншота."];
            }

            if (screenshotEvent.TimestampUtc > request.TimestampUtc.AddMinutes(5))
            {
                errors[nameof(screenshotEvent.TimestampUtc)] = ["Время события скриншота находится в будущем."];
            }

            ValidateOptional(
                screenshotEvent.ForegroundProcess,
                255,
                nameof(screenshotEvent.ForegroundProcess),
                errors);
            ValidateOptional(
                screenshotEvent.ForegroundWindowTitle,
                2_000,
                nameof(screenshotEvent.ForegroundWindowTitle),
                errors);
        }

        return errors.Count == 0 ? null : errors;
    }

    private static void ValidateRequired(
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

    private static void ValidateOptional(
        string? value,
        int maximumLength,
        string key,
        IDictionary<string, string[]> errors)
    {
        if (value?.Length > maximumLength)
        {
            errors[key] = [$"Значение не должно превышать {maximumLength} символов."];
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseInteger(string? value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;

    private static ScreenshotCaptureMode ParseCaptureMode(string? jsonValue)
    {
        try
        {
            var value = JsonSerializer.Deserialize<string>(jsonValue ?? "null");
            return Enum.TryParse<ScreenshotCaptureMode>(value, true, out var parsed)
                ? parsed
                : ScreenshotCaptureMode.PrimaryMonitor;
        }
        catch (JsonException)
        {
            return ScreenshotCaptureMode.PrimaryMonitor;
        }
    }

    private static IResult InvalidEnrollmentToken() => Results.Json(
        new { message = "Код регистрации недействителен, использован или истёк." },
        statusCode: StatusCodes.Status401Unauthorized);

    private static IResult OperatorSelectionRequired() => Results.Json(
        new { message = "Требуется выбрать монтажёра и подтвердить PIN-код." },
        statusCode: StatusCodes.Status428PreconditionRequired);
}
