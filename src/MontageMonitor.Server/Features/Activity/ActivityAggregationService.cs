using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Activity;

public sealed class ActivityAggregationService(
    MonitoringDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string HeartbeatIntervalSetting = "agent.heartbeatIntervalSeconds";

    public async Task<HeartbeatTiming> GetHeartbeatTimingAsync(CancellationToken cancellationToken)
    {
        var jsonValue = await dbContext.SystemSettings.AsNoTracking()
            .Where(item => item.Key == HeartbeatIntervalSetting)
            .Select(item => item.JsonValue)
            .SingleAsync(cancellationToken);
        var interval = int.TryParse(jsonValue, out var configuredInterval)
            ? Math.Clamp(configuredInterval, 15, 300)
            : 30;

        return new HeartbeatTiming(interval, Math.Max(90, interval * 3));
    }

    public Task AcquireComputerLockAsync(Guid computerId, CancellationToken cancellationToken)
    {
        var lockKey = BitConverter.ToInt64(computerId.ToByteArray(), 0);
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey});",
            cancellationToken);
    }

    public async Task RebuildFromHeartbeatAsync(
        Guid employeeId,
        Guid computerId,
        DateTimeOffset heartbeatTimestampUtc,
        HeartbeatTiming timing,
        CancellationToken cancellationToken)
    {
        var predecessor = await dbContext.Heartbeats.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.TimestampUtc < heartbeatTimestampUtc)
            .OrderByDescending(item => item.TimestampUtc)
            .Select(item => (DateTimeOffset?)item.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var anchor = predecessor ?? heartbeatTimestampUtc;

        var applicationStart = await FindApplicationSessionStartAsync(computerId, anchor, cancellationToken);
        var humanStart = await FindHumanSessionStartAsync(computerId, anchor, cancellationToken);
        var machineStart = await FindMachineSessionStartAsync(computerId, anchor, cancellationToken);
        var renderStart = await FindRenderSessionStartAsync(computerId, anchor, cancellationToken);
        var rebuildStart = new DateTimeOffset?[] { anchor, applicationStart, humanStart, machineStart, renderStart }
            .Where(item => item.HasValue)
            .Min()!.Value;

        await dbContext.ApplicationSessions
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc >= rebuildStart)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.HumanStateSessions
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc >= rebuildStart)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MachineStateSessions
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc >= rebuildStart)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RenderSessions
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc >= rebuildStart)
            .ExecuteDeleteAsync(cancellationToken);

        var rawSamples = await dbContext.Heartbeats.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.TimestampUtc >= rebuildStart)
            .OrderBy(item => item.TimestampUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.EventId)
            .Select(item => new ActivitySample(
                item.TimestampUtc,
                item.HumanState,
                item.MachineState,
                item.ForegroundProcess,
                item.ForegroundExecutablePath,
                item.ForegroundWindowTitle,
                item.RenderProgram,
                item.RenderProcessStartedAtUtc,
                item.RenderProcessCpuPercent,
                item.RenderProcessWorkingSetBytes,
                item.RenderProcessIoReadBytesPerSecond,
                item.RenderProcessIoWriteBytesPerSecond,
                item.RenderChildProcessCount,
                item.RenderGpuLoadPercent,
                item.RenderOutputFolder,
                item.RenderOutputFile,
                item.RenderOutputFileSizeBytes,
                item.RenderDetectionConfidence,
                item.RenderDetectionReason,
                item.EmployeeId))
            .ToListAsync(cancellationToken);

        // При конфликтующих событиях с одинаковым временем последнее полученное состояние побеждает.
        var samples = rawSamples
            .GroupBy(item => item.TimestampUtc)
            .Select(group => group.Last())
            .ToList();
        if (samples.Count == 0)
        {
            return;
        }

        var classifications = await dbContext.ApplicationRules.AsNoTracking()
            .Where(item => item.IsEnabled)
            .ToDictionaryAsync(
                item => item.NormalizedProcessName,
                item => item.Classification,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
        var sessions = ActivitySessionBuilder.Build(
            computerId,
            samples,
            classifications,
            timing,
            timeProvider.GetUtcNow());

        dbContext.ApplicationSessions.AddRange(sessions.Applications);
        dbContext.HumanStateSessions.AddRange(sessions.HumanStates);
        dbContext.MachineStateSessions.AddRange(sessions.MachineStates);
        dbContext.RenderSessions.AddRange(sessions.Renders);
    }

    public async Task MarkStaleAgentsOfflineAsync(CancellationToken cancellationToken)
    {
        var timing = await GetHeartbeatTimingAsync(cancellationToken);
        var staleBefore = timeProvider.GetUtcNow().AddSeconds(-timing.OfflineAfterSeconds);
        var staleAgents = await dbContext.Agents.AsNoTracking()
            .Where(item => item.Status == AgentStatus.Online && item.LastSeenAtUtc < staleBefore)
            .Select(item => new { item.Id, item.ComputerId })
            .ToListAsync(cancellationToken);

        foreach (var staleAgent in staleAgents)
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await AcquireComputerLockAsync(staleAgent.ComputerId, cancellationToken);

                var agent = await dbContext.Agents
                    .SingleAsync(item => item.Id == staleAgent.Id, cancellationToken);
                if (agent.Status != AgentStatus.Online || agent.LastSeenAtUtc >= staleBefore)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return;
                }

                var lastHeartbeat = await dbContext.Heartbeats.AsNoTracking()
                    .Where(item => item.ComputerId == staleAgent.ComputerId)
                    .OrderByDescending(item => item.TimestampUtc)
                    .Select(item => new { item.EmployeeId, item.TimestampUtc })
                    .FirstOrDefaultAsync(cancellationToken);
                if (lastHeartbeat is null)
                {
                    agent.Status = AgentStatus.Offline;
                    agent.UpdatedAtUtc = timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                var observedNow = timeProvider.GetUtcNow();
                var expectedOfflineAt = lastHeartbeat.TimestampUtc.AddSeconds(timing.IntervalSeconds);
                var offlineStartedAt = expectedOfflineAt < observedNow ? expectedOfflineAt : observedNow;
                await CloseOpenSessionsAsync(staleAgent.ComputerId, offlineStartedAt, cancellationToken);

                dbContext.HumanStateSessions.Add(new HumanStateSession
                {
                    EmployeeId = lastHeartbeat.EmployeeId,
                    ComputerId = staleAgent.ComputerId,
                    StartedAtUtc = offlineStartedAt,
                    State = HumanState.Offline,
                    CreatedAtUtc = observedNow,
                    UpdatedAtUtc = observedNow,
                });
                agent.Status = AgentStatus.Offline;
                agent.UpdatedAtUtc = observedNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
    }

    private async Task CloseOpenSessionsAsync(
        Guid computerId,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var application = await dbContext.ApplicationSessions
            .SingleOrDefaultAsync(
                item => item.ComputerId == computerId && item.EndedAtUtc == null,
                cancellationToken);
        if (application is not null)
        {
            application.EndedAtUtc = Max(application.StartedAtUtc, endedAtUtc);
            application.UpdatedAtUtc = now;
        }

        var human = await dbContext.HumanStateSessions
            .SingleOrDefaultAsync(
                item => item.ComputerId == computerId && item.EndedAtUtc == null,
                cancellationToken);
        if (human is not null)
        {
            human.EndedAtUtc = Max(human.StartedAtUtc, endedAtUtc);
            human.UpdatedAtUtc = now;
        }

        var machine = await dbContext.MachineStateSessions
            .SingleOrDefaultAsync(
                item => item.ComputerId == computerId && item.EndedAtUtc == null,
                cancellationToken);
        if (machine is not null)
        {
            machine.EndedAtUtc = Max(machine.StartedAtUtc, endedAtUtc);
            machine.UpdatedAtUtc = now;
        }

        var render = await dbContext.RenderSessions
            .SingleOrDefaultAsync(
                item => item.ComputerId == computerId && item.EndedAtUtc == null,
                cancellationToken);
        if (render is not null)
        {
            render.EndedAtUtc = Max(render.StartedAtUtc, endedAtUtc);
            render.UpdatedAtUtc = now;
        }
    }

    private Task<DateTimeOffset?> FindApplicationSessionStartAsync(
        Guid computerId,
        DateTimeOffset anchor,
        CancellationToken cancellationToken) =>
        dbContext.ApplicationSessions.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc <= anchor &&
                           (item.EndedAtUtc == null || item.EndedAtUtc >= anchor))
            .Select(item => (DateTimeOffset?)item.StartedAtUtc)
            .MinAsync(cancellationToken);

    private Task<DateTimeOffset?> FindHumanSessionStartAsync(
        Guid computerId,
        DateTimeOffset anchor,
        CancellationToken cancellationToken) =>
        dbContext.HumanStateSessions.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc <= anchor &&
                           (item.EndedAtUtc == null || item.EndedAtUtc >= anchor))
            .Select(item => (DateTimeOffset?)item.StartedAtUtc)
            .MinAsync(cancellationToken);

    private Task<DateTimeOffset?> FindMachineSessionStartAsync(
        Guid computerId,
        DateTimeOffset anchor,
        CancellationToken cancellationToken) =>
        dbContext.MachineStateSessions.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc <= anchor &&
                           (item.EndedAtUtc == null || item.EndedAtUtc >= anchor))
            .Select(item => (DateTimeOffset?)item.StartedAtUtc)
            .MinAsync(cancellationToken);

    private Task<DateTimeOffset?> FindRenderSessionStartAsync(
        Guid computerId,
        DateTimeOffset anchor,
        CancellationToken cancellationToken) =>
        dbContext.RenderSessions.AsNoTracking()
            .Where(item => item.ComputerId == computerId && item.StartedAtUtc <= anchor &&
                           (item.EndedAtUtc == null || item.EndedAtUtc >= anchor))
            .Select(item => (DateTimeOffset?)item.StartedAtUtc)
            .MinAsync(cancellationToken);

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;
}

public sealed record HeartbeatTiming(int IntervalSeconds, int OfflineAfterSeconds);

public sealed record ActivitySample(
    DateTimeOffset TimestampUtc,
    HumanState HumanState,
    MachineState MachineState,
    string? ProcessName,
    string? ExecutablePath,
    string? WindowTitle,
    string? RenderProgram,
    DateTimeOffset? RenderProcessStartedAtUtc,
    double? RenderProcessCpuPercent,
    long? RenderProcessWorkingSetBytes,
    double? RenderProcessIoReadBytesPerSecond,
    double? RenderProcessIoWriteBytesPerSecond,
    int? RenderChildProcessCount,
    double? RenderGpuLoadPercent,
    string? RenderOutputFolder,
    string? RenderOutputFile,
    long? RenderOutputFileSizeBytes,
    byte? RenderDetectionConfidence,
    string? RenderDetectionReason,
    Guid EmployeeId = default);

public sealed record AggregatedActivitySessions(
    IReadOnlyList<ApplicationSession> Applications,
    IReadOnlyList<HumanStateSession> HumanStates,
    IReadOnlyList<MachineStateSession> MachineStates,
    IReadOnlyList<RenderSession> Renders);

public static class ActivitySessionBuilder
{
    public static AggregatedActivitySessions Build(
        Guid employeeId,
        Guid computerId,
        IReadOnlyList<ActivitySample> samples,
        IReadOnlyDictionary<string, ApplicationClassification> classifications,
        HeartbeatTiming timing,
        DateTimeOffset createdAtUtc)
        => Build(
            computerId,
            samples.Select(item => item with { EmployeeId = employeeId }).ToList(),
            classifications,
            timing,
            createdAtUtc);

    public static AggregatedActivitySessions Build(
        Guid computerId,
        IReadOnlyList<ActivitySample> samples,
        IReadOnlyDictionary<string, ApplicationClassification> classifications,
        HeartbeatTiming timing,
        DateTimeOffset createdAtUtc)
    {
        var applications = new List<ApplicationSession>();
        var humanStates = new List<HumanStateSession>();
        var machineStates = new List<MachineStateSession>();
        var renders = new List<RenderSession>();
        var renderStatistics = new Dictionary<Guid, RenderStatistics>();
        if (samples.Count == 0)
        {
            return new(applications, humanStates, machineStates, renders);
        }

        var previous = samples[0];
        var application = OpenApplication(previous);
        var human = OpenHuman(previous);
        var machine = OpenMachine(previous);
        var render = OpenRender(previous);

        foreach (var sample in samples.Skip(1))
        {
            var gap = sample.TimestampUtc - previous.TimestampUtc;
            var operatorChanged = sample.EmployeeId != previous.EmployeeId;
            if (gap.TotalSeconds > timing.OfflineAfterSeconds || operatorChanged)
            {
                var hasOfflineGap = gap.TotalSeconds > timing.OfflineAfterSeconds;
                var onlineEndedAt = hasOfflineGap
                    ? previous.TimestampUtc.AddSeconds(timing.IntervalSeconds)
                    : sample.TimestampUtc;
                Close(application, onlineEndedAt);
                Close(human, onlineEndedAt);
                Close(machine, onlineEndedAt);
                Close(render, onlineEndedAt);
                FinalizeRender(render);

                if (hasOfflineGap)
                {
                    humanStates.Add(new HumanStateSession
                    {
                        EmployeeId = previous.EmployeeId,
                        ComputerId = computerId,
                        StartedAtUtc = onlineEndedAt,
                        EndedAtUtc = sample.TimestampUtc,
                        State = HumanState.Offline,
                        CreatedAtUtc = createdAtUtc,
                        UpdatedAtUtc = createdAtUtc,
                    });
                }

                application = OpenApplication(sample);
                human = OpenHuman(sample);
                machine = OpenMachine(sample);
                render = OpenRender(sample);
            }
            else
            {
                if (!SameApplication(previous, sample))
                {
                    Close(application, sample.TimestampUtc);
                    application = OpenApplication(sample);
                }

                if (previous.HumanState != sample.HumanState)
                {
                    Close(human, sample.TimestampUtc);
                    human = OpenHuman(sample);
                }

                if (previous.MachineState != sample.MachineState)
                {
                    Close(machine, sample.TimestampUtc);
                    machine = OpenMachine(sample);
                }
                else
                {
                    UpdateMachine(machine, sample);
                }

                if (!SameRender(previous, sample))
                {
                    Close(render, sample.TimestampUtc);
                    FinalizeRender(render);
                    render = OpenRender(sample);
                }
                else
                {
                    UpdateRender(render, sample);
                }
            }

            previous = sample;
        }

        FinalizeRender(render);
        return new(applications, humanStates, machineStates, renders);

        ApplicationSession? OpenApplication(ActivitySample sample)
        {
            var processName = Normalize(sample.ProcessName);
            if (processName is null)
            {
                return null;
            }

            var normalizedProcess = processName.ToUpperInvariant();
            var classification = classifications.TryGetValue(normalizedProcess, out var configured)
                ? configured
                : ApplicationClassification.Neutral;
            var session = new ApplicationSession
            {
                EmployeeId = sample.EmployeeId,
                ComputerId = computerId,
                StartedAtUtc = sample.TimestampUtc,
                ProcessName = processName,
                ExecutablePath = Normalize(sample.ExecutablePath),
                WindowTitle = Normalize(sample.WindowTitle),
                Classification = classification,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
            };
            applications.Add(session);
            return session;
        }

        HumanStateSession OpenHuman(ActivitySample sample)
        {
            var session = new HumanStateSession
            {
                EmployeeId = sample.EmployeeId,
                ComputerId = computerId,
                StartedAtUtc = sample.TimestampUtc,
                State = sample.HumanState,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
            };
            humanStates.Add(session);
            return session;
        }

        MachineStateSession OpenMachine(ActivitySample sample)
        {
            var isProcessing = sample.MachineState is MachineState.Render or MachineState.Proxy;
            var session = new MachineStateSession
            {
                EmployeeId = sample.EmployeeId,
                ComputerId = computerId,
                StartedAtUtc = sample.TimestampUtc,
                State = sample.MachineState,
                DetectionConfidence = isProcessing ? sample.RenderDetectionConfidence ?? 0 : (byte)100,
                DetectionReason = isProcessing
                    ? Normalize(sample.RenderDetectionReason) ?? "Машинная работа определена Agent"
                    : "Состояние из heartbeat Agent",
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
            };
            machineStates.Add(session);
            return session;
        }

        static void UpdateMachine(MachineStateSession session, ActivitySample sample)
        {
            if (sample.MachineState is not (MachineState.Render or MachineState.Proxy))
            {
                return;
            }

            session.DetectionConfidence = Math.Max(
                session.DetectionConfidence,
                sample.RenderDetectionConfidence ?? 0);
            session.DetectionReason = Normalize(sample.RenderDetectionReason) ?? session.DetectionReason;
        }

        RenderSession? OpenRender(ActivitySample sample)
        {
            if (sample.MachineState is not (MachineState.Render or MachineState.Proxy) ||
                Normalize(sample.RenderProgram) is not { } program)
            {
                return null;
            }

            var session = new RenderSession
            {
                EmployeeId = sample.EmployeeId,
                ComputerId = computerId,
                Type = sample.MachineState == MachineState.Proxy ? ProcessingType.Proxy : ProcessingType.Render,
                Program = program,
                StartedAtUtc = sample.TimestampUtc,
                OutputFolder = Normalize(sample.RenderOutputFolder),
                OutputFile = Normalize(sample.RenderOutputFile),
                FileSizeBytes = sample.RenderOutputFileSizeBytes,
                DetectionConfidence = sample.RenderDetectionConfidence ?? 0,
                DetectionReason = Normalize(sample.RenderDetectionReason) ??
                                  (sample.MachineState == MachineState.Proxy
                                      ? "Proxy определён Agent"
                                      : "Render определён Agent"),
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
            };
            renders.Add(session);
            renderStatistics[session.Id] = new RenderStatistics();
            UpdateRender(session, sample);
            return session;
        }

        void UpdateRender(RenderSession? session, ActivitySample sample)
        {
            if (session is null)
            {
                return;
            }

            var statistics = renderStatistics[session.Id];
            if (sample.RenderProcessCpuPercent.HasValue)
            {
                statistics.CpuTotal += sample.RenderProcessCpuPercent.Value;
                statistics.CpuSamples++;
                statistics.MaxCpu = Math.Max(statistics.MaxCpu, sample.RenderProcessCpuPercent.Value);
            }

            session.FileSizeBytes = sample.RenderOutputFileSizeBytes ?? session.FileSizeBytes;
            session.DetectionConfidence = Math.Max(
                session.DetectionConfidence,
                sample.RenderDetectionConfidence ?? 0);
            session.DetectionReason = Normalize(sample.RenderDetectionReason) ?? session.DetectionReason;
        }

        void FinalizeRender(RenderSession? session)
        {
            if (session is null || !renderStatistics.TryGetValue(session.Id, out var statistics))
            {
                return;
            }

            session.AverageCpuPercent = statistics.CpuSamples == 0
                ? null
                : statistics.CpuTotal / statistics.CpuSamples;
            session.MaxCpuPercent = statistics.CpuSamples == 0 ? null : statistics.MaxCpu;
        }
    }

    private static bool SameApplication(ActivitySample left, ActivitySample right) =>
        string.Equals(Normalize(left.ProcessName), Normalize(right.ProcessName), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Normalize(left.ExecutablePath), Normalize(right.ExecutablePath), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Normalize(left.WindowTitle), Normalize(right.WindowTitle), StringComparison.Ordinal);

    private static bool SameRender(ActivitySample left, ActivitySample right)
    {
        var leftIsRender = left.MachineState is MachineState.Render or MachineState.Proxy &&
                           Normalize(left.RenderProgram) is not null;
        var rightIsRender = right.MachineState is MachineState.Render or MachineState.Proxy &&
                            Normalize(right.RenderProgram) is not null;
        if (!leftIsRender || !rightIsRender)
        {
            return leftIsRender == rightIsRender;
        }

        return left.MachineState == right.MachineState && string.Equals(
                   Normalize(left.RenderProgram),
                   Normalize(right.RenderProgram),
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   Normalize(left.RenderOutputFile),
                   Normalize(right.RenderOutputFile),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void Close(Entity? session, DateTimeOffset endedAtUtc)
    {
        switch (session)
        {
            case ApplicationSession application:
                application.EndedAtUtc = Max(application.StartedAtUtc, endedAtUtc);
                break;
            case HumanStateSession human:
                human.EndedAtUtc = Max(human.StartedAtUtc, endedAtUtc);
                break;
            case MachineStateSession machine:
                machine.EndedAtUtc = Max(machine.StartedAtUtc, endedAtUtc);
                break;
            case RenderSession render:
                render.EndedAtUtc = Max(render.StartedAtUtc, endedAtUtc);
                break;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;

    private sealed class RenderStatistics
    {
        public double CpuTotal { get; set; }

        public int CpuSamples { get; set; }

        public double MaxCpu { get; set; }
    }
}
