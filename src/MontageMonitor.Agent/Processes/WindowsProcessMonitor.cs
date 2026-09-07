using System.Diagnostics;
using System.Runtime.InteropServices;
using MontageMonitor.Agent.Abstractions;

namespace MontageMonitor.Agent.Processes;

internal sealed class WindowsProcessMonitor : IProcessMonitor
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);
    private readonly Dictionary<int, PreviousProcessSample> _previous = [];

    public IReadOnlyList<ProcessMetricsSnapshot> Capture(IReadOnlyCollection<string> processNames)
    {
        var targets = processNames
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizeProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
        {
            _previous.Clear();
            return [];
        }

        var capturedAt = DateTimeOffset.UtcNow;
        var childrenByParent = CaptureChildCounts();
        var snapshots = new List<ProcessMetricsSnapshot>();
        var activeProcessIds = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var processName = NormalizeProcessName(process.ProcessName);
                    if (!targets.Contains(processName))
                    {
                        continue;
                    }

                    process.Refresh();
                    var startedAt = TryGetStartTime(process);
                    var cpuTime = process.TotalProcessorTime;
                    var countersAvailable = GetProcessIoCounters(process.Handle, out var ioCounters);
                    var cpuLoad = 0d;
                    var readRate = 0d;
                    var writeRate = 0d;
                    if (_previous.TryGetValue(process.Id, out var previous) &&
                        previous.StartedAtUtc == startedAt)
                    {
                        var elapsedSeconds = (capturedAt - previous.CapturedAtUtc).TotalSeconds;
                        if (elapsedSeconds > 0)
                        {
                            cpuLoad = Math.Clamp(
                                (cpuTime - previous.TotalProcessorTime).TotalSeconds /
                                elapsedSeconds /
                                Environment.ProcessorCount * 100,
                                0,
                                100);
                            if (countersAvailable)
                            {
                                readRate = Rate(ioCounters.ReadTransferCount, previous.ReadBytes, elapsedSeconds);
                                writeRate = Rate(ioCounters.WriteTransferCount, previous.WriteBytes, elapsedSeconds);
                            }
                        }
                    }

                    _previous[process.Id] = new PreviousProcessSample(
                        capturedAt,
                        startedAt,
                        cpuTime,
                        countersAvailable ? ioCounters.ReadTransferCount : 0,
                        countersAvailable ? ioCounters.WriteTransferCount : 0);
                    activeProcessIds.Add(process.Id);
                    snapshots.Add(new ProcessMetricsSnapshot(
                        process.Id,
                        processName,
                        startedAt,
                        cpuLoad,
                        Math.Max(0, process.WorkingSet64),
                        readRate,
                        writeRate,
                        childrenByParent.GetValueOrDefault(process.Id)));
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception or
                    NotSupportedException or UnauthorizedAccessException)
                {
                    // Процесс мог завершиться или запрещать чтение метрик между перечислением и снимком.
                }
            }
        }

        foreach (var processId in _previous.Keys.Where(item => !activeProcessIds.Contains(item)).ToArray())
        {
            _previous.Remove(processId);
        }

        return snapshots;
    }

    private static Dictionary<int, int> CaptureChildCounts()
    {
        var counts = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            return counts;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return counts;
            }

            do
            {
                var parentId = unchecked((int)entry.ParentProcessId);
                counts[parentId] = counts.GetValueOrDefault(parentId) + 1;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return counts;
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static double Rate(ulong current, ulong previous, double elapsedSeconds) =>
        current >= previous ? (current - previous) / elapsedSeconds : 0;

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint processHandle, out IoCounters ioCounters);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    private sealed record PreviousProcessSample(
        DateTimeOffset CapturedAtUtc,
        DateTimeOffset? StartedAtUtc,
        TimeSpan TotalProcessorTime,
        ulong ReadBytes,
        ulong WriteBytes);
}
