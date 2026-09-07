using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Screenshots;

internal sealed class WindowsScreenshotCapture : IScreenshotCapture
{
    private static readonly ImageCodecInfo JpegEncoder = ImageCodecInfo.GetImageEncoders()
        .Single(item => item.FormatID == ImageFormat.Jpeg.Guid);

    public IReadOnlyList<CapturedScreenshot> Capture(
        ScreenshotCaptureMode captureMode,
        int maxWidth,
        int jpegQuality)
    {
        if (!Environment.UserInteractive)
        {
            return [];
        }

        var allScreens = Screen.AllScreens;
        var selected = captureMode == ScreenshotCaptureMode.AllMonitors
            ? allScreens.Select((screen, index) => (screen, index))
            : allScreens.Select((screen, index) => (screen, index)).Where(item => item.screen.Primary);
        var result = new List<CapturedScreenshot>();
        foreach (var (screen, index) in selected)
        {
            try
            {
                var captured = CaptureScreen(screen, index, maxWidth, jpegQuality);
                result.Add(captured);
            }
            catch (Exception exception) when (
                exception is ExternalException or Win32Exception or ArgumentException)
            {
                // Отказ одного дисплея не должен завершать Agent или мешать остальным дисплеям.
            }
        }

        return result;
    }

    private static CapturedScreenshot CaptureScreen(
        Screen screen,
        int screenIndex,
        int configuredMaxWidth,
        int configuredQuality)
    {
        var bounds = screen.Bounds;
        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        var maxWidth = Math.Clamp(configuredMaxWidth, 320, 7_680);
        var targetWidth = Math.Min(source.Width, maxWidth);
        var targetHeight = checked((int)Math.Round(source.Height * (targetWidth / (double)source.Width)));
        using var resized = targetWidth == source.Width
            ? null
            : Resize(source, targetWidth, targetHeight);
        var image = resized ?? source;
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            (long)Math.Clamp(configuredQuality, 55, 65));
        image.Save(output, JpegEncoder, parameters);
        return new CapturedScreenshot(screenIndex, image.Width, image.Height, output.ToArray());
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.SmoothingMode = SmoothingMode.HighSpeed;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return target;
    }
}
