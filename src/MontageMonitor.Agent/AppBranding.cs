using System.Drawing;

namespace MontageMonitor.Agent;

internal static class AppBranding
{
    public static Icon Icon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        var executablePath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(executablePath)
            ? SystemIcons.Application
            : Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application;
    }
}
