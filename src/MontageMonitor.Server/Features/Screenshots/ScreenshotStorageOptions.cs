namespace MontageMonitor.Server.Features.Screenshots;

public sealed class ScreenshotStorageOptions
{
    public const string SectionName = "ScreenshotStorage";

    public string RootPath { get; set; } = "/data/screenshots";

    public long MaxUploadBytes { get; set; } = 8 * 1_024 * 1_024;
}
