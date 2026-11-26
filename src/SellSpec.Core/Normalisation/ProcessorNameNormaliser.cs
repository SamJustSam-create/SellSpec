using System.Text.RegularExpressions;

namespace SellSpec.Core.Normalisation;

/// <summary>
/// Turns firmware processor strings such as "12th Gen Intel(R) Core(TM) i7-1260P" or
/// "AMD Ryzen 7 5800H with Radeon Graphics" into tidy buyer-facing names without changing meaning.
/// </summary>
public static partial class ProcessorNameNormaliser
{
    public sealed record Result(string Name, string? FamilyShortName, string? Manufacturer, int? Generation);

    public static Result Normalise(string? raw)
    {
        var cleaned = GenericValueDetector.Clean(raw);
        if (cleaned is null)
        {
            return new Result("Unknown processor", null, null, null);
        }

        var name = cleaned;
        name = TrademarkMarks().Replace(name, string.Empty);
        name = ClockSuffix().Replace(name, string.Empty);
        name = RadeonSuffix().Replace(name, string.Empty);
        name = CpuWord().Replace(name, " ");
        name = ProcessorWord().Replace(name, " ");
        name = CoreCountSuffix().Replace(name, " ");
        name = GenericIntelPrefix().Replace(name, "Intel ");
        name = GenericValueDetector.CollapseWhitespace(name);

        int? generation = null;
        var genMatch = LeadingGeneration().Match(name);
        if (genMatch.Success)
        {
            generation = int.Parse(genMatch.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            name = GenericValueDetector.CollapseWhitespace(name[genMatch.Length..]);
            name = $"{name} ({genMatch.Groups["n"].Value}{genMatch.Groups["suffix"].Value} Gen)";
        }

        // Normalise brand casing.
        name = Regex.Replace(name, @"^intel\b", "Intel", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"^amd\b", "AMD", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"^qualcomm\b", "Qualcomm", RegexOptions.IgnoreCase);
        name = name.Trim(' ', '-', ',');

        if (name.Length == 0)
        {
            name = cleaned;
        }

        var manufacturer = DetectManufacturer(name);
        var family = DetectFamily(name);
        return new Result(name, family, manufacturer, generation);
    }

    public static string? DetectManufacturer(string name)
    {
        if (name.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (name.StartsWith("AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (name.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Qualcomm", StringComparison.OrdinalIgnoreCase)) return "Qualcomm";
        if (name.StartsWith("Apple", StringComparison.OrdinalIgnoreCase)) return "Apple";
        if (name.Contains("VIA", StringComparison.Ordinal)) return "VIA";
        return null;
    }

    /// <summary>"Intel Core i5", "Intel Core Ultra 7", "AMD Ryzen 7", "Intel Celeron", "Snapdragon X Elite".</summary>
    public static string? DetectFamily(string name)
    {
        var m = IntelCoreUltra().Match(name);
        if (m.Success) return $"Intel Core Ultra {m.Groups["tier"].Value}";

        m = IntelCoreI().Match(name);
        if (m.Success) return $"Intel Core i{m.Groups["tier"].Value}";

        m = IntelCoreBranded().Match(name);
        if (m.Success) return $"Intel Core {m.Groups["tier"].Value}";

        m = AmdRyzen().Match(name);
        if (m.Success) return $"AMD Ryzen {m.Groups["tier"].Value}";

        foreach (var (pattern, label) in new (string, string)[]
        {
            (@"\bIntel\b.*\bXeon\b", "Intel Xeon"),
            (@"\bIntel\b.*\bCeleron\b", "Intel Celeron"),
            (@"\bIntel\b.*\bPentium\b", "Intel Pentium"),
            (@"\bIntel\b.*\bAtom\b", "Intel Atom"),
            (@"\bIntel\b.*\bCore\s*2\b", "Intel Core 2"),
            (@"\bIntel\b.*\bN\d{2,3}\b", "Intel N-series"),
            (@"\bAMD\b.*\bAthlon\b", "AMD Athlon"),
            (@"\bAMD\b.*\bA\d{1,2}-\d", "AMD A-series"),
            (@"\bAMD\b.*\bFX\b", "AMD FX"),
            (@"\bAMD\b.*\bEPYC\b", "AMD EPYC"),
            (@"\bAMD\b.*\bThreadripper\b", "AMD Ryzen Threadripper"),
            (@"\bSnapdragon\s+X\s+Elite\b", "Snapdragon X Elite"),
            (@"\bSnapdragon\s+X\s+Plus\b", "Snapdragon X Plus"),
            (@"\bSnapdragon\b", "Qualcomm Snapdragon"),
        })
        {
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase)) return label;
        }

        return null;
    }

    [GeneratedRegex(@"\((?:R|TM|C)\)|®|™", RegexOptions.IgnoreCase)]
    private static partial Regex TrademarkMarks();

    [GeneratedRegex(@"\s*@\s*\d+(?:\.\d+)?\s*[GM]Hz.*$", RegexOptions.IgnoreCase)]
    private static partial Regex ClockSuffix();

    [GeneratedRegex(@"\s+(?:with|w/)\s+Radeon(?:\s+\w+)*\s+Graphics\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonSuffix();

    [GeneratedRegex(@"\s+CPU\b", RegexOptions.IgnoreCase)]
    private static partial Regex CpuWord();

    [GeneratedRegex(@"\s+Processor\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProcessorWord();

    [GeneratedRegex(@"^Genuine\s+Intel\s+", RegexOptions.IgnoreCase)]
    private static partial Regex GenericIntelPrefix();

    /// <summary>"AMD Ryzen 5 5600 6-Core Processor" carries the core count in the name; it is reported separately.</summary>
    [GeneratedRegex(@"\s+\d{1,2}-Core\b", RegexOptions.IgnoreCase)]
    private static partial Regex CoreCountSuffix();

    [GeneratedRegex(@"^(?<n>\d{1,2})(?<suffix>st|nd|rd|th)\s+Gen(?:eration)?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingGeneration();

    [GeneratedRegex(@"\bCore\s+Ultra\s+(?<tier>[5-9])\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoreUltra();

    [GeneratedRegex(@"\bCore\s+i(?<tier>[3579])\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoreI();

    [GeneratedRegex(@"\bCore\s+(?<tier>[3579])\s+\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoreBranded();

    [GeneratedRegex(@"\bRyzen\s+(?<tier>AI\s+\d|[3579])\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmdRyzen();
}
