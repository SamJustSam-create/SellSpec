namespace SellSpec.Core.Models;

public sealed record StorageDevice
{
    public string Model { get; init; } = "Unknown drive";
    public Spec<string> Manufacturer { get; init; } = Spec<string>.Unavailable();
    public Spec<ulong> CapacityBytes { get; init; } = Spec<ulong>.Unavailable();
    public StorageMediaType Media { get; init; } = StorageMediaType.Unknown;
    public StorageBus Bus { get; init; } = StorageBus.Unknown;
    public StorageLocation Location { get; init; } = StorageLocation.Unknown;
    public bool IsBootDrive { get; init; }
    public IReadOnlyList<string> DriveLetters { get; init; } = Array.Empty<string>();

    public Spec<int> PowerOnHours { get; init; } = Spec<int>.Unavailable();
    public Spec<HealthStatusKind> ReportedHealth { get; init; } = Spec<HealthStatusKind>.Unavailable();
    public Spec<int> WearPercentUsed { get; init; } = Spec<int>.Unavailable();
    public Spec<int> TemperatureC { get; init; } = Spec<int>.Unavailable();
    public Spec<bool> PredictedFailure { get; init; } = Spec<bool>.Unavailable();
    public Spec<int> SpindleRpm { get; init; } = Spec<int>.Unavailable();
    public string? FirmwareVersion { get; init; }

    /// <summary>Disk number as understood by Windows (Disk 0, Disk 1...). Used to de-duplicate sources.</summary>
    public int? DiskNumber { get; init; }

    /// <summary>Internal drives are included in listings and reports; external, removable and virtual drives are excluded by default.</summary>
    public bool IncludeInListing => Location == StorageLocation.Internal;

    /// <summary>"NVMe SSD", "SATA SSD", "Hard disk (HDD)"...</summary>
    public string TypeLabel
    {
        get
        {
            return (Media, Bus) switch
            {
                (StorageMediaType.Ssd, StorageBus.Nvme) => "NVMe SSD",
                (StorageMediaType.Ssd, StorageBus.Sata) => "SATA SSD",
                (StorageMediaType.Ssd, StorageBus.Usb) => "External SSD",
                (StorageMediaType.Ssd, _) => "SSD",
                (StorageMediaType.Hdd, StorageBus.Usb) => "External hard disk",
                (StorageMediaType.Hdd, _) => SpindleRpm.HasValue ? $"Hard disk ({SpindleRpm.Value} rpm HDD)" : "Hard disk (HDD)",
                (StorageMediaType.Scm, _) => "Storage-class memory",
                (_, StorageBus.Nvme) => "NVMe drive",
                (_, StorageBus.SdCard) => "Memory card",
                (_, StorageBus.Virtual) => "Virtual disk",
                (_, StorageBus.Usb) => "USB drive",
                _ => "Drive",
            };
        }
    }

    /// <summary>Short buyer-facing type for listings: "SSD" or "HDD".</summary>
    public string ShortTypeLabel => Media switch
    {
        StorageMediaType.Ssd => Bus == StorageBus.Nvme ? "NVMe SSD" : "SSD",
        StorageMediaType.Hdd => "HDD",
        StorageMediaType.Scm => "Optane",
        _ => "drive",
    };
}
