using System.Text.RegularExpressions;
using SellSpec.Core.Models;

namespace SellSpec.Core.Normalisation;

/// <summary>Classifies graphics adapters as integrated, dedicated or virtual and tidies their names.</summary>
public static partial class GraphicsClassifier
{
    public static string NormaliseName(string? raw)
    {
        var cleaned = GenericValueDetector.Clean(raw) ?? "Unknown graphics adapter";
        var name = Marks().Replace(cleaned, string.Empty);
        name = GenericValueDetector.CollapseWhitespace(name);
        name = Regex.Replace(name, @"^Intel\s+Iris\s+Xe\s+Graphics$", "Intel Iris Xe Graphics", RegexOptions.IgnoreCase);
        return name;
    }

    public static string? VendorFromName(string name, string? adapterCompatibility)
    {
        var n = name + " " + (adapterCompatibility ?? string.Empty);
        if (n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (n.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(n, @"\bAMD\b|\bRadeon\b|\bATI\b", RegexOptions.IgnoreCase)) return "AMD";
        if (n.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (n.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) || n.Contains("Adreno", StringComparison.OrdinalIgnoreCase)) return "Qualcomm";
        if (n.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
        if (n.Contains("VMware", StringComparison.OrdinalIgnoreCase)) return "VMware";
        if (n.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) || n.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) return "Oracle";
        if (n.Contains("Parallels", StringComparison.OrdinalIgnoreCase)) return "Parallels";
        if (n.Contains("Matrox", StringComparison.OrdinalIgnoreCase)) return "Matrox";
        if (n.Contains("ASPEED", StringComparison.OrdinalIgnoreCase)) return "ASPEED";
        return null;
    }

    public static bool IsFallbackDriver(string name) =>
        name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Standard VGA", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Classification uses the adapter name first and dedicated memory as a tie-breaker. Integrated parts
    /// often report a large "dedicated" allocation carved from system RAM, so memory alone is not trusted.
    /// </summary>
    public static GraphicsKind Classify(string name, string? vendor, ulong? reportedDedicatedBytes)
    {
        var n = name;

        if (VirtualAdapter().IsMatch(n)) return GraphicsKind.Virtual;
        if (IsFallbackDriver(n)) return GraphicsKind.Unknown;

        switch (vendor)
        {
            case "NVIDIA":
                return GraphicsKind.Dedicated;

            case "AMD":
                // Vega 3/6/8/10/11 are APU graphics ("Radeon RX Vega 10 Graphics"); Vega 56/64 are discrete cards.
                if (Regex.IsMatch(n, @"\bVega\s+(3|6|8|10|11)\b", RegexOptions.IgnoreCase)) return GraphicsKind.Integrated;
                if (Regex.IsMatch(n, @"\bRadeon\s+(RX|Pro|R9|R7\s+M|R5\s+M|HD\s+\d{4}|VII)\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(n, @"\bFirePro\b|\bRadeon\s+\d{3,4}M?\b(?!\s*Graphics)", RegexOptions.IgnoreCase))
                {
                    // "Radeon 780M Graphics", "Radeon 680M" are integrated; "Radeon 610M" too. Exclude the 3-digit M series.
                    if (Regex.IsMatch(n, @"\bRadeon\s+\d{3}M\b", RegexOptions.IgnoreCase)) return GraphicsKind.Integrated;
                    return GraphicsKind.Dedicated;
                }
                if (Regex.IsMatch(n, @"Radeon(\s+\w+)?\s+Graphics|Vega|\b\d{3}M\b|R[2-8]\s+Graphics", RegexOptions.IgnoreCase))
                {
                    return GraphicsKind.Integrated;
                }
                break;

            case "Intel":
                if (Regex.IsMatch(n, @"\bArc\b.*\b[AB]\d{3}\b", RegexOptions.IgnoreCase)) return GraphicsKind.Dedicated;
                if (Regex.IsMatch(n, @"Iris|UHD|HD Graphics|Arc\b|Xe\b|Graphics", RegexOptions.IgnoreCase)) return GraphicsKind.Integrated;
                break;

            case "Qualcomm":
                return GraphicsKind.Integrated;

            case "Microsoft":
            case "VMware":
            case "Oracle":
            case "Parallels":
                return GraphicsKind.Virtual;

            case "Matrox":
            case "ASPEED":
                return GraphicsKind.Dedicated; // server management graphics; harmless classification
        }

        if (reportedDedicatedBytes is >= 2UL * 1024 * 1024 * 1024) return GraphicsKind.Dedicated;
        return GraphicsKind.Unknown;
    }

    [GeneratedRegex(@"\((?:R|TM|C)\)|®|™", RegexOptions.IgnoreCase)]
    private static partial Regex Marks();

    [GeneratedRegex(@"VMware|VirtualBox|Hyper-V|Remote Display|RDP|Virtual|Parallels|QXL|Red Hat|Citrix|DisplayLink|IddCx|Indirect|USB Display|Meta Virtual|SpaceDesk|Duet", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualAdapter();
}
