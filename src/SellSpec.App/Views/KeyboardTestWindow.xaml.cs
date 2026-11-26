using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SellSpec.App.Views;

/// <summary>Visual keyboard that lights keys as they are pressed. No key presses are stored beyond the pressed set.</summary>
public partial class KeyboardTestWindow : Window
{
    private const double Unit = 46;
    private readonly Dictionary<Key, Border> _keys = new();
    private readonly HashSet<Key> _pressed = new();

    private static readonly (string Label, Key Key, double Width)[][] Layout =
    {
        new[] { ("Esc", Key.Escape, 1.0), ("F1", Key.F1, 1.0), ("F2", Key.F2, 1.0), ("F3", Key.F3, 1.0), ("F4", Key.F4, 1.0), ("F5", Key.F5, 1.0), ("F6", Key.F6, 1.0), ("F7", Key.F7, 1.0), ("F8", Key.F8, 1.0), ("F9", Key.F9, 1.0), ("F10", Key.F10, 1.0), ("F11", Key.F11, 1.0), ("F12", Key.F12, 1.0), ("PrtSc", Key.Snapshot, 1.0), ("Ins", Key.Insert, 1.0), ("Del", Key.Delete, 1.0) },
        new[] { ("`", Key.Oem3, 1.0), ("1", Key.D1, 1.0), ("2", Key.D2, 1.0), ("3", Key.D3, 1.0), ("4", Key.D4, 1.0), ("5", Key.D5, 1.0), ("6", Key.D6, 1.0), ("7", Key.D7, 1.0), ("8", Key.D8, 1.0), ("9", Key.D9, 1.0), ("0", Key.D0, 1.0), ("-", Key.OemMinus, 1.0), ("=", Key.OemPlus, 1.0), ("Backspace", Key.Back, 2.0), ("Home", Key.Home, 1.0) },
        new[] { ("Tab", Key.Tab, 1.5), ("Q", Key.Q, 1.0), ("W", Key.W, 1.0), ("E", Key.E, 1.0), ("R", Key.R, 1.0), ("T", Key.T, 1.0), ("Y", Key.Y, 1.0), ("U", Key.U, 1.0), ("I", Key.I, 1.0), ("O", Key.O, 1.0), ("P", Key.P, 1.0), ("[", Key.OemOpenBrackets, 1.0), ("]", Key.Oem6, 1.0), ("\\", Key.Oem5, 1.5), ("PgUp", Key.PageUp, 1.0) },
        new[] { ("Caps", Key.CapsLock, 1.75), ("A", Key.A, 1.0), ("S", Key.S, 1.0), ("D", Key.D, 1.0), ("F", Key.F, 1.0), ("G", Key.G, 1.0), ("H", Key.H, 1.0), ("J", Key.J, 1.0), ("K", Key.K, 1.0), ("L", Key.L, 1.0), (";", Key.Oem1, 1.0), ("'", Key.Oem7, 1.0), ("Enter", Key.Return, 2.25), ("PgDn", Key.PageDown, 1.0) },
        new[] { ("Shift", Key.LeftShift, 2.25), ("Z", Key.Z, 1.0), ("X", Key.X, 1.0), ("C", Key.C, 1.0), ("V", Key.V, 1.0), ("B", Key.B, 1.0), ("N", Key.N, 1.0), ("M", Key.M, 1.0), (",", Key.OemComma, 1.0), (".", Key.OemPeriod, 1.0), ("/", Key.Oem2, 1.0), ("Shift", Key.RightShift, 1.75), ("↑", Key.Up, 1.0), ("End", Key.End, 1.0) },
        new[] { ("Ctrl", Key.LeftCtrl, 1.25), ("Win", Key.LWin, 1.25), ("Alt", Key.LeftAlt, 1.25), ("Space", Key.Space, 6.0), ("Alt", Key.RightAlt, 1.25), ("Ctrl", Key.RightCtrl, 1.25), ("←", Key.Left, 1.0), ("↓", Key.Down, 1.0), ("→", Key.Right, 1.0) },
    };

    public KeyboardTestWindow()
    {
        InitializeComponent();
        BuildKeyboard();
        PreviewKeyDown += OnKey;
        PreviewKeyUp += OnKey;
        Loaded += (_, _) => { Focus(); UpdateCount(); };
    }

    public int PressedCount => _pressed.Count;
    public int TotalKeys => _keys.Count;

    private void BuildKeyboard()
    {
        foreach (var row in Layout)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (label, key, width) in row)
            {
                var border = new Border
                {
                    Width = Unit * width - 6,
                    Height = Unit - 6,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                border.SetResourceReference(BackgroundProperty, "ControlBackgroundBrush");
                border.SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
                ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                panel.Children.Add(border);
                _keys[key] = border;
            }
            KeyboardPanel.Children.Add(panel);
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
        if (_keys.TryGetValue(key, out var border))
        {
            if (_pressed.Add(key))
            {
                border.SetResourceReference(BackgroundProperty, "AccentBrush");
                border.SetResourceReference(BorderBrushProperty, "AccentBrush");
                ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentBrush");
                UpdateCount();
            }
        }
        // Keep focus on the window: Tab, arrows, Enter and Space must not drive the buttons.
        e.Handled = true;
    }

    private void UpdateCount()
    {
        CountText.Text = $"{_pressed.Count} of {_keys.Count} keys registered";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _pressed.Clear();
        foreach (var border in _keys.Values)
        {
            border.SetResourceReference(BackgroundProperty, "ControlBackgroundBrush");
            border.SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
            ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        }
        UpdateCount();
        Focus();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
