using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SellSpec.Core.Detection;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
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

    public ConditionQuestionnaire Condition => Questionnaire.ToQuestionnaire();
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
            return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
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
