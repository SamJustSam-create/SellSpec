namespace SellSpec.Core.Models;

public sealed record GraphicsAdapter
{
    /// <summary>Normalised adapter name, e.g. "NVIDIA GeForce RTX 3060 Laptop GPU".</summary>
    public string Name { get; init; } = "Unknown graphics adapter";
    public string? RawName { get; init; }
    public Spec<string> Vendor { get; init; } = Spec<string>.Unavailable();
    public GraphicsKind Kind { get; init; } = GraphicsKind.Unknown;

    /// <summary>Only populated for dedicated adapters when read from a reliable source. Shared system memory is never reported here.</summary>
    public Spec<ulong> DedicatedMemoryBytes { get; init; } = Spec<ulong>.Unavailable();

    public string? DriverVersion { get; init; }
    public DateOnly? DriverDate { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>True for "Microsoft Basic Display Adapter" or similar fallback drivers.</summary>
    public bool IsFallbackDriver { get; init; }

    public bool IncludeInListing => Kind is GraphicsKind.Integrated or GraphicsKind.Dedicated && !IsFallbackDriver;

    public string KindLabel => Kind switch
    {
        GraphicsKind.Integrated => "Integrated",
        GraphicsKind.Dedicated => "Dedicated",
        GraphicsKind.Virtual => "Virtual",
        _ => "Unknown type",
    };
}
