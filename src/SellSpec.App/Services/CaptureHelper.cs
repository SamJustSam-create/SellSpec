using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SellSpec.App.Services;

/// <summary>Developer-only helpers for the --capture switch: render visuals to PNG without a screen capture tool.</summary>
public static class CaptureHelper
{
    public static void RenderToPng(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var width = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
        if (width == 0 || height == 0) return;
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
