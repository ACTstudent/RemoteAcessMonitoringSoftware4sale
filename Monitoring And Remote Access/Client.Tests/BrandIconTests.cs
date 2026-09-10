using System.Drawing;
using Client.Services;

namespace Client.Tests;

// Every installed machine showed the tray's placeholder "C" because the mark was
// a loose file that the single-file publish never left on disk. These run
// against the built client assembly, so they fail if it stops being embedded.
public class BrandIconTests
{
    [Fact]
    public void MarkIsEmbeddedInTheClientAssembly()
    {
        using var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream(BrandIcon.ResourceName);
        Assert.NotNull(stream);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void LoadsTheFrameWindowsAsksFor(int pixels)
    {
        using var icon = BrandIcon.Load(new Size(pixels, pixels));

        Assert.NotNull(icon);
        Assert.Equal(pixels, icon!.Width);
        Assert.NotEqual(IntPtr.Zero, icon.Handle);

        // Decoded, not merely parsed: a frame the loader cannot read comes back blank.
        using var bitmap = icon.ToBitmap();
        var visible = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).A > 0) visible++;
        Assert.True(visible > 0, "The loaded frame has no visible pixels.");
    }
}
