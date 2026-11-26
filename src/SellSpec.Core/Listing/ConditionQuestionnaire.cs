using System.Text.Json.Serialization;

namespace SellSpec.Core.Listing;

public enum ConditionGrade
{
    Unspecified,
    LikeNew,
    Excellent,
    Good,
    Fair,
    Poor,
    ForPartsOrNotWorking,
}

public enum ComponentCondition
{
    Unspecified,
    NoIssues,
    MinorWear,
    Damaged,
    NotApplicable,
}

public enum Answer
{
    Unspecified,
    Yes,
    No,
}

/// <summary>
/// Seller-supplied information that software cannot detect. Every field may be left unspecified; unspecified
/// answers never turn into positive claims in listings or reports.
/// </summary>
public sealed record ConditionQuestionnaire
{
    public ConditionGrade OverallCondition { get; init; } = ConditionGrade.Unspecified;
    public Answer HasCosmeticDamage { get; init; } = Answer.Unspecified;
    public string? CosmeticDamageNotes { get; init; }

    public ComponentCondition Screen { get; init; } = ComponentCondition.Unspecified;
    public ComponentCondition Keyboard { get; init; } = ComponentCondition.Unspecified;
    public ComponentCondition Trackpad { get; init; } = ComponentCondition.Unspecified;
    public ComponentCondition Hinge { get; init; } = ComponentCondition.Unspecified;

    public Answer HasBatteryConcern { get; init; } = Answer.Unspecified;
    public string? BatteryNotes { get; init; }

    public Answer ChargerIncluded { get; init; } = Answer.Unspecified;
    public Answer OriginalPackagingIncluded { get; init; } = Answer.Unspecified;

    public Answer HasKnownDefects { get; init; } = Answer.Unspecified;
    public string? KnownDefects { get; init; }

    public string? RepairOrUpgradeHistory { get; init; }
    public string? AdditionalAccessories { get; init; }
    public string? SellerNotes { get; init; }

    public decimal? AskingPrice { get; init; }

    public static ConditionQuestionnaire Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        OverallCondition == ConditionGrade.Unspecified && HasCosmeticDamage == Answer.Unspecified &&
        Screen == ComponentCondition.Unspecified && Keyboard == ComponentCondition.Unspecified &&
        Trackpad == ComponentCondition.Unspecified && Hinge == ComponentCondition.Unspecified &&
        HasBatteryConcern == Answer.Unspecified && ChargerIncluded == Answer.Unspecified &&
        OriginalPackagingIncluded == Answer.Unspecified && HasKnownDefects == Answer.Unspecified &&
        string.IsNullOrWhiteSpace(RepairOrUpgradeHistory) && string.IsNullOrWhiteSpace(AdditionalAccessories) &&
        string.IsNullOrWhiteSpace(SellerNotes) && AskingPrice is null;

    public static string GradeLabel(ConditionGrade grade) => grade switch
    {
        ConditionGrade.LikeNew => "Like New",
        ConditionGrade.Excellent => "Excellent",
        ConditionGrade.Good => "Good",
        ConditionGrade.Fair => "Fair",
        ConditionGrade.Poor => "Poor",
        ConditionGrade.ForPartsOrNotWorking => "For Parts or Not Working",
        _ => "Not specified",
    };

    public static string ComponentLabel(ComponentCondition condition) => condition switch
    {
        ComponentCondition.NoIssues => "No issues",
        ComponentCondition.MinorWear => "Minor wear",
        ComponentCondition.Damaged => "Damaged",
        ComponentCondition.NotApplicable => "Not applicable",
        _ => "Not specified",
    };

    /// <summary>Splits free text such as "mouse, laptop bag" or line-separated lists into tidy items.</summary>
    public static IReadOnlyList<string> SplitList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..])
            .ToList();
    }
}
