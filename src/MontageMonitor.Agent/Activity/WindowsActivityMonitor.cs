using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MontageMonitor.Agent.Abstractions;

namespace MontageMonitor.Agent.Activity;

internal sealed class WindowsActivityMonitor : IActivityMonitor
{
    public ActivitySnapshot Capture()
    {
        var windowHandle = NativeMethods.GetForegroundWindow();
        if (windowHandle == nint.Zero)
        {
            return new ActivitySnapshot(null, null, null);
        }

        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        string? processName = null;
        string? executablePath = null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            processName = process.ProcessName;
            try
            {
                var mainModule = process.MainModule;
                executablePath = mainModule?.FileName;
                processName = mainModule?.ModuleName ?? processName;
            }
            catch (Exception exception) when (
                exception is System.ComponentModel.Win32Exception or InvalidOperationException or
                NotSupportedException)
            {
                // Некоторые системные/elevated процессы не разрешают читать executable path.
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            // Окно могло закрыться между вызовами Win32 API.
        }

        var title = ReadWindowTitle(windowHandle);
        if (!string.IsNullOrWhiteSpace(processName) && !Path.HasExtension(processName))
        {
            processName += ".exe";
        }

        return new ActivitySnapshot(
            Normalize(processName, 255),
            Normalize(executablePath, 2_000),
            Normalize(title, 2_000));
    }

    private static string? ReadWindowTitle(nint windowHandle)
    {
        var length = NativeMethods.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new StringBuilder(Math.Min(length + 1, 2_001));
        return NativeMethods.GetWindowText(windowHandle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(nint windowHandle);
    }
}
