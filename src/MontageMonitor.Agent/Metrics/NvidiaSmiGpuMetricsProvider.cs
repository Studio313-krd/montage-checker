using System.Diagnostics;
using System.Globalization;
using MontageMonitor.Agent.Abstractions;

namespace MontageMonitor.Agent.Metrics;

internal sealed class NvidiaSmiGpuMetricsProvider : IGpuMetricsProvider
{
    private readonly string _executable = FindExecutable();
    private DateTimeOffset _nextCaptureAtUtc;
    private DateTimeOffset _unavailableUntilUtc;
    private GpuMetricsSnapshot _cached = new(null);

    public async Task<GpuMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextCaptureAtUtc || now < _unavailableUntilUtc)
        {
            return _cached;
        }

        Process? process = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _executable,
                    Arguments = "--query-gpu=utilization.gpu,utilization.encoder --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
            };
            if (!process.Start())
            {
                return MarkUnavailable(now);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0)
            {
                return MarkUnavailable(now);
            }

            var loads = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(line => line.Split(',', StringSplitOptions.TrimEntries))
                .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var load)
                    ? (double?)load
                    : null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            _cached = new GpuMetricsSnapshot(loads.Length == 0 ? null : Math.Clamp(loads.Max(), 0, 100));
            _nextCaptureAtUtc = now.AddSeconds(15);
            return _cached;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return MarkUnavailable(now);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            return MarkUnavailable(now);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private GpuMetricsSnapshot MarkUnavailable(DateTimeOffset now)
    {
        _cached = new GpuMetricsSnapshot(null);
        _unavailableUntilUtc = now.AddMinutes(5);
        return _cached;
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Процесс уже завершился или недоступен — это не влияет на необязательную метрику GPU.
        }
    }

    private static string FindExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installedPath = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        return File.Exists(installedPath) ? installedPath : "nvidia-smi.exe";
    }
}
