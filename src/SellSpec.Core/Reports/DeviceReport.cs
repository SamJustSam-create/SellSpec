using SellSpec.Core.Models;

namespace SellSpec.Core.Reports;

public sealed record ReportField(string Label, string Value, Provenance Provenance, string? Note = null)
{
    /// <summary>Short bracketed tag shown next to values whose provenance a buyer should know about.</summary>
    public string? ProvenanceTag => Provenance switch
    {
        Provenance.Estimated => "estimated",
        Provenance.FirmwareReported => "firmware reported",
        Provenance.SellerProvided => "seller provided",
        Provenance.Unavailable => "unavailable",
        Provenance.NotApplicable => "not applicable",
        _ => null,
    };
}

public sealed record ReportSection(string Title, IReadOnlyList<ReportField> Fields);

public sealed record ReportStatusRow(string Title, HealthStatusKind Status, string StatusLabel, string Value, string? Detail, Provenance Provenance);

/// <summary>
/// A public, redacted view of a scan plus seller input. Everything here may be shown to a buyer; nothing in
/// <see cref="PrivateIdentifiers"/> is ever copied into it.
/// </summary>
public sealed record DeviceReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string ApplicationName { get; init; } = "SellSpec";
    public string ApplicationVersion { get; init; } = "0.0.0";
    public DateTimeOffset ScannedAt { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }

    public string DeviceName { get; init; } = string.Empty;
    public string? DeviceSecondaryName { get; init; }
    public string DeviceType { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;

    /// <summary>One-line summary used as the report subtitle: "Intel Core i5-1335U · 16 GB RAM · 512 GB NVMe SSD".</summary>
    public string Headline { get; init; } = string.Empty;

    public IReadOnlyList<ReportSection> Specifications { get; init; } = Array.Empty<ReportSection>();
    public IReadOnlyList<ReportStatusRow> Health { get; init; } = Array.Empty<ReportStatusRow>();
    public IReadOnlyList<ReportStatusRow> HardwareTests { get; init; } = Array.Empty<ReportStatusRow>();
    public IReadOnlyList<ReportField> SellerCondition { get; init; } = Array.Empty<ReportField>();
    public IReadOnlyList<string> KnownIssues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Notices { get; init; } = Array.Empty<string>();

    public string Disclaimer { get; init; } = DefaultDisclaimer;

    /// <summary>True only when the report was produced from the labelled demonstration device.</summary>
    public bool IsDemoData { get; init; }

    public const string DefaultDisclaimer =
        "Specifications were collected from the device by SellSpec at the time shown. Physical condition, included items and some test " +
        "results were supplied by the seller and have not been independently verified. Battery and storage health figures are values " +
        "reported by the device and are estimates, not guarantees. This report is not a certification and is not tamper-proof.";
}
