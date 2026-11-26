namespace SellSpec.Core.Models;

public sealed record MemoryModule
{
    public ulong CapacityBytes { get; init; }
    public string? Technology { get; init; }
    public int? SpeedMT { get; init; }
    public int? ConfiguredSpeedMT { get; init; }
    public string? FormFactor { get; init; }
    public string? Slot { get; init; }
    public string? BankLabel { get; init; }
    public string? Manufacturer { get; init; }
    public string? PartNumber { get; init; }
}

public sealed record MemoryInfo
{
    /// <summary>Sum of installed module capacities (binary bytes).</summary>
    public Spec<ulong> InstalledBytes { get; init; } = Spec<ulong>.Unavailable();

    /// <summary>Memory visible to Windows after firmware/graphics reservations.</summary>
    public Spec<ulong> UsableBytes { get; init; } = Spec<ulong>.Unavailable();

    /// <summary>DDR4, DDR5, LPDDR5 and so on.</summary>
    public Spec<string> Technology { get; init; } = Spec<string>.Unavailable();

    /// <summary>Configured transfer rate in MT/s (commonly marketed as MHz).</summary>
    public Spec<int> ConfiguredSpeedMT { get; init; } = Spec<int>.Unavailable();

    public IReadOnlyList<MemoryModule> Modules { get; init; } = Array.Empty<MemoryModule>();

    /// <summary>Slot count as reported by firmware. Does not imply the memory is physically replaceable.</summary>
    public Spec<int> ReportedSlots { get; init; } = Spec<int>.Unavailable();

    /// <summary>Maximum capacity as reported by firmware. Frequently wrong on laptops; never present as verified.</summary>
    public Spec<ulong> ReportedMaxBytes { get; init; } = Spec<ulong>.Unavailable();

    /// <summary>
    /// Always "Not verified" in the MVP. The note carries firmware hints (e.g. LPDDR is normally soldered) without making a claim.
    /// </summary>
    public Spec<string> Upgradeability { get; init; } = Spec<string>.Unavailable();

    public static MemoryInfo Unavailable(string? note = null) => new()
    {
        InstalledBytes = Spec<ulong>.Unavailable(note),
        Upgradeability = Spec<string>.Estimated("Not verified", "Upgrade options were not checked against manufacturer information."),
    };
}
