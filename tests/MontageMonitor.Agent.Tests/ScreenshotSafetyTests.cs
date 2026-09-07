using MontageMonitor.Agent.Screenshots;
using MontageMonitor.Server.Features.Screenshots;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ScreenshotSafetyTests
{
    [Fact]
    public void LockedAndDisabledSessionsCannotBeCaptured()
    {
        Assert.False(ScreenshotCapturePolicy.CanCapture(true, HumanState.Locked));
        Assert.False(ScreenshotCapturePolicy.CanCapture(false, HumanState.Active));
        Assert.True(ScreenshotCapturePolicy.CanCapture(true, HumanState.Idle));
    }

    [Theory]
    [InlineData("1Password.exe")]
    [InlineData("C:\\Tools\\KEEPASS.EXE")]
    public void PrivacyProcessMatchingIgnoresCaseAndPath(string processName)
    {
        Assert.True(ScreenshotCapturePolicy.IsPrivacyExcluded(
            processName,
            ["1password.exe", "KeePass.exe"]));
    }

    [Fact]
    public void JpegInspectorReadsDimensionsFromHeader()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            0x00, 0x02, 0x00, 0x03, 0x03,
            0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00,
            0xFF, 0xD9,
        ];

        Assert.True(JpegInspector.TryGetDimensions(jpeg, out var width, out var height));
        Assert.Equal(3, width);
        Assert.Equal(2, height);
    }

    [Fact]
    public void JpegInspectorRejectsExtensionOnlyPayload()
    {
        byte[] fakeJpeg = [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xD9];

        Assert.False(JpegInspector.TryGetDimensions(fakeJpeg, out _, out _));
    }
}
