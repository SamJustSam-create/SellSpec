using System.Windows;
using System.Windows.Threading;
using SellSpec.App.Services;
using SellSpec.App.ViewModels;
using SellSpec.Core.Diagnostics;

namespace SellSpec.App;

public partial class App : Application
{
    private FileScanLogger? _logger;
    private ShellViewModel? _shell;

    public App() : this(StartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray()))
    {
    }

    public App(StartupOptions options)
    {
        Options = options;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) => { _logger?.Log(LogLevel.Error, "App", "Unobserved task exception.", e.Exception); e.SetObserved(); };
    }

    public StartupOptions Options { get; }

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths(Options.Portable);
        var settingsStore = new Core.Settings.JsonSettingsStore(paths.SettingsFile);
        var settings = settingsStore.Load();
        _logger = new FileScanLogger(paths.LogDirectory, settings.EnableDiagnosticLogging);
        _logger.Log(LogLevel.Info, "App", $"SellSpec {AppInfo.Version} starting (demo: {Options.DemoMode}, portable: {paths.IsPortable}).");

        var theme = new ThemeManager(this);
        theme.Apply(settings.Theme);

        var answersStore = new Core.Sessions.JsonSavedAnswersStore(paths.AnswersFile);
        var shell = new ShellViewModel(paths, settingsStore, answersStore, settings, _logger, theme, Options);
        _shell = shell;
        var window = new MainWindow { DataContext = shell };
        MainWindow = window;
        window.Show();

        _logger.Log(LogLevel.Info, "App", $"Main window shown after {Program.StartupStopwatch.ElapsedMilliseconds} ms.");
        _ = shell.InitialiseAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Persistence.Flush();
        _logger?.Log(LogLevel.Info, "App", "Exiting.");
        _logger?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Log(LogLevel.Error, "App", "Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            "SellSpec ran into a problem and could not complete that action.\n\n" +
            "Nothing has been changed on your computer. You can keep using the app; if the problem repeats, use Settings to open the log folder and include the latest log when reporting it.",
            "SellSpec", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Log(LogLevel.Error, "App", "Fatal exception.", e.ExceptionObject as Exception);
    }
}
