using System.Text;

namespace SellSpec.Core.Reports;

public static class TextReportWriter
{
    public static string Write(DeviceReport report)
    {
        var sb = new StringBuilder();
        var rule = new string('=', 72);
        var thin = new string('-', 72);

        sb.AppendLine(rule);
        sb.AppendLine("SELLSPEC DEVICE REPORT");
        sb.AppendLine(rule);
        sb.AppendLine(report.DeviceName);
        if (!string.IsNullOrWhiteSpace(report.DeviceSecondaryName)) sb.AppendLine(report.DeviceSecondaryName);
        if (!string.IsNullOrWhiteSpace(report.Headline)) sb.AppendLine(report.Headline);
        sb.AppendLine($"{report.DeviceType} · {report.OperatingSystem}");
        sb.AppendLine($"Scanned {report.ScannedAt:d MMMM yyyy, h:mm tt} with SellSpec {report.ApplicationVersion}");
        sb.AppendLine();

        foreach (var notice in report.Notices)
        {
            sb.AppendLine("NOTICE: " + notice);
            sb.AppendLine();
        }

        foreach (var section in report.Specifications)
        {
            sb.AppendLine(section.Title.ToUpperInvariant());
            sb.AppendLine(thin);
            foreach (var f in section.Fields)
            {
                sb.AppendLine(FormatField(f));
            }
            sb.AppendLine();
        }

        sb.AppendLine("DEVICE HEALTH");
        sb.AppendLine(thin);
        foreach (var row in report.Health)
        {
            sb.AppendLine($"{row.Title,-34} {row.StatusLabel,-14} {row.Value}");
            if (!string.IsNullOrWhiteSpace(row.Detail)) sb.AppendLine($"{string.Empty,-34} {row.Detail}");
        }
        sb.AppendLine();

        sb.AppendLine("HARDWARE TESTS");
        sb.AppendLine(thin);
        foreach (var row in report.HardwareTests)
        {
            sb.AppendLine($"{row.Title,-34} {row.StatusLabel,-14} {row.Value}");
            if (!string.IsNullOrWhiteSpace(row.Detail)) sb.AppendLine($"{string.Empty,-34} {row.Detail}");
        }
        sb.AppendLine();

        sb.AppendLine("SELLER-REPORTED CONDITION");
        sb.AppendLine(thin);
        foreach (var f in report.SellerCondition)
        {
            sb.AppendLine(FormatField(f));
        }
        sb.AppendLine();

        if (report.KnownIssues.Count > 0)
        {
            sb.AppendLine("KNOWN ISSUES");
            sb.AppendLine(thin);
            foreach (var issue in report.KnownIssues) sb.AppendLine("- " + issue);
            sb.AppendLine();
        }

        sb.AppendLine(rule);
        sb.AppendLine(report.Disclaimer);
        sb.AppendLine($"Generated {report.GeneratedAt:d MMMM yyyy, h:mm tt}. SellSpec – Scan it. List it. Sell it.");
        return sb.ToString();
    }

    private static string FormatField(ReportField f)
    {
        var value = f.Value;
        if (f.ProvenanceTag is { } tag && tag is not ("unavailable" or "not applicable")) value += $" ({tag})";
        return $"{f.Label,-34} {value}";
    }
}
