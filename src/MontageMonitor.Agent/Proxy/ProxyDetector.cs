using System.IO.Enumeration;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent.Proxy;

internal sealed class ProxyDetector(
    IProcessMonitor processMonitor,
    IWatchedFolderMonitor watchedFolderMonitor) : IProxyDetector
{
    private readonly ProxyStateMachine _stateMachine = new();

    public Task<ProxyDetectionResult> EvaluateAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<AgentProxyRule> rules,
        IReadOnlyList<AgentWatchedFolder> folders,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabledRules = rules
            .Where(item => !string.IsNullOrWhiteSpace(item.ProcessName))
            .ToArray();
        var processes = processMonitor.Capture(enabledRules.Select(item => item.ProcessName).ToArray());
        var file = watchedFolderMonitor.Capture(folders);
        var evidence = new List<ProxyEvidence>();

        foreach (var rule in enabledRules)
        {
            foreach (var process in processes.Where(item =>
                         string.Equals(
                             item.ProcessName,
                             NormalizeProcessName(rule.ProcessName),
                             StringComparison.OrdinalIgnoreCase)))
            {
                evidence.Add(ProxyEvidence.Create(rule, process, file));
            }
        }

        return Task.FromResult(_stateMachine.Update(timestampUtc, evidence));
    }

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
    }
}

internal sealed record ProxyEvidence(
    AgentProxyRule Rule,
    ProcessMetricsSnapshot Process,
    WatchedFileSnapshot? File,
    byte Confidence,
    bool CpuAboveThreshold,
    bool IoAboveThreshold,
    bool FileNameMatched,
    string Reason)
{
    public bool HasProxyFileSignal => File is { IsGrowing: true } or { WasCreated: true };

    public static ProxyEvidence Create(
        AgentProxyRule rule,
        ProcessMetricsSnapshot process,
        WatchedFileSnapshot? file)
    {
        var cpuAbove = process.CpuLoadPercent >= rule.CpuThresholdPercent;
        var ioAbove = process.IoWriteBytesPerSecond >= rule.DiskWriteThresholdBytesPerSecond;
        var fileNameMatched = file is not null && MatchesAny(Path.GetFileName(file.FilePath), rule.FileNamePatterns);
        var score = 20;
        var signals = new List<string> { $"encoder {process.ProcessName}" };

        if (cpuAbove)
        {
            score += 25;
            signals.Add($"CPU {process.CpuLoadPercent:F0}%");
        }

        if (ioAbove)
        {
            score += 20;
            signals.Add($"запись процесса {FormatRate(process.IoWriteBytesPerSecond)}");
        }

        if (file is not null)
        {
            if (file.WasCreated || file.IsGrowing)
            {
                score += 25;
                signals.Add(file.WasCreated ? "создан proxy-файл" : "proxy-файл растёт");
            }

            score += 20;
            signals.Add("папка Proxy");
            if (file.HasKnownExtension)
            {
                score += 5;
                signals.Add("видео-расширение");
            }

            if (fileNameMatched)
            {
                score += 15;
                signals.Add("имя соответствует proxy-правилу");
            }
        }

        return new ProxyEvidence(
            rule,
            process,
            file,
            (byte)Math.Clamp(score, 0, 100),
            cpuAbove,
            ioAbove,
            fileNameMatched,
            string.Join(" + ", signals));
    }

    public ProxyTelemetrySnapshot ToTelemetry(string? reason = null) => new(
        Process.ProcessName,
        Process.StartedAtUtc,
        Process.CpuLoadPercent,
        Process.WorkingSetBytes,
        Process.IoReadBytesPerSecond,
        Process.IoWriteBytesPerSecond,
        Process.ChildProcessCount,
        File?.Folder,
        File?.FilePath,
        File?.FileSizeBytes,
        Confidence,
        reason ?? Reason,
        FileNameMatched);

    private static bool MatchesAny(string fileName, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) &&
            FileSystemName.MatchesSimpleExpression(pattern.Trim(), fileName, ignoreCase: true));

    private static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond >= 1_048_576
            ? $"{bytesPerSecond / 1_048_576:F1} МБ/с"
            : $"{bytesPerSecond / 1_024:F0} КБ/с";
}

internal sealed class ProxyStateMachine
{
    private CandidateKey? _candidate;
    private DateTimeOffset? _candidateSinceUtc;
    private CandidateKey? _active;
    private DateTimeOffset? _inactiveSinceUtc;

    public ProxyDetectionResult Update(
        DateTimeOffset timestampUtc,
        IReadOnlyList<ProxyEvidence> evidence)
    {
        var strongest = evidence
            .Where(item => item.HasProxyFileSignal)
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.FileNameMatched)
            .ThenByDescending(item => item.Process.IoWriteBytesPerSecond)
            .FirstOrDefault();

        if (_active.HasValue)
        {
            var activeEvidence = evidence.FirstOrDefault(item => Key(item) == _active.Value);
            if (activeEvidence is null)
            {
                Reset();
                return new ProxyDetectionResult(MachineState.Normal, null);
            }

            var activeSignals = activeEvidence.HasProxyFileSignal ||
                                activeEvidence.CpuAboveThreshold || activeEvidence.IoAboveThreshold;
            if (activeSignals)
            {
                _inactiveSinceUtc = null;
            }
            else
            {
                _inactiveSinceUtc ??= timestampUtc;
                if (timestampUtc - _inactiveSinceUtc >=
                    TimeSpan.FromSeconds(activeEvidence.Rule.FinishTimeoutSeconds))
                {
                    Reset();
                    return new ProxyDetectionResult(
                        MachineState.Normal,
                        activeEvidence.ToTelemetry("Proxy завершён: файл не растёт, CPU и запись ниже порогов."));
                }
            }

            return new ProxyDetectionResult(
                MachineState.Proxy,
                activeEvidence.ToTelemetry("Proxy подтверждён: " + activeEvidence.Reason));
        }

        if (strongest is null || strongest.Confidence < strongest.Rule.MinimumConfidence)
        {
            _candidate = null;
            _candidateSinceUtc = null;
            var diagnostic = evidence.OrderByDescending(item => item.Confidence).FirstOrDefault();
            return new ProxyDetectionResult(MachineState.Normal, diagnostic?.ToTelemetry());
        }

        var strongestKey = Key(strongest);
        if (_candidate != strongestKey)
        {
            _candidate = strongestKey;
            _candidateSinceUtc = timestampUtc;
            return new ProxyDetectionResult(
                MachineState.Normal,
                strongest.ToTelemetry("Кандидат Proxy: " + strongest.Reason));
        }

        if (timestampUtc - _candidateSinceUtc < TimeSpan.FromSeconds(strongest.Rule.ConfirmationSeconds))
        {
            return new ProxyDetectionResult(
                MachineState.Normal,
                strongest.ToTelemetry("Подтверждение Proxy: " + strongest.Reason));
        }

        _active = strongestKey;
        _candidate = null;
        _candidateSinceUtc = null;
        _inactiveSinceUtc = null;
        return new ProxyDetectionResult(
            MachineState.Proxy,
            strongest.ToTelemetry("Proxy подтверждён: " + strongest.Reason));
    }

    private void Reset()
    {
        _active = null;
        _candidate = null;
        _candidateSinceUtc = null;
        _inactiveSinceUtc = null;
    }

    private static CandidateKey Key(ProxyEvidence evidence) =>
        new(evidence.Rule.Id, evidence.Process.ProcessId);

    private readonly record struct CandidateKey(Guid RuleId, int ProcessId);
}
