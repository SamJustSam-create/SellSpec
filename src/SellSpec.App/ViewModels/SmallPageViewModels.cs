using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.App.Services;
using SellSpec.Core.Listing;
using SellSpec.Core.Privacy;
using SellSpec.Core.Settings;

namespace SellSpec.App.ViewModels;

public sealed partial class ChecklistItemViewModel : ObservableObject
{
    public ChecklistItemViewModel(ChecklistItem item)
    {
        Item = item;
    }

    public ChecklistItem Item { get; }
    public string Title => Item.Title;
    public string Description => Item.Description;
    public bool HasSettingsLink => Item.SettingsUri is not null;
    public string SettingsLabel => Item.SettingsLabel ?? "Open settings";

    [ObservableProperty] private bool _isDone;

    [RelayCommand]
    private void OpenSettings()
    {
        if (Item.SettingsUri is not null) ExternalLauncher.OpenUri(Item.SettingsUri);
    }
}

public sealed partial class PrivacyChecklistViewModel : ObservableObject
{
    public PrivacyChecklistViewModel()
    {
        Items = new ObservableCollection<ChecklistItemViewModel>(PreSaleChecklist.Items.Select(i => new ChecklistItemViewModel(i)));
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DoneCount));
        }
    }

    public ObservableCollection<ChecklistItemViewModel> Items { get; }
    public int DoneCount => Items.Count(i => i.IsDone);
    public int TotalCount => Items.Count;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private bool _loading;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Marketplaces = new ObservableCollection<MarketplaceOption>(Enum.GetValues<Marketplace>().Select(m => new MarketplaceOption(m, MarketplaceTemplate.DisplayName(m))));
        Load(shell.Session.Settings);
        shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult)) OnPropertyChanged(nameof(CanSaveDiagnostics));
        };
    }

    public ObservableCollection<MarketplaceOption> Marketplaces { get; }
    public string DataFolder => _shell.Paths.DataDirectory;
    public string LogFolder => _shell.Paths.LogDirectory;
    public bool IsPortable => _shell.Paths.IsPortable;
    public string StorageModeLabel => IsPortable ? "Portable mode: settings and logs are kept in the data folder next to SellSpec.exe." : "Settings and logs are kept in your local app data folder.";

    [ObservableProperty] private ThemeMode _theme;
    [ObservableProperty] private MarketplaceOption _defaultMarketplace = null!;
    [ObservableProperty] private int _ebayTitleLimit;
    [ObservableProperty] private int _facebookTitleLimit;
    [ObservableProperty] private int _gumtreeTitleLimit;
    [ObservableProperty] private bool _includeSellSpecFooter;
    [ObservableProperty] private bool _includeBatteryHealth;
    [ObservableProperty] private bool _includeWindowsActivation;
    [ObservableProperty] private string _currencySymbol = "$";
    [ObservableProperty] private bool _enableDiagnosticLogging;
    [ObservableProperty] private bool _showProvenanceLabels;
    [ObservableProperty] private string? _statusMessage;

    // Detection problem reporting (not persisted; excluded from settings saves).
    [ObservableProperty] private string _diagnosticsNotes = string.Empty;
    [ObservableProperty] private string? _diagnosticsStatus;

    public bool CanSaveDiagnostics => _shell.Session.ScanResult is not null;
    public string IssueTrackerUrl => ExternalLauncher.IssueTrackerUrl;

    private void Load(AppSettings s)
    {
        _loading = true;
        Theme = s.Theme;
        DefaultMarketplace = Marketplaces.First(m => m.Value == s.DefaultMarketplace);
        EbayTitleLimit = s.EbayTitleLimit;
        FacebookTitleLimit = s.FacebookTitleLimit;
        GumtreeTitleLimit = s.GumtreeTitleLimit;
        IncludeSellSpecFooter = s.IncludeSellSpecFooter;
        IncludeBatteryHealth = s.IncludeBatteryHealthInListing;
        IncludeWindowsActivation = s.IncludeWindowsActivationInListing;
        CurrencySymbol = s.CurrencySymbol;
        EnableDiagnosticLogging = s.EnableDiagnosticLogging;
        ShowProvenanceLabels = s.ShowProvenanceLabels;
        _loading = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(StatusMessage) or nameof(DiagnosticsNotes) or nameof(DiagnosticsStatus) or nameof(CanSaveDiagnostics)) return;
        Save();
    }

    [RelayCommand]
    private void SaveDiagnosticsBundle()
    {
        var result = _shell.Session.ScanResult;
        if (result is null)
        {
            DiagnosticsStatus = "Scan the device first.";
            return;
        }

        string? logText = null;
        if (_shell.Logger is FileScanLogger { CurrentFile: { } logFile } && System.IO.File.Exists(logFile))
        {
            try
            {
                using var stream = new System.IO.FileStream(logFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var reader = new System.IO.StreamReader(stream);
                logText = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                logText = null;
            }
        }

        Core.Diagnostics.DiagnosticsBundle bundle;
        try
        {
            bundle = Core.Diagnostics.DiagnosticsBundleBuilder.Build(new Core.Diagnostics.DiagnosticsBundleRequest(result, AppInfo.Version, logText, DiagnosticsNotes));
        }
        catch (Core.Privacy.RedactionException ex)
        {
            DiagnosticsStatus = "Not saved: your notes contain " + string.Join(", ", ex.Findings) + ". Please remove it; the bundle must not carry identifiers.";
            return;
        }

        var name = $"SellSpec diagnostics - {result.Snapshot.Identity.DisplayName} - {DateTime.Now:yyyy-MM-dd}";
        var path = _shell.Export.AskForPath(name, "Zip archive (*.zip)|*.zip", ".zip");
        if (path is null) return;
        try
        {
            _shell.Export.WriteBytes(path, bundle.Zip, bundle.CombinedText, result.Snapshot.Private);
            _shell.RememberExportFolder();
            DiagnosticsStatus = $"Saved {System.IO.Path.GetFileName(path)} ({bundle.Zip.Length / 1024.0:0.#} KB). Attach it to a detection report on GitHub.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            DiagnosticsStatus = "The file could not be saved to that location.";
        }
    }

    [RelayCommand]
    private void OpenIssueTracker()
    {
        if (!ExternalLauncher.OpenUri(ExternalLauncher.IssueTrackerUrl))
        {
            DiagnosticsStatus = $"Your browser could not be opened. Visit {ExternalLauncher.IssueTrackerUrl} manually.";
        }
    }

    private void Save()
    {
        var settings = _shell.Session.Settings with
        {
            Theme = Theme,
            DefaultMarketplace = DefaultMarketplace?.Value ?? Marketplace.General,
            EbayTitleLimit = Math.Clamp(EbayTitleLimit, 20, 200),
            FacebookTitleLimit = Math.Clamp(FacebookTitleLimit, 20, 200),
            GumtreeTitleLimit = Math.Clamp(GumtreeTitleLimit, 20, 200),
            IncludeSellSpecFooter = IncludeSellSpecFooter,
            IncludeBatteryHealthInListing = IncludeBatteryHealth,
            IncludeWindowsActivationInListing = IncludeWindowsActivation,
            CurrencySymbol = string.IsNullOrWhiteSpace(CurrencySymbol) ? "$" : CurrencySymbol.Trim(),
            EnableDiagnosticLogging = EnableDiagnosticLogging,
            ShowProvenanceLabels = ShowProvenanceLabels,
        };
        if (_shell.Logger is FileScanLogger fileLogger) fileLogger.Enabled = EnableDiagnosticLogging;
        _shell.SaveSettings(settings);
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private void OpenDataFolder() => ExternalLauncher.OpenFolder(DataFolder);

    [RelayCommand]
    private void OpenLogFolder() => ExternalLauncher.OpenFolder(LogFolder);

    [RelayCommand]
    private void ResetDefaults()
    {
        Load(AppSettings.Default);
        Save();
    }
}

public sealed partial class AboutViewModel : ObservableObject
{
    public AboutViewModel(ShellViewModel shell)
    {
        Version = AppInfo.Version;
        IsElevated = shell.Session.IsElevated;
        IsDemo = shell.Session.IsDemo;
        Runtime = $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} · {Environment.OSVersion.VersionString}";
    }

    public string Version { get; }
    public string Runtime { get; }
    public bool IsElevated { get; }
    public bool IsDemo { get; }
    public string Tagline => AppInfo.Tagline;

    public string PrivacyStatement =>
        "SellSpec works entirely on this computer. It does not use the internet, has no accounts, and sends nothing anywhere. " +
        "It reads hardware details through standard Windows interfaces and never collects personal files, browser data, passwords, " +
        "product keys, network addresses or saved Wi-Fi networks. Serial numbers are shown only to you and are never written to listings, " +
        "reports or exports.";

    public string ThirdPartyNotices =>
        "SellSpec is built with .NET and WPF (Microsoft, MIT licence).\n" +
        "System.Management (Microsoft, MIT licence) provides access to Windows Management Instrumentation.\n" +
        "CommunityToolkit.Mvvm (.NET Foundation, MIT licence) provides MVVM helpers.\n" +
        "Icons are drawn with the Segoe Fluent Icons / Segoe MDL2 Assets fonts that ship with Windows.";
}
