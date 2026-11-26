using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.App.Services;
using SellSpec.Core.Demo;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Sessions;
using SellSpec.Core.Settings;
using ThemeMode = SellSpec.Core.Settings.ThemeMode;
using SellSpec.Windows;

namespace SellSpec.App.ViewModels;

public sealed record NavigationItem(string Key, string Title, string Glyph, bool IsFooter = false);

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISavedAnswersStore _answers;
    private readonly IScanLogger _logger;
    private readonly ThemeManager _theme;
    private readonly StartupOptions _options;
    private readonly Dictionary<string, object> _pages = new();
    private CancellationTokenSource? _scanCts;
    private int _scanCount;

    public ShellViewModel(AppPaths paths, ISettingsStore settingsStore, ISavedAnswersStore answersStore, AppSettings settings, IScanLogger logger, ThemeManager theme, StartupOptions options)
    {
        Paths = paths;
        _settingsStore = settingsStore;
        _answers = answersStore;
        _logger = logger;
        _theme = theme;
        _options = options;
        Export = new ExportService(logger) { LastDirectory = settings.LastExportFolder };
        Session = new AppSession(options.DemoMode, WindowsHardwareProviders.IsElevated()) { Settings = settings };
        Persistence = new AnswerPersistence(Session, answersStore, logger);

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("overview", "Overview", ""),
            new("specifications", "Specifications", ""),
            new("health", "Device Health", ""),
            new("tests", "Hardware Tests", ""),
            new("listing", "Create Listing", ""),
            new("report", "Export Report", ""),
            new("privacy", "Pre-sale Checklist", ""),
            new("settings", "Settings", "", IsFooter: true),
            new("about", "About", "", IsFooter: true),
        };
        SelectedItem = NavigationItems[0];
    }

    public AppPaths Paths { get; }
    public AppSession Session { get; }
    public ExportService Export { get; }

    /// <summary>Remembers answers and test results per device model between sessions (Core.Sessions).</summary>
    public AnswerPersistence Persistence { get; }
    public ThemeManager Theme => _theme;
    public IScanLogger Logger => _logger;
    public StartupOptions Options => _options;

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public IEnumerable<NavigationItem> MainItems => NavigationItems.Where(n => !n.IsFooter);
    public IEnumerable<NavigationItem> FooterItems => NavigationItems.Where(n => n.IsFooter);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavigationItem? _selectedItem;

    public object? CurrentPage => SelectedItem is null ? null : GetPage(SelectedItem.Key);

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanStatusText = "Preparing to scan…";
    [ObservableProperty] private string _scanSummaryText = string.Empty;
    [ObservableProperty] private bool _isNavCompact;

    public bool IsDemo => Session.IsDemo;
    public string AppVersion => AppInfo.Version;

    /// <summary>Set by the main window so capture mode can render pages to PNG.</summary>
    public Func<string, Task>? PageCaptureHandler { get; set; }

    public async Task InitialiseAsync()
    {
        await ScanAsync();
        if (_options.CaptureDirectory is not null && PageCaptureHandler is not null)
        {
            await CapturePagesAsync(_options.CaptureDirectory);
            Application.Current.Shutdown();
        }
    }

    public object GetPage(string key)
    {
        if (_pages.TryGetValue(key, out var page)) return page;
        page = key switch
        {
            "overview" => new OverviewViewModel(this),
            "specifications" => new SpecificationsViewModel(this),
            "health" => new DeviceHealthViewModel(this),
            "tests" => new HardwareTestsViewModel(this),
            "listing" => new CreateListingViewModel(this),
            "report" => new ExportReportViewModel(this),
            "privacy" => new PrivacyChecklistViewModel(),
            "settings" => new SettingsViewModel(this),
            "about" => new AboutViewModel(this),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        _pages[key] = page;
        return page;
    }

    [RelayCommand]
    public void Navigate(string key)
    {
        var item = NavigationItems.FirstOrDefault(n => n.Key == key);
        if (item is not null) SelectedItem = item;
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    public async Task RescanAsync()
    {
        await ScanAsync();
    }

    private bool CanRescan() => !IsScanning;

    [RelayCommand]
    public void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    public void RescanElevated()
    {
        // The elevated instance restores the remembered answers, so make sure they are on disk first.
        Persistence.Flush();
        if (!ExternalLauncher.RelaunchElevated())
        {
            ScanSummaryText = "Administrator rescan was not started.";
        }
    }

    [RelayCommand]
    private void RestoreSavedAnswers()
    {
        if (Session.SavedAnswersOffer is not { } saved) return;
        Session.ApplySaved(saved);
        Session.SavedAnswersStatus = $"Restored your answers and test results from {saved.SavedAt.LocalDateTime:d MMMM yyyy}. Check that they still describe this device.";
        _logger.Log(LogLevel.Info, "Answers", "Remembered answers restored by the seller.");
    }

    [RelayCommand]
    private void DiscardSavedAnswers()
    {
        if (Session.SavedAnswersOffer is not { } saved) return;
        _answers.Remove(saved.DeviceKey);
        Session.SavedAnswersOffer = null;
        Session.SavedAnswersStatus = "The saved answers for this model were discarded.";
    }

    /// <summary>Forgets every remembered device (Settings). The current session is untouched.</summary>
    public void ClearSavedAnswers()
    {
        _answers.Clear();
        Session.SavedAnswersOffer = null;
        Session.SavedAnswersStatus = null;
    }

    private void OfferSavedAnswers(string? deviceKey, bool isFirstScan)
    {
        Session.SavedAnswersOffer = null;
        if (!SavedAnswersPolicy.CanRemember(Session.Settings.RememberAnswers, Session.IsDemo, deviceKey)) return;

        var saved = _answers.Find(deviceKey!);
        var action = SavedAnswersPolicy.Decide(saved, Session.HasAnswers, _options.ElevatedRescan && isFirstScan);
        // The key is model information only (see Core.Sessions.DeviceKey), so it is safe to log.
        _logger.Log(LogLevel.Info, "Answers", $"Device key '{deviceKey}': remembered answers {(saved is null ? "none" : "found")}, action {action}.");
        switch (action)
        {
            case SavedAnswersAction.Restore:
                Session.ApplySaved(saved!);
                Session.SavedAnswersStatus = "Restored the answers and test results you entered before the administrator rescan.";
                _logger.Log(LogLevel.Info, "Answers", "Remembered answers restored after the elevated rescan.");
                break;
            case SavedAnswersAction.Offer:
                Session.SavedAnswersOffer = saved;
                break;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Session.Settings = settings;
        try
        {
            _settingsStore.Save(settings with { LastExportFolder = Export.LastDirectory });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Settings", "Settings could not be saved.", ex);
        }
        _theme.Apply(settings.Theme);
    }

    public void RememberExportFolder()
    {
        if (Export.LastDirectory is not null && Export.LastDirectory != Session.Settings.LastExportFolder)
        {
            SaveSettings(Session.Settings with { LastExportFolder = Export.LastDirectory });
        }
    }

    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        RescanCommand.NotifyCanExecuteChanged();
        ScanProgress = 0;
        ScanStatusText = Session.IsDemo ? "Loading demonstration device…" : "Scanning this computer…";
        Session.SavedAnswersStatus = null;
        _scanCts = new CancellationTokenSource();

        try
        {
            ScanResult result;
            if (Session.IsDemo)
            {
                await Task.Delay(300, _scanCts.Token);
                var snapshot = DemoDeviceFactory.CreateLaptop();
                result = new ScanResult { Snapshot = snapshot, ScannedAt = DateTimeOffset.Now, Duration = TimeSpan.FromMilliseconds(300), IsElevated = false };
                ScanProgress = 1;
            }
            else
            {
                var scanner = WindowsHardwareProviders.CreateScanner(_logger);
                var progress = new Progress<ScanProgress>(p =>
                {
                    ScanProgress = p.Fraction;
                    ScanStatusText = p.Stage == ScanStage.Started ? $"Reading {p.Component.ToLowerInvariant()}…" : $"{p.Completed} of {p.Total} components checked";
                });
                result = await scanner.ScanAsync(progress, _scanCts.Token);
            }

            // Answers and test results describe a device: keep them across a rescan of the same model, drop them
            // when the device changed, and offer back what an earlier session remembered.
            var deviceKey = DeviceKey.For(result.Snapshot);
            if (!SavedAnswersPolicy.KeepSessionAcrossRescan(Session.CurrentDeviceKey, deviceKey))
            {
                Session.Tests.Clear();
                Session.Questionnaire.Reset();
            }
            Session.CurrentDeviceKey = deviceKey;
            Session.ScanResult = result;
            OfferSavedAnswers(deviceKey, isFirstScan: _scanCount == 0);
            _scanCount++;
            ScanSummaryText = result.WasCancelled
                ? "Scan cancelled. Some details are missing."
                : $"Scanned in {result.Duration.TotalSeconds:0.0} s" + (result.Issues.Count > 0 ? $" · {result.Issues.Count} note{(result.Issues.Count == 1 ? string.Empty : "s")}" : string.Empty);
            ScanStatusText = result.WasCancelled ? "Scan cancelled" : "Scan complete";
            _logger.Log(LogLevel.Info, "App", $"Scan presented: {result.Snapshot.Identity.DisplayName} in {result.Duration.TotalMilliseconds:0} ms.");
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "Scan cancelled";
            ScanSummaryText = "Scan cancelled before it finished.";
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "App", "Scan failed unexpectedly.", ex);
            ScanStatusText = "Scan failed";
            ScanSummaryText = "The scan could not be completed. You can try again with Rescan.";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
            RescanCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task CapturePagesAsync(string directory)
    {
        foreach (var item in NavigationItems)
        {
            SelectedItem = item;
            await Task.Delay(400);
            await PageCaptureHandler!(System.IO.Path.Combine(directory, $"{Array.IndexOf(NavigationItems.ToArray(), item) + 1:00}-{item.Key}.png"));
        }

        // Dark theme samples.
        _theme.Apply(ThemeMode.Dark);
        foreach (var key in new[] { "overview", "health", "listing" })
        {
            Navigate(key);
            await Task.Delay(400);
            await PageCaptureHandler!(System.IO.Path.Combine(directory, $"dark-{key}.png"));
        }
        _theme.Apply(Session.Settings.Theme);
        Navigate("overview");

        // Keyboard test window layout.
        var keyboard = new Views.KeyboardTestWindow { Owner = Application.Current.MainWindow };
        keyboard.Show();
        await Task.Delay(400);
        CaptureHelper.RenderToPng((FrameworkElement)keyboard.Content, System.IO.Path.Combine(directory, "keyboard-test.png"));
        keyboard.Close();

        // Sample exports in every format, written through the same redaction-checked path as the UI.
        if (Session.Snapshot is { } s)
        {
            var report = Core.Reports.ReportBuilder.Build(new Core.Reports.ReportRequest(s, Session.Condition, Session.Tests.Results, Session.ScannedAt, AppInfo.Version, Session.IsDemo));
            Export.WriteBytes(System.IO.Path.Combine(directory, "report.pdf"), Core.Reports.PdfReportWriter.Write(report), Core.Reports.TextReportWriter.Write(report), s.Private);
            Export.WriteText(System.IO.Path.Combine(directory, "report.txt"), Core.Reports.TextReportWriter.Write(report), s.Private);
            Export.WriteText(System.IO.Path.Combine(directory, "report.json"), Core.Reports.JsonReportWriter.Write(report), s.Private);
            if (GetPage("report") is ExportReportViewModel { PngRenderer: { } png })
            {
                Export.WriteBytes(System.IO.Path.Combine(directory, "report.png"), png(report), Core.Reports.TextReportWriter.Write(report), s.Private);
            }
            foreach (var marketplace in Enum.GetValues<Core.Listing.Marketplace>())
            {
                var listing = Core.Listing.ListingGenerator.Generate(new Core.Listing.ListingRequest(s, Session.Condition, Session.Tests.Results, marketplace, Session.Settings.ToListingOptions()));
                Export.WriteText(System.IO.Path.Combine(directory, $"listing-{marketplace}.txt"), listing.CombinedText, s.Private);
            }
        }
    }
}
