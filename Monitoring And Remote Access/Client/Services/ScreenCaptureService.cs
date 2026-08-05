using System.Drawing.Imaging;

namespace Client.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    public string CaptureBase64()
    {
        Rectangle bounds = Screen.PrimaryScreen!.Bounds;
        using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 40L);

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
