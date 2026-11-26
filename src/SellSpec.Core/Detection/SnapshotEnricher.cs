using System.Collections.Concurrent;
using SellSpec.Core.Health;
using SellSpec.Core.Models;

namespace SellSpec.Core.Detection;

/// <summary>Cross-component reasoning that needs more than one provider's output.</summary>
public static class SnapshotEnricher
{
    public static DeviceSnapshot Enrich(DeviceSnapshot snapshot, ConcurrentBag<ScanIssue>? issues = null)
    {
        var identity = snapshot.Identity;
        var battery = snapshot.Battery;

        // Form factor fallback: firmware did not say, so use battery presence.
        if (!identity.FormFactor.HasValue || identity.FormFactor.Value == DeviceFormFactor.Unknown)
        {
            var guess = battery.Presence switch
            {
                BatteryPresence.Present => DeviceFormFactor.Laptop,
                BatteryPresence.NotPresent => DeviceFormFactor.Desktop,
                _ => DeviceFormFactor.Unknown,
            };
            if (guess != DeviceFormFactor.Unknown)
            {
                identity = identity with { FormFactor = Spec<DeviceFormFactor>.Estimated(guess, "Estimated from battery presence; firmware did not report a chassis type.") };
            }
        }

        // A desktop-class device with no battery data: make the "not applicable" state explicit.
        if (battery.Presence == BatteryPresence.Unknown && identity.FormFactor.HasValue &&
            identity.FormFactor.Value is DeviceFormFactor.Desktop or DeviceFormFactor.MiniPc or DeviceFormFactor.Server)
        {
            battery = BatteryInfo.NotApplicable("No battery detected. This is normal for desktop computers.");
        }

        var os = snapshot.OperatingSystem;
        var enriched = snapshot with { Identity = identity, Battery = battery };
        enriched = enriched with
        {
            OperatingSystem = os with { Windows11Compatibility = Windows11Compatibility.Assess(enriched) },
        };

        if (issues is not null)
        {
            if (identity.IsVirtualMachine)
            {
                issues.Add(new ScanIssue("Device", $"This looks like a virtual machine ({identity.VirtualisationPlatform ?? "unknown platform"}). Listings describe virtual hardware, not a physical computer.", ScanIssueSeverity.Warning));
            }
            if (enriched.Graphics.Any(g => g.IsFallbackDriver))
            {
                issues.Add(new ScanIssue("Graphics", "A basic display driver is in use; graphics details may be incomplete until the proper driver is installed.", ScanIssueSeverity.Warning));
            }
            if (enriched.Storage.Count == 0)
            {
                issues.Add(new ScanIssue("Storage", "No storage devices were detected.", ScanIssueSeverity.Warning));
            }
        }

        return enriched;
    }
}
