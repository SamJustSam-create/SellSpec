using System.IO.Compression;
using System.Text;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class DiagnosticsBundleTests
{
    private static ScanResult Result(DeviceSnapshot snapshot) => new()
    {
        Snapshot = snapshot,
        ScannedAt = new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.FromHours(11)),
        Duration = TimeSpan.FromMilliseconds(1234),
        Issues = new[] { new ScanIssue("Storage", "Drive power-on hours may need administrator rights.", ScanIssueSeverity.Info) },
    };

    private static Dictionary<string, string> Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public void Bundle_contains_every_expected_entry()
    {
        var bundle = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(Scenarios.StandardLaptop()), "0.1.1-test", "2026-11-26 09:00:00.000 [Info] Scan: Scan started.", "Battery showed 0% but it is new."));
        var entries = Unzip(bundle.Zip);

        Assert.Equal(new[] { "README.txt", "report.json", "raw-detection.txt", "scan-issues.txt", "log.txt", "notes.txt" }, bundle.Entries);
        Assert.Equal(bundle.Entries.Count, entries.Count);
        Assert.Contains("\"deviceName\": \"Lenovo IdeaPad Slim 5 14IAH8\"", entries["report.json"]);
        Assert.Contains("Raw manufacturer             LENOVO", entries["raw-detection.txt"]);
        Assert.Contains("Raw model                    82XF", entries["raw-detection.txt"]);
        Assert.Contains("[FirmwareReported]", entries["raw-detection.txt"]);
        Assert.Contains("administrator rights", entries["scan-issues.txt"]);
        Assert.Contains("Scan started", entries["log.txt"]);
        Assert.Equal("Battery showed 0% but it is new.", entries["notes.txt"]);
        Assert.Contains("no serial numbers", entries["README.txt"]);
    }

    [Fact]
    public void Optional_entries_are_omitted_when_empty()
    {
        var bundle = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(Scenarios.CustomDesktop()), "0.1.1-test"));
        Assert.DoesNotContain("log.txt", bundle.Entries);
        Assert.DoesNotContain("notes.txt", bundle.Entries);
        Assert.Equal(4, bundle.Entries.Count);
    }

    [Theory]
    [MemberData(nameof(Scenarios.All), MemberType = typeof(Scenarios))]
    public void No_scenario_leaks_private_identifiers(string name, DeviceSnapshot snapshot)
    {
        var bundle = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(snapshot), "0.1.1-test", "log line with nothing sensitive"));
        var entries = Unzip(bundle.Zip);
        foreach (var (entry, text) in entries)
        {
            Assert.Empty(Redactor.FindLeaks(text, snapshot.Private));
            Assert.DoesNotContain("PF3K8QX2", text);
            Assert.DoesNotContain("S6PENX0T512345A", text);
        }
        Assert.Empty(Redactor.FindLeaks(bundle.CombinedText, snapshot.Private));
        Assert.True(name.Length > 0);
    }

    [Fact]
    public void Log_text_is_sanitised_before_packaging()
    {
        var snapshot = Scenarios.StandardLaptop();
        var log = "adapter 00:1A:2B:3C:4D:5E at 10.0.0.7 user C:\\Users\\sam\\x";
        var bundle = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(snapshot), "0.1.1-test", log));
        var text = Unzip(bundle.Zip)["log.txt"];
        Assert.DoesNotContain("00:1A", text);
        Assert.DoesNotContain("10.0.0.7", text);
        Assert.DoesNotContain("\\sam\\", text);
    }

    [Fact]
    public void Demo_bundles_are_labelled_as_demonstration_data()
    {
        var bundle = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(Demo.DemoDeviceFactory.CreateLaptop()), "0.1.1-test", IsDemoData: true));
        var entries = Unzip(bundle.Zip);
        Assert.Contains("DEMONSTRATION DATA", entries["README.txt"]);
        Assert.Contains("DEMONSTRATION DATA", entries["raw-detection.txt"]);
        Assert.Contains("DEMONSTRATION DATA", entries["report.json"]);
    }

    [Fact]
    public void Account_and_machine_names_are_stripped_from_logs_and_refused_in_notes()
    {
        var snapshot = Scenarios.StandardLaptop();
        var terms = new[] { "samuel", "DESKTOP-ABC123" };
        var ok = DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(snapshot), "0.1.1-test", "samuel started scan on DESKTOP-ABC123", null, false, terms));
        var log = Unzip(ok.Zip)["log.txt"];
        Assert.DoesNotContain("samuel", log);
        Assert.DoesNotContain("DESKTOP-ABC123", log);
        Assert.Contains("[redacted]", log);

        var ex = Assert.Throws<RedactionException>(() =>
            DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(snapshot), "0.1.1-test", null, "it happened on DESKTOP-ABC123", false, terms)));
        Assert.Contains("account or computer name", ex.Findings);
    }

    [Fact]
    public void User_notes_containing_a_serial_are_refused()
    {
        var snapshot = Scenarios.StandardLaptop();
        var ex = Assert.Throws<RedactionException>(() =>
            DiagnosticsBundleBuilder.Build(new DiagnosticsBundleRequest(Result(snapshot), "0.1.1-test", null, "my serial is PF3K8QX2")));
        Assert.Contains("private identifier", ex.Findings);
    }
}
