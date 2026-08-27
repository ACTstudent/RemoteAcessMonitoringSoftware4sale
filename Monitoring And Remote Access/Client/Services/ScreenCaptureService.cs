using System.Drawing.Imaging;

namespace Client.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    public string CaptureBase64()
    {
        Rectangle bounds = Screen.PrimaryScreen!.Bounds;
        using (Bitmap captured = new Bitmap(bounds.Width, bounds.Height))
        {
            using (Graphics g = Graphics.FromImage(captured))
            {
                g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
            }

            const int maxWidth = 1280;
            var scale = Math.Min(1d, maxWidth / (double)captured.Width);
            var targetWidth = Math.Max(1, (int)Math.Round(captured.Width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(captured.Height * scale));
            using var bitmap = new Bitmap(targetWidth, targetHeight);
            using (var resizeGraphics = Graphics.FromImage(bitmap))
            {
                resizeGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                resizeGraphics.DrawImage(captured, 0, 0, targetWidth, targetHeight);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 35L);

                bitmap.Save(ms, jpegEncoder, encoderParams);
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid) return codec;
        }

        throw new InvalidOperationException("JPEG encoder not found.");
    }
}
