using System.Text.RegularExpressions;

namespace SellSpec.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging abstraction. Implementations must pass messages through <see cref="LogSanitiser"/>.</summary>
public interface IScanLogger
{
    void Log(LogLevel level, string component, string message, Exception? exception = null);
}

public sealed class NullScanLogger : IScanLogger
{
    public static NullScanLogger Instance { get; } = new();
    public void Log(LogLevel level, string component, string message, Exception? exception = null) { }
}

/// <summary>Collects log lines in memory (used by tests and the in-app diagnostics view).</summary>
public sealed class MemoryScanLogger : IScanLogger
{
    private readonly List<string> _lines = new();
    private readonly object _gate = new();

    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return _lines.ToArray(); }
    }

    public void Log(LogLevel level, string component, string message, Exception? exception = null)
    {
        var line = LogSanitiser.FormatLine(level, component, message, exception);
        lock (_gate) _lines.Add(line);
    }
}

/// <summary>
/// Removes values that must never be written to disk: serial numbers, UUIDs, MAC addresses, IP addresses,
/// user profile paths and anything registered as private for the current scan.
/// </summary>
public static partial class LogSanitiser
{
    private static readonly object Gate = new();
    private static readonly List<string> PrivateValues = new();

    public static void RegisterPrivateValues(IEnumerable<string> values)
    {
        lock (Gate)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v) && v.Length >= 4 && !PrivateValues.Contains(v, StringComparer.Ordinal))
                {
                    PrivateValues.Add(v);
                }
            }
        }
    }

    public static void ClearPrivateValues()
    {
        lock (Gate) PrivateValues.Clear();
    }

    public static string Sanitise(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;
        lock (Gate)
        {
            foreach (var v in PrivateValues)
            {
                result = result.Replace(v, "[redacted]", StringComparison.OrdinalIgnoreCase);
            }
        }

        result = MacAddress().Replace(result, "[mac]");
        result = Ipv4Address().Replace(result, "[ip]");
        result = Privacy.Redactor.Ipv6Pattern.Replace(result, "[ip]");
        result = Guid().Replace(result, "[guid]");
        result = UserProfilePath().Replace(result, "$1[user]");
        return result;
    }

    public static string FormatLine(LogLevel level, string component, string message, Exception? exception)
    {
        var text = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {component}: {Sanitise(message)}";
        if (exception is not null)
        {
            // Exception messages can echo query text or paths; sanitise them too. Stack traces are omitted
            // because they can contain user-profile paths and add nothing for a packaged application.
            text += $" | {exception.GetType().Name}: {Sanitise(exception.Message)}";
        }
        return text;
    }

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex MacAddress();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex Ipv4Address();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b")]
    private static partial Regex Guid();

    [GeneratedRegex(@"([A-Za-z]:\\Users\\)[^\\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfilePath();
}
