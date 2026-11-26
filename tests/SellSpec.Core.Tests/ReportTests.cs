using System.Text;
using System.Text.RegularExpressions;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Reports;
using SellSpec.Core.Reports.Pdf;
using SellSpec.Core.Settings;
using SellSpec.Core.Testing;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class ReportBuilderTests
{
    private static ReportRequest Request(DeviceSnapshot s, ConditionQuestionnaire? q = null, IReadOnlyList<HardwareTestResult>? tests = null) =>
        new(s, q ?? ConditionQuestionnaire.Empty, tests ?? Array.Empty<HardwareTestResult>(), new DateTimeOffset(2026, 11, 25, 10, 30, 0, TimeSpan.FromHours(11)), "0.1.0-test");

    [Fact]
    public void Report_contains_core_sections_and_disclaimer()
    {
        var report = ReportBuilder.Build(Request(Scenarios.StandardLaptop()));
        Assert.Equal("Lenovo IdeaPad Slim 5 14IAH8", report.DeviceName);
        Assert.Equal("Laptop", report.DeviceType);
        Assert.Contains("Intel Core i5-1335U", report.Headline);
        Assert.Contains(report.Specifications, s => s.Title == "Processor");
        Assert.Contains(report.Specifications, s => s.Title == "Battery");
        Assert.Contains(report.Specifications, s => s.Title == "Display");
        Assert.Contains(report.Health, h => h.Title == "Reported battery health" && h.Status == HealthStatusKind.Good);
        Assert.Equal(6, report.HardwareTests.Count);
        Assert.Contains("not tamper-proof", report.Disclaimer);
        Assert.Contains("supplied by the seller", report.Disclaimer);
    }

    [Fact]
    public void Upgradeability_is_always_not_verified()
    {
        var report = ReportBuilder.Build(Request(Scenarios.SolderedRamLaptop()));
        var memory = report.Specifications.Single(s => s.Title == "Memory");
        var upgrade = memory.Fields.Single(f => f.Label == "Upgradeable");
        Assert.Equal("Not verified", upgrade.Value);
        Assert.Contains("soldered", upgrade.Note);
    }

    [Fact]
    public void Desktop_report_marks_battery_not_applicable_and_skips_external_display()
    {
        var report = ReportBuilder.Build(Request(Scenarios.CustomDesktop()));
        var battery = report.Specifications.Single(s => s.Title == "Battery").Fields.Single();
        Assert.Contains("Not applicable", battery.Value);
        Assert.DoesNotContain(report.Specifications, s => s.Title == "Display");
        Assert.Contains(report.Specifications.Single(s => s.Title == "System").Fields, f => f.Label == "Motherboard" && f.Value == "Gigabyte B550M K");
    }

    [Fact]
    public void Unavailable_values_are_labelled_not_omitted_in_reports()
    {
        var report = ReportBuilder.Build(Request(Scenarios.NonAdministrator()));
        var storage = report.Specifications.Single(s => s.Title == "Storage");
        var hours = storage.Fields.Single(f => f.Label == "Power-on hours");
        Assert.Equal("Not available", hours.Value);
        Assert.Equal(Provenance.Unavailable, hours.Provenance);
        Assert.Contains("administrator", hours.Note);
    }

    [Fact]
    public void Seller_condition_and_known_issues_flow_through()
    {
        var q = new ConditionQuestionnaire { OverallCondition = ConditionGrade.Excellent, HasKnownDefects = Answer.Yes, KnownDefects = "Dead pixel top-left", ChargerIncluded = Answer.Yes, AskingPrice = 1200.50m };
        var report = ReportBuilder.Build(Request(Scenarios.StandardLaptop(), q));
        Assert.Contains(report.SellerCondition, f => f.Label == "Overall condition" && f.Value == "Excellent" && f.Provenance == Provenance.SellerProvided);
        Assert.Contains(report.SellerCondition, f => f.Label == "Asking price" && f.Value == "$1,200.50");
        Assert.Contains("Dead pixel top-left", report.KnownIssues);
    }

    [Fact]
    public void Virtual_machine_notice_and_demo_notice()
    {
        var report = ReportBuilder.Build(Request(Scenarios.VirtualMachine()) with { IsDemoData = true });
        Assert.Contains(report.Notices, n => n.Contains("virtual machine"));
        Assert.Contains(report.Notices, n => n.Contains("DEMONSTRATION"));
        Assert.True(report.IsDemoData);
    }
}

public sealed class ReportWriterTests
{
    private static DeviceReport Report(DeviceSnapshot s) =>
        ReportBuilder.Build(new ReportRequest(s, new ConditionQuestionnaire { OverallCondition = ConditionGrade.Good, HasKnownDefects = Answer.No }, Array.Empty<HardwareTestResult>(), DateTimeOffset.Now, "0.1.0"));

    [Fact]
    public void Text_report_is_complete()
    {
        var text = TextReportWriter.Write(Report(Scenarios.StandardLaptop()));
        Assert.Contains("SELLSPEC DEVICE REPORT", text);
        Assert.Contains("Lenovo IdeaPad Slim 5 14IAH8", text);
        Assert.Contains("DEVICE HEALTH", text);
        Assert.Contains("HARDWARE TESTS", text);
        Assert.Contains("SELLER-REPORTED CONDITION", text);
        Assert.Contains("Not tested", text);
        Assert.Contains(DeviceReport.DefaultDisclaimer[..40], text);
    }

    [Fact]
    public void Json_round_trips_and_uses_string_enums()
    {
        var report = Report(Scenarios.GamingLaptop());
        var json = JsonReportWriter.Write(report);
        Assert.Contains("\"schemaVersion\": \"1.0\"", json);
        Assert.Contains("\"status\": \"Good\"", json);
        Assert.Contains("\"provenance\": \"Detected\"", json);
        var back = JsonReportWriter.Read(json);
        Assert.NotNull(back);
        Assert.Equal(report.DeviceName, back!.DeviceName);
        Assert.Equal(report.Specifications.Count, back.Specifications.Count);
        Assert.Equal(report.Health.Count, back.Health.Count);
    }

    [Theory]
    [MemberData(nameof(Scenarios.All), MemberType = typeof(Scenarios))]
    public void No_export_leaks_private_identifiers(string name, DeviceSnapshot snapshot)
    {
        var report = Report(snapshot);
        var text = TextReportWriter.Write(report);
        var json = JsonReportWriter.Write(report);
        var pdf = Encoding.Latin1.GetString(PdfReportWriter.Write(report));

        Assert.Empty(Redactor.FindLeaks(text, snapshot.Private));
        Assert.Empty(Redactor.FindLeaks(json, snapshot.Private));
        Assert.Empty(Redactor.FindLeaks(pdf, snapshot.Private));
        Assert.DoesNotContain("PF3K8QX2", text + json + pdf);
        Assert.DoesNotContain("S6PENX0T512345A", text + json + pdf);
        Assert.True(name.Length > 0);
    }

    [Fact]
    public void Pdf_has_valid_structure_and_cross_reference_offsets()
    {
        var bytes = PdfReportWriter.Write(Report(Scenarios.GamingLaptop()));
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Page ", text);
        Assert.Contains("/BaseFont /Helvetica", text);
        Assert.Contains("/Title (SellSpec device report", text);

        var startxref = Regex.Match(text, @"startxref\n(\d+)\n%%EOF");
        Assert.True(startxref.Success);
        var xrefPos = int.Parse(startxref.Groups[1].Value);
        Assert.Equal("xref", text.Substring(xrefPos, 4));

        var entries = Regex.Matches(text[xrefPos..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.True(entries.Count >= 8);
        for (var i = 0; i < entries.Count; i++)
        {
            var expected = $"{i + 1} 0 obj";
            Assert.Equal(expected, text.Substring(entries[i], expected.Length));
        }
    }

    [Fact]
    public void Pdf_paginates_long_reports()
    {
        var s = Scenarios.MultiDriveDevice();
        var q = new ConditionQuestionnaire { SellerNotes = string.Join(" ", Enumerable.Repeat("This is a long seller note that wraps across many lines.", 60)), HasKnownDefects = Answer.Yes, KnownDefects = string.Join(" ", Enumerable.Repeat("Known defect text.", 80)) };
        var report = ReportBuilder.Build(new ReportRequest(s, q, Array.Empty<HardwareTestResult>(), DateTimeOffset.Now, "0.1.0"));
        var text = Encoding.Latin1.GetString(PdfReportWriter.Write(report));
        var pages = Regex.Matches(text, @"/Type /Page ").Count;
        Assert.True(pages >= 2, $"Expected pagination, got {pages} page(s).");
        Assert.Contains($"Page 1 of {pages}", text.Replace("\\(", "(").Replace("\\)", ")"));
    }

    [Fact]
    public void Pdf_text_escaping_and_encoding()
    {
        Assert.Equal("a\\(b\\)\\\\c", PdfText.Escape("a(b)\\c"));
        var ansi = PdfText.ToWinAnsi("16\u00D71080 \u2013 \u2022 test \u2122");
        Assert.Equal((char)0x96, ansi[8]);
        Assert.Equal((char)0x95, ansi[10]);
        Assert.Equal((char)0x99, ansi[^1]);
        Assert.Equal("?", PdfText.ToWinAnsi("\u4E2D"));
    }

    [Fact]
    public void Pdf_metrics_wrap_text()
    {
        var lines = PdfMetrics.Wrap("The quick brown fox jumps over the lazy dog and keeps running far away", PdfFont.Helvetica, 10, 120);
        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(PdfMetrics.Width(l, PdfFont.Helvetica, 10) <= 120.01));
        Assert.True(PdfMetrics.Width("WWW", PdfFont.HelveticaBold, 10) > PdfMetrics.Width("iii", PdfFont.HelveticaBold, 10));
    }
}

public sealed class RedactorTests
{
    [Fact]
    public void Detects_private_identifiers_and_network_data()
    {
        var ids = Scenarios.PrivateIds;
        Assert.Contains("private identifier", Redactor.FindLeaks("Serial PF3K8QX2 here", ids));
        Assert.Contains("private identifier", Redactor.FindLeaks("serial pf3k8qx2 lower", ids));
        Assert.Contains("MAC address", Redactor.FindLeaks("mac 00:1A:2B:3C:4D:5E", ids));
        Assert.Contains("IP address", Redactor.FindLeaks("ip 192.168.0.12 end", ids));
        Assert.Contains("product key", Redactor.FindLeaks("key ABCDE-12345-FGHIJ-67890-KLMNO", ids));
        Assert.Contains("user profile path", Redactor.FindLeaks(@"C:\Users\sam\Desktop", ids));
    }

    [Fact]
    public void Does_not_flag_version_strings_or_ordinary_text()
    {
        var ids = Scenarios.PrivateIds;
        Assert.Empty(Redactor.FindLeaks("Windows 11 Home 24H2 (build 26100.4061) version 10.0.26200", ids));
        Assert.Empty(Redactor.FindLeaks("Driver 31.0.101.5186", ids));
        Assert.Empty(Redactor.FindLeaks("Intel Core i5-1335U, 16 GB RAM, 512 GB NVMe SSD, 1920x1080", ids));
        Assert.Empty(Redactor.FindLeaks("2026-11-26 16:55:22.912 [Info] Scan: Scan started.", ids));
        Assert.Empty(Redactor.FindLeaks(@"C:\Users\[user]\Documents", ids));
    }

    [Theory]
    [InlineData("addr 2001:db8:85a3:0:0:8a2e:370:7334 end")]
    [InlineData("link fe80::1a2b:3c4d:5e6f:7a8b%12")]
    [InlineData("loopback ::1 here")]
    [InlineData("prefix 2001:db8::")]
    public void Detects_ipv6_addresses(string text)
    {
        Assert.Contains("IPv6 address", Redactor.FindLeaks(text, Scenarios.PrivateIds));
    }

    [Fact]
    public void Short_private_values_are_matched_as_whole_tokens()
    {
        var ids = new PrivateIdentifiers { SystemSerialNumber = "AB12" };
        Assert.Contains("private identifier", Redactor.FindLeaks("tag AB12 end", ids));
        Assert.Empty(Redactor.FindLeaks("model XAB12Y end", ids));
    }

    [Fact]
    public void Additional_terms_are_matched_as_whole_words_and_generic_names_ignored()
    {
        var ids = Scenarios.PrivateIds;
        Assert.Contains("account or computer name", Redactor.FindLeaks("hello from sam", ids, new[] { "sam" }));
        Assert.Empty(Redactor.FindLeaks("Samsung SSD 970", ids, new[] { "sam" }));
        Assert.Empty(Redactor.FindLeaks("the user pressed save", ids, new[] { "user" }));
        Assert.Contains("account or computer name", Redactor.FindLeaks("on DESKTOP-ABC123 today", ids, new[] { "DESKTOP-ABC123" }));
        Assert.Equal("hi [redacted], from [redacted]", Redactor.ReplaceTerms("hi sam, from DESKTOP-ABC123", new[] { "sam", "DESKTOP-ABC123" }));
    }

    [Fact]
    public void EnsureClean_throws_on_leak()
    {
        var ex = Assert.Throws<RedactionException>(() => Redactor.EnsureClean("PF3K8QX2", Scenarios.PrivateIds));
        Assert.Contains("private identifier", ex.Findings);
    }
}

public sealed class SettingsStoreTests
{
    [Fact]
    public void Round_trips_and_recovers_from_corruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sellspec-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        var store = new JsonSettingsStore(path);

        Assert.Equal(AppSettings.Default, store.Load());

        var settings = new AppSettings { Theme = ThemeMode.Dark, EbayTitleLimit = 70, DefaultMarketplace = Marketplace.Ebay, CurrencySymbol = "\u00A3" };
        store.Save(settings);
        Assert.Equal(settings, store.Load());

        File.WriteAllText(path, "{ not json");
        Assert.Equal(AppSettings.Default, store.Load());

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Listing_options_clamp_limits()
    {
        var options = (new AppSettings { EbayTitleLimit = 5, CurrencySymbol = " " }).ToListingOptions();
        Assert.Equal(20, options.EbayTitleLimit);
        Assert.Equal("$", options.CurrencySymbol);
    }
}

public sealed class MiscTests
{
    [Fact]
    public void Test_tone_is_valid_wav()
    {
        var wav = TestToneGenerator.CreateWav(440, 0.5, ToneChannel.Left);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44 + (int)(TestToneGenerator.SampleRate * 0.5) * 4, wav.Length);
        // Right channel silent for a left-only tone.
        var rightSample = BitConverter.ToInt16(wav, 44 + 2 + 4 * 1000);
        Assert.Equal(0, rightSample);
    }

    [Fact]
    public void Checklist_has_eight_informational_items()
    {
        Assert.Equal(8, PreSaleChecklist.Items.Count);
        Assert.All(PreSaleChecklist.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        Assert.All(PreSaleChecklist.Items.Where(i => i.SettingsUri is not null), i => Assert.StartsWith("ms-settings:", i.SettingsUri));
    }

    [Fact]
    public void Demo_device_is_clearly_labelled()
    {
        var demo = Demo.DemoDeviceFactory.CreateLaptop();
        Assert.Equal("SellSpec Demo", demo.Identity.Manufacturer.Value);
        Assert.StartsWith("SellSpec Demo", demo.Identity.DisplayName);
        Assert.Equal(87, demo.Battery.HealthPercent.Value);
    }

    [Fact]
    public void Spec_helpers_behave()
    {
        var unavailable = Spec<string>.Unavailable("why");
        Assert.False(unavailable.HasValue);
        Assert.Equal("fallback", unavailable.ValueOr("fallback"));
        Assert.Equal("Firmware reported", Spec.Label(Provenance.FirmwareReported));
        Assert.False(Spec.FirmwareText("To Be Filled By O.E.M.").HasValue);
        Assert.Equal(5, Spec.Detected("hello").Map(s => s.Length).Value);
    }
}
