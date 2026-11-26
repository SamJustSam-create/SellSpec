using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.App.Services;
using SellSpec.Core.Listing;
using SellSpec.Core.Privacy;

namespace SellSpec.App.ViewModels;

public sealed record MarketplaceOption(Marketplace Value, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class CreateListingViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private bool _suppressEditTracking;
    private Listing? _lastGenerated;

    public CreateListingViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Marketplaces = new ObservableCollection<MarketplaceOption>(Enum.GetValues<Marketplace>().Select(m => new MarketplaceOption(m, MarketplaceTemplate.DisplayName(m))));
        _selectedMarketplace = Marketplaces.First(m => m.Value == shell.Session.Settings.DefaultMarketplace);
        Questionnaire = shell.Session.Questionnaire;
        Questionnaire.Changed += (_, _) => RegenerateIfUntouched();
        shell.Session.Tests.Changed += (_, _) => RegenerateIfUntouched();
        shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult) || e.PropertyName == nameof(AppSession.Settings))
            {
                IsEdited = false;
                Regenerate();
            }
        };
        Regenerate();
    }

    public ShellViewModel Shell => _shell;
    public QuestionnaireViewModel Questionnaire { get; }
    public ObservableCollection<MarketplaceOption> Marketplaces { get; }
    public ObservableCollection<string> Warnings { get; } = new();

    [ObservableProperty] private MarketplaceOption _selectedMarketplace;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isEdited;
    [ObservableProperty] private string _titleCounter = string.Empty;
    [ObservableProperty] private bool _titleTooLong;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _showQuestionnaire = true;

    public bool HasWarnings => Warnings.Count > 0;
    public bool HasSnapshot => _shell.Session.Snapshot is not null;

    partial void OnSelectedMarketplaceChanged(MarketplaceOption value)
    {
        IsEdited = false;
        Regenerate();
    }

    partial void OnTitleChanged(string value)
    {
        if (!_suppressEditTracking) IsEdited = true;
        UpdateTitleCounter();
    }

    partial void OnDescriptionChanged(string value)
    {
        if (!_suppressEditTracking) IsEdited = true;
    }

    private void RegenerateIfUntouched()
    {
        if (!IsEdited) Regenerate();
        else StatusMessage = "Answers changed. Use Regenerate to rebuild the listing (your edits will be replaced).";
    }

    [RelayCommand]
    public void Regenerate()
    {
        var s = _shell.Session.Snapshot;
        Warnings.Clear();
        if (s is null)
        {
            _suppressEditTracking = true;
            Title = string.Empty;
            Description = "Scan the device first to generate a listing.";
            _suppressEditTracking = false;
            OnPropertyChanged(nameof(HasSnapshot));
            return;
        }

        var listing = ListingGenerator.Generate(new ListingRequest(s, _shell.Session.Condition, _shell.Session.Tests.Results, SelectedMarketplace.Value, _shell.Session.Settings.ToListingOptions()));
        _lastGenerated = listing;
        _suppressEditTracking = true;
        Title = listing.Title;
        Description = listing.Description;
        _suppressEditTracking = false;
        IsEdited = false;
        foreach (var w in listing.Warnings) Warnings.Add(w);
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasSnapshot));
        StatusMessage = null;
        UpdateTitleCounter();
    }

    private void UpdateTitleCounter()
    {
        var limit = _lastGenerated?.TitleLimit;
        TitleCounter = limit is { } l ? $"{Title.Length} / {l} characters" : $"{Title.Length} characters";
        TitleTooLong = limit is { } lim && Title.Length > lim;
    }

    [RelayCommand]
    private void CopyTitle() => Copy(Title, "Title copied.");

    [RelayCommand]
    private void CopyDescription() => Copy(Description, "Description copied.");

    [RelayCommand]
    private void CopyAll() => Copy(Title + Environment.NewLine + Environment.NewLine + Description, "Title and description copied.");

    private void Copy(string text, string success)
    {
        var s = _shell.Session.Snapshot;
        if (s is not null)
        {
            var leaks = Redactor.FindLeaks(text, s.Private);
            if (leaks.Count > 0)
            {
                StatusMessage = "The text contains a private identifier (" + string.Join(", ", leaks) + "). Remove it before copying.";
                return;
            }
        }
        StatusMessage = ClipboardService.TrySetText(text) ? success : "The clipboard is busy. Try again in a moment.";
    }

    [RelayCommand]
    private void ExportText()
    {
        var s = _shell.Session.Snapshot;
        if (s is null) return;
        var path = _shell.Export.AskForPath($"{s.Identity.DisplayName} listing", "Text files (*.txt)|*.txt", ".txt");
        if (path is null) return;
        try
        {
            _shell.Export.WriteText(path, Title + Environment.NewLine + Environment.NewLine + Description, s.Private);
            _shell.RememberExportFolder();
            StatusMessage = $"Saved to {path}";
        }
        catch (RedactionException ex)
        {
            StatusMessage = "Not saved: the text contains " + string.Join(", ", ex.Findings) + ". Remove it and try again.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusMessage = "The file could not be saved to that location.";
        }
    }

    [RelayCommand]
    private void ResetQuestionnaire()
    {
        Questionnaire.Reset();
        IsEdited = false;
        Regenerate();
    }
}
