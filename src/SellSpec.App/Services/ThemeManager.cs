using System.Windows;
using Microsoft.Win32;
using SellSpec.Core.Settings;
using ThemeMode = SellSpec.Core.Settings.ThemeMode;

namespace SellSpec.App.Services;

/// <summary>Swaps the Light/Dark resource dictionary at runtime and follows the Windows app theme when asked to.</summary>
public sealed class ThemeManager
{
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private readonly Application _app;
    private ThemeMode _mode = ThemeMode.System;

    public ThemeManager(Application app)
    {
        _app = app;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _mode == ThemeMode.System)
            {
                _app.Dispatcher.BeginInvoke(() => Apply(ThemeMode.System));
            }
        };
    }

    public bool IsDark { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeMode mode)
    {
        _mode = mode;
        var dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };

        var dictionaries = _app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source is { } s && (s == LightUri || s == DarkUri || s.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) || s.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)));
        var replacement = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(1, replacement);
        }

        IsDark = dark;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}
