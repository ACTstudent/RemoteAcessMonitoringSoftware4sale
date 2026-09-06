using Client.Services;

namespace Client.Tests;

/// <summary>
/// Capture and input must describe the same rectangle.
///
/// They did not. The capture used the primary monitor and the input mapped
/// across the whole virtual desktop, so on a second display a teacher's click
/// landed nowhere near where they aimed it — and because the two rectangles are
/// identical on a single display, nothing caught it. The existing
/// ScaleCoordinate test could not: the arithmetic was always right, it was the
/// choice of rectangle that was wrong.
///
/// These tests assert the relationship rather than any particular screen size,
/// so they hold on a developer's laptop and on a build agent alike.
/// </summary>
public sealed class CaptureGeometryTests
{
    [Fact]
    public void TheTopLeftOfTheImageIsTheTopLeftOfTheCapturedDesktop()
    {
        var bounds = CaptureGeometry.DesktopBounds;

        var point = CaptureGeometry.ToDesktopPoint(0, 0);

        Assert.Equal(bounds.Left, point.X);
        Assert.Equal(bounds.Top, point.Y);
    }

    [Fact]
    public void TheBottomRightOfTheImageIsTheBottomRightOfTheCapturedDesktop()
    {
        var bounds = CaptureGeometry.DesktopBounds;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;   // headless agent

        var point = CaptureGeometry.ToDesktopPoint(10000, 10000);

        Assert.Equal(bounds.Right - 1, point.X);
        Assert.Equal(bounds.Bottom - 1, point.Y);
    }

    [Fact]
    public void TheCentreOfTheImageIsTheCentreOfTheCapturedDesktop()
    {
        var bounds = CaptureGeometry.DesktopBounds;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var point = CaptureGeometry.ToDesktopPoint(5000, 5000);

        // This is the assertion the old code failed: with a 1920 capture and a
        // 3840 input space, the centre of the image mapped to x=1920 — the first
        // pixel of the second monitor — instead of x=960.
        Assert.InRange(point.X, bounds.Left + (bounds.Width / 2) - 1, bounds.Left + (bounds.Width / 2) + 1);
        Assert.InRange(point.Y, bounds.Top + (bounds.Height / 2) - 1, bounds.Top + (bounds.Height / 2) + 1);
    }

    [Fact]
    public void EveryPointOnTheImageLandsInsideTheCapturedDesktop()
    {
        var bounds = CaptureGeometry.DesktopBounds;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        for (var normalized = 0; normalized <= 10000; normalized += 250)
        {
            var point = CaptureGeometry.ToDesktopPoint(normalized, normalized);
            Assert.InRange(point.X, bounds.Left, bounds.Right - 1);
            Assert.InRange(point.Y, bounds.Top, bounds.Bottom - 1);
        }
    }

    [Fact]
    public void AClickBeyondTheImageIsClampedRatherThanEscapingTheDesktop()
    {
        var bounds = CaptureGeometry.DesktopBounds;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var past = CaptureGeometry.ToDesktopPoint(99999, 99999);
        var before = CaptureGeometry.ToDesktopPoint(-5000, -5000);

        Assert.Equal(bounds.Right - 1, past.X);
        Assert.Equal(bounds.Bottom - 1, past.Y);
        Assert.Equal(bounds.Left, before.X);
        Assert.Equal(bounds.Top, before.Y);
    }

    /// <summary>
    /// The mapping is defined against a desktop that can start at a negative
    /// origin, which is what a monitor placed left of or above the primary one
    /// produces. Checked here with explicit numbers because a test machine
    /// usually has a single display at (0,0) and would never exercise it.
    /// </summary>
    [Theory]
    [InlineData(-1920, 3840, 0, 0, -1920)]        // left edge of a left-hand monitor
    [InlineData(-1920, 3840, 5000, 0, 0)]         // centre of a two-monitor desktop
    [InlineData(-1920, 3840, 10000, 0, 1919)]     // right edge of the right-hand monitor
    [InlineData(0, 1920, 5000, 0, 960)]           // single monitor, unchanged
    public void ANegativeDesktopOriginIsCarriedThrough(int left, int width, int normalized, int _, int expected)
    {
        Assert.Equal(expected, left + CaptureGeometry.ScaleCoordinate(normalized, width));
    }

    [Fact]
    public void InputSimulatorScalesThroughTheSameDefinition()
    {
        // InputSimulator.ScaleCoordinate is still the entry point other code
        // uses; it must not drift into a second implementation.
        for (var normalized = 0; normalized <= 10000; normalized += 1000)
        {
            Assert.Equal(
                CaptureGeometry.ScaleCoordinate(normalized, 1920),
                InputSimulator.ScaleCoordinate(normalized, 1920));
        }
    }
}
