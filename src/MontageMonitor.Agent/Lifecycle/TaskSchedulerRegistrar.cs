using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MontageMonitor.Agent.Lifecycle;

internal static class TaskSchedulerRegistrar
{
    private const string TaskName = "MontageMonitor Agent";

    public static TaskRegistrationResult Register()
    {
        var comObjects = new List<object>();
        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Windows Task Scheduler недоступен.");
            dynamic scheduler = Track(Activator.CreateInstance(schedulerType)!, comObjects);
            scheduler.Connect();
            dynamic rootFolder = Track(scheduler.GetFolder("\\"), comObjects);
            dynamic definition = Track(scheduler.NewTask(0), comObjects);
            var userName = WindowsIdentity.GetCurrent().Name;

            definition.RegistrationInfo.Description =
                "Запускает MontageMonitor при входе пользователя и перезапускает после сбоя.";
            definition.RegistrationInfo.Author = "MontageMonitor";

            dynamic principal = Track(definition.Principal, comObjects);
            principal.UserId = userName;
            principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
            principal.RunLevel = 0; // TASK_RUNLEVEL_LUA

            dynamic trigger = Track(definition.Triggers.Create(9), comObjects); // TASK_TRIGGER_LOGON
            trigger.UserId = userName;
            trigger.Enabled = true;

            dynamic settings = Track(definition.Settings, comObjects);
            settings.Enabled = true;
            settings.AllowDemandStart = true;
            settings.StartWhenAvailable = true;
            settings.DisallowStartIfOnBatteries = false;
            settings.StopIfGoingOnBatteries = false;
            settings.ExecutionTimeLimit = "PT0S";
            settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW
            settings.RestartCount = 250;
            settings.RestartInterval = "PT1M";

            dynamic action = Track(definition.Actions.Create(0), comObjects); // TASK_ACTION_EXEC
            action.Path = Environment.ProcessPath
                ?? throw new InvalidOperationException("Не удалось определить путь MontageMonitor.Agent.exe.");
            action.WorkingDirectory = AppContext.BaseDirectory;

            _ = Track(rootFolder.RegisterTaskDefinition(
                TaskName,
                definition,
                6, // TASK_CREATE_OR_UPDATE
                userName,
                null,
                3, // TASK_LOGON_INTERACTIVE_TOKEN
                null), comObjects);
            return new TaskRegistrationResult(true, null);
        }
        catch (Exception exception) when (
            exception is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new TaskRegistrationResult(false, exception.Message);
        }
        finally
        {
            for (var index = comObjects.Count - 1; index >= 0; index--)
            {
                if (Marshal.IsComObject(comObjects[index]))
                {
                    _ = Marshal.FinalReleaseComObject(comObjects[index]);
                }
            }
        }
    }

    private static dynamic Track(object value, ICollection<object> collection)
    {
        collection.Add(value);
        return value;
    }
}

internal sealed record TaskRegistrationResult(bool Succeeded, string? Error);
