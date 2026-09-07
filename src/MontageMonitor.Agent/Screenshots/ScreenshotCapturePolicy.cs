using MontageMonitor.Shared.States;

namespace MontageMonitor.Agent.Screenshots;

internal static class ScreenshotCapturePolicy
{
    public static bool CanCapture(bool enabled, HumanState humanState) =>
        enabled && humanState != HumanState.Locked;

    public static bool IsPrivacyExcluded(string? processName, IReadOnlyList<string> exclusions)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalized = Path.GetFileName(processName.Trim());
        return exclusions.Any(item =>
            string.Equals(Path.GetFileName(item.Trim()), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
