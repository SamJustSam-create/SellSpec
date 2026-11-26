using System.Globalization;
using SellSpec.Core.Models;

namespace SellSpec.Core.Health;

public static class StorageHealthAssessor
{
    /// <summary>Above this percentage of rated endurance used, an SSD is flagged for attention.</summary>
    public const int WearAttentionPercent = 80;
    public const int WearIssuePercent = 95;

    /// <summary>Roughly four years of continuous operation.</summary>
    public const int PowerOnHoursAttention = 35_000;

    public sealed record Assessment(HealthStatusKind Status, string Summary, string? Detail);

    public static Assessment Assess(StorageDevice drive)
    {
        if (drive.PredictedFailure is { HasValue: true, Value: true })
        {
            return new Assessment(HealthStatusKind.Issue, "Failure predicted", "The drive's self-monitoring (SMART) reports an imminent failure. Back up data and disclose this to buyers.");
        }

        if (drive.ReportedHealth is { HasValue: true, Value: HealthStatusKind.Issue })
        {
            return new Assessment(HealthStatusKind.Issue, "Unhealthy (reported)", "Windows reports this drive as unhealthy.");
        }

        if (drive.WearPercentUsed is { HasValue: true } wear && wear.Value >= WearIssuePercent)
        {
            return new Assessment(HealthStatusKind.Issue, $"{wear.Value}% of rated endurance used", "The SSD has nearly exhausted its rated write endurance.");
        }

        var attentionReasons = new List<string>();
        if (drive.ReportedHealth is { HasValue: true, Value: HealthStatusKind.Attention })
        {
            attentionReasons.Add("Windows reports a warning for this drive.");
        }
        if (drive.WearPercentUsed is { HasValue: true } w2 && w2.Value >= WearAttentionPercent)
        {
            attentionReasons.Add($"{w2.Value}% of rated endurance used.");
        }
        if (drive.PowerOnHours is { HasValue: true } hours && hours.Value >= PowerOnHoursAttention)
        {
            attentionReasons.Add($"High power-on time ({FormatHours(hours.Value)}).");
        }

        if (attentionReasons.Count > 0)
        {
            return new Assessment(HealthStatusKind.Attention, "Attention recommended", string.Join(" ", attentionReasons));
        }

        if (drive.ReportedHealth is { HasValue: true, Value: HealthStatusKind.Good })
        {
            var detail = "Windows reports this drive as healthy.";
            if (drive.PredictedFailure is { HasValue: true, Value: false }) detail += " SMART does not predict failure.";
            if (drive.WearPercentUsed.HasValue) detail += $" {drive.WearPercentUsed.Value}% of rated endurance used.";
            return new Assessment(HealthStatusKind.Good, "Healthy (reported)", detail);
        }

        if (drive.PredictedFailure is { HasValue: true, Value: false })
        {
            return new Assessment(HealthStatusKind.Good, "No failure predicted", "SMART self-monitoring does not predict failure. Detailed health status was not available.");
        }

        return new Assessment(HealthStatusKind.Unknown, "Health not available",
            drive.ReportedHealth.Note ?? "This drive did not expose health information to Windows. This does not mean the drive is faulty, only that it could not be checked.");
    }

    public static string FormatHours(int hours)
    {
        var years = hours / (24d * 365.25);
        var text = hours.ToString("N0", CultureInfo.InvariantCulture) + " hours";
        if (years >= 0.5)
        {
            text += $" (about {years.ToString("0.#", CultureInfo.InvariantCulture)} years powered on)";
        }
        return text;
    }
}
