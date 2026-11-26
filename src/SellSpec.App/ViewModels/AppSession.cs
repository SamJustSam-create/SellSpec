using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SellSpec.Core.Detection;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Sessions;
using SellSpec.Core.Settings;
using SellSpec.Core.Testing;

namespace SellSpec.App.ViewModels;

/// <summary>Everything the pages share: the latest scan, the seller's answers, hardware test results and settings.</summary>
public sealed partial class AppSession : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Snapshot))]
    [NotifyPropertyChangedFor(nameof(HasScan))]
    private ScanResult? _scanResult;

    [ObservableProperty]
    private AppSettings _settings = AppSettings.Default;

    /// <summary>Answers remembered from an earlier session for this device model, waiting for the seller to restore or discard them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedAnswersOffer))]
    [NotifyPropertyChangedFor(nameof(SavedAnswersOfferDetail))]
    private SavedDeviceAnswers? _savedAnswersOffer;

    /// <summary>One-line note about remembered answers (restored, discarded, or why they could not be saved).</summary>
    [ObservableProperty]
    private string? _savedAnswersStatus;

    public AppSession(bool isDemo, bool isElevated)
    {
        IsDemo = isDemo;
        IsElevated = isElevated;
        Questionnaire = new QuestionnaireViewModel(this);
        Tests = new HardwareTestStore();
    }

    public bool IsDemo { get; }
    public bool IsElevated { get; }
    public DeviceSnapshot? Snapshot => ScanResult?.Snapshot;
    public bool HasScan => ScanResult is not null;
    public DateTimeOffset ScannedAt => ScanResult?.ScannedAt ?? DateTimeOffset.Now;
    public QuestionnaireViewModel Questionnaire { get; }
    public HardwareTestStore Tests { get; }

    /// <summary>Key of the scanned device model (see <see cref="DeviceKey"/>), or null when it cannot be identified; nothing is remembered then.</summary>
    public string? CurrentDeviceKey { get; set; }

    public ConditionQuestionnaire Condition => Questionnaire.ToQuestionnaire();

    /// <summary>True once the seller has answered anything or recorded any test result in this session.</summary>
    public bool HasAnswers => !Condition.IsEmpty || Tests.Results.Count > 0;

    public bool HasSavedAnswersOffer => SavedAnswersOffer is not null;

    public string SavedAnswersOfferDetail
    {
        get
        {
            if (SavedAnswersOffer is not { } saved) return string.Empty;
            var parts = new List<string>();
            if (!saved.Questionnaire.IsEmpty) parts.Add("questionnaire answers");
            if (saved.Tests.Count > 0) parts.Add(saved.Tests.Count == 1 ? "one hardware test result" : $"{saved.Tests.Count} hardware test results");
            var what = string.Join(" and ", parts);
            what = char.ToUpperInvariant(what[0]) + what[1..];
            return $"{what} were saved for a {saved.DeviceLabel} on {saved.SavedAt.LocalDateTime:d MMMM yyyy}. Restore them if this is the same device, or discard them and start again. Anything you enter now replaces what was saved.";
        }
    }

    /// <summary>Puts remembered answers and test results into the current session and clears the offer.</summary>
    public void ApplySaved(SavedDeviceAnswers saved)
    {
        Questionnaire.Apply(saved.Questionnaire);
        Tests.Replace(saved.Tests);
        SavedAnswersOffer = null;
    }
}

/// <summary>Keeps one result per test kind and raises a change notification when anything is set.</summary>
public sealed class HardwareTestStore
{
    private readonly Dictionary<HardwareTestKind, HardwareTestResult> _results = new();

    public event EventHandler? Changed;

    public ReadOnlyCollection<HardwareTestResult> Results => _results.Values.OrderBy(r => r.Kind).ToList().AsReadOnly();

    public HardwareTestResult Get(HardwareTestKind kind) => _results.TryGetValue(kind, out var r) ? r : HardwareTestResult.NotTested(kind);

    public void Set(HardwareTestResult result)
    {
        if (result.Outcome == TestOutcome.NotTested)
        {
            _results.Remove(result.Kind);
        }
        else
        {
            _results[result.Kind] = result with { CompletedAt = result.CompletedAt ?? DateTimeOffset.Now };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces every result at once (used when remembered results come back), raising a single change.</summary>
    public void Replace(IEnumerable<HardwareTestResult> results)
    {
        _results.Clear();
        foreach (var result in results)
        {
            if (result.Outcome == TestOutcome.NotTested) continue;
            _results[result.Kind] = result with { CompletedAt = result.CompletedAt ?? DateTimeOffset.Now };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _results.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Editable backing for the physical condition questionnaire. Every answer defaults to "not specified".</summary>
public sealed partial class QuestionnaireViewModel : ObservableObject
{
    private readonly AppSession _session;

    public QuestionnaireViewModel(AppSession session)
    {
        _session = session;
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.Snapshot)) OnPropertyChanged(nameof(ShowPortableQuestions));
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShowPortableQuestions) && e.PropertyName != nameof(Summary))
            {
                OnPropertyChanged(nameof(Summary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Changed;

    [ObservableProperty] private ConditionGrade _overallCondition = ConditionGrade.Unspecified;
    [ObservableProperty] private Answer _hasCosmeticDamage = Answer.Unspecified;
    [ObservableProperty] private string _cosmeticDamageNotes = string.Empty;
    [ObservableProperty] private ComponentCondition _screen = ComponentCondition.Unspecified;
    [ObservableProperty] private ComponentCondition _keyboard = ComponentCondition.Unspecified;
    [ObservableProperty] private ComponentCondition _trackpad = ComponentCondition.Unspecified;
    [ObservableProperty] private ComponentCondition _hinge = ComponentCondition.Unspecified;
    [ObservableProperty] private Answer _hasBatteryConcern = Answer.Unspecified;
    [ObservableProperty] private string _batteryNotes = string.Empty;
    [ObservableProperty] private Answer _chargerIncluded = Answer.Unspecified;
    [ObservableProperty] private Answer _originalPackagingIncluded = Answer.Unspecified;
    [ObservableProperty] private Answer _hasKnownDefects = Answer.Unspecified;
    [ObservableProperty] private string _knownDefects = string.Empty;
    [ObservableProperty] private string _repairOrUpgradeHistory = string.Empty;
    [ObservableProperty] private string _additionalAccessories = string.Empty;
    [ObservableProperty] private string _sellerNotes = string.Empty;
    [ObservableProperty] private string _askingPriceText = string.Empty;

    /// <summary>Laptop-only questions (keyboard, trackpad, hinge, battery) are hidden for desktops.</summary>
    public bool ShowPortableQuestions
    {
        get
        {
            var ff = _session.Snapshot?.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown) ?? DeviceFormFactor.Unknown;
            return ff is DeviceFormFactor.Laptop or DeviceFormFactor.Convertible or DeviceFormFactor.Tablet or DeviceFormFactor.Unknown || (_session.Snapshot?.HasBattery ?? false);
        }
    }

    public decimal? AskingPrice
    {
        get
        {
            var text = AskingPriceText.Trim().TrimStart('$', '£', '€', 'A', 'U', 'S').Replace(",", string.Empty).Trim();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        }
    }

    public string Summary
    {
        get
        {
            var q = ToQuestionnaire();
            if (q.IsEmpty) return "No answers yet. Unanswered questions are left out of the listing.";
            var parts = new List<string>();
            if (q.OverallCondition != ConditionGrade.Unspecified) parts.Add(ConditionQuestionnaire.GradeLabel(q.OverallCondition));
            if (q.ChargerIncluded == Answer.Yes) parts.Add("charger included");
            if (q.HasKnownDefects == Answer.Yes) parts.Add("issues disclosed");
            if (q.AskingPrice is { } p) parts.Add(SpecSummary.FormatPrice(p, _session.Settings.CurrencySymbol));
            return parts.Count == 0 ? "Some answers provided." : string.Join(" · ", parts);
        }
    }

    public ConditionQuestionnaire ToQuestionnaire() => new()
    {
        OverallCondition = OverallCondition,
        HasCosmeticDamage = HasCosmeticDamage,
        CosmeticDamageNotes = Clean(CosmeticDamageNotes),
        Screen = ShowPortableQuestions || Screen != ComponentCondition.Unspecified ? Screen : ComponentCondition.Unspecified,
        Keyboard = ShowPortableQuestions ? Keyboard : ComponentCondition.Unspecified,
        Trackpad = ShowPortableQuestions ? Trackpad : ComponentCondition.Unspecified,
        Hinge = ShowPortableQuestions ? Hinge : ComponentCondition.Unspecified,
        HasBatteryConcern = ShowPortableQuestions ? HasBatteryConcern : Answer.Unspecified,
        BatteryNotes = Clean(BatteryNotes),
        ChargerIncluded = ChargerIncluded,
        OriginalPackagingIncluded = OriginalPackagingIncluded,
        HasKnownDefects = HasKnownDefects,
        KnownDefects = Clean(KnownDefects),
        RepairOrUpgradeHistory = Clean(RepairOrUpgradeHistory),
        AdditionalAccessories = Clean(AdditionalAccessories),
        SellerNotes = Clean(SellerNotes),
        AskingPrice = AskingPrice,
    };

    /// <summary>Loads answers (the reverse of <see cref="ToQuestionnaire"/>), e.g. remembered ones from an earlier session.</summary>
    public void Apply(ConditionQuestionnaire q)
    {
        OverallCondition = q.OverallCondition;
        HasCosmeticDamage = q.HasCosmeticDamage;
        CosmeticDamageNotes = q.CosmeticDamageNotes ?? string.Empty;
        Screen = q.Screen;
        Keyboard = q.Keyboard;
        Trackpad = q.Trackpad;
        Hinge = q.Hinge;
        HasBatteryConcern = q.HasBatteryConcern;
        BatteryNotes = q.BatteryNotes ?? string.Empty;
        ChargerIncluded = q.ChargerIncluded;
        OriginalPackagingIncluded = q.OriginalPackagingIncluded;
        HasKnownDefects = q.HasKnownDefects;
        KnownDefects = q.KnownDefects ?? string.Empty;
        RepairOrUpgradeHistory = q.RepairOrUpgradeHistory ?? string.Empty;
        AdditionalAccessories = q.AdditionalAccessories ?? string.Empty;
        SellerNotes = q.SellerNotes ?? string.Empty;
        AskingPriceText = q.AskingPrice is { } price ? price.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
    }

    public void Reset()
    {
        OverallCondition = ConditionGrade.Unspecified;
        HasCosmeticDamage = Answer.Unspecified;
        CosmeticDamageNotes = string.Empty;
        Screen = Keyboard = Trackpad = Hinge = ComponentCondition.Unspecified;
        HasBatteryConcern = Answer.Unspecified;
        BatteryNotes = string.Empty;
        ChargerIncluded = OriginalPackagingIncluded = HasKnownDefects = Answer.Unspecified;
        KnownDefects = RepairOrUpgradeHistory = AdditionalAccessories = SellerNotes = AskingPriceText = string.Empty;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length > 2000 ? t[..2000] : t;
    }
}
