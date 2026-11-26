using System.Globalization;
using System.IO.Compression;
using System.Text;
using SellSpec.Core.Detection;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Reports;
using SellSpec.Core.Testing;

namespace SellSpec.Core.Diagnostics;

/// <param name="SensitiveTerms">
/// Extra terms that must never appear in the bundle, such as the Windows account name and the computer name.
/// They are replaced in the log and cause the user's notes to be refused.
/// </param>
public sealed record DiagnosticsBundleRequest(
    ScanResult ScanResult,
    string ApplicationVersion,
    string? LogText = null,
    string? UserNotes = null,
    bool IsDemoData = false,
    IReadOnlyList<string>? SensitiveTerms = null);

/// <summary>The zip bytes plus the concatenated text of every entry, so callers can run their own redaction check.</summary>
public sealed record DiagnosticsBundle(byte[] Zip, string CombinedText, IReadOnlyList<string> Entries);

/// <summary>
/// Packages everything a maintainer needs to diagnose a wrong or missing specification: the public JSON report,
/// a raw detection dump (firmware strings, codes, provenance and notes), the scan issues, the sanitised log and
/// the user's own description. Private identifiers are never included; every entry is checked by
/// <see cref="Redactor"/> before the archive is written.
/// </summary>
public static class DiagnosticsBundleBuilder
{
    public const string ReadMeEntry = "README.txt";
    public const string ReportEntry = "report.json";
    public const string RawDetectionEntry = "raw-detection.txt";
    public const string IssuesEntry = "scan-issues.txt";
    public const string LogEntry = "log.txt";
    public const string NotesEntry = "notes.txt";

    public static DiagnosticsBundle Build(DiagnosticsBundleRequest request)
    {
        var result = request.ScanResult;
        var s = result.Snapshot;

        // Belt and braces: make sure the sanitiser knows every identifier from this scan before any text is produced.
        LogSanitiser.RegisterPrivateValues(s.Private.AllValues());

        var report = ReportBuilder.Build(new ReportRequest(s, ConditionQuestionnaire.Empty, Array.Empty<HardwareTestResult>(), result.ScannedAt, request.ApplicationVersion, request.IsDemoData));

        var parts = new List<(string Name, string Text)>
        {
            (ReadMeEntry, ReadMe(request)),
            (ReportEntry, JsonReportWriter.Write(report)),
            (RawDetectionEntry, RawDetection(request)),
            (IssuesEntry, Issues(result)),
        };
        if (!string.IsNullOrWhiteSpace(request.LogText))
        {
            // The log is machine-generated: sanitise it and strip the account/computer names rather than refusing.
            parts.Add((LogEntry, Redactor.ReplaceTerms(LogSanitiser.Sanitise(request.LogText), request.SensitiveTerms)));
        }
        if (!string.IsNullOrWhiteSpace(request.UserNotes))
        {
            parts.Add((NotesEntry, request.UserNotes.Trim()));
        }

        // Notes are the user's own words: refuse (with the reason) instead of silently editing them.
        foreach (var (_, text) in parts)
        {
            Redactor.EnsureClean(text, s.Private, request.SensitiveTerms);
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in parts)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        var combined = string.Join("\n\n", parts.Select(p => $"=== {p.Name} ===\n{p.Text}"));
        return new DiagnosticsBundle(stream.ToArray(), combined, parts.Select(p => p.Name).ToList());
    }

    private static string ReadMe(DiagnosticsBundleRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SellSpec diagnostics bundle");
        sb.AppendLine("===========================");
        sb.AppendLine();
        sb.AppendLine($"SellSpec version: {request.ApplicationVersion}");
        sb.AppendLine($"Scanned: {request.ScanResult.ScannedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        if (request.IsDemoData)
        {
            sb.AppendLine();
            sb.AppendLine("DEMONSTRATION DATA: this bundle was produced from SellSpec's built-in demo device, not from real hardware.");
        }
        sb.AppendLine();
        sb.AppendLine("Contents");
        sb.AppendLine($"  {ReportEntry,-20} the public specification report, exactly as the JSON export");
        sb.AppendLine($"  {RawDetectionEntry,-20} raw firmware strings, codes, provenance and notes for every component");
        sb.AppendLine($"  {IssuesEntry,-20} problems the scanner recorded (timeouts, failed queries, warnings)");
        sb.AppendLine($"  {LogEntry,-20} today's sanitised diagnostic log, if logging was enabled");
        sb.AppendLine($"  {NotesEntry,-20} the description typed by the person reporting the problem, if any");
        sb.AppendLine();
        sb.AppendLine("Privacy");
        sb.AppendLine("  This bundle contains no serial numbers, UUIDs, MAC or IP addresses, user names, product keys,");
        sb.AppendLine("  network names or personal files. Every file was checked for those before the bundle was written.");
        sb.AppendLine("  It describes the computer's hardware the way a specification sheet would.");
        sb.AppendLine();
        sb.AppendLine("How to use it");
        sb.AppendLine("  Attach this zip to a detection report at https://github.com/SamJustSam-create/SellSpec/issues/new/choose");
        sb.AppendLine("  and say what SellSpec showed and what the correct value is (a link to the maker's spec sheet helps).");
        return sb.ToString();
    }

    private static string Issues(ScanResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Scan duration: {result.Duration.TotalMilliseconds:0} ms; elevated: {result.IsElevated}; cancelled: {result.WasCancelled}");
        sb.AppendLine($"Issues recorded: {result.Issues.Count}");
        foreach (var issue in result.Issues)
        {
            sb.AppendLine($"[{issue.Severity}] {issue.Component}: {issue.Message}{(issue.Detail is { } d ? $" ({d})" : string.Empty)}");
        }
        return sb.ToString();
    }

    /// <summary>Everything the detection layer produced, with provenance, in a stable text layout. Private: only reachable through <see cref="Build"/>, which redaction-checks it.</summary>
    private static string RawDetection(DiagnosticsBundleRequest request)
    {
        var s = request.ScanResult.Snapshot;
        var sb = new StringBuilder();
        if (request.IsDemoData)
        {
            sb.AppendLine("# DEMONSTRATION DATA (SellSpec demo device, not real hardware)");
        }
        void H(string title) { sb.AppendLine(); sb.AppendLine($"## {title}"); }
        void L(string label, string? value) => sb.AppendLine($"{label,-28} {value ?? "-"}");
        string F<T>(Spec<T> spec) => spec.HasValue
            ? $"{spec.Value} [{spec.Provenance}]{(spec.Note is { } n ? $" ({n})" : string.Empty)}"
            : $"- [{spec.Provenance}]{(spec.Note is { } n2 ? $" ({n2})" : string.Empty)}";

        sb.AppendLine("# SellSpec raw detection dump");
        L("SellSpec version", request.ApplicationVersion);
        L("Scanned", request.ScanResult.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        L("Elevated", request.ScanResult.IsElevated.ToString());

        var id = s.Identity;
        H("Identity");
        L("Raw manufacturer", id.RawManufacturer);
        L("Raw model", id.RawModel);
        L("Raw version", id.RawVersion);
        L("Manufacturer", F(id.Manufacturer));
        L("Product name", F(id.ProductName));
        L("Model number", F(id.ModelNumber));
        L("SKU", F(id.Sku));
        L("System family", F(id.SystemFamily));
        L("Form factor", F(id.FormFactor));
        L("Chassis type codes", id.ChassisTypeCodes.Count == 0 ? "-" : string.Join(",", id.ChassisTypeCodes));
        L("Architecture", F(id.Architecture));
        L("Board manufacturer", F(id.BoardManufacturer));
        L("Board model", F(id.BoardModel));
        L("Board version", F(id.BoardVersion));
        L("BIOS vendor", F(id.BiosVendor));
        L("BIOS version", F(id.BiosVersion));
        L("BIOS date", F(id.BiosReleaseDate));
        L("Firmware type", F(id.FirmwareType));
        L("Custom-built", id.IsCustomBuilt.ToString());
        L("Virtual machine", id.IsVirtualMachine ? id.VirtualisationPlatform ?? "yes" : "no");
        L("Display name", id.DisplayName);

        var cpu = s.Processor;
        H("Processor");
        L("Raw name", cpu.RawName);
        L("Name", F(cpu.Name));
        L("Family", cpu.FamilyShortName);
        L("Manufacturer", F(cpu.Manufacturer));
        L("Physical cores", F(cpu.PhysicalCores));
        L("Logical processors", F(cpu.LogicalProcessors));
        L("Clock MHz", F(cpu.ClockSpeedMHz));
        L("Architecture", F(cpu.Architecture));
        L("Installed count", F(cpu.InstalledCount));
        L("L2 KB", F(cpu.L2CacheKB));
        L("L3 KB", F(cpu.L3CacheKB));
        L("Socket", cpu.SocketDesignation);

        var m = s.Memory;
        H("Memory");
        L("Installed bytes", F(m.InstalledBytes));
        L("Usable bytes", F(m.UsableBytes));
        L("Technology", F(m.Technology));
        L("Configured MT/s", F(m.ConfiguredSpeedMT));
        L("Reported slots", F(m.ReportedSlots));
        L("Reported max bytes", F(m.ReportedMaxBytes));
        L("Upgradeability", F(m.Upgradeability));
        var mi = 0;
        foreach (var mod in m.Modules)
        {
            mi++;
            L($"Module {mi}", $"{mod.CapacityBytes} B; {mod.Technology ?? "-"}; rated {mod.SpeedMT?.ToString() ?? "-"}; configured {mod.ConfiguredSpeedMT?.ToString() ?? "-"}; {mod.FormFactor ?? "-"}; slot {mod.Slot ?? "-"}; bank {mod.BankLabel ?? "-"}; {mod.Manufacturer ?? "-"}; part {mod.PartNumber ?? "-"}");
        }

        H("Storage");
        var di = 0;
        foreach (var d in s.Storage)
        {
            di++;
            sb.AppendLine($"-- drive {di} --");
            L("Model", d.Model);
            L("Manufacturer", F(d.Manufacturer));
            L("Capacity bytes", F(d.CapacityBytes));
            L("Media / bus / location", $"{d.Media} / {d.Bus} / {d.Location}");
            L("Boot drive", d.IsBootDrive.ToString());
            L("Drive letters", d.DriveLetters.Count == 0 ? "-" : string.Join(",", d.DriveLetters));
            L("Disk number", d.DiskNumber?.ToString() ?? "-");
            L("Reported health", F(d.ReportedHealth));
            L("Power-on hours", F(d.PowerOnHours));
            L("Wear % used", F(d.WearPercentUsed));
            L("Temperature C", F(d.TemperatureC));
            L("Predicted failure", F(d.PredictedFailure));
            L("Spindle rpm", F(d.SpindleRpm));
            L("Firmware", d.FirmwareVersion);
        }

        H("Graphics");
        var gi = 0;
        foreach (var g in s.Graphics)
        {
            gi++;
            sb.AppendLine($"-- adapter {gi} --");
            L("Raw name", g.RawName);
            L("Name", g.Name);
            L("Vendor", F(g.Vendor));
            L("Kind", g.Kind.ToString());
            L("Dedicated bytes", F(g.DedicatedMemoryBytes));
            L("Driver", $"{g.DriverVersion ?? "-"} {(g.DriverDate is { } dd ? dd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)}".Trim());
            L("Active / fallback", $"{g.IsActive} / {g.IsFallbackDriver}");
        }

        H("Displays");
        var xi = 0;
        foreach (var d in s.Displays)
        {
            xi++;
            sb.AppendLine($"-- display {xi} --");
            L("Name", d.Name);
            L("Manufacturer", F(d.Manufacturer));
            L("EDID codes", $"{d.ManufacturerCode ?? "-"} / {d.ProductCode ?? "-"}");
            L("Internal / primary", $"{d.IsInternal} / {d.IsPrimary}");
            L("Native", F(d.NativeResolution));
            L("Current", F(d.CurrentResolution));
            L("Refresh current", F(d.CurrentRefreshRateHz));
            L("Refresh max", F(d.MaxRefreshRateHz));
            L("Physical size", F(d.PhysicalSize));
            L("Diagonal inches", F(d.DiagonalInches));
            L("Touch", F(d.Touch));
            L("Marketing name", d.ResolutionMarketingName);
            L("Connection", d.ConnectionLabel);
        }

        var b = s.Battery;
        H("Battery");
        L("Presence", b.Presence.ToString());
        L("Design mWh", F(b.DesignCapacityMwh));
        L("Full charge mWh", F(b.FullChargeCapacityMwh));
        L("Health %", F(b.HealthPercent));
        L("Cycle count", F(b.CycleCount));
        L("Charge %", F(b.ChargePercent));
        L("Charging status", F(b.ChargingStatus));
        L("Chemistry", F(b.Chemistry));
        L("Windows condition", F(b.ReportedCondition));
        var bi = 0;
        foreach (var p in b.Packs)
        {
            bi++;
            L($"Pack {bi}", $"{p.Name ?? "-"}; {p.Manufacturer ?? "-"}; design {F(p.DesignCapacityMwh)}; full {F(p.FullChargeCapacityMwh)}; cycles {F(p.CycleCount)}; {F(p.Chemistry)}");
        }

        var c = s.Connectivity;
        H("Connectivity");
        L("Wi-Fi adapter", F(c.WifiAdapter));
        L("Wi-Fi standard", F(c.WifiStandard));
        L("Bluetooth", F(c.Bluetooth));
        L("Ethernet", F(c.Ethernet));
        L("Webcam", F(c.Webcam));
        L("Audio", F(c.Audio));
        L("Fingerprint", F(c.FingerprintReader));
        L("Network adapters", c.NetworkAdapters.Count == 0 ? "-" : string.Join("; ", c.NetworkAdapters));
        L("Cameras", c.Cameras.Count == 0 ? "-" : string.Join("; ", c.Cameras));
        L("Audio devices", c.AudioDevices.Count == 0 ? "-" : string.Join("; ", c.AudioDevices));
        L("Bluetooth radios", c.BluetoothRadios.Count == 0 ? "-" : string.Join("; ", c.BluetoothRadios));

        var os = s.OperatingSystem;
        H("Windows");
        L("Name", F(os.Name));
        L("Edition", F(os.Edition));
        L("Version", F(os.Version));
        L("Build", F(os.Build));
        L("Architecture", F(os.Architecture));
        L("Install date", F(os.InstallDate));
        L("Activation", F(os.Activation));
        L("Licence channel", F(os.LicenceChannel));
        L("TPM", F(os.TpmVersion));
        L("Secure Boot", F(os.SecureBootEnabled));
        L("Firmware type", F(os.FirmwareType));
        if (os.Windows11Compatibility is { } w11)
        {
            L("Windows 11 readiness", $"{w11.Status}: {w11.Summary}");
            foreach (var check in w11.Checks)
            {
                L($"  {check.Requirement}", $"{check.Status}: {check.Detail}");
            }
        }

        return sb.ToString();
    }
}
