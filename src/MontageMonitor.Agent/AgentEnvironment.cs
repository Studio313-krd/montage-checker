using System.Reflection;

namespace MontageMonitor.Agent;

internal static class AgentEnvironment
{
    public const string ServerBaseUrl = "https://montage.checker.studio313.ru";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.10.0";

    public static string WindowsUser { get; } =
        $"{Environment.UserDomainName}\\{Environment.UserName}";
}
