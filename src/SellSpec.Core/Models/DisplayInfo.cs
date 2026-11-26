namespace SellSpec.Core.Models;

public readonly record struct Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
    public long PixelCount => (long)Width * Height;
}

public readonly record struct PhysicalSize(double WidthCm, double HeightCm)
{
    public double DiagonalInches => Math.Sqrt(WidthCm * WidthCm + HeightCm * HeightCm) / 2.54;
}

public sealed record DisplayInfo
{
    /// <summary>EDID friendly name if present (e.g. "DELL U2722D"); null for most laptop panels.</summary>
    public string? Name { get; init; }

    /// <summary>Decoded manufacturer, e.g. "LG Display", "AU Optronics", "Dell".</summary>
    public Spec<string> Manufacturer { get; init; } = Spec<string>.Unavailable();
    public string? ManufacturerCode { get; init; }
    public string? ProductCode { get; init; }

    public bool IsInternal { get; init; }
    public bool IsPrimary { get; init; }

    public Spec<Resolution> NativeResolution { get; init; } = Spec<Resolution>.Unavailable();
    public Spec<Resolution> CurrentResolution { get; init; } = Spec<Resolution>.Unavailable();
    public Spec<double> CurrentRefreshRateHz { get; init; } = Spec<double>.Unavailable();
    public Spec<double> MaxRefreshRateHz { get; init; } = Spec<double>.Unavailable();
    public Spec<PhysicalSize> PhysicalSize { get; init; } = Spec<PhysicalSize>.Unavailable();

    /// <summary>Diagonal in inches, estimated from EDID physical size and snapped to common panel sizes.</summary>
    public Spec<double> DiagonalInches { get; init; } = Spec<double>.Unavailable();

    public Spec<bool> Touch { get; init; } = Spec<bool>.Unavailable();

    /// <summary>"Full HD", "QHD", "4K UHD" when the native resolution matches a well-known marketing name.</summary>
    public string? ResolutionMarketingName { get; init; }

    public string? ConnectionLabel { get; init; }

    public string LocationLabel => IsInternal ? "Built-in display" : "External monitor";
}
