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

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => Environment.Exit(1);
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Environment.ExitCode = 1;

        try
        {
            Application.Run(new TrayApplicationContext());
            return Environment.ExitCode;
        }
        catch
        {
            return 1;
        }
    }
}
