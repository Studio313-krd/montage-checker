using System.Reflection;

namespace MontageMonitor.Agent;

internal static class AgentEnvironment
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.8.0";

    public static string WindowsUser { get; } =
        $"{Environment.UserDomainName}\\{Environment.UserName}";
}
