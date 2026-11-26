using System.Text.Json;
using System.Text.Json.Serialization;

namespace SellSpec.Core.Reports;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeviceReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
}

/// <summary>Machine-readable export for future integrations. Mirrors the public report exactly; no private identifiers.</summary>
public static class JsonReportWriter
{
    public static string Write(DeviceReport report) => JsonSerializer.Serialize(report, ReportJsonContext.Default.DeviceReport);

    public static DeviceReport? Read(string json) => JsonSerializer.Deserialize(json, ReportJsonContext.Default.DeviceReport);
}
