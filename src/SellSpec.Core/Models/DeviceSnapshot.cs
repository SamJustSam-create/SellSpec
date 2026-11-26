namespace SellSpec.Core.Models;

/// <summary>Everything SellSpec detected about the device in one scan.</summary>
public sealed record DeviceSnapshot
{
    public DeviceIdentity Identity { get; init; } = new();
    public ProcessorInfo Processor { get; init; } = new();
    public MemoryInfo Memory { get; init; } = new();
    public IReadOnlyList<StorageDevice> Storage { get; init; } = Array.Empty<StorageDevice>();
    public IReadOnlyList<GraphicsAdapter> Graphics { get; init; } = Array.Empty<GraphicsAdapter>();
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = Array.Empty<DisplayInfo>();
    public BatteryInfo Battery { get; init; } = new();
    public ConnectivityInfo Connectivity { get; init; } = new();
    public OperatingSystemInfo OperatingSystem { get; init; } = new();

    /// <summary>Owner-only identifiers; excluded from every export.</summary>
    public PrivateIdentifiers Private { get; init; } = PrivateIdentifiers.Empty;

    public IEnumerable<StorageDevice> ListingStorage => Storage.Where(s => s.IncludeInListing);
    public IEnumerable<GraphicsAdapter> ListingGraphics => Graphics.Where(g => g.IncludeInListing);
    public DisplayInfo? PrimaryDisplay => Displays.FirstOrDefault(d => d.IsInternal) ?? Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
    public bool HasBattery => Battery.Presence == BatteryPresence.Present;
}
