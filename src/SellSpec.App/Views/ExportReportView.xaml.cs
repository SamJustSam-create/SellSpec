using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SellSpec.App.ViewModels;
using SellSpec.Core.Reports;

namespace SellSpec.App.Views;

public partial class ExportReportView : UserControl
{
    public ExportReportView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ExportReportViewModel vm)
            {
                vm.PngRenderer = RenderPng;
            }
        };
    }

    /// <summary>Renders a fresh, unscaled report document at 2× pixel density for a crisp image.</summary>
    private static byte[] RenderPng(DeviceReport report)
    {
        const double scale = 2.0;
        var view = new ReportDocumentView { DataContext = report };
        view.Measure(new Size(view.Width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, view.Width, view.DesiredSize.Height));
        view.UpdateLayout();

        var width = (int)Math.Ceiling(view.ActualWidth * scale);
        var height = (int)Math.Ceiling(view.ActualHeight * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
