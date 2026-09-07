using System.Runtime.InteropServices;
using MontageMonitor.Agent.Abstractions;

namespace MontageMonitor.Agent.Metrics;

internal sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuSample;

    public SystemMetricsSnapshot Capture()
    {
        var cpu = CaptureCpu();
        var memory = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
        };
        var memoryPercent = NativeMethods.GlobalMemoryStatusEx(ref memory)
            ? memory.MemoryLoad
            : 0;
        return new SystemMetricsSnapshot(cpu, memoryPercent);
    }

    private double CaptureCpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleValue = idle.ToUInt64();
        var kernelValue = kernel.ToUInt64();
        var userValue = user.ToUInt64();
        if (!_hasCpuSample)
        {
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;
            _hasCpuSample = true;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var totalDelta = kernelValue - _previousKernel + userValue - _previousUser;
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        return totalDelta == 0
            ? 0
            : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
