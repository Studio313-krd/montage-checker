using System.IO.Enumeration;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Rendering;

internal sealed class WindowsWatchedFolderMonitor : IWatchedFolderMonitor
{
    private static readonly HashSet<string> DefaultVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mxf", ".avi", ".webm", ".mkv", ".m4v",
    };

    private readonly Dictionary<string, FileObservation> _knownFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _configurationKey;
    private bool _hasBaseline;

    public WatchedFileSnapshot? Capture(IReadOnlyList<AgentWatchedFolder> folders)
    {
        var configurationKey = string.Join(
            '|',
            folders.OrderBy(item => item.Id).Select(item =>
                $"{item.Id}:{item.PathPattern}:{string.Join(',', item.Extensions)}:{string.Join(',', item.FileNamePatterns)}"));
        if (!string.Equals(_configurationKey, configurationKey, StringComparison.Ordinal))
        {
            _configurationKey = configurationKey;
            _knownFiles.Clear();
            _hasBaseline = false;
        }

        WatchedFileSnapshot? strongestSignal = null;
        long strongestGrowth = -1;
        foreach (var folder in folders)
        {
            var extensions = folder.Extensions.Count == 0
                ? DefaultVideoExtensions
                : folder.Extensions
                    .Select(NormalizeExtension)
                    .Where(item => item is not null)
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in ExpandDirectories(folder.PathPattern))
            {
                foreach (var file in EnumerateRecentFiles(directory, extensions, folder.FileNamePatterns))
                {
                    var current = new FileObservation(file.Length, file.LastWriteTimeUtc);
                    if (!_knownFiles.TryGetValue(file.FullName, out var previous))
                    {
                        _knownFiles[file.FullName] = current;
                        if (!_hasBaseline)
                        {
                            continue;
                        }

                        var createdSignal = new WatchedFileSnapshot(
                            directory,
                            file.FullName,
                            file.Length,
                            true,
                            file.Length > 0,
                            true);
                        if (file.Length > strongestGrowth)
                        {
                            strongestSignal = createdSignal;
                            strongestGrowth = file.Length;
                        }

                        continue;
                    }

                    _knownFiles[file.FullName] = current;
                    var growth = file.Length - previous.SizeBytes;
                    if (growth <= 0)
                    {
                        continue;
                    }

                    var growingSignal = new WatchedFileSnapshot(
                        directory,
                        file.FullName,
                        file.Length,
                        false,
                        true,
                        true);
                    if (growth > strongestGrowth)
                    {
                        strongestSignal = growingSignal;
                        strongestGrowth = growth;
                    }
                }
            }
        }

        _hasBaseline = true;
        return strongestSignal;
    }

    private static IEnumerable<FileInfo> EnumerateRecentFiles(
        string directory,
        IReadOnlySet<string> extensions,
        IReadOnlyList<string> fileNamePatterns)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file => extensions.Contains(file.Extension) && MatchesAny(file.Name, fileNamePatterns))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(500)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or
            PathTooLongException or NotSupportedException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ExpandDirectories(string pathPattern)
    {
        if (string.IsNullOrWhiteSpace(pathPattern))
        {
            return [];
        }

        try
        {
            var fullPattern = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(pathPattern.Trim().TrimEnd('\\', '/')));
            var root = Path.GetPathRoot(fullPattern);
            if (string.IsNullOrWhiteSpace(root))
            {
                return [];
            }

            IEnumerable<string> current = [root];
            var segments = fullPattern[root.Length..]
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var wildcard = segment.Contains('*') || segment.Contains('?');
                current = current.SelectMany(parent => wildcard
                    ? EnumerateDirectories(parent, segment)
                    : Directory.Exists(Path.Combine(parent, segment))
                        ? [Path.Combine(parent, segment)]
                        : []);
            }

            return current.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
            PathTooLongException or NotSupportedException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string parent, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(parent, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or
            PathTooLongException or ArgumentException)
        {
            return [];
        }
    }

    private static bool MatchesAny(string fileName, IReadOnlyList<string> patterns) =>
        patterns.Count == 0 || patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) &&
            FileSystemName.MatchesSimpleExpression(pattern, fileName, true));

    private static string? NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private sealed record FileObservation(long SizeBytes, DateTime LastWriteTimeUtc);
}
