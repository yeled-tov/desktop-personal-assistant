using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DesktopAssistant.Core;
using Forms = System.Windows.Forms;

namespace DesktopAssistant.Desktop;

/// <summary>A captured monitor. Bounds are physical pixels in virtual-screen coordinates.</summary>
public sealed record MonitorShot(BitmapSource Image, Int32Rect Bounds, bool Primary, string DeviceName);

/// <summary>A screenshot prepared for the model, with the mapping back to real screen pixels.</summary>
public sealed record AgentScreenshot(byte[] Jpeg, int Width, int Height, int OriginX, int OriginY, double Scale)
{
    public (int X, int Y) ToScreen(double x, double y) => (OriginX + (int)Math.Round(x / Scale), OriginY + (int)Math.Round(y / Scale));
}

public static class ScreenCapture
{
    public static IReadOnlyList<MonitorShot> CaptureMonitors()
    {
        var shots = new List<MonitorShot>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            shots.Add(new MonitorShot(CaptureRect(b.Left, b.Top, b.Width, b.Height), new Int32Rect(b.Left, b.Top, b.Width, b.Height), screen.Primary, screen.DeviceName));
        }
        return shots;
    }

    public static BitmapSource CaptureRect(int x, int y, int width, int height)
    {
        using var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { Native.DeleteObject(handle); }
    }

    /// <summary>Captures the monitor under the given window (or the primary one) scaled down for the model.</summary>
    public static AgentScreenshot CaptureForAgent(nint focusWindow = 0, int maxSide = 1568)
    {
        Forms.Screen screen;
        try { screen = focusWindow != 0 ? Forms.Screen.FromHandle(focusWindow) : Forms.Screen.FromPoint(Forms.Cursor.Position); }
        catch { screen = Forms.Screen.PrimaryScreen!; }
        var b = screen.Bounds;
        var image = CaptureRect(b.Left, b.Top, b.Width, b.Height);
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(b.Width, b.Height));
        var jpeg = EncodeJpeg(image, scale, 80);
        return new AgentScreenshot(jpeg, (int)Math.Round(b.Width * scale), (int)Math.Round(b.Height * scale), b.Left, b.Top, scale);
    }

    public static byte[] EncodeJpeg(BitmapSource source, double scale = 1.0, int quality = 85)
    {
        BitmapSource image = source;
        if (scale < 0.999)
        {
            image = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            image.Freeze();
        }
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Limits a screenshot to a size models accept while keeping text readable.</summary>
    public static byte[] EncodeForModel(BitmapSource source, int maxSide = 2048)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.PixelWidth, source.PixelHeight));
        return EncodeJpeg(source, scale, 85);
    }
}
