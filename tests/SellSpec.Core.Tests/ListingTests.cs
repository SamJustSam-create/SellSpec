using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Testing;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class TitleBuilderTests
{
    [Fact]
    public void Standard_laptop_title_matches_expected_shape()
    {
        var result = TitleBuilder.Build(Scenarios.StandardLaptop(), null);
        Assert.Equal("Lenovo IdeaPad Slim 5 14IAH8 – Intel Core i5-1335U (13th Gen), 16GB RAM, 512GB SSD", result.Title);
        Assert.False(result.WasShortened);
    }

    [Fact]
    public void Ebay_limit_is_respected_by_shortening_not_truncating()
    {
        var result = TitleBuilder.Build(Scenarios.StandardLaptop(), 80);
        Assert.True(result.Title.Length <= 80, result.Title);
        Assert.True(result.WasShortened);
        Assert.False(result.WasTruncated);
        Assert.Equal("Lenovo IdeaPad Slim 5 14IAH8 – Intel Core i5, 16GB RAM, 512GB SSD", result.Title);
    }

    [Fact]
    public void Gumtree_limit_falls_back_to_short_device_name()
    {
        var result = TitleBuilder.Build(Scenarios.StandardLaptop(), 64);
        Assert.True(result.Title.Length <= 64, result.Title);
        Assert.Contains("Lenovo IdeaPad Slim 5", result.Title);
    }

    [Fact]
    public void Gaming_laptop_title_includes_gpu()
    {
        var result = TitleBuilder.Build(Scenarios.GamingLaptop(), 99);
        Assert.Contains("RTX 3060", result.Title);
        Assert.True(result.Title.Length <= 99);
    }

    [Fact]
    public void Custom_desktop_title_leads_with_type()
    {
        var result = TitleBuilder.Build(Scenarios.CustomDesktop(), 80);
        Assert.StartsWith("Custom gaming PC", result.Title);
        Assert.Contains("RTX 3070", result.Title);
        Assert.DoesNotContain("Gigabyte", result.Title);
    }

    [Fact]
    public void Incomplete_manufacturer_falls_back_to_device_type()
    {
        var result = TitleBuilder.Build(Scenarios.IncompleteManufacturer(), 80);
        Assert.StartsWith("Windows laptop", result.Title);
        Assert.DoesNotContain("O.E.M", result.Title);
    }

    [Fact]
    public void Tiny_limit_truncates_as_a_last_resort()
    {
        var result = TitleBuilder.Build(Scenarios.StandardLaptop(), 4);
        Assert.True(result.Title.Length <= 4);
        Assert.True(result.WasTruncated);
        Assert.False(result.Title.EndsWith(' '));
    }

    [Fact]
    public void Two_drives_of_same_type_are_combined_in_titles()
    {
        var result = TitleBuilder.Build(Scenarios.GamingLaptop(), null);
        Assert.Contains("512GB + 1TB SSD", result.Title);
        var desktop = TitleBuilder.Build(Scenarios.CustomDesktop(), null);
        Assert.Contains("512GB SSD + 2TB HDD", desktop.Title);
    }
}

public sealed class ListingGeneratorTests
{
    private static ListingRequest Request(DeviceSnapshot s, Marketplace m, ConditionQuestionnaire? q = null, IReadOnlyList<HardwareTestResult>? tests = null) =>
        new(s, q ?? ConditionQuestionnaire.Empty, tests ?? Array.Empty<HardwareTestResult>(), m);

    private static readonly ConditionQuestionnaire GoodCondition = new()
    {
        OverallCondition = ConditionGrade.Good,
        HasCosmeticDamage = Answer.Yes,
        CosmeticDamageNotes = "light scuff on the lid",
        Screen = ComponentCondition.NoIssues,
        Keyboard = ComponentCondition.NoIssues,
        ChargerIncluded = Answer.Yes,
        OriginalPackagingIncluded = Answer.No,
        HasKnownDefects = Answer.Yes,
        KnownDefects = "Right USB-A port is loose",
        HasBatteryConcern = Answer.No,
        AdditionalAccessories = "laptop sleeve, USB-C hub",
        SellerNotes = "Pick-up preferred.",
        AskingPrice = 650,
    };

    [Fact]
    public void General_listing_contains_specs_condition_included_and_issues()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.General, GoodCondition));
        var d = listing.Description;

        Assert.Contains("Selling a Lenovo IdeaPad Slim 5 14IAH8 laptop in good condition.", d);
        Assert.Contains("Processor: Intel Core i5-1335U (13th Gen) (10 cores / 12 threads)", d);
        Assert.Contains("Memory: 16 GB DDR5 RAM", d);
        Assert.Contains("Storage: 512 GB NVMe SSD", d);
        Assert.Contains("Graphics: Intel Iris Xe Graphics (integrated)", d);
        Assert.Contains("Display: 14-inch Full HD (1920×1080)", d);
        Assert.Contains("Operating system: Windows 11 Home (activated)", d);
        Assert.Contains("Connectivity: Wi-Fi 6E, Bluetooth, webcam, fingerprint reader", d);
        Assert.Contains("Reported battery health: 87% of original capacity (software-reported estimate)", d);
        Assert.Contains("Good overall condition with cosmetic wear: light scuff on the lid.", d);
        Assert.Contains("Screen: no issues. Keyboard: no issues.", d);
        Assert.Contains("Known issues:", d);
        Assert.Contains("Right USB-A port is loose", d);
        Assert.Contains("Charger", d);
        Assert.Contains("Laptop sleeve", d);
        Assert.Contains("USB-C hub", d);
        Assert.Contains("Asking price: $650", d);
        Assert.Contains("Pick-up preferred", d);
        Assert.Contains("Listing prepared with SellSpec.", d);
        Assert.DoesNotContain("Original box", d);
    }

    [Fact]
    public void Unanswered_questionnaire_makes_no_claims()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.General));
        var d = listing.Description;
        Assert.DoesNotContain("condition", d.Split("Specifications:")[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Charger", d);
        Assert.DoesNotContain("No known faults", d);
        Assert.DoesNotContain("Known issues", d);
        Assert.DoesNotContain("Included:", d);
        Assert.Contains(listing.Warnings, w => w.Contains("questionnaire"));
        Assert.Contains("Condition", listing.Sections.Select(s => s.Heading));
        Assert.Equal("Not stated by the seller.", listing.Sections.Single(s => s.Heading == "Condition").Lines[0]);
    }

    [Fact]
    public void Explicit_no_defects_is_attributed_to_seller()
    {
        var q = new ConditionQuestionnaire { HasKnownDefects = Answer.No };
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.General, q));
        Assert.Contains("No known faults, as described by the seller.", listing.Description);
    }

    [Fact]
    public void Failed_hardware_test_is_disclosed_and_passes_are_attributed()
    {
        var tests = new[]
        {
            new HardwareTestResult(HardwareTestKind.Keyboard, TestOutcome.Passed, TestSource.SellerConfirmed),
            new HardwareTestResult(HardwareTestKind.Display, TestOutcome.Passed, TestSource.SellerConfirmed),
            new HardwareTestResult(HardwareTestKind.Speakers, TestOutcome.Failed, TestSource.SellerConfirmed, Notes: "left speaker silent"),
        };
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.General, null, tests));
        Assert.Contains("Seller confirmed working: keyboard, display.", listing.Description);
        Assert.Contains("Speakers test failed (seller-reported): left speaker silent", listing.Description);
    }

    [Fact]
    public void Charger_not_included_is_stated()
    {
        var q = new ConditionQuestionnaire { ChargerIncluded = Answer.No };
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.General, q));
        Assert.Contains("Charger not included", listing.Description);
    }

    [Fact]
    public void Ebay_template_respects_title_limit_and_omits_price()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.GamingLaptop(), Marketplace.Ebay, GoodCondition));
        Assert.True(listing.Title.Length <= 80, listing.Title);
        Assert.Equal(80, listing.TitleLimit);
        Assert.DoesNotContain("Asking price", listing.Description);
        Assert.StartsWith("ASUS ROG Strix G15 G513QM laptop in good condition.", listing.Description);
        Assert.Contains("• Processor:", listing.Description);
    }

    [Fact]
    public void Facebook_template_uses_hyphen_bullets_and_no_price()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.FacebookMarketplace, GoodCondition));
        Assert.True(listing.Title.Length <= 99);
        Assert.Contains("- Processor:", listing.Description);
        Assert.DoesNotContain("Asking price", listing.Description);
        Assert.DoesNotContain("•", listing.Description);
    }

    [Fact]
    public void Gumtree_template_includes_price_and_short_title()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.StandardLaptop(), Marketplace.Gumtree, GoodCondition));
        Assert.True(listing.Title.Length <= 64, listing.Title);
        Assert.Contains("Asking price: $650", listing.Description);
    }

    [Fact]
    public void Custom_title_limits_are_honoured()
    {
        var options = new ListingOptions { EbayTitleLimit = 40 };
        var listing = ListingGenerator.Generate(new ListingRequest(Scenarios.StandardLaptop(), ConditionQuestionnaire.Empty, Array.Empty<HardwareTestResult>(), Marketplace.Ebay, options));
        Assert.True(listing.Title.Length <= 40, listing.Title);
    }

    [Fact]
    public void Gaming_laptop_lists_both_graphics_adapters_dedicated_first()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.GamingLaptop(), Marketplace.General));
        Assert.Contains("Graphics: NVIDIA GeForce RTX 3060 Laptop GPU (6 GB) + AMD Radeon Graphics (integrated)", listing.Description);
        Assert.Contains("Storage: 512 GB NVMe SSD + 1 TB SSD", listing.Description);
        Assert.Contains("144 Hz", listing.Description);
    }

    [Fact]
    public void Desktop_omits_battery_and_external_monitor_but_names_motherboard()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.CustomDesktop(), Marketplace.General));
        Assert.DoesNotContain("battery", listing.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Display:", listing.Description);
        Assert.Contains("Motherboard: Gigabyte B550M K", listing.Description);
        Assert.Contains("Storage: 512 GB NVMe SSD + 2 TB HDD", listing.Description);
        Assert.Contains("custom-built desktop PC built on a Gigabyte B550M K motherboard", listing.Description);
    }

    [Fact]
    public void Multi_drive_device_excludes_external_and_virtual_drives()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.MultiDriveDevice(), Marketplace.General));
        Assert.Contains("Storage: 512 GB NVMe SSD + 1 TB HDD", listing.Description);
        Assert.DoesNotContain("SanDisk", listing.Description);
        Assert.DoesNotContain("Virtual", listing.Description);
    }

    [Fact]
    public void Unavailable_battery_health_is_omitted_not_invented()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.UnavailableBatteryHealth(), Marketplace.General));
        Assert.DoesNotContain("battery health", listing.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Soldered_ram_is_never_advertised_as_upgradeable()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.SolderedRamLaptop(), Marketplace.General));
        Assert.DoesNotContain("upgrad", listing.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Memory: 16 GB LPDDR5 RAM", listing.Description);
    }

    [Fact]
    public void Virtual_machine_produces_warning()
    {
        var listing = ListingGenerator.Generate(Request(Scenarios.VirtualMachine(), Marketplace.General));
        Assert.Contains(listing.Warnings, w => w.Contains("virtual machine", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listing.Warnings, w => w.Contains("No internal storage", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Scenarios.All), MemberType = typeof(Scenarios))]
    public void Every_scenario_generates_clean_listings_for_every_marketplace(string name, DeviceSnapshot snapshot)
    {
        foreach (var marketplace in Enum.GetValues<Marketplace>())
        {
            var listing = ListingGenerator.Generate(Request(snapshot, marketplace, GoodCondition));
            Assert.False(string.IsNullOrWhiteSpace(listing.Title), name);
            Assert.False(string.IsNullOrWhiteSpace(listing.Description), name);
            if (listing.TitleLimit is { } limit) Assert.True(listing.Title.Length <= limit, $"{name}/{marketplace}: {listing.Title}");
            Assert.Empty(Redactor.FindLeaks(listing.CombinedText, snapshot.Private));
            Assert.DoesNotContain("Unavailable", listing.Description);
            Assert.DoesNotContain("null", listing.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Footer_can_be_disabled()
    {
        var options = new ListingOptions { IncludeSellSpecFooter = false };
        var listing = ListingGenerator.Generate(new ListingRequest(Scenarios.StandardLaptop(), ConditionQuestionnaire.Empty, Array.Empty<HardwareTestResult>(), Marketplace.General, options));
        Assert.DoesNotContain("SellSpec", listing.Description);
    }
}

public sealed class ConditionQuestionnaireTests
{
    [Fact]
    public void Empty_detection()
    {
        Assert.True(ConditionQuestionnaire.Empty.IsEmpty);
        Assert.False((ConditionQuestionnaire.Empty with { ChargerIncluded = Answer.Yes }).IsEmpty);
    }

    [Fact]
    public void Splits_accessory_lists()
    {
        var items = ConditionQuestionnaire.SplitList("mouse, laptop bag;  usb hub\nstylus");
        Assert.Equal(new[] { "Mouse", "Laptop bag", "Usb hub", "Stylus" }, items);
    }
}
