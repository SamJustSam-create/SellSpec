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
    /// <summary>Account names too generic to treat as personal; matching them would refuse ordinary text.</summary>
    private static readonly HashSet<string> GenericAccountNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "users", "admin", "administrator", "owner", "guest", "default", "home", "pc", "test", "windows", "system", "public", "me",
    };

    /// <summary>
    /// Finds private identifiers, network addresses, product keys, user-profile paths and any additional sensitive
    /// terms (for example the Windows account or machine name) in <paramref name="output"/>.
    /// </summary>
    public static IReadOnlyList<string> FindLeaks(string output, PrivateIdentifiers privateIdentifiers, IEnumerable<string>? additionalTerms = null)
    {
        var findings = new List<string>();
        if (string.IsNullOrEmpty(output)) return findings;

        foreach (var value in privateIdentifiers.AllValues())
        {
            if (value.Length >= 5)
            {
                if (output.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add("private identifier");
                    break;
                }
            }
            else if (value.Length >= 3 && Regex.IsMatch(output, $@"(?<![A-Za-z0-9]){Regex.Escape(value)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
            {
                // Short identifiers are only flagged as whole tokens, otherwise "123" would hit every spec sheet.
                findings.Add("private identifier");
                break;
            }
        }

        if (additionalTerms is not null)
        {
            foreach (var term in additionalTerms)
            {
                if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 3 || GenericAccountNames.Contains(term.Trim())) continue;
                if (Regex.IsMatch(output, $@"(?<![A-Za-z0-9]){Regex.Escape(term.Trim())}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
                {
                    findings.Add("account or computer name");
                    break;
                }
            }
        }

        if (MacAddress().IsMatch(output)) findings.Add("MAC address");
        if (Ipv4Address().IsMatch(output)) findings.Add("IP address");
        if (Ipv6Address().IsMatch(output)) findings.Add("IPv6 address");
        if (ProductKey().IsMatch(output)) findings.Add("product key");
        if (WindowsUserPath().IsMatch(output)) findings.Add("user profile path");

        return findings;
    }

    public static void EnsureClean(string output, PrivateIdentifiers privateIdentifiers, IEnumerable<string>? additionalTerms = null)
    {
        var findings = FindLeaks(output, privateIdentifiers, additionalTerms);
        if (findings.Count > 0)
        {
            throw new RedactionException(findings);
        }
    }

    /// <summary>Replaces whole-word occurrences of each term with "[redacted]" (used for machine-generated text such as logs).</summary>
    public static string ReplaceTerms(string text, IEnumerable<string>? terms)
    {
        if (string.IsNullOrEmpty(text) || terms is null) return text;
        var result = text;
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 3 || GenericAccountNames.Contains(term.Trim())) continue;
            result = Regex.Replace(result, $@"(?<![A-Za-z0-9]){Regex.Escape(term.Trim())}(?![A-Za-z0-9])", "[redacted]", RegexOptions.IgnoreCase);
        }
        return result;
    }

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex MacAddress();

    // Excludes version-like strings such as 10.0.26200 by requiring four octets of at most 3 digits with values <= 255,
    // and rejecting anything followed by another dot-number group.
    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\d.])")]
    private static partial Regex Ipv4Address();

    // IPv6: at least four colon-separated hex groups, or a "::" compressed form with at least one group.
    // Timestamps ("16:55:22") have only three groups and never match; MAC addresses are caught separately.
    [GeneratedRegex(@"(?<![:\w])(?:(?:[0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,6}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,5})?|::[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})(?![:\w])")]
    private static partial Regex Ipv6Address();

    [GeneratedRegex(@"\b[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b")]
    private static partial Regex ProductKey();

    // "C:\Users\[user]\..." is what LogSanitiser leaves behind and is already redacted, so it is excluded here.
    [GeneratedRegex(@"[A-Za-z]:\\Users\\(?!\[user\])[^\\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPath();

    /// <summary>Exposed for the log sanitiser so both components agree on what an IPv6 address looks like.</summary>
    internal static Regex Ipv6Pattern => Ipv6Address();
}
