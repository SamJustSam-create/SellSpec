using SellSpec.Core.Models;

namespace SellSpec.Core.Detection;

public enum ScanIssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A non-fatal problem encountered during a scan. Messages are written for end users; details are sanitised for logs.</summary>
public sealed record ScanIssue(string Component, string Message, ScanIssueSeverity Severity, string? Detail = null);

public enum ScanStage
{
    Started,
    Completed,
    Failed,
    TimedOut,
    Cancelled,
}

public sealed record ScanProgress(string Component, ScanStage Stage, int Completed, int Total)
{
    public double Fraction => Total == 0 ? 0 : (double)Completed / Total;
}

public sealed record ScanResult
{
    public required DeviceSnapshot Snapshot { get; init; }
    public IReadOnlyList<ScanIssue> Issues { get; init; } = Array.Empty<ScanIssue>();
    public TimeSpan Duration { get; init; }
    public DateTimeOffset ScannedAt { get; init; }
    public bool WasCancelled { get; init; }

    /// <summary>True when the process ran elevated. Some reliability counters are only readable with elevation.</summary>
    public bool IsElevated { get; init; }

    public bool HasErrors => Issues.Any(i => i.Severity == ScanIssueSeverity.Error);
}
