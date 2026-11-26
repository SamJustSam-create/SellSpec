namespace SellSpec.Core.Listing;

public enum Marketplace
{
    General,
    FacebookMarketplace,
    Ebay,
    Gumtree,
}

/// <summary>Per-platform formatting rules. Title limits are configurable because platforms change them.</summary>
public sealed record MarketplaceTemplate
{
    public Marketplace Marketplace { get; init; }
    public string Name { get; init; } = "General";

    /// <summary>Maximum title length in characters, or null for no limit.</summary>
    public int? TitleMaxLength { get; init; }

    /// <summary>Character used for bullet points. Facebook strips most formatting, so a hyphen reads best there.</summary>
    public string Bullet { get; init; } = "-";

    /// <summary>Whether to spell out the asking price in the body (eBay has a dedicated price field).</summary>
    public bool IncludePriceInBody { get; init; } = true;

    /// <summary>Whether the body opens with a short conversational sentence.</summary>
    public bool ConversationalOpening { get; init; } = true;

    /// <summary>Whether to include the "Specifications were read from the device" line.</summary>
    public bool IncludeProvenanceLine { get; init; } = true;

    /// <summary>Whether section headings end with a colon ("Specifications:").</summary>
    public bool HeadingsWithColon { get; init; } = true;

    /// <summary>Whether to include core/thread counts in the processor line.</summary>
    public bool DetailedProcessor { get; init; }

    public static MarketplaceTemplate For(Marketplace marketplace, ListingOptions options) => marketplace switch
    {
        Marketplace.FacebookMarketplace => new MarketplaceTemplate
        {
            Marketplace = marketplace,
            Name = "Facebook Marketplace",
            TitleMaxLength = options.FacebookTitleLimit,
            Bullet = "-",
            IncludePriceInBody = false,
            ConversationalOpening = true,
            DetailedProcessor = false,
        },
        Marketplace.Ebay => new MarketplaceTemplate
        {
            Marketplace = marketplace,
            Name = "eBay",
            TitleMaxLength = options.EbayTitleLimit,
            Bullet = "•",
            IncludePriceInBody = false,
            ConversationalOpening = false,
            DetailedProcessor = true,
        },
        Marketplace.Gumtree => new MarketplaceTemplate
        {
            Marketplace = marketplace,
            Name = "Gumtree",
            TitleMaxLength = options.GumtreeTitleLimit,
            Bullet = "-",
            IncludePriceInBody = true,
            ConversationalOpening = true,
            DetailedProcessor = false,
        },
        _ => new MarketplaceTemplate
        {
            Marketplace = Marketplace.General,
            Name = "General",
            TitleMaxLength = options.GeneralTitleLimit,
            Bullet = "•",
            IncludePriceInBody = true,
            ConversationalOpening = true,
            DetailedProcessor = true,
        },
    };

    public static string DisplayName(Marketplace marketplace) => marketplace switch
    {
        Marketplace.FacebookMarketplace => "Facebook Marketplace",
        Marketplace.Ebay => "eBay",
        Marketplace.Gumtree => "Gumtree",
        _ => "General",
    };
}

public sealed record ListingOptions
{
    /// <summary>eBay allows 80 characters in a listing title.</summary>
    public int EbayTitleLimit { get; init; } = 80;

    /// <summary>Facebook Marketplace titles are capped at 99 characters.</summary>
    public int FacebookTitleLimit { get; init; } = 99;

    /// <summary>Gumtree (AU/UK) titles are capped at 64 characters.</summary>
    public int GumtreeTitleLimit { get; init; } = 64;

    public int? GeneralTitleLimit { get; init; } = 120;

    public bool IncludeBatteryHealth { get; init; } = true;
    public bool IncludeSellSpecFooter { get; init; } = true;
    public bool IncludeWindowsActivation { get; init; } = true;
    public string CurrencySymbol { get; init; } = "$";

    public static ListingOptions Default { get; } = new();
}
