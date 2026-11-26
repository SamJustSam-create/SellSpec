using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.Core.Detection;
using SellSpec.Core.Health;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;

namespace SellSpec.App.ViewModels;

public sealed record SpecCard(string Title, string Glyph, string Value, string? Detail, string? Tag, HealthStatusKind? Status = null, string? StatusLabel = null);

public sealed record IssueRow(ScanIssueSeverity Severity, string Message, HealthStatusKind Status)
{
    public string StatusLabel => Severity switch
    {
        ScanIssueSeverity.Error => "Problem",
        ScanIssueSeverity.Warning => "Attention",
        _ => "Note",
    };
}

public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public OverviewViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult)) Refresh();
        };
        Refresh();
    }

    public ShellViewModel Shell => _shell;

    [ObservableProperty] private string _deviceName = "Scanning…";
    [ObservableProperty] private string? _secondaryName;
    [ObservableProperty] private string _deviceTypeLabel = string.Empty;
    [ObservableProperty] private string _deviceGlyph = "";
    [ObservableProperty] private string _operatingSystemLabel = string.Empty;
    [ObservableProperty] private string _scannedAtLabel = string.Empty;
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private bool _isVirtualMachine;
    [ObservableProperty] private bool _isCustomBuilt;
    [ObservableProperty] private string? _headline;

    public ObservableCollection<SpecCard> Cards { get; } = new();
    public ObservableCollection<IssueRow> Issues { get; } = new();
    public bool HasIssues => Issues.Count > 0;

    [RelayCommand]
    private void OpenSpecifications() => _shell.Navigate("specifications");

    [RelayCommand]
    private void OpenListing() => _shell.Navigate("listing");

    [RelayCommand]
    private void OpenHealth() => _shell.Navigate("health");

    private void Refresh()
    {
        var result = _shell.Session.ScanResult;
        Cards.Clear();
        Issues.Clear();
        if (result is null)
        {
            HasSnapshot = false;
            OnPropertyChanged(nameof(HasIssues));
            return;
        }

        var s = result.Snapshot;
        HasSnapshot = true;
        DeviceName = s.Identity.DisplayName;
        SecondaryName = s.Identity.SecondaryName;
        var ff = s.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown);
        DeviceTypeLabel = DeviceIdentity.FormFactorLabel(ff) + (s.Identity.FormFactor.Provenance == Provenance.Estimated ? " (estimated)" : string.Empty);
        DeviceGlyph = ff is DeviceFormFactor.Desktop or DeviceFormFactor.MiniPc or DeviceFormFactor.Server ? "" : "";
        OperatingSystemLabel = SpecSummary.OperatingSystem(s, includeActivation: false) ?? "Windows";
        ScannedAtLabel = $"Scanned {result.ScannedAt:d MMM yyyy, h:mm tt}";
        IsVirtualMachine = s.Identity.IsVirtualMachine;
        IsCustomBuilt = s.Identity.IsCustomBuilt;
        Headline = string.Join(" · ", new[] { SpecSummary.Processor(s, false), SpecSummary.Memory(s, false), SpecSummary.Storage(s) }.Where(x => x is not null));

        Cards.Add(ProcessorCard(s));
        Cards.Add(MemoryCard(s));
        Cards.Add(StorageCard(s));
        Cards.Add(GraphicsCard(s));
        Cards.Add(DisplayCard(s));
        Cards.Add(BatteryCard(s));

        foreach (var issue in result.Issues.Where(i => i.Severity != ScanIssueSeverity.Info).Take(6))
        {
            Issues.Add(new IssueRow(issue.Severity, issue.Message, issue.Severity == ScanIssueSeverity.Error ? HealthStatusKind.Issue : HealthStatusKind.Attention));
        }
        foreach (var issue in result.Issues.Where(i => i.Severity == ScanIssueSeverity.Info).Take(3))
        {
            Issues.Add(new IssueRow(issue.Severity, issue.Message, HealthStatusKind.Unknown));
        }
        OnPropertyChanged(nameof(HasIssues));
    }

    private static string? Tag<T>(Spec<T> spec) => spec.Provenance switch
    {
        Provenance.Estimated => "Estimated",
        Provenance.FirmwareReported => "Firmware reported",
        Provenance.Unavailable => "Unavailable",
        _ => null,
    };

    private static SpecCard ProcessorCard(DeviceSnapshot s)
    {
        var cpu = s.Processor;
        if (!cpu.Name.HasValue) return new SpecCard("Processor", "", "Not available", cpu.Name.Note, "Unavailable");
        var details = new List<string>();
        if (cpu.PhysicalCores.HasValue) details.Add(cpu.LogicalProcessors.HasValue && cpu.LogicalProcessors.Value != cpu.PhysicalCores.Value ? $"{cpu.PhysicalCores.Value} cores / {cpu.LogicalProcessors.Value} threads" : $"{cpu.PhysicalCores.Value} cores");
        if (cpu.ClockSpeedMHz.HasValue) details.Add($"{cpu.ClockSpeedMHz.Value / 1000d:0.0#} GHz");
        return new SpecCard("Processor", "", cpu.Name.Value!, string.Join(" · ", details), null);
    }

    private static SpecCard MemoryCard(DeviceSnapshot s)
    {
        var m = s.Memory;
        var size = m.InstalledBytes.HasValue ? m.InstalledBytes : m.UsableBytes;
        if (!size.HasValue) return new SpecCard("Memory", "", "Not available", size.Note, "Unavailable");
        var value = CapacityFormatter.FormatMemory(size.Value) + (m.Technology.HasValue && !m.Technology.Value!.Contains("inferred") ? " " + m.Technology.Value : string.Empty);
        var details = new List<string>();
        if (m.Modules.Count > 0) details.Add(m.Modules.Count == 1 ? "1 module" : $"{m.Modules.Count} × {CapacityFormatter.FormatMemory(m.Modules[0].CapacityBytes)}");
        if (m.ConfiguredSpeedMT.HasValue) details.Add($"{m.ConfiguredSpeedMT.Value} MT/s");
        details.Add("Upgrade: not verified");
        return new SpecCard("Memory", "", value, string.Join(" · ", details), Tag(size));
    }

    private static SpecCard StorageCard(DeviceSnapshot s)
    {
        var drives = s.ListingStorage.Where(d => d.CapacityBytes.HasValue).OrderByDescending(d => d.IsBootDrive).ThenByDescending(d => d.CapacityBytes.Value).ToList();
        if (drives.Count == 0) return new SpecCard("Storage", "", "No internal drives", "No internal storage was detected.", "Unavailable");
        var primary = drives[0];
        var value = $"{CapacityFormatter.FormatStorage(primary.CapacityBytes.Value)} {primary.TypeLabel}";
        if (drives.Count > 1) value += $" + {drives.Count - 1} more";
        var health = StorageHealthAssessor.Assess(primary);
        var detail = drives.Count > 1 ? string.Join(" + ", drives.Skip(1).Select(d => $"{CapacityFormatter.FormatStorage(d.CapacityBytes.Value)} {d.ShortTypeLabel}")) + " · " : string.Empty;
        detail += primary.Model;
        return new SpecCard("Storage", "", value, detail, null, health.Status, health.Summary);
    }

    private static SpecCard GraphicsCard(DeviceSnapshot s)
    {
        var gpus = s.ListingGraphics.OrderByDescending(g => g.Kind == GraphicsKind.Dedicated).ToList();
        if (gpus.Count == 0)
        {
            var fallback = s.Graphics.FirstOrDefault(g => g.IsFallbackDriver);
            return new SpecCard("Graphics", "", fallback is null ? "Not available" : "Basic display driver", fallback is null ? null : "Install the proper graphics driver for full details.", "Unavailable");
        }
        var primary = gpus[0];
        var value = primary.Name;
        var details = new List<string>();
        if (primary.Kind == GraphicsKind.Dedicated)
        {
            details.Add(primary.DedicatedMemoryBytes.HasValue ? $"{CapacityFormatter.FormatGraphicsMemory(primary.DedicatedMemoryBytes.Value)} dedicated" : "Dedicated");
        }
        else
        {
            details.Add("Integrated");
        }
        if (gpus.Count > 1) details.Add($"+ {gpus[1].Name}");
        return new SpecCard("Graphics", "", value, string.Join(" · ", details), primary.Kind == GraphicsKind.Dedicated && !primary.DedicatedMemoryBytes.HasValue ? "Memory not detected" : null);
    }

    private static SpecCard DisplayCard(DeviceSnapshot s)
    {
        var d = s.PrimaryDisplay;
        var ff = s.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown);
        if (d is null) return new SpecCard("Display", "", "Not available", "No display information was reported.", "Unavailable");
        if (!d.IsInternal && ff is DeviceFormFactor.Desktop or DeviceFormFactor.MiniPc)
        {
            var monitor = d.Name ?? "External monitor";
            var res = d.NativeResolution.HasValue ? d.NativeResolution.Value.ToString() : string.Empty;
            return new SpecCard("Display", "", "External monitor", $"{monitor} {res}".Trim() + " · not part of this PC", null);
        }
        var parts = new List<string>();
        if (d.DiagonalInches.HasValue) parts.Add(DisplayNaming.FormatDiagonal(d.DiagonalInches.Value));
        if (d.ResolutionMarketingName is not null) parts.Add(d.ResolutionMarketingName);
        else if (d.NativeResolution.HasValue) parts.Add(d.NativeResolution.Value.ToString());
        var value = parts.Count == 0 ? "Built-in display" : string.Join(" ", parts);
        var details = new List<string>();
        if (d.NativeResolution.HasValue && d.ResolutionMarketingName is not null) details.Add(d.NativeResolution.Value.ToString());
        if (d.MaxRefreshRateHz.HasValue) details.Add($"{Math.Round(d.MaxRefreshRateHz.Value)} Hz");
        if (d.Touch.HasValue) details.Add(d.Touch.Value ? "Touchscreen" : "No touch");
        return new SpecCard("Display", "", value, string.Join(" · ", details), d.DiagonalInches.HasValue ? "Size estimated" : null);
    }

    private static SpecCard BatteryCard(DeviceSnapshot s)
    {
        var b = s.Battery;
        if (b.Presence == BatteryPresence.NotPresent)
        {
            return new SpecCard("Battery", "", "Not applicable", "No battery; normal for a desktop PC.", null, HealthStatusKind.NotApplicable, "Not applicable");
        }
        if (!b.HealthPercent.HasValue)
        {
            return new SpecCard("Battery", "", "Health unavailable", b.HealthPercent.Note ?? "The battery did not report capacity information.", "Unavailable", HealthStatusKind.Unknown, "Unavailable");
        }
        var details = new List<string>();
        if (b.CycleCount.HasValue && b.CycleCount.Value > 0) details.Add($"{b.CycleCount.Value} cycles");
        if (b.ChargePercent.HasValue) details.Add($"{b.ChargePercent.Value}% charged");
        if (b.ChargingStatus.HasValue) details.Add(b.ChargingStatus.Value!.ToLowerInvariant());
        return new SpecCard("Battery", "", $"{b.HealthPercent.Value}% reported health", string.Join(" · ", details), "Reported estimate", b.HealthStatus, HealthDashboardBuilder.StatusLabel(b.HealthStatus));
    }
}
