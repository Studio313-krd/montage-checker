namespace MontageMonitor.Agent.Configuration;

internal static class AgentPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MontageMonitor");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "agent.json");

    public static string QueueDatabase { get; } = Path.Combine(DataDirectory, "queue.db");

    public static string ScreenshotQueueDirectory { get; } = Path.Combine(DataDirectory, "screenshots");
}
