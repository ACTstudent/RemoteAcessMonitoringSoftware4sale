using System.Drawing;

namespace Client.Services;

/// <summary>
/// The CAMS mark, read from inside the client assembly.
/// </summary>
/// <remarks>
/// It used to be loaded from a cams.ico copied next to Client.exe. The release
/// build publishes a single-file executable, which folds that file into the
/// bundle rather than leaving it on disk, so no installed lab machine ever had
/// it: the tray found nothing and quietly drew its placeholder "C" instead. An
/// embedded resource travels inside the assembly however the client is
/// published, installed or copied.
/// </remarks>
public static class BrandIcon
{
    public const string ResourceName = "cams.ico";

    /// <summary>
    /// The frame nearest <paramref name="size"/>, rather than one bitmap rescaled.
    /// Returns null if the resource cannot be read, so the tray falls back to the
    /// icon it draws itself - a missing icon must never be the reason a lab
    /// machine has no agent.
    /// </summary>
    public static Icon? Load(Size? size = null)
    {
        try
        {
            using var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            return size is Size wanted ? new Icon(stream, wanted) : new Icon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
