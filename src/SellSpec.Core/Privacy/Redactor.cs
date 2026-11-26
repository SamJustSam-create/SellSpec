using System.Text.RegularExpressions;
using SellSpec.Core.Models;

namespace SellSpec.Core.Privacy;

public sealed class RedactionException : Exception
{
    public IReadOnlyList<string> Findings { get; }

    public RedactionException(IReadOnlyList<string> findings)
        : base("The export contains information that must not leave the device: " + string.Join(", ", findings))
    {
        Findings = findings;
    }
}

/// <summary>
/// Final safety net before anything is written to the clipboard or disk. Reports and listings are built from
/// public models only, so a hit here indicates a bug; the export is refused rather than risk a leak.
/// </summary>
public static partial class Redactor
{
    public static IReadOnlyList<string> FindLeaks(string output, PrivateIdentifiers privateIdentifiers)
    {
        var findings = new List<string>();
        if (string.IsNullOrEmpty(output)) return findings;

        foreach (var value in privateIdentifiers.AllValues())
        {
            // Very short identifiers (e.g. "0") would false-positive on ordinary text.
            if (value.Length < 5) continue;
            if (output.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("private identifier");
                break;
            }
        }

        if (MacAddress().IsMatch(output)) findings.Add("MAC address");
        if (Ipv4Address().IsMatch(output)) findings.Add("IP address");
        if (ProductKey().IsMatch(output)) findings.Add("product key");
        if (WindowsUserPath().IsMatch(output)) findings.Add("user profile path");

        return findings;
    }

    public static void EnsureClean(string output, PrivateIdentifiers privateIdentifiers)
    {
        var findings = FindLeaks(output, privateIdentifiers);
        if (findings.Count > 0)
        {
            throw new RedactionException(findings);
        }
    }

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex MacAddress();

    // Excludes version-like strings such as 10.0.26200 by requiring four octets of at most 3 digits with values <= 255,
    // and rejecting anything followed by another dot-number group.
    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\d.])")]
    private static partial Regex Ipv4Address();

    [GeneratedRegex(@"\b[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b")]
    private static partial Regex ProductKey();

    // "C:\Users\[user]\..." is what LogSanitiser leaves behind and is already redacted, so it is excluded here.
    [GeneratedRegex(@"[A-Za-z]:\\Users\\(?!\[user\])[^\\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPath();
}
