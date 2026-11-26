namespace SellSpec.Core.Normalisation;

/// <summary>
/// Recognises placeholder strings that OEM and motherboard firmware emit instead of real values.
/// </summary>
public static class GenericValueDetector
{
    private static readonly string[] ExactPlaceholders =
    {
        "to be filled by o.e.m.",
        "to be filled by oem",
        "system product name",
        "system manufacturer",
        "system version",
        "system serial number",
        "default string",
        "default",
        "o.e.m.",
        "oem",
        "not applicable",
        "not available",
        "n/a",
        "na",
        "none",
        "null",
        "unknown",
        "undefined",
        "invalid",
        "not specified",
        "no enclosure",
        "chassis manufacture",
        "chassis version",
        "chassis serial number",
        "base board product name",
        "base board manufacturer",
        "base board version",
        "type1productconfigid",
        "sku",
        "sku number",
        "xxxxxx",
        "0",
        "00",
        "-",
        "--",
        "...",
        "1234567890",
        "123456789",
        "0123456789",
        "empty",
        "standard",
        "all series",
        "product name",
        "manufacturer",
        "version",
        "x.x",
        "1.0",
        "rev x.0x",
    };

    /// <summary>Returns the trimmed value, or null when it is empty or a known placeholder.</summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = CollapseWhitespace(raw.Trim().Trim('\0'));
        if (trimmed.Length == 0) return null;
        if (IsPlaceholder(trimmed)) return null;
        return trimmed;
    }

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = CollapseWhitespace(value.Trim()).ToLowerInvariant();
        if (ExactPlaceholders.Contains(v)) return true;
        if (v.StartsWith("to be filled", StringComparison.Ordinal)) return true;
        if (v.All(c => c == 'x' || c == 'X' || c == '0' || c == ' ' || c == '-')) return true;
        return false;
    }

    public static string CollapseWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var chars = new char[value.Length];
        var n = 0;
        var lastWasSpace = false;
        foreach (var c in value)
        {
            var isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (lastWasSpace) continue;
                chars[n++] = ' ';
            }
            else
            {
                chars[n++] = c;
            }
            lastWasSpace = isSpace;
        }
        return new string(chars, 0, n).Trim();
    }
}
