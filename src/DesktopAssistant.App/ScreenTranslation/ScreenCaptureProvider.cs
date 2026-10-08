using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace DesktopAssistant.ScreenTranslation;

public sealed class ScreenCaptureProvider : IScreenCaptureProvider
{
    public Task<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = Forms.SystemInformation.VirtualScreen;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return Task.FromResult(new ScreenFrame(image, new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height)));
        }
        finally { Native.DeleteObject(handle); }
    }

    public async Task<ScreenFrame> CaptureWithoutAssistantAsync(CancellationToken cancellationToken = default)
    {
        var app = Application.Current;
        var main = app?.MainWindow;
        var wasVisible = false;
        if (main is not null)
        {
            wasVisible = await main.Dispatcher.InvokeAsync(() => main.IsVisible).Task;
            if (wasVisible) { await main.Dispatcher.InvokeAsync(main.Hide).Task; await Task.Delay(180, cancellationToken); }
        }
        try { return await CaptureAsync(cancellationToken); }
        finally
        {
            if (wasVisible && main is not null) await main.Dispatcher.InvokeAsync(() => { main.Show(); main.Activate(); }).Task;
        }
    }

    private static class Native
    {
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(IntPtr hObject);
    }
}
