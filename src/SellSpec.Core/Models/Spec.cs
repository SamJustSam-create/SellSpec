namespace SellSpec.Core.Models;

/// <summary>
/// Describes where a specification value came from and how much trust a buyer should place in it.
/// Every important specification in SellSpec carries one of these states so that the UI, listings and
/// reports can label values honestly instead of presenting everything as fact.
/// </summary>
public enum Provenance
{
    /// <summary>Read directly from the operating system or hardware (for example via WMI, registry or Win32 APIs).</summary>
    Detected,

    /// <summary>Reported by the device firmware (SMBIOS/ACPI/EDID). Usually right, but OEM firmware can be generic or wrong.</summary>
    FirmwareReported,

    /// <summary>Confirmed against a manufacturer specification source. Not produced by the MVP; reserved for future features.</summary>
    ManufacturerVerified,

    /// <summary>Entered by the seller (questionnaire or confirmed hardware test).</summary>
    SellerProvided,

    /// <summary>Derived by calculation or heuristic (for example screen diagonal from EDID dimensions, Wi-Fi generation from an adapter name).</summary>
    Estimated,

    /// <summary>The value could not be read. It must never be presented as a passing or positive result.</summary>
    Unavailable,

    /// <summary>The specification does not apply to this device (for example battery health on a desktop).</summary>
    NotApplicable,
}

/// <summary>
/// A value paired with its <see cref="Provenance"/> and an optional human-readable note explaining the source or caveat.
/// </summary>
public sealed record Spec<T>(T? Value, Provenance Provenance, string? Note = null)
{
    /// <summary>True when a real value is present (not unavailable / not applicable).</summary>
    public bool HasValue => Value is not null && Provenance is not (Provenance.Unavailable or Provenance.NotApplicable);

    public bool IsUnavailable => Provenance == Provenance.Unavailable;
    public bool IsNotApplicable => Provenance == Provenance.NotApplicable;

    public static Spec<T> Detected(T value, string? note = null) => new(value, Provenance.Detected, note);
    public static Spec<T> Firmware(T value, string? note = null) => new(value, Provenance.FirmwareReported, note);
    public static Spec<T> Estimated(T value, string? note = null) => new(value, Provenance.Estimated, note);
    public static Spec<T> Seller(T value, string? note = null) => new(value, Provenance.SellerProvided, note);
    public static Spec<T> Unavailable(string? note = null) => new(default, Provenance.Unavailable, note);
    public static Spec<T> NotApplicable(string? note = null) => new(default, Provenance.NotApplicable, note);

    /// <summary>Returns the value, or <paramref name="fallback"/> when unavailable.</summary>
    public T ValueOr(T fallback) => HasValue ? Value! : fallback;

    /// <summary>Projects the value to another type while preserving provenance.</summary>
    public Spec<TOut> Map<TOut>(Func<T, TOut> projector) =>
        HasValue ? new Spec<TOut>(projector(Value!), Provenance, Note) : new Spec<TOut>(default, Provenance, Note);

    public override string ToString() => HasValue ? $"{Value} [{Provenance}]" : Provenance.ToString();
}

/// <summary>Helpers for creating specs with inferred type parameters.</summary>
public static class Spec
{
    public static Spec<T> Detected<T>(T value, string? note = null) => Spec<T>.Detected(value, note);
    public static Spec<T> Firmware<T>(T value, string? note = null) => Spec<T>.Firmware(value, note);
    public static Spec<T> Estimated<T>(T value, string? note = null) => Spec<T>.Estimated(value, note);
    public static Spec<T> Seller<T>(T value, string? note = null) => Spec<T>.Seller(value, note);

    /// <summary>Creates a firmware-reported spec, or an unavailable spec when the raw value is null, empty or a known generic placeholder.</summary>
    public static Spec<string> FirmwareText(string? raw, string? unavailableNote = null)
    {
        var cleaned = Normalisation.GenericValueDetector.Clean(raw);
        return cleaned is null ? Spec<string>.Unavailable(unavailableNote) : Spec<string>.Firmware(cleaned);
    }

    /// <summary>Creates a detected spec, or an unavailable spec when the raw value is null or whitespace.</summary>
    public static Spec<string> DetectedText(string? raw, string? unavailableNote = null)
    {
        var cleaned = Normalisation.GenericValueDetector.Clean(raw);
        return cleaned is null ? Spec<string>.Unavailable(unavailableNote) : Spec<string>.Detected(cleaned);
    }

    public static string Label(Provenance provenance) => provenance switch
    {
        Provenance.Detected => "Detected",
        Provenance.FirmwareReported => "Firmware reported",
        Provenance.ManufacturerVerified => "Manufacturer verified",
        Provenance.SellerProvided => "Seller provided",
        Provenance.Estimated => "Estimated",
        Provenance.Unavailable => "Unavailable",
        Provenance.NotApplicable => "Not applicable",
        _ => provenance.ToString(),
    };
}
