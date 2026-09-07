using System.IO.Enumeration;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent.Rendering;

internal sealed class RenderDetector(
    IProcessMonitor processMonitor,
    IWatchedFolderMonitor watchedFolderMonitor,
    IGpuMetricsProvider gpuMetricsProvider) : IRenderDetector
{
    private readonly RenderStateMachine _stateMachine = new();

    public async Task<RenderDetectionResult> EvaluateAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<AgentRenderRule> rules,
        IReadOnlyList<AgentWatchedFolder> folders,
        CancellationToken cancellationToken)
    {
        var enabledRules = rules
            .Where(item => !string.IsNullOrWhiteSpace(item.ProcessName))
            .ToArray();
        var processNames = enabledRules.Select(item => item.ProcessName).ToArray();
        var processes = processMonitor.Capture(processNames);
        var file = watchedFolderMonitor.Capture(folders);
        var gpu = await gpuMetricsProvider.CaptureAsync(cancellationToken);
        var evidence = new List<RenderEvidence>();

        foreach (var rule in enabledRules)
        {
            var matchingFile = MatchesFileRule(file, rule.FileNamePatterns) ? file : null;
            foreach (var process in processes.Where(item =>
                         string.Equals(item.ProcessName, NormalizeProcessName(rule.ProcessName),
                             StringComparison.OrdinalIgnoreCase)))
            {
                evidence.Add(RenderEvidence.Create(rule, process, matchingFile, gpu));
            }
        }

        return _stateMachine.Update(timestampUtc, evidence);
    }

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
    }

    private static bool MatchesFileRule(WatchedFileSnapshot? file, IReadOnlyList<string> patterns)
    {
        if (file is null || patterns.Count == 0)
        {
            return true;
        }

        var fileName = Path.GetFileName(file.FilePath);
        return patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) &&
            FileSystemName.MatchesSimpleExpression(pattern.Trim(), fileName, ignoreCase: true));
    }
}

internal sealed record RenderEvidence(
    AgentRenderRule Rule,
    ProcessMetricsSnapshot Process,
    WatchedFileSnapshot? File,
    double? GpuLoadPercent,
    byte Confidence,
    bool CpuAboveThreshold,
    bool IoAboveThreshold,
    bool GpuActive,
    string Reason)
{
    public static RenderEvidence Create(
        AgentRenderRule rule,
        ProcessMetricsSnapshot process,
        WatchedFileSnapshot? file,
        GpuMetricsSnapshot gpu)
    {
        var cpuAbove = process.CpuLoadPercent >= rule.CpuThresholdPercent;
        var ioAbove = process.IoWriteBytesPerSecond >= rule.DiskWriteThresholdBytesPerSecond;
        var gpuActive = gpu.LoadPercent >= 20;
        var score = 20;
        var signals = new List<string> { $"процесс {process.ProcessName}" };
        if (cpuAbove)
        {
            score += 30;
            signals.Add($"CPU {process.CpuLoadPercent:F0}%");
        }

        if (ioAbove)
        {
            score += 25;
            signals.Add($"запись процесса {FormatRate(process.IoWriteBytesPerSecond)}");
        }

        if (gpuActive)
        {
            score += 10;
            signals.Add($"GPU {gpu.LoadPercent:F0}%");
        }

        if (file is not null)
        {
            if (file.WasCreated || file.IsGrowing)
            {
                score += 20;
                signals.Add(file.WasCreated ? "создан выходной файл" : "выходной файл растёт");
            }

            score += 10;
            signals.Add("папка Render");
            if (file.HasKnownExtension)
            {
                score += 5;
                signals.Add("видео-расширение");
            }
        }

        return new RenderEvidence(
            rule,
            process,
            file,
            gpu.LoadPercent,
            (byte)Math.Clamp(score, 0, 100),
            cpuAbove,
            ioAbove,
            gpuActive,
            string.Join(" + ", signals));
    }

    public RenderTelemetrySnapshot ToTelemetry(string? reason = null) => new(
        Process.ProcessName,
        Process.StartedAtUtc,
        Process.CpuLoadPercent,
        Process.WorkingSetBytes,
        Process.IoReadBytesPerSecond,
        Process.IoWriteBytesPerSecond,
        Process.ChildProcessCount,
        GpuLoadPercent,
        File?.Folder,
        File?.FilePath,
        File?.FileSizeBytes,
        Confidence,
        reason ?? Reason);

    private static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond >= 1_048_576
            ? $"{bytesPerSecond / 1_048_576:F1} МБ/с"
            : $"{bytesPerSecond / 1_024:F0} КБ/с";
}

internal sealed class RenderStateMachine
{
    private CandidateKey? _candidate;
    private DateTimeOffset? _candidateSinceUtc;
    private CandidateKey? _active;
    private DateTimeOffset? _inactiveSinceUtc;

    public RenderDetectionResult Update(
        DateTimeOffset timestampUtc,
        IReadOnlyList<RenderEvidence> evidence)
    {
        var strongest = evidence
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.Process.CpuLoadPercent)
            .ThenByDescending(item => item.Process.IoWriteBytesPerSecond)
            .FirstOrDefault();

        if (_active.HasValue)
        {
            var activeEvidence = evidence.FirstOrDefault(item => Key(item) == _active.Value);
            if (activeEvidence is null)
            {
                Reset();
                return new RenderDetectionResult(MachineState.Normal, null);
            }

            var activeSignals = activeEvidence.CpuAboveThreshold || activeEvidence.IoAboveThreshold ||
                                activeEvidence.File is { IsGrowing: true } or { WasCreated: true };
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
                    return new RenderDetectionResult(
                        MachineState.Normal,
                        activeEvidence.ToTelemetry("Render завершён: CPU и запись оставались ниже порогов."));
                }
            }

            return new RenderDetectionResult(
                MachineState.Render,
                activeEvidence.ToTelemetry("Render подтверждён: " + activeEvidence.Reason));
        }

        if (strongest is null || strongest.Confidence < strongest.Rule.MinimumConfidence)
        {
            _candidate = null;
            _candidateSinceUtc = null;
            return new RenderDetectionResult(MachineState.Normal, strongest?.ToTelemetry());
        }

        var strongestKey = Key(strongest);
        if (_candidate != strongestKey)
        {
            _candidate = strongestKey;
            _candidateSinceUtc = timestampUtc;
            return new RenderDetectionResult(
                MachineState.Normal,
                strongest.ToTelemetry("Кандидат Render: " + strongest.Reason));
        }

        if (timestampUtc - _candidateSinceUtc < TimeSpan.FromSeconds(strongest.Rule.ConfirmationSeconds))
        {
            return new RenderDetectionResult(
                MachineState.Normal,
                strongest.ToTelemetry("Подтверждение Render: " + strongest.Reason));
        }

        _active = strongestKey;
        _candidate = null;
        _candidateSinceUtc = null;
        _inactiveSinceUtc = null;
        return new RenderDetectionResult(
            MachineState.Render,
            strongest.ToTelemetry("Render подтверждён: " + strongest.Reason));
    }

    private void Reset()
    {
        _active = null;
        _candidate = null;
        _candidateSinceUtc = null;
        _inactiveSinceUtc = null;
    }

    private static CandidateKey Key(RenderEvidence evidence) =>
        new(evidence.Rule.Id, evidence.Process.ProcessId);

    private readonly record struct CandidateKey(Guid RuleId, int ProcessId);
}
