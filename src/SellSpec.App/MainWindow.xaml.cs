using System.Windows;
using SellSpec.App.ViewModels;

namespace SellSpec.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCompactNav();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.PageCaptureHandler = CapturePageAsync;
                UpdateCompactNav();
            }
        };
    }

    private void UpdateCompactNav()
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.IsNavCompact = ActualWidth < 1000;
        }
    }

    /// <summary>Developer capture mode: renders the whole window to a PNG so UI changes can be reviewed without a screen.</summary>
    private async Task CapturePageAsync(string path)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Services.CaptureHelper.RenderToPng(RootGrid, path);
    }
}
