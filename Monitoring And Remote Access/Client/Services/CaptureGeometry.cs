namespace Client.Services;

/// <summary>
/// The one definition of "the screen" for remote support.
///
/// Capture and input have to agree on this. They did not: the capture took
/// <c>Screen.PrimaryScreen.Bounds</c> while input mapped the teacher's click
/// across <c>SystemInformation.VirtualScreen</c>. On a single display those are
/// the same rectangle and everything worked, so the mismatch was invisible in
/// testing. On two 1920-wide displays the teacher saw 1920 pixels and their
/// clicks were spread over 3840: a click on the centre of what they could see
/// landed on the first pixel of a monitor they could not, and the right-hand
/// edge landed at the far edge of it.
///
/// Taking the whole virtual desktop also removes a blind spot — a second
/// monitor is no longer unwatched.
/// </summary>
public static class CaptureGeometry
{
    /// <summary>Every display, as one rectangle. Its origin can be negative when a monitor sits left of or above the primary one.</summary>
    public static Rectangle DesktopBounds => SystemInformation.VirtualScreen;

    /// <summary>
    /// Maps a coordinate normalised to 0..10000 onto a screen axis.
    /// </summary>
    /// <param name="normalizedCoordinate">The teacher's click, as a fraction of the image they were shown.</param>
    /// <param name="screenSize">The length of that axis in pixels.</param>
    public static int ScaleCoordinate(int normalizedCoordinate, int screenSize) =>
        screenSize <= 1 ? 0 : (int)Math.Round(Math.Clamp(normalizedCoordinate, 0, 10000) / 10000d * (screenSize - 1));

    /// <summary>
    /// Turns a normalised click into an absolute desktop point, inside the same
    /// rectangle <see cref="DesktopBounds"/> hands to the capture.
    /// </summary>
    public static Point ToDesktopPoint(int normalizedX, int normalizedY)
    {
        var bounds = DesktopBounds;
        return new Point(
            bounds.Left + ScaleCoordinate(normalizedX, bounds.Width),
            bounds.Top + ScaleCoordinate(normalizedY, bounds.Height));
    }
}
