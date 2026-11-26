using System.Text;
using SellSpec.Core.Models;
using SellSpec.Core.Testing;

namespace SellSpec.Core.Listing;

public sealed record ListingRequest(
    DeviceSnapshot Snapshot,
    ConditionQuestionnaire Condition,
    IReadOnlyList<HardwareTestResult> Tests,
    Marketplace Marketplace,
    ListingOptions? Options = null);

public sealed record ListingSection(string Heading, IReadOnlyList<string> Lines);

public sealed record Listing
{
    public required Marketplace Marketplace { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<ListingSection> Sections { get; init; } = Array.Empty<ListingSection>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public int? TitleLimit { get; init; }
    public int TitleLength => Title.Length;

    public string CombinedText => Title + Environment.NewLine + Environment.NewLine + Description;
}

/// <summary>
/// Deterministic, offline listing generation. Everything in the output traces back to a detected value or a
/// seller answer; unavailable values are left out rather than guessed.
/// </summary>
public static class ListingGenerator
{
    public static Listing Generate(ListingRequest request)
    {
        var options = request.Options ?? ListingOptions.Default;
        var template = MarketplaceTemplate.For(request.Marketplace, options);
        var s = request.Snapshot;
        var q = request.Condition;
        var warnings = new List<string>();

        var title = TitleBuilder.Build(s, template.TitleMaxLength);
        if (title.WasTruncated)
        {
            warnings.Add($"The title was cut to fit {template.Name}'s {template.TitleMaxLength}-character limit. Check that it still reads well.");
        }
        else if (title.WasShortened)
        {
            warnings.Add($"A shorter title variant was used to fit {template.Name}'s {template.TitleMaxLength}-character limit.");
        }

        if (s.Identity.IsVirtualMachine)
        {
            warnings.Add("This device appears to be a virtual machine. The listing describes virtual hardware.");
        }
        if (!s.ListingStorage.Any())
        {
            warnings.Add("No internal storage was detected, so the listing has no storage line.");
        }
        if (q.IsEmpty)
        {
            warnings.Add("The condition questionnaire has not been completed, so the listing makes no condition claims.");
        }

        var sections = new List<ListingSection>();
        var body = new StringBuilder();
        var noun = SpecSummary.DeviceTypeNoun(s);
        var nounTitle = char.ToUpperInvariant(noun[0]) + noun[1..];

        // Opening
        var opening = BuildOpening(s, q, template, noun);
        body.AppendLine(opening);
        body.AppendLine();

        // Specifications
        var specs = BuildSpecLines(s, template, options);
        sections.Add(new ListingSection("Specifications", specs));
        AppendSection(body, template, "Specifications", specs);

        // Condition
        var condition = BuildConditionLines(s, q);
        if (condition.Count > 0)
        {
            sections.Add(new ListingSection("Condition", condition));
            AppendSection(body, template, "Condition", condition, asBullets: false);
        }
        else
        {
            sections.Add(new ListingSection("Condition", new[] { "Not stated by the seller." }));
        }

        // Tests
        var tested = BuildTestLines(request.Tests);
        if (tested.Count > 0)
        {
            sections.Add(new ListingSection("Seller-tested", tested));
            AppendSection(body, template, "Tested by the seller", tested);
        }

        // Included
        var included = BuildIncludedLines(q, nounTitle);
        if (included.Count > 0)
        {
            sections.Add(new ListingSection("Included", included));
            AppendSection(body, template, "Included", included);
        }

        // Known issues
        var issues = BuildKnownIssueLines(q, request.Tests, s);
        if (issues.Count > 0)
        {
            sections.Add(new ListingSection("Known issues", issues));
            AppendSection(body, template, "Known issues", issues);
        }

        // Notes
        var notes = BuildNoteLines(q, template, options);
        if (notes.Count > 0)
        {
            sections.Add(new ListingSection("Notes", notes));
            AppendSection(body, template, "Notes", notes, asBullets: false);
        }

        if (template.IncludeProvenanceLine)
        {
            body.AppendLine("Specifications were read directly from the device" +
                            (q.IsEmpty ? "." : "; condition details were provided by the seller."));
        }
        if (options.IncludeSellSpecFooter)
        {
            body.AppendLine("Listing prepared with SellSpec.");
        }

        return new Listing
        {
            Marketplace = request.Marketplace,
            Title = title.Title,
            Description = body.ToString().TrimEnd() + Environment.NewLine,
            Sections = sections,
            Warnings = warnings,
            TitleLimit = template.TitleMaxLength,
        };
    }

    private static string BuildOpening(DeviceSnapshot s, ConditionQuestionnaire q, MarketplaceTemplate template, string noun)
    {
        var name = s.Identity.IsCustomBuilt ? $"custom-built {noun}" : $"{s.Identity.DisplayName} {noun}";
        if (s.Identity.IsCustomBuilt && s.Identity.SecondaryName is { } board)
        {
            name += $" built on a {board.Replace(" motherboard", string.Empty)} motherboard";
        }

        var grade = q.OverallCondition;
        var conditionPhrase = grade switch
        {
            ConditionGrade.LikeNew => " in like-new condition",
            ConditionGrade.Excellent => " in excellent condition",
            ConditionGrade.Good => " in good condition",
            ConditionGrade.Fair => " in fair condition",
            ConditionGrade.Poor => " in poor cosmetic condition",
            ConditionGrade.ForPartsOrNotWorking => ", sold for parts or repair",
            _ => string.Empty,
        };

        return template.ConversationalOpening
            ? $"Selling a {name}{conditionPhrase}."
            : $"{Capitalise(name)}{conditionPhrase}.";
    }

    private static List<string> BuildSpecLines(DeviceSnapshot s, MarketplaceTemplate template, ListingOptions options)
    {
        var lines = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value}");
        }

        if (!s.Identity.IsCustomBuilt && s.Identity.ModelNumber.HasValue && s.Identity.ProductName.HasValue &&
            !string.Equals(s.Identity.ModelNumber.Value, s.Identity.ProductName.Value, StringComparison.OrdinalIgnoreCase))
        {
            Add("Model", s.Identity.ModelNumber.Value);
        }
        if (s.Identity.IsCustomBuilt && s.Identity.BoardModel.HasValue)
        {
            var vendor = s.Identity.BoardManufacturer.HasValue ? s.Identity.BoardManufacturer.Value + " " : string.Empty;
            Add("Motherboard", vendor + s.Identity.BoardModel.Value);
        }

        Add("Processor", SpecSummary.Processor(s, template.DetailedProcessor));
        Add("Memory", SpecSummary.Memory(s));
        Add("Storage", SpecSummary.Storage(s));
        Add("Graphics", SpecSummary.Graphics(s));
        Add("Display", SpecSummary.Display(s));
        Add("Operating system", SpecSummary.OperatingSystem(s, options.IncludeWindowsActivation));
        Add("Connectivity", SpecSummary.Connectivity(s));
        if (options.IncludeBatteryHealth)
        {
            Add("Reported battery health", SpecSummary.BatteryHealth(s));
        }
        return lines;
    }

    private static List<string> BuildConditionLines(DeviceSnapshot s, ConditionQuestionnaire q)
    {
        var lines = new List<string>();
        var grade = q.OverallCondition;

        var sentence = grade switch
        {
            ConditionGrade.LikeNew => "Like-new condition",
            ConditionGrade.Excellent => "Excellent condition",
            ConditionGrade.Good => "Good overall condition",
            ConditionGrade.Fair => "Fair condition",
            ConditionGrade.Poor => "Poor cosmetic condition",
            ConditionGrade.ForPartsOrNotWorking => "Sold for parts or not working",
            _ => null,
        };

        var cosmetic = q.HasCosmeticDamage switch
        {
            Answer.Yes => string.IsNullOrWhiteSpace(q.CosmeticDamageNotes) ? "with some cosmetic wear" : $"with cosmetic wear: {Trim(q.CosmeticDamageNotes)}",
            Answer.No => "with no noticeable cosmetic damage",
            _ => null,
        };

        if (sentence is not null && cosmetic is not null)
        {
            lines.Add($"{sentence} {cosmetic}.");
        }
        else if (sentence is not null)
        {
            lines.Add($"{sentence}.");
        }
        else if (cosmetic is not null)
        {
            lines.Add(Capitalise(cosmetic) + ".");
        }

        var components = new List<string>();
        void Component(string label, ComponentCondition c)
        {
            switch (c)
            {
                case ComponentCondition.NoIssues: components.Add($"{label}: no issues"); break;
                case ComponentCondition.MinorWear: components.Add($"{label}: minor wear"); break;
                case ComponentCondition.Damaged: components.Add($"{label}: damaged (see known issues)"); break;
            }
        }
        Component("Screen", q.Screen);
        Component("Keyboard", q.Keyboard);
        Component("Trackpad", q.Trackpad);
        Component("Hinge", q.Hinge);
        if (components.Count > 0)
        {
            lines.Add(string.Join(". ", components) + ".");
        }

        if (q.HasBatteryConcern == Answer.No && s.HasBattery)
        {
            lines.Add("No battery concerns reported by the seller.");
        }

        return lines;
    }

    private static List<string> BuildTestLines(IReadOnlyList<HardwareTestResult> tests)
    {
        var passed = tests.Where(t => t.IsPass).Select(t => HardwareTestCatalog.Title(t.Kind).ToLowerInvariant()).ToList();
        if (passed.Count == 0) return new List<string>();
        var withNotes = tests.Where(t => t.Outcome == TestOutcome.PassedWithNotes && !string.IsNullOrWhiteSpace(t.Notes)).ToList();
        var lines = new List<string> { $"Seller confirmed working: {string.Join(", ", passed)}." };
        foreach (var t in withNotes)
        {
            lines.Add($"{HardwareTestCatalog.Title(t.Kind)}: {Trim(t.Notes)}");
        }
        return lines;
    }

    private static List<string> BuildIncludedLines(ConditionQuestionnaire q, string nounTitle)
    {
        var lines = new List<string>();
        var anyAnswer = q.ChargerIncluded != Answer.Unspecified || q.OriginalPackagingIncluded != Answer.Unspecified || !string.IsNullOrWhiteSpace(q.AdditionalAccessories);
        if (!anyAnswer) return lines;

        lines.Add(nounTitle);
        if (q.ChargerIncluded == Answer.Yes) lines.Add("Charger");
        if (q.OriginalPackagingIncluded == Answer.Yes) lines.Add("Original box");
        lines.AddRange(ConditionQuestionnaire.SplitList(q.AdditionalAccessories));
        if (q.ChargerIncluded == Answer.No) lines.Add("Charger not included");
        return lines;
    }

    private static List<string> BuildKnownIssueLines(ConditionQuestionnaire q, IReadOnlyList<HardwareTestResult> tests, DeviceSnapshot s)
    {
        var lines = new List<string>();
        if (q.HasKnownDefects == Answer.Yes)
        {
            lines.Add(string.IsNullOrWhiteSpace(q.KnownDefects) ? "Seller reports known issues; please ask for details." : Trim(q.KnownDefects)!);
        }
        if (q.HasBatteryConcern == Answer.Yes)
        {
            lines.Add(string.IsNullOrWhiteSpace(q.BatteryNotes) ? "Seller reports a battery concern." : $"Battery: {Trim(q.BatteryNotes)}");
        }
        foreach (var t in tests.Where(t => t.IsFail))
        {
            var text = $"{HardwareTestCatalog.Title(t.Kind)} test failed (seller-reported)";
            if (!string.IsNullOrWhiteSpace(t.Notes)) text += $": {Trim(t.Notes)}";
            lines.Add(text);
        }
        foreach (var c in new[] { ("Screen", q.Screen), ("Keyboard", q.Keyboard), ("Trackpad", q.Trackpad), ("Hinge", q.Hinge) }.Where(c => c.Item2 == ComponentCondition.Damaged))
        {
            if (!lines.Any(l => l.Contains(c.Item1, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add($"{c.Item1} damaged (see seller notes)");
            }
        }
        if (s.Battery.HealthStatus == HealthStatusKind.Issue && s.Battery.HealthPercent.HasValue)
        {
            lines.Add($"Battery capacity is reduced to about {s.Battery.HealthPercent.Value}% of original (software-reported).");
        }

        if (lines.Count == 0 && q.HasKnownDefects == Answer.No && !tests.Any(t => t.IsFail))
        {
            lines.Add("No known faults, as described by the seller.");
        }
        return lines;
    }

    private static List<string> BuildNoteLines(ConditionQuestionnaire q, MarketplaceTemplate template, ListingOptions options)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.RepairOrUpgradeHistory)) lines.Add($"Repair/upgrade history: {Trim(q.RepairOrUpgradeHistory)}");
        if (!string.IsNullOrWhiteSpace(q.SellerNotes)) lines.Add(Trim(q.SellerNotes)!);
        if (template.IncludePriceInBody && q.AskingPrice is { } price && price > 0)
        {
            lines.Add($"Asking price: {SpecSummary.FormatPrice(price, options.CurrencySymbol)}");
        }
        return lines;
    }

    private static void AppendSection(StringBuilder body, MarketplaceTemplate template, string heading, IReadOnlyList<string> lines, bool asBullets = true)
    {
        if (lines.Count == 0) return;
        body.AppendLine(template.HeadingsWithColon ? heading + ":" : heading);
        foreach (var line in lines)
        {
            body.AppendLine(asBullets ? $"{template.Bullet} {line}" : line);
        }
        body.AppendLine();
    }

    private static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Normalisation.GenericValueDetector.CollapseWhitespace(text.Trim());
        return t.TrimEnd('.');
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
