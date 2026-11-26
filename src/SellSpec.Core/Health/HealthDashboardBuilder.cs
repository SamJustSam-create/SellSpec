using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Core.Testing;

namespace SellSpec.Core.Health;

/// <summary>One row on the device health dashboard. Colour is always paired with a text label.</summary>
public sealed record HealthItem(string Group, string Title, HealthStatusKind Status, string Value, string? Detail, Provenance Provenance)
{
    public string StatusLabel => HealthDashboardBuilder.StatusLabel(Status);
}

public static class HealthDashboardBuilder
{
    public static string StatusLabel(HealthStatusKind status) => status switch
    {
        HealthStatusKind.Good => "Good",
        HealthStatusKind.Attention => "Attention",
        HealthStatusKind.Issue => "Issue",
        HealthStatusKind.NotApplicable => "Not applicable",
        _ => "Unavailable",
    };

    public static IReadOnlyList<HealthItem> Build(DeviceSnapshot snapshot, IReadOnlyList<HardwareTestResult> tests)
    {
        var items = new List<HealthItem>();

        // Battery
        var battery = snapshot.Battery;
        if (battery.Presence == BatteryPresence.NotPresent)
        {
            items.Add(new HealthItem("Battery", "Battery health", HealthStatusKind.NotApplicable, "No battery", "No battery detected. This is normal for desktop computers.", Provenance.NotApplicable));
        }
        else if (battery.HealthPercent.HasValue)
        {
            items.Add(new HealthItem("Battery", "Reported battery health", battery.HealthStatus, $"{battery.HealthPercent.Value}% of design capacity",
                BatteryHealthCalculator.Describe(battery.HealthPercent.Value!) + (battery.HealthPercent.Note is { } n ? " " + n : string.Empty) +
                " This is a reported capacity estimate, not a guarantee of runtime or battery safety.", battery.HealthPercent.Provenance));
            items.Add(new HealthItem("Battery", "Battery cycle count", battery.CycleCount.HasValue && battery.CycleCount.Value > 0 ? HealthStatusKind.Good : HealthStatusKind.Unknown,
                battery.CycleCount.HasValue ? battery.CycleCount.Value!.ToString() : "Not reported", battery.CycleCount.Note, battery.CycleCount.Provenance));
        }
        else
        {
            items.Add(new HealthItem("Battery", "Reported battery health", HealthStatusKind.Unknown, "Unavailable",
                battery.HealthPercent.Note ?? "The battery did not report capacity information.", Provenance.Unavailable));
        }

        // Storage
        var drives = snapshot.Storage.Where(s => s.Location is StorageLocation.Internal or StorageLocation.Unknown).ToList();
        if (drives.Count == 0)
        {
            items.Add(new HealthItem("Storage", "Storage health", HealthStatusKind.Unknown, "No internal drives detected", null, Provenance.Unavailable));
        }
        foreach (var drive in drives)
        {
            var label = drive.CapacityBytes.HasValue ? $"{CapacityFormatter.FormatStorage(drive.CapacityBytes.Value)} {drive.TypeLabel}" : drive.TypeLabel;
            var a = StorageHealthAssessor.Assess(drive);
            items.Add(new HealthItem("Storage", $"{label} health", a.Status, a.Summary, $"{drive.Model}. {a.Detail}", drive.ReportedHealth.HasValue ? drive.ReportedHealth.Provenance : Provenance.Unavailable));
            items.Add(new HealthItem("Storage", $"{label} power-on hours",
                drive.PowerOnHours.HasValue ? (drive.PowerOnHours.Value >= StorageHealthAssessor.PowerOnHoursAttention ? HealthStatusKind.Attention : HealthStatusKind.Good) : HealthStatusKind.Unknown,
                drive.PowerOnHours.HasValue ? StorageHealthAssessor.FormatHours(drive.PowerOnHours.Value!) : "Not available",
                drive.PowerOnHours.Note, drive.PowerOnHours.Provenance));
        }

        // Windows
        var os = snapshot.OperatingSystem;
        var (actStatus, actText, actDetail) = os.Activation.HasValue
            ? os.Activation.Value switch
            {
                ActivationStatus.Licensed => (HealthStatusKind.Good, "Activated", os.LicenceChannel.HasValue ? $"Windows is activated ({os.LicenceChannel.Value} licence)." : "Windows is activated."),
                ActivationStatus.GracePeriod => (HealthStatusKind.Attention, "Grace period", "Windows is in an activation grace period."),
                ActivationStatus.Notification => (HealthStatusKind.Issue, "Not activated", "Windows is showing activation notifications."),
                ActivationStatus.Unlicensed => (HealthStatusKind.Issue, "Not activated", "Windows is not activated."),
                _ => (HealthStatusKind.Unknown, "Unknown", null),
            }
            : (HealthStatusKind.Unknown, "Unavailable", os.Activation.Note ?? "Activation status could not be read.");
        items.Add(new HealthItem("Windows", "Windows activation", actStatus, actText, actDetail, os.Activation.Provenance));

        if (os.Windows11Compatibility is { } w11)
        {
            items.Add(new HealthItem("Windows", "Windows 11 compatibility", w11.Status, w11.Status switch
            {
                HealthStatusKind.Good => "Compatible (checked requirements)",
                HealthStatusKind.Attention => "Likely compatible",
                HealthStatusKind.Issue => "Requirement not met",
                _ => "Not fully assessed",
            }, w11.Summary, Provenance.Estimated));
        }

        // Device warnings
        if (snapshot.Identity.IsVirtualMachine)
        {
            items.Add(new HealthItem("Device", "Virtual machine", HealthStatusKind.Attention, "Detected",
                $"This appears to be a virtual machine ({snapshot.Identity.VirtualisationPlatform ?? "unknown platform"}). Specifications describe the virtual hardware, not a physical computer.", Provenance.Detected));
        }
        foreach (var gpu in snapshot.Graphics.Where(g => g.IsFallbackDriver))
        {
            items.Add(new HealthItem("Device", "Graphics driver", HealthStatusKind.Attention, "Basic display driver in use",
                $"{gpu.Name} is active, which usually means the proper graphics driver is not installed. Graphics details may be incomplete.", Provenance.Detected));
        }

        // Hardware tests
        foreach (var kind in HardwareTestCatalog.All)
        {
            var result = tests.FirstOrDefault(t => t.Kind == kind) ?? HardwareTestResult.NotTested(kind);
            var status = result.Outcome switch
            {
                TestOutcome.Passed => HealthStatusKind.Good,
                TestOutcome.PassedWithNotes => HealthStatusKind.Attention,
                TestOutcome.Failed => HealthStatusKind.Issue,
                _ => HealthStatusKind.Unknown,
            };
            var value = HardwareTestCatalog.OutcomeLabel(result.Outcome);
            var detail = result.WasTested
                ? (result.Source == TestSource.SellerConfirmed ? "Seller-confirmed result." : "Automatically measured.") +
                  (result.Evidence is { } e ? $" {e}." : string.Empty) + (result.Notes is { } notes && notes.Length > 0 ? $" Notes: {notes}" : string.Empty)
                : "Run this test from the Hardware Tests page.";
            items.Add(new HealthItem("Hardware tests", $"{HardwareTestCatalog.Title(kind)} test", status, value, detail,
                result.WasTested ? Provenance.SellerProvided : Provenance.Unavailable));
        }

        return items;
    }
}
