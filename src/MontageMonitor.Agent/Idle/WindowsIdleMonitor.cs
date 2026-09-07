using System.Runtime.InteropServices;
using Microsoft.Win32;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent.Idle;

internal sealed class WindowsIdleMonitor : IIdleMonitor
{
    private volatile bool _isLocked;
    private bool _disposed;

    public WindowsIdleMonitor()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public IdleSnapshot Capture(int idleThresholdSeconds)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        var idleSeconds = NativeMethods.GetLastInputInfo(ref info)
            ? checked((int)(unchecked((uint)Environment.TickCount) - info.Time) / 1_000)
            : 0;
        return IdleStateResolver.Resolve(idleSeconds, _isLocked, idleThresholdSeconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _disposed = true;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason == SessionSwitchReason.SessionLock)
        {
            _isLocked = true;
        }
        else if (eventArgs.Reason == SessionSwitchReason.SessionUnlock)
        {
            _isLocked = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);
    }
}

internal static class IdleStateResolver
{
    public static IdleSnapshot Resolve(int idleSeconds, bool isLocked, int idleThresholdSeconds)
    {
        var normalizedIdleSeconds = Math.Max(0, idleSeconds);
        var normalizedThreshold = Math.Max(0, idleThresholdSeconds);
        var state = isLocked
            ? HumanState.Locked
            : normalizedIdleSeconds >= normalizedThreshold
                ? HumanState.Idle
                : HumanState.Active;
        return new IdleSnapshot(normalizedIdleSeconds, state);
    }
}
