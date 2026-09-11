using System.Security.Principal;

namespace MontageMonitor.Agent;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var userIdentity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var singleInstance = new Mutex(
            true,
            $"Local\\MontageMonitor.Agent.{userIdentity.Replace('-', '_')}",
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        AgentDiagnosticLog.Write($"Starting Agent {AgentEnvironment.Version}");
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            AgentDiagnosticLog.Failure(eventArgs.Exception);
            Environment.Exit(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                AgentDiagnosticLog.Failure(exception);
            }
            Environment.ExitCode = 1;
        };

        try
        {
            Application.Run(new TrayApplicationContext());
            return Environment.ExitCode;
        }
        catch (Exception exception)
        {
            AgentDiagnosticLog.Failure(exception);
            return 1;
        }
    }
}
