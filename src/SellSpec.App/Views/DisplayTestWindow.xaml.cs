using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SellSpec.App.Views;

/// <summary>Full-screen solid colours for spotting dead pixels, bleed and marks.</summary>
public partial class DisplayTestWindow : Window
{
    private static readonly (string Name, Color Colour)[] Colours =
    {
        ("Red", Colors.Red),
        ("Green", Color.FromRgb(0, 255, 0)),
        ("Blue", Colors.Blue),
        ("White", Colors.White),
        ("Black", Colors.Black),
    };

    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private int _index;

    public DisplayTestWindow()
    {
        InitializeComponent();
        _hintTimer.Tick += (_, _) => { Hint.Visibility = Visibility.Collapsed; _hintTimer.Stop(); };
        MouseLeftButtonUp += (_, _) => Next();
        MouseRightButtonUp += (_, _) => Previous();
        PreviewKeyDown += OnKeyDown;
        MouseMove += (_, _) => ShowHint();
        Loaded += (_, _) => { Focus(); Apply(); };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
            case Key.Up:
            case Key.Back:
                Previous();
                break;
            default:
                Next();
                break;
        }
        e.Handled = true;
    }

    private void Next()
    {
        if (_index >= Colours.Length - 1)
        {
            Close();
            return;
        }
        _index++;
        Apply();
    }

    private void Previous()
    {
        if (_index == 0) return;
        _index--;
        Apply();
    }

    private void Apply()
    {
        Background = new SolidColorBrush(Colours[_index].Colour);
        HintTitle.Text = $"{Colours[_index].Name} ({_index + 1} of {Colours.Length})";
        ShowHint();
    }

    private void ShowHint()
    {
        Hint.Visibility = Visibility.Visible;
        _hintTimer.Stop();
        _hintTimer.Start();
    }
}
