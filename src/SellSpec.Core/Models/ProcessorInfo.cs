namespace SellSpec.Core.Models;

public sealed record ProcessorInfo
{
    public Spec<string> Manufacturer { get; init; } = Spec<string>.Unavailable();

    /// <summary>Buyer-friendly normalised name, e.g. "Intel Core i5-1335U".</summary>
    public Spec<string> Name { get; init; } = Spec<string>.Unavailable();

    /// <summary>Short family used in listing titles, e.g. "Intel Core i5", "AMD Ryzen 7".</summary>
    public string? FamilyShortName { get; init; }

    public string? RawName { get; init; }

    public Spec<int> PhysicalCores { get; init; } = Spec<int>.Unavailable();
    public Spec<int> LogicalProcessors { get; init; } = Spec<int>.Unavailable();

    /// <summary>Reported maximum/base clock in MHz as exposed by firmware.</summary>
    public Spec<int> ClockSpeedMHz { get; init; } = Spec<int>.Unavailable();

    public Spec<string> Architecture { get; init; } = Spec<string>.Unavailable();
    public Spec<int> InstalledCount { get; init; } = Spec<int>.Unavailable();
    public Spec<int> L2CacheKB { get; init; } = Spec<int>.Unavailable();
    public Spec<int> L3CacheKB { get; init; } = Spec<int>.Unavailable();
    public string? SocketDesignation { get; init; }

    public static ProcessorInfo Unavailable(string? note = null) => new()
    {
        Name = Spec<string>.Unavailable(note),
    };
}
