using System.Text;
using MontageMonitor.Agent.Configuration;

namespace MontageMonitor.Agent;

internal static class AgentDiagnosticLog
{
    private static readonly object Gate = new();
    private static string? _lastStatus;
    private static DateTimeOffset _lastStatusAtUtc;

    public static string FilePath { get; } = Path.Combine(AgentPaths.DataDirectory, "diagnostics.log");

    public static void Status(AgentRuntimeStatus status)
    {
        lock (Gate)
        {
            var key = $"{status.State}: {status.Details}";
            var now = DateTimeOffset.UtcNow;
            if (key == _lastStatus && now - _lastStatusAtUtc < TimeSpan.FromMinutes(1))
            {
                return;
            }

            _lastStatus = key;
            _lastStatusAtUtc = now;
            Write($"{key}; queue={status.QueuedCount}; lastSyncUtc={status.LastSyncAtUtc:O}");
        }
    }

    public static void Failure(Exception exception)
    {
        // Do not log arbitrary exception messages: they can contain request data or file names.
        Write($"Fatal: {exception.GetType().FullName}; HResult={exception.HResult}; " +
              $"inner={exception.InnerException?.GetType().FullName}; method={exception.TargetSite?.Name}");
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AgentPaths.DataDirectory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= 1_048_576)
                {
                    File.Move(FilePath, FilePath + ".previous", overwrite: true);
                }

                var line = message.Replace('\r', ' ').Replace('\n', ' ');
                File.AppendAllText(FilePath, $"{DateTimeOffset.UtcNow:O} {line}\n", new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A diagnostic file must never prevent monitoring or shutdown.
            }
        }
    }
}
