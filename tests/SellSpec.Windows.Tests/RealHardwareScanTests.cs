using System.Diagnostics;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Reports;
using SellSpec.Core.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SellSpec.Windows.Tests;

/// <summary>
/// Integration tests that run the real Windows providers on the machine executing the tests. They assert
/// structural properties (no crash, timeouts honoured, redaction holds) rather than specific hardware values.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealHardwareScanTests
{
    private readonly ITestOutputHelper _output;

    public RealHardwareScanTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Scan_completes_and_produces_usable_snapshot()
    {
        var logger = new MemoryScanLogger();
        var scanner = WindowsHardwareProviders.CreateScanner(logger);
        var progress = new List<ScanProgress>();
        var stopwatch = Stopwatch.StartNew();

        var result = await scanner.ScanAsync(new Progress<ScanProgress>(p => { lock (progress) progress.Add(p); }), CancellationToken.None);
        stopwatch.Stop();

        _output.WriteLine($"Scan took {result.Duration.TotalMilliseconds:0} ms (elevated: {result.IsElevated})");
        foreach (var issue in result.Issues)
        {
            _output.WriteLine($"ISSUE [{issue.Severity}] {issue.Component}: {issue.Message} {issue.Detail}");
        }
        foreach (var line in logger.Lines)
        {
            _output.WriteLine(line);
        }

        var s = result.Snapshot;
        var report = ReportBuilder.Build(new ReportRequest(s, ConditionQuestionnaire.Empty, Array.Empty<HardwareTestResult>(), result.ScannedAt, "test"));
        var text = TextReportWriter.Write(report);
        _output.WriteLine(text);

        var listing = ListingGenerator.Generate(new ListingRequest(s, ConditionQuestionnaire.Empty, Array.Empty<HardwareTestResult>(), Marketplace.General));
        _output.WriteLine("TITLE: " + listing.Title);
        _output.WriteLine(listing.Description);
        _output.WriteLine("PRIVATE IDS: " + s.Private.AllValues().Count());

        Assert.False(result.WasCancelled);
        Assert.True(result.Duration < TimeSpan.FromSeconds(60), "Scan exceeded the overall budget.");
        Assert.True(s.Processor.Name.HasValue, "Processor name should be detected on a real Windows machine.");
        Assert.True(s.OperatingSystem.Name.HasValue, "OS name should be detected.");
        Assert.True(s.Memory.InstalledBytes.HasValue || s.Memory.UsableBytes.HasValue, "Memory should be detected.");
        Assert.NotEmpty(s.Storage);
        Assert.NotEmpty(s.Graphics);
        Assert.Equal(HardwareScanner.ComponentCount, progress.Count(p => p.Stage != ScanStage.Started));
        Assert.DoesNotContain(result.Issues, i => i.Severity == ScanIssueSeverity.Error);

        // Redaction on real data: none of the serial numbers may appear in any export.
        var json = JsonReportWriter.Write(report);
        Assert.Empty(Redactor.FindLeaks(text, s.Private));
        Assert.Empty(Redactor.FindLeaks(json, s.Private));
        Assert.Empty(Redactor.FindLeaks(listing.CombinedText, s.Private));
        var pdf = PdfReportWriter.Write(report);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public async Task Scan_can_be_cancelled_quickly()
    {
        var scanner = WindowsHardwareProviders.CreateScanner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
        var stopwatch = Stopwatch.StartNew();
        var result = await scanner.ScanAsync(null, cts.Token);
        stopwatch.Stop();

        Assert.True(result.WasCancelled);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Cancellation took {stopwatch.Elapsed}.");
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public void Native_calls_do_not_throw()
    {
        var power = Native.NativeInfo.GetPowerStatus();
        var firmware = Native.NativeInfo.GetFirmwareType();
        var tpm = Native.NativeInfo.GetTpm();
        var digitizer = Native.NativeInfo.GetDigitizer();
        var displays = Native.NativeInfo.GetDisplayDevices();
        var wlan = Native.NativeInfo.GetWlanInterfaces();

        _output.WriteLine($"Power: {power}; Firmware: {firmware}; TPM: {tpm}; Digitizer: {digitizer}; Displays: {displays.Count}; WLAN: {wlan.Count}");
        foreach (var d in displays)
        {
            _output.WriteLine($"  {d.AdapterDeviceName} primary={d.IsPrimary} current={d.Current} modes={d.Modes.Count} id={d.MonitorDeviceId}");
        }
        foreach (var w in wlan)
        {
            _output.WriteLine($"  WLAN {w.Description}: phys={string.Join(",", w.PhyTypes)}");
        }

        Assert.NotNull(firmware);
        Assert.NotEmpty(displays);
    }
}
