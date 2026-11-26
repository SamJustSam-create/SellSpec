using System.Globalization;
using SellSpec.Core.Health;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Core.Testing;

namespace SellSpec.Core.Reports;

public sealed record ReportRequest(
    DeviceSnapshot Snapshot,
    ConditionQuestionnaire Condition,
    IReadOnlyList<HardwareTestResult> Tests,
    DateTimeOffset ScannedAt,
    string ApplicationVersion,
    bool IsDemoData = false,
    string CurrencySymbol = "$");

/// <summary>Turns a snapshot and seller input into the public <see cref="DeviceReport"/>.</summary>
public static class ReportBuilder
{
    public static DeviceReport Build(ReportRequest request)
    {
        var s = request.Snapshot;
        var q = request.Condition;
        var sections = new List<ReportSection>();

        sections.Add(new ReportSection("System", SystemFields(s)));
        sections.Add(new ReportSection("Processor", ProcessorFields(s)));
        sections.Add(new ReportSection("Memory", MemoryFields(s)));
        var driveIndex = 0;
        foreach (var drive in s.ListingStorage)
        {
            driveIndex++;
            sections.Add(new ReportSection(s.ListingStorage.Count() > 1 ? $"Storage {driveIndex}" : "Storage", StorageFields(drive)));
        }
        if (driveIndex == 0)
        {
            sections.Add(new ReportSection("Storage", new[] { new ReportField("Internal drives", "None detected", Provenance.Unavailable) }));
        }
        sections.Add(new ReportSection("Graphics", GraphicsFields(s)));
        var display = s.PrimaryDisplay;
        if (display is not null && (display.IsInternal || s.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown) is DeviceFormFactor.AllInOne or DeviceFormFactor.Laptop or DeviceFormFactor.Tablet or DeviceFormFactor.Convertible))
        {
            sections.Add(new ReportSection("Display", DisplayFields(display)));
        }
        sections.Add(new ReportSection("Battery", BatteryFields(s)));
        sections.Add(new ReportSection("Connectivity", ConnectivityFields(s)));
        sections.Add(new ReportSection("Windows", OperatingSystemFields(s)));

        var health = HealthDashboardBuilder.Build(s, request.Tests);
        var healthRows = health.Where(h => h.Group != "Hardware tests")
            .Select(h => new ReportStatusRow(h.Title, h.Status, h.StatusLabel, h.Value, h.Detail, h.Provenance)).ToList();
        var testRows = health.Where(h => h.Group == "Hardware tests")
            .Select(h => new ReportStatusRow(h.Title, h.Status, h.StatusLabel, h.Value, h.Detail, h.Provenance)).ToList();

        var headlineParts = new[] { SpecSummary.Processor(s, false), SpecSummary.Memory(s, false), SpecSummary.Storage(s) }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var notices = new List<string>();
        if (s.Identity.IsVirtualMachine) notices.Add($"This appears to be a virtual machine ({s.Identity.VirtualisationPlatform ?? "unknown platform"}); the specifications describe virtual hardware.");
        if (request.IsDemoData) notices.Add("DEMONSTRATION DATA: this report was generated from SellSpec's built-in demo device, not from real hardware.");

        return new DeviceReport
        {
            ApplicationVersion = request.ApplicationVersion,
            ScannedAt = request.ScannedAt,
            GeneratedAt = DateTimeOffset.Now,
            DeviceName = s.Identity.DisplayName,
            DeviceSecondaryName = s.Identity.SecondaryName,
            DeviceType = DeviceIdentity.FormFactorLabel(s.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown)),
            OperatingSystem = SpecSummary.OperatingSystem(s, includeActivation: false) ?? "Windows",
            Headline = string.Join(" · ", headlineParts),
            Specifications = sections,
            Health = healthRows,
            HardwareTests = testRows,
            SellerCondition = ConditionFields(s, q, request.CurrencySymbol),
            KnownIssues = KnownIssues(q, request.Tests),
            Notices = notices,
            IsDemoData = request.IsDemoData,
        };
    }

    private static ReportField F<T>(string label, Spec<T> spec, Func<T, string> format, string unavailableText = "Not available")
    {
        if (spec.HasValue) return new ReportField(label, format(spec.Value!), spec.Provenance, spec.Note);
        return new ReportField(label, spec.IsNotApplicable ? "Not applicable" : unavailableText, spec.Provenance, spec.Note);
    }

    private static List<ReportField> SystemFields(DeviceSnapshot s)
    {
        var id = s.Identity;
        var fields = new List<ReportField>();
        if (id.IsCustomBuilt)
        {
            fields.Add(new ReportField("Type", "Custom-built PC", Provenance.Estimated, "The system manufacturer and model match the motherboard, which indicates a self-built computer."));
            fields.Add(F("Motherboard", id.BoardModel, v => id.BoardManufacturer.HasValue && !v.StartsWith(id.BoardManufacturer.Value!, StringComparison.OrdinalIgnoreCase) ? $"{id.BoardManufacturer.Value} {v}" : v));
        }
        else
        {
            fields.Add(F("Manufacturer", id.Manufacturer, v => v));
            fields.Add(F("Product", id.ProductName, v => v));
            if (id.ModelNumber.HasValue && (!id.ProductName.HasValue || !string.Equals(id.ModelNumber.Value, id.ProductName.Value, StringComparison.OrdinalIgnoreCase)))
            {
                fields.Add(F("Model number", id.ModelNumber, v => v));
            }
            if (id.Sku.HasValue) fields.Add(F("Manufacturer SKU", id.Sku, v => v));
        }
        fields.Add(F("Form factor", id.FormFactor, DeviceIdentity.FormFactorLabel));
        fields.Add(F("Architecture", id.Architecture, v => v));
        if (id.BiosVersion.HasValue)
        {
            fields.Add(F("BIOS/UEFI version", id.BiosVersion, v => id.BiosReleaseDate.HasValue ? $"{v} ({id.BiosReleaseDate.Value:MMM yyyy})" : v));
        }
        return fields;
    }

    private static List<ReportField> ProcessorFields(DeviceSnapshot s)
    {
        var cpu = s.Processor;
        var fields = new List<ReportField> { F("Processor", cpu.Name, v => v) };
        if (cpu.PhysicalCores.HasValue || cpu.LogicalProcessors.HasValue)
        {
            var cores = cpu.PhysicalCores.HasValue ? cpu.PhysicalCores.Value.ToString() : "?";
            var threads = cpu.LogicalProcessors.HasValue ? cpu.LogicalProcessors.Value.ToString() : "?";
            fields.Add(new ReportField("Cores / threads", $"{cores} / {threads}", cpu.PhysicalCores.Provenance));
        }
        if (cpu.ClockSpeedMHz.HasValue) fields.Add(F("Reported clock speed", cpu.ClockSpeedMHz, v => (v / 1000d).ToString("0.0#", CultureInfo.InvariantCulture) + " GHz"));
        if (cpu.InstalledCount.HasValue && cpu.InstalledCount.Value > 1) fields.Add(F("Installed processors", cpu.InstalledCount, v => v.ToString()));
        return fields;
    }

    private static List<ReportField> MemoryFields(DeviceSnapshot s)
    {
        var m = s.Memory;
        var fields = new List<ReportField> { F("Installed memory", m.InstalledBytes, CapacityFormatter.FormatMemory) };
        if (m.UsableBytes.HasValue && m.InstalledBytes.HasValue && m.UsableBytes.Value < m.InstalledBytes.Value)
        {
            fields.Add(F("Usable memory", m.UsableBytes, CapacityFormatter.FormatMemoryPrecise));
        }
        fields.Add(F("Memory type", m.Technology, v => v));
        if (m.ConfiguredSpeedMT.HasValue) fields.Add(F("Configured speed", m.ConfiguredSpeedMT, v => $"{v} MT/s"));
        if (m.Modules.Count > 0)
        {
            fields.Add(new ReportField("Modules", string.Join(" + ", m.Modules.Select(x => CapacityFormatter.FormatMemory(x.CapacityBytes) + (x.FormFactor is { } ff ? $" {ff}" : string.Empty))), Provenance.FirmwareReported));
        }
        if (m.ReportedSlots.HasValue) fields.Add(F("Memory slots (firmware)", m.ReportedSlots, v => $"{v} reported"));
        fields.Add(new ReportField("Upgradeable", m.Upgradeability.HasValue ? m.Upgradeability.Value! : "Not verified", Provenance.Estimated, m.Upgradeability.Note));
        return fields;
    }

    private static List<ReportField> StorageFields(StorageDevice d)
    {
        var fields = new List<ReportField>
        {
            new("Drive", d.Model, Provenance.Detected),
            F("Capacity", d.CapacityBytes, CapacityFormatter.FormatStorage),
            new("Type", d.TypeLabel, Provenance.Detected),
        };
        if (d.IsBootDrive) fields.Add(new ReportField("Role", "Windows system drive", Provenance.Detected));
        var health = StorageHealthAssessor.Assess(d);
        fields.Add(new ReportField("Reported health", health.Summary, d.ReportedHealth.HasValue ? d.ReportedHealth.Provenance : Provenance.Unavailable, health.Detail));
        fields.Add(F("Power-on hours", d.PowerOnHours, StorageHealthAssessor.FormatHours));
        if (d.WearPercentUsed.HasValue) fields.Add(F("Endurance used", d.WearPercentUsed, v => $"{v}%"));
        return fields;
    }

    private static List<ReportField> GraphicsFields(DeviceSnapshot s)
    {
        var fields = new List<ReportField>();
        foreach (var g in s.ListingGraphics.OrderByDescending(g => g.Kind == GraphicsKind.Dedicated))
        {
            var value = g.Name;
            if (g.Kind == GraphicsKind.Dedicated && g.DedicatedMemoryBytes.HasValue) value += $" – {CapacityFormatter.FormatGraphicsMemory(g.DedicatedMemoryBytes.Value)} dedicated memory";
            fields.Add(new ReportField(g.KindLabel + " graphics", value, Provenance.Detected, g.Kind == GraphicsKind.Dedicated && !g.DedicatedMemoryBytes.HasValue ? "Dedicated memory size not reliably detected." : null));
        }
        if (fields.Count == 0)
        {
            fields.Add(new ReportField("Graphics", "Not available", Provenance.Unavailable));
        }
        return fields;
    }

    private static List<ReportField> DisplayFields(DisplayInfo d)
    {
        var fields = new List<ReportField>
        {
            new("Display", d.LocationLabel + (d.Name is { } n ? $" ({n})" : string.Empty), Provenance.Detected),
            F("Screen size", d.DiagonalInches, DisplayNaming.FormatDiagonal),
            F("Native resolution", d.NativeResolution, r => d.ResolutionMarketingName is { } mk ? $"{r} ({mk})" : r.ToString()),
        };
        if (d.MaxRefreshRateHz.HasValue) fields.Add(F("Refresh rate", d.MaxRefreshRateHz, v => $"up to {Math.Round(v)} Hz"));
        else if (d.CurrentRefreshRateHz.HasValue) fields.Add(F("Refresh rate", d.CurrentRefreshRateHz, v => $"{Math.Round(v)} Hz"));
        fields.Add(F("Touchscreen", d.Touch, v => v ? "Yes" : "No"));
        if (d.Manufacturer.HasValue) fields.Add(F("Panel manufacturer", d.Manufacturer, v => v));
        return fields;
    }

    private static List<ReportField> BatteryFields(DeviceSnapshot s)
    {
        var b = s.Battery;
        if (b.Presence == BatteryPresence.NotPresent)
        {
            return new List<ReportField> { new("Battery", "Not applicable (no battery detected)", Provenance.NotApplicable) };
        }
        var fields = new List<ReportField>
        {
            F("Reported battery health", b.HealthPercent, v => $"{v}% of design capacity", "Not available"),
            F("Design capacity", b.DesignCapacityMwh, v => $"{v:N0} mWh"),
            F("Full charge capacity", b.FullChargeCapacityMwh, v => $"{v:N0} mWh"),
            F("Cycle count", b.CycleCount, v => v == 0 ? "0 (not tracked by firmware)" : v.ToString(), "Not reported"),
        };
        if (b.Chemistry.HasValue) fields.Add(F("Chemistry", b.Chemistry, v => v));
        if (b.Packs.Count > 1) fields.Add(new ReportField("Batteries", $"{b.Packs.Count} packs (combined figures shown)", Provenance.Detected));
        return fields;
    }

    private static List<ReportField> ConnectivityFields(DeviceSnapshot s)
    {
        var c = s.Connectivity;
        var fields = new List<ReportField>();
        if (c.WifiAdapter.HasValue || c.WifiStandard.HasValue)
        {
            var text = c.WifiStandard.HasValue ? c.WifiStandard.Value! : "Available";
            if (c.WifiAdapter.HasValue) text += $" – {c.WifiAdapter.Value}";
            fields.Add(new ReportField("Wi-Fi", text, c.WifiStandard.HasValue ? c.WifiStandard.Provenance : c.WifiAdapter.Provenance, c.WifiStandard.Note));
        }
        else
        {
            fields.Add(new ReportField("Wi-Fi", c.WifiAdapter.IsUnavailable && c.WifiAdapter.Note is null ? "Not detected" : "Not available", Provenance.Unavailable));
        }
        fields.Add(F("Bluetooth", c.Bluetooth, v => v ? "Available" : "Not detected"));
        fields.Add(F("Ethernet", c.Ethernet, v => v ? "Available" : "Not detected"));
        fields.Add(F("Webcam", c.Webcam, v => v ? (c.Cameras.Count > 0 ? c.Cameras[0] : "Available") : "Not detected"));
        fields.Add(F("Audio", c.Audio, v => v ? "Available" : "Not detected"));
        if (c.FingerprintReader is { HasValue: true, Value: true }) fields.Add(new ReportField("Fingerprint reader", "Available", c.FingerprintReader.Provenance));
        return fields;
    }

    private static List<ReportField> OperatingSystemFields(DeviceSnapshot s)
    {
        var os = s.OperatingSystem;
        var fields = new List<ReportField>
        {
            new("Edition", os.FullName, os.Name.Provenance),
            F("Version", os.Version, v => os.Build.HasValue ? $"{v} (build {os.Build.Value})" : v),
            F("Activation", os.Activation, v => v switch
            {
                ActivationStatus.Licensed => os.LicenceChannel.HasValue ? $"Activated ({os.LicenceChannel.Value} licence)" : "Activated",
                ActivationStatus.GracePeriod => "Grace period",
                ActivationStatus.Notification => "Not activated (notification)",
                ActivationStatus.Unlicensed => "Not activated",
                _ => "Unknown",
            }),
            F("TPM", os.TpmVersion, v => $"TPM {v}"),
            F("Secure Boot", os.SecureBootEnabled, v => v ? "Enabled" : "Disabled (capable)"),
        };
        if (os.Windows11Compatibility is { } w11)
        {
            fields.Add(new ReportField("Windows 11 readiness", w11.Summary, Provenance.Estimated));
        }
        return fields;
    }

    private static List<ReportField> ConditionFields(DeviceSnapshot s, ConditionQuestionnaire q, string currency)
    {
        var fields = new List<ReportField>();
        void Add(string label, string? value, string? note = null)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(new ReportField(label, value, Provenance.SellerProvided, note));
        }

        Add("Overall condition", q.OverallCondition == ConditionGrade.Unspecified ? null : ConditionQuestionnaire.GradeLabel(q.OverallCondition));
        Add("Cosmetic damage", q.HasCosmeticDamage switch
        {
            Answer.Yes => string.IsNullOrWhiteSpace(q.CosmeticDamageNotes) ? "Yes" : $"Yes – {q.CosmeticDamageNotes.Trim()}",
            Answer.No => "None reported",
            _ => null,
        });
        foreach (var (label, c) in new[] { ("Screen", q.Screen), ("Keyboard", q.Keyboard), ("Trackpad", q.Trackpad), ("Hinge", q.Hinge) })
        {
            if (c is not (ComponentCondition.Unspecified or ComponentCondition.NotApplicable)) Add(label, ConditionQuestionnaire.ComponentLabel(c));
        }
        if (s.HasBattery)
        {
            Add("Battery concerns", q.HasBatteryConcern switch
            {
                Answer.Yes => string.IsNullOrWhiteSpace(q.BatteryNotes) ? "Yes" : $"Yes – {q.BatteryNotes.Trim()}",
                Answer.No => "None reported",
                _ => null,
            });
        }
        Add("Charger included", q.ChargerIncluded switch { Answer.Yes => "Yes", Answer.No => "No", _ => null });
        Add("Original packaging", q.OriginalPackagingIncluded switch { Answer.Yes => "Yes", Answer.No => "No", _ => null });
        Add("Additional accessories", q.AdditionalAccessories?.Trim());
        Add("Repair or upgrade history", q.RepairOrUpgradeHistory?.Trim());
        Add("Seller notes", q.SellerNotes?.Trim());
        if (q.AskingPrice is { } price && price > 0) Add("Asking price", SpecSummary.FormatPrice(price, currency));

        if (fields.Count == 0)
        {
            fields.Add(new ReportField("Condition", "Not provided by the seller", Provenance.Unavailable));
        }
        return fields;
    }

    private static List<string> KnownIssues(ConditionQuestionnaire q, IReadOnlyList<HardwareTestResult> tests)
    {
        var list = new List<string>();
        if (q.HasKnownDefects == Answer.Yes)
        {
            list.Add(string.IsNullOrWhiteSpace(q.KnownDefects) ? "Seller reports known issues (no details given)." : q.KnownDefects.Trim());
        }
        else if (q.HasKnownDefects == Answer.No)
        {
            list.Add("No known faults, as described by the seller.");
        }
        foreach (var t in tests.Where(t => t.IsFail))
        {
            list.Add($"{HardwareTestCatalog.Title(t.Kind)} test failed (seller-reported){(string.IsNullOrWhiteSpace(t.Notes) ? string.Empty : ": " + t.Notes.Trim())}");
        }
        return list;
    }
}
