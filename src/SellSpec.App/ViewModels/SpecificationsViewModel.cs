using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SellSpec.Core.Health;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;

namespace SellSpec.App.ViewModels;

public sealed record SpecRow(string Label, string Value, string? ProvenanceLabel, string? Note, bool IsMuted);

public sealed record SpecGroup(string Title, string Glyph, IReadOnlyList<SpecRow> Rows, bool IsExpanded = true);

public sealed partial class SpecificationsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public SpecificationsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult) || e.PropertyName == nameof(AppSession.Settings)) Refresh();
        };
        Refresh();
    }

    public ObservableCollection<SpecGroup> Groups { get; } = new();

    [ObservableProperty] private bool _showTechnicalDetails;
    [ObservableProperty] private bool _showPrivateIdentifiers;
    [ObservableProperty] private bool _hasPrivateIdentifiers;

    partial void OnShowTechnicalDetailsChanged(bool value) => Refresh();
    partial void OnShowPrivateIdentifiersChanged(bool value) => Refresh();

    private bool ShowProvenance => _shell.Session.Settings.ShowProvenanceLabels;

    private void Refresh()
    {
        Groups.Clear();
        var s = _shell.Session.Snapshot;
        if (s is null) return;
        HasPrivateIdentifiers = s.Private.AllValues().Any();

        Groups.Add(new SpecGroup("System", "", SystemRows(s)));
        Groups.Add(new SpecGroup("Processor", "", ProcessorRows(s)));
        Groups.Add(new SpecGroup("Memory", "", MemoryRows(s)));
        var index = 0;
        foreach (var d in s.Storage)
        {
            index++;
            var title = s.Storage.Count > 1 ? $"Storage {index}" : "Storage";
            if (!d.IncludeInListing) title += $" ({d.Location.ToString().ToLowerInvariant()}, not listed)";
            Groups.Add(new SpecGroup(title, "", StorageRows(d), d.IncludeInListing));
        }
        if (s.Storage.Count == 0) Groups.Add(new SpecGroup("Storage", "", new[] { Row("Drives", "None detected", Provenance.Unavailable) }));
        Groups.Add(new SpecGroup("Graphics", "", GraphicsRows(s)));
        index = 0;
        foreach (var d in s.Displays)
        {
            index++;
            Groups.Add(new SpecGroup(s.Displays.Count > 1 ? $"Display {index} – {d.LocationLabel}" : $"Display – {d.LocationLabel}", "", DisplayRows(d), d.IsInternal || s.Displays.Count == 1));
        }
        Groups.Add(new SpecGroup("Battery", "", BatteryRows(s)));
        Groups.Add(new SpecGroup("Connectivity", "", ConnectivityRows(s)));
        Groups.Add(new SpecGroup("Windows", "", WindowsRows(s)));
        if (ShowPrivateIdentifiers && HasPrivateIdentifiers)
        {
            Groups.Add(new SpecGroup("Private identifiers (never exported)", "", PrivateRows(s)));
        }
    }

    private SpecRow Row<T>(string label, Spec<T> spec, Func<T, string>? format = null, string unavailable = "Not available")
    {
        format ??= v => v?.ToString() ?? string.Empty;
        if (spec.HasValue) return new SpecRow(label, format(spec.Value!), ShowProvenance ? Spec.Label(spec.Provenance) : null, spec.Note, false);
        return new SpecRow(label, spec.IsNotApplicable ? "Not applicable" : unavailable, ShowProvenance ? Spec.Label(spec.Provenance) : null, spec.Note, true);
    }

    private SpecRow Row(string label, string value, Provenance provenance, string? note = null) =>
        new(label, value, ShowProvenance ? Spec.Label(provenance) : null, note, provenance is Provenance.Unavailable or Provenance.NotApplicable);

    private List<SpecRow> SystemRows(DeviceSnapshot s)
    {
        var id = s.Identity;
        var rows = new List<SpecRow>();
        if (id.IsCustomBuilt)
        {
            rows.Add(Row("Type", "Custom-built PC", Provenance.Estimated, "The system manufacturer and model match the motherboard, which indicates a self-built computer."));
        }
        else
        {
            rows.Add(Row("Manufacturer", id.Manufacturer));
            rows.Add(Row("Product name", id.ProductName));
            rows.Add(Row("Model number", id.ModelNumber));
            if (id.Sku.HasValue || ShowTechnicalDetails) rows.Add(Row("Manufacturer SKU", id.Sku));
            if (id.SystemFamily.HasValue && ShowTechnicalDetails) rows.Add(Row("System family", id.SystemFamily));
        }
        rows.Add(Row("Form factor", id.FormFactor, DeviceIdentity.FormFactorLabel));
        rows.Add(Row("Architecture", id.Architecture));
        rows.Add(Row("Motherboard", id.BoardModel, v => id.BoardManufacturer.HasValue && !v.StartsWith(id.BoardManufacturer.Value!, StringComparison.OrdinalIgnoreCase) ? $"{id.BoardManufacturer.Value} {v}" : v));
        rows.Add(Row("BIOS/UEFI version", id.BiosVersion, v => id.BiosReleaseDate.HasValue ? $"{v} ({id.BiosReleaseDate.Value:d MMM yyyy})" : v));
        rows.Add(Row("Firmware mode", id.FirmwareType));
        if (id.IsVirtualMachine) rows.Add(Row("Virtual machine", id.VirtualisationPlatform ?? "Yes", Provenance.Detected));
        if (ShowTechnicalDetails)
        {
            rows.Add(Row("Raw manufacturer string", id.RawManufacturer ?? "—", Provenance.FirmwareReported));
            rows.Add(Row("Raw model string", id.RawModel ?? "—", Provenance.FirmwareReported));
            if (id.RawVersion is not null) rows.Add(Row("Raw version string", id.RawVersion, Provenance.FirmwareReported));
            rows.Add(Row("BIOS vendor", id.BiosVendor));
            rows.Add(Row("Board version", id.BoardVersion));
            rows.Add(Row("Chassis type codes", id.ChassisTypeCodes.Count == 0 ? "—" : string.Join(", ", id.ChassisTypeCodes), Provenance.FirmwareReported));
        }
        return rows;
    }

    private List<SpecRow> ProcessorRows(DeviceSnapshot s)
    {
        var cpu = s.Processor;
        var rows = new List<SpecRow>
        {
            Row("Processor", cpu.Name),
            Row("Manufacturer", cpu.Manufacturer),
            Row("Physical cores", cpu.PhysicalCores),
            Row("Logical processors", cpu.LogicalProcessors),
            Row("Reported clock speed", cpu.ClockSpeedMHz, v => (v / 1000d).ToString("0.0#", CultureInfo.InvariantCulture) + " GHz"),
            Row("Architecture", cpu.Architecture),
            Row("Installed processors", cpu.InstalledCount),
        };
        if (ShowTechnicalDetails)
        {
            rows.Add(Row("Raw name", cpu.RawName ?? "—", Provenance.FirmwareReported));
            rows.Add(Row("L2 cache", cpu.L2CacheKB, v => $"{v} KB"));
            rows.Add(Row("L3 cache", cpu.L3CacheKB, v => $"{v} KB"));
            if (cpu.SocketDesignation is not null) rows.Add(Row("Socket", cpu.SocketDesignation, Provenance.FirmwareReported));
        }
        return rows;
    }

    private List<SpecRow> MemoryRows(DeviceSnapshot s)
    {
        var m = s.Memory;
        var rows = new List<SpecRow>
        {
            Row("Installed memory", m.InstalledBytes, CapacityFormatter.FormatMemory),
            Row("Usable memory", m.UsableBytes, CapacityFormatter.FormatMemoryPrecise),
            Row("Memory type", m.Technology),
            Row("Configured speed", m.ConfiguredSpeedMT, v => $"{v} MT/s"),
            Row("Modules detected", m.Modules.Count == 0 ? "None reported" : string.Join(" + ", m.Modules.Select(x => CapacityFormatter.FormatMemory(x.CapacityBytes) + (x.FormFactor is { } ff ? $" {ff}" : string.Empty))), m.Modules.Count == 0 ? Provenance.Unavailable : Provenance.FirmwareReported),
            Row("Memory slots (firmware)", m.ReportedSlots, v => $"{v} reported"),
            Row("Maximum memory (firmware)", m.ReportedMaxBytes, CapacityFormatter.FormatMemory),
            Row("Upgradeable", m.Upgradeability.HasValue ? m.Upgradeability.Value! : "Not verified", Provenance.Estimated, m.Upgradeability.Note),
        };
        if (ShowTechnicalDetails)
        {
            var i = 0;
            foreach (var module in m.Modules)
            {
                i++;
                var parts = new List<string> { CapacityFormatter.FormatMemory(module.CapacityBytes) };
                if (module.Technology is not null) parts.Add(module.Technology);
                if (module.SpeedMT is not null) parts.Add($"{module.SpeedMT} MT/s rated");
                if (module.ConfiguredSpeedMT is not null) parts.Add($"{module.ConfiguredSpeedMT} MT/s configured");
                if (module.Manufacturer is not null) parts.Add(module.Manufacturer);
                if (module.PartNumber is not null) parts.Add(module.PartNumber);
                if (module.Slot is not null) parts.Add($"slot {module.Slot}");
                rows.Add(Row($"Module {i}", string.Join(" · ", parts), Provenance.FirmwareReported));
            }
        }
        return rows;
    }

    private List<SpecRow> StorageRows(StorageDevice d)
    {
        var health = StorageHealthAssessor.Assess(d);
        var rows = new List<SpecRow>
        {
            Row("Model", d.Model, Provenance.Detected),
            Row("Manufacturer", d.Manufacturer),
            Row("Capacity", d.CapacityBytes, v => $"{CapacityFormatter.FormatStorage(v)} ({CapacityFormatter.FormatStoragePrecise(v)} usable)"),
            Row("Type", d.TypeLabel, Provenance.Detected),
            Row("Location", d.Location switch { StorageLocation.Internal => "Internal", StorageLocation.External => "External", StorageLocation.Removable => "Removable", StorageLocation.Virtual => "Virtual", _ => "Unknown" }, Provenance.Detected),
            Row("Role", d.IsBootDrive ? "Windows system drive" : (d.DriveLetters.Count > 0 ? "Data drive" : "No drive letter"), Provenance.Detected),
            Row("Reported health", health.Summary, d.ReportedHealth.HasValue ? d.ReportedHealth.Provenance : Provenance.Unavailable, health.Detail),
            Row("Power-on hours", d.PowerOnHours, StorageHealthAssessor.FormatHours),
            Row("Endurance used", d.WearPercentUsed, v => $"{v}%"),
            Row("Temperature", d.TemperatureC, v => $"{v} °C"),
            Row("Failure predicted (SMART)", d.PredictedFailure, v => v ? "Yes" : "No"),
        };
        if (ShowTechnicalDetails)
        {
            rows.Add(Row("Drive letters", d.DriveLetters.Count == 0 ? "—" : string.Join(", ", d.DriveLetters), Provenance.Detected));
            rows.Add(Row("Disk number", d.DiskNumber?.ToString() ?? "—", Provenance.Detected));
            rows.Add(Row("Firmware", d.FirmwareVersion ?? "—", Provenance.FirmwareReported));
            rows.Add(Row("Spindle speed", d.SpindleRpm, v => $"{v} rpm"));
        }
        return rows;
    }

    private List<SpecRow> GraphicsRows(DeviceSnapshot s)
    {
        var rows = new List<SpecRow>();
        foreach (var g in s.Graphics.OrderByDescending(g => g.Kind == GraphicsKind.Dedicated))
        {
            var label = g.Kind == GraphicsKind.Dedicated ? "Dedicated graphics" : g.Kind == GraphicsKind.Integrated ? "Integrated graphics" : g.Kind == GraphicsKind.Virtual ? "Virtual adapter" : "Graphics adapter";
            rows.Add(Row(label, g.Name, Provenance.Detected, g.IsFallbackDriver ? "Basic display driver in use; install the proper driver." : null));
            if (g.Kind == GraphicsKind.Dedicated) rows.Add(Row("Dedicated memory", g.DedicatedMemoryBytes, CapacityFormatter.FormatGraphicsMemory));
            if (ShowTechnicalDetails)
            {
                rows.Add(Row("Driver version", g.DriverVersion ?? "—", Provenance.Detected, g.DriverDate is { } d ? $"Driver dated {d:d MMM yyyy}" : null));
                rows.Add(Row("Raw name", g.RawName ?? "—", Provenance.FirmwareReported));
            }
        }
        if (rows.Count == 0) rows.Add(Row("Graphics", "Not available", Provenance.Unavailable));
        return rows;
    }

    private List<SpecRow> DisplayRows(DisplayInfo d)
    {
        var rows = new List<SpecRow>
        {
            Row("Display", d.Name ?? d.LocationLabel, Provenance.Detected),
            Row("Screen size", d.DiagonalInches, DisplayNaming.FormatDiagonal),
            Row("Native resolution", d.NativeResolution, r => d.ResolutionMarketingName is { } mk ? $"{r} ({mk})" : r.ToString()),
            Row("Current resolution", d.CurrentResolution, r => r.ToString()),
            Row("Refresh rate", d.MaxRefreshRateHz, v => d.CurrentRefreshRateHz.HasValue && Math.Abs(d.CurrentRefreshRateHz.Value - v) > 1 ? $"up to {Math.Round(v)} Hz (currently {Math.Round(d.CurrentRefreshRateHz.Value)} Hz)" : $"{Math.Round(v)} Hz"),
            Row("Touchscreen", d.Touch, v => v ? "Yes" : "No"),
            Row("Panel manufacturer", d.Manufacturer),
            Row("Physical size", d.PhysicalSize, p => $"{p.WidthCm:0} × {p.HeightCm:0} cm"),
        };
        if (ShowTechnicalDetails)
        {
            rows.Add(Row("Connection", d.ConnectionLabel ?? "—", Provenance.Detected));
            rows.Add(Row("EDID codes", $"{d.ManufacturerCode ?? "—"} / {d.ProductCode ?? "—"}", Provenance.FirmwareReported));
            rows.Add(Row("Primary display", d.IsPrimary ? "Yes" : "No", Provenance.Detected));
        }
        return rows;
    }

    private List<SpecRow> BatteryRows(DeviceSnapshot s)
    {
        var b = s.Battery;
        if (b.Presence == BatteryPresence.NotPresent)
        {
            return new List<SpecRow> { Row("Battery", "Not applicable", Provenance.NotApplicable, "No battery detected. This is normal for desktop computers.") };
        }
        var rows = new List<SpecRow>
        {
            Row("Reported battery health", b.HealthPercent, v => $"{v}% of design capacity – {BatteryHealthCalculator.Describe(v)}"),
            Row("Design capacity", b.DesignCapacityMwh, v => $"{v:N0} mWh"),
            Row("Full charge capacity", b.FullChargeCapacityMwh, v => $"{v:N0} mWh"),
            Row("Cycle count", b.CycleCount, v => v == 0 ? "0 (not tracked by firmware)" : v.ToString(), "Not reported"),
            Row("Current charge", b.ChargePercent, v => $"{v}%"),
            Row("Status", b.ChargingStatus),
            Row("Chemistry", b.Chemistry),
            Row("Windows condition", b.ReportedCondition),
        };
        if (b.Packs.Count > 1) rows.Add(Row("Batteries", $"{b.Packs.Count} packs; combined figures shown", Provenance.Detected));
        if (ShowTechnicalDetails)
        {
            var i = 0;
            foreach (var p in b.Packs)
            {
                i++;
                rows.Add(Row($"Pack {i}", $"{p.Name ?? "Battery"} · {(p.Manufacturer ?? "unknown maker")} · design {(p.DesignCapacityMwh.HasValue ? p.DesignCapacityMwh.Value + " mWh" : "n/a")} · full {(p.FullChargeCapacityMwh.HasValue ? p.FullChargeCapacityMwh.Value + " mWh" : "n/a")}", Provenance.FirmwareReported));
            }
        }
        return rows;
    }

    private List<SpecRow> ConnectivityRows(DeviceSnapshot s)
    {
        var c = s.Connectivity;
        var rows = new List<SpecRow>
        {
            Row("Wi-Fi adapter", c.WifiAdapter, unavailable: "Not detected"),
            Row("Wi-Fi standard", c.WifiStandard, unavailable: c.WifiAdapter.HasValue ? "Not determined" : "Not applicable"),
            Row("Bluetooth", c.Bluetooth, v => v ? (c.BluetoothRadios.Count > 0 ? $"Available – {c.BluetoothRadios[0]}" : "Available") : "Not detected"),
            Row("Ethernet", c.Ethernet, v => v ? "Available" : "Not detected"),
            Row("Webcam", c.Webcam, v => v ? (c.Cameras.Count > 0 ? string.Join(", ", c.Cameras) : "Available") : "Not detected"),
            Row("Audio", c.Audio, v => v ? (c.AudioDevices.Count > 0 ? string.Join(", ", c.AudioDevices.Take(2)) : "Available") : "Not detected"),
            Row("Fingerprint reader", c.FingerprintReader, v => v ? "Available" : "Not detected"),
        };
        if (ShowTechnicalDetails && c.NetworkAdapters.Count > 0)
        {
            rows.Add(Row("Network adapters", string.Join("; ", c.NetworkAdapters), Provenance.Detected, "Addresses and network names are never collected."));
        }
        return rows;
    }

    private List<SpecRow> WindowsRows(DeviceSnapshot s)
    {
        var os = s.OperatingSystem;
        var rows = new List<SpecRow>
        {
            Row("Edition", os.FullName, os.Name.Provenance),
            Row("Version", os.Version),
            Row("Build", os.Build),
            Row("Architecture", os.Architecture),
            Row("Activation", os.Activation, v => v switch
            {
                ActivationStatus.Licensed => "Activated",
                ActivationStatus.GracePeriod => "Grace period",
                ActivationStatus.Notification => "Not activated (notification mode)",
                ActivationStatus.Unlicensed => "Not activated",
                _ => "Unknown",
            }),
            Row("Licence channel", os.LicenceChannel, v => v, "Not determined"),
            Row("TPM", os.TpmVersion, v => v == "present" ? "Present (version unknown)" : $"TPM {v}"),
            Row("Secure Boot", os.SecureBootEnabled, v => v ? "Enabled" : "Disabled (firmware is capable)"),
            Row("Installed", os.InstallDate, v => v.ToString("d MMM yyyy", CultureInfo.CurrentCulture)),
        };
        if (os.Windows11Compatibility is { } w11)
        {
            rows.Add(Row("Windows 11 readiness", w11.Summary, Provenance.Estimated));
            foreach (var check in w11.Checks)
            {
                rows.Add(Row($"  {check.Requirement}", $"{HealthDashboardBuilder.StatusLabel(check.Status)} – {check.Detail}", Provenance.Estimated));
            }
        }
        return rows;
    }

    private List<SpecRow> PrivateRows(DeviceSnapshot s)
    {
        var p = s.Private;
        var rows = new List<SpecRow>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(Row(label, value, Provenance.FirmwareReported, "Shown for your reference only. Never included in listings, reports or exports."));
        }
        Add("System serial number", p.SystemSerialNumber);
        Add("System UUID", p.SystemUuid);
        Add("Motherboard serial", p.BaseBoardSerialNumber);
        Add("BIOS serial", p.BiosSerialNumber);
        var i = 0;
        foreach (var serial in p.StorageSerialNumbers) Add($"Drive {++i} serial", serial);
        i = 0;
        foreach (var serial in p.BatterySerialNumbers) Add($"Battery {++i} serial", serial);
        return rows;
    }
}
