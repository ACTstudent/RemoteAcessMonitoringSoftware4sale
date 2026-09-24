using Client.Services;

namespace Client.Tests;

// The sign-in header is tinted, and the seal ships as a flat image on white
// paper. These pin that the seal travels inside the client and that the paper
// is cut away, so the header never shows a white square around it.
public class SchoolLogoTests
{
    [Fact]
    public void SealIsEmbeddedInTheClientAssembly()
    {
        using var stream = typeof(SchoolLogo).Assembly.GetManifestResourceStream(SchoolLogo.ResourceName);
        Assert.NotNull(stream);
    }

    [Theory]
    [InlineData(108)]
    [InlineData(162)]
    public void LoadsAtTheRequestedSize(int pixels)
    {
        using var logo = SchoolLogo.Load(pixels);

        Assert.NotNull(logo);
        Assert.Equal(pixels, logo!.Width);
        Assert.Equal(pixels, logo.Height);
    }

    [Fact]
    public void PaperAroundTheSealIsTransparent()
    {
        using var logo = SchoolLogo.Load(108)!;
        int last = logo.Width - 1;

        Assert.Equal(0, logo.GetPixel(0, 0).A);
        Assert.Equal(0, logo.GetPixel(last, 0).A);
        Assert.Equal(0, logo.GetPixel(0, last).A);
        Assert.Equal(0, logo.GetPixel(last, last).A);
    }

    [Fact]
    public void SealItselfStaysOpaque()
    {
        using var logo = SchoolLogo.Load(108)!;
        int middle = logo.Width / 2;

        Assert.Equal(255, logo.GetPixel(middle, middle).A);
    }
}
