using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Client.Services;

/// <summary>
/// The Pardo Elementary School seal shown on the sign-in screen, read from
/// inside the client assembly.
/// </summary>
/// <remarks>
/// The embedded file is the portal's own pardo_logo.png, linked rather than
/// copied so the two cannot drift apart. It is a flat image on white paper,
/// which would show as a white square on the tinted sign-in header, so the
/// paper around the seal is cut away as it loads. Only white reachable from
/// the edge of the image goes; white inside the seal is part of the artwork.
/// </remarks>
public static class SchoolLogo
{
    public const string ResourceName = "pardo_logo.png";

    // Anything at least this light on every channel counts as paper.
    private const int PaperThreshold = 232;

    // How dark the seal's outer ring is, measured as 255 minus its darkest
    // channel (#5B577A). Used to judge how much of a fringe pixel is seal.
    private const float RingDepth = 168f;

    /// <summary>
    /// The seal at <paramref name="pixels"/> square with a transparent background,
    /// or null if the resource cannot be read. A missing logo must never stop a
    /// pupil signing in, so the caller simply leaves the space empty.
    /// </summary>
    public static Bitmap? Load(int pixels)
    {
        try
        {
            using var stream = typeof(SchoolLogo).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using var source = new Bitmap(stream);
            using var cutOut = WithoutPaper(source);

            var scaled = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(cutOut, new Rectangle(0, 0, pixels, pixels));
            return scaled;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the paper around the artwork transparent. The paper is found by
    /// flooding in from the border, and the one-pixel fringe where paper meets
    /// the seal is given partial transparency so no pale halo is left behind.
    /// </summary>
    internal static Bitmap WithoutPaper(Bitmap source)
    {
        int w = source.Width, h = source.Height;
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) g.DrawImage(source, new Rectangle(0, 0, w, h));

        var bounds = new Rectangle(0, 0, w, h);
        var data = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var px = new int[w * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);

            var paper = new bool[px.Length];
            var queue = new Queue<int>();
            void Visit(int i)
            {
                if (paper[i] || !IsPaper(px[i])) return;
                paper[i] = true;
                queue.Enqueue(i);
            }

            for (int x = 0; x < w; x++) { Visit(x); Visit((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Visit(y * w); Visit(y * w + w - 1); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % w, y = i / w;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }

            for (int i = 0; i < px.Length; i++)
            {
                if (paper[i]) { px[i] = 0; continue; }
                if (TouchesPaper(paper, i, w, h)) px[i] = Unblend(px[i]);
            }

            Marshal.Copy(px, 0, data.Scan0, px.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }
        return result;
    }

    private static bool IsPaper(int argb)
    {
        var c = Color.FromArgb(argb);
        return Math.Min(c.R, Math.Min(c.G, c.B)) >= PaperThreshold;
    }

    private static bool TouchesPaper(bool[] paper, int i, int w, int h)
    {
        int x = i % w, y = i / w;
        return (x > 0 && paper[i - 1]) || (x < w - 1 && paper[i + 1]) ||
               (y > 0 && paper[i - w]) || (y < h - 1 && paper[i + w]);
    }

    /// <summary>
    /// A fringe pixel is part seal and part white paper. Estimates how much is
    /// seal from how dark it is, then removes the white so the pixel carries
    /// only the seal's colour at that opacity.
    /// </summary>
    private static int Unblend(int argb)
    {
        var c = Color.FromArgb(argb);
        int darkest = Math.Min(c.R, Math.Min(c.G, c.B));
        float alpha = Math.Clamp((255 - darkest) / RingDepth, 0f, 1f);
        if (alpha <= 0f) return 0;

        int Channel(int v) => Math.Clamp((int)Math.Round((v - (1 - alpha) * 255) / alpha), 0, 255);
        return Color.FromArgb((int)Math.Round(alpha * 255), Channel(c.R), Channel(c.G), Channel(c.B)).ToArgb();
    }
}
