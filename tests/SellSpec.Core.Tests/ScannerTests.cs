using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class HardwareScannerTests
{
    [Fact]
    public async Task Failing_provider_does_not_fail_the_scan()
    {
        var snapshot = Scenarios.StandardLaptop();
        var providers = FakeProviders.FromSnapshot(snapshot, processor: _ => throw new InvalidOperationException("WMI exploded"));
        var logger = new MemoryScanLogger();
        var scanner = new HardwareScanner(providers, ScanOptions.Default, logger);

        var result = await scanner.ScanAsync();

        Assert.False(result.WasCancelled);
        Assert.Contains(result.Issues, i => i.Component == "Processor" && i.Severity == ScanIssueSeverity.Error);
        Assert.False(result.Snapshot.Processor.Name.HasValue);
        Assert.True(result.Snapshot.Memory.InstalledBytes.HasValue, "Other components still complete.");
        Assert.Contains(logger.Lines, l => l.Contains("Processor") && l.Contains("InvalidOperationException"));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("PF3K8QX2"));
    }

    [Fact]
    public async Task Slow_provider_times_out_and_is_marked_unavailable()
    {
        var snapshot = Scenarios.StandardLaptop();
        var providers = FakeProviders.FromSnapshot(snapshot, battery: async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new BatteryResult(snapshot.Battery, Array.Empty<string>());
        });
        var options = ScanOptions.Default with { ComponentTimeout = TimeSpan.FromMilliseconds(150), SlowComponentTimeout = TimeSpan.FromMilliseconds(150) };
        var scanner = new HardwareScanner(providers, options);

        var result = await scanner.ScanAsync();

        Assert.Contains(result.Issues, i => i.Component == "Battery" && i.Severity == ScanIssueSeverity.Warning);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
        Assert.NotEqual(HealthStatusKind.Good, result.Snapshot.Battery.HealthStatus);
    }

    [Fact]
    public async Task Cancellation_is_honoured_and_reported()
    {
        var snapshot = Scenarios.StandardLaptop();
        using var cts = new CancellationTokenSource();
        var providers = FakeProviders.FromSnapshot(snapshot, storage: async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StorageResult.Empty;
        });
        var scanner = new HardwareScanner(providers);
        cts.CancelAfter(100);

        var result = await scanner.ScanAsync(null, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Contains(result.Issues, i => i.Component == "Storage" && i.Severity == ScanIssueSeverity.Info);
    }

    [Fact]
    public async Task Progress_reports_every_component()
    {
        var snapshot = Scenarios.StandardLaptop();
        var scanner = new HardwareScanner(FakeProviders.FromSnapshot(snapshot));
        var reports = new List<ScanProgress>();
        var progress = new SynchronousProgress(reports);

        var result = await scanner.ScanAsync(progress);

        Assert.Equal(HardwareScanner.ComponentCount, reports.Count(r => r.Stage == ScanStage.Started));
        Assert.Equal(HardwareScanner.ComponentCount, reports.Count(r => r.Stage == ScanStage.Completed));
        Assert.Equal(HardwareScanner.ComponentCount, reports.Where(r => r.Stage == ScanStage.Completed).Max(r => r.Completed));
        Assert.Empty(result.Issues);
        Assert.Equal("Lenovo IdeaPad Slim 5 14IAH8", result.Snapshot.Identity.DisplayName);
    }

    [Fact]
    public async Task Private_identifiers_are_registered_with_the_log_sanitiser()
    {
        LogSanitiser.ClearPrivateValues();
        var snapshot = Scenarios.StandardLaptop();
        var scanner = new HardwareScanner(FakeProviders.FromSnapshot(snapshot));
        await scanner.ScanAsync();

        Assert.Equal("serial [redacted] seen", LogSanitiser.Sanitise("serial PF3K8QX2 seen"));
        LogSanitiser.ClearPrivateValues();
    }

    private sealed class SynchronousProgress : IProgress<ScanProgress>
    {
        private readonly List<ScanProgress> _list;
        public SynchronousProgress(List<ScanProgress> list) => _list = list;
        public void Report(ScanProgress value) { lock (_list) _list.Add(value); }
    }
}

public sealed class SnapshotEnricherTests
{
    [Fact]
    public void Unknown_form_factor_is_estimated_from_battery()
    {
        var s = Scenarios.StandardLaptop() with { Identity = Scenarios.StandardLaptop().Identity with { FormFactor = Spec<DeviceFormFactor>.Unavailable() } };
        var enriched = SnapshotEnricher.Enrich(s);
        Assert.Equal(DeviceFormFactor.Laptop, enriched.Identity.FormFactor.Value);
        Assert.Equal(Provenance.Estimated, enriched.Identity.FormFactor.Provenance);
    }

    [Fact]
    public void Desktop_with_unknown_battery_becomes_not_applicable()
    {
        var s = Scenarios.CustomDesktop() with { Battery = BatteryInfo.Unavailable() };
        var enriched = SnapshotEnricher.Enrich(s);
        Assert.Equal(BatteryPresence.NotPresent, enriched.Battery.Presence);
    }

    [Fact]
    public void Windows_11_assessment_is_attached()
    {
        var enriched = SnapshotEnricher.Enrich(Scenarios.StandardLaptop());
        Assert.NotNull(enriched.OperatingSystem.Windows11Compatibility);
    }
}

public sealed class LogSanitiserTests
{
    [Fact]
    public void Removes_network_identifiers_and_user_paths()
    {
        // The GUID must not be one of the fixture identifiers: scanner tests register those as private values
        // in the (static) sanitiser, and test classes run in no fixed order, so a shared GUID would be replaced
        // with "[redacted]" instead of "[guid]" depending on scheduling.
        var text = "adapter 00:1A:2B:3C:4D:5E at 192.168.1.20 for C:\\Users\\sam\\Documents id 0f1e2d3c-4b5a-4978-8a6b-5c4d3e2f1a0b";
        var clean = LogSanitiser.Sanitise(text);
        Assert.DoesNotContain("00:1A", clean);
        Assert.DoesNotContain("192.168", clean);
        Assert.DoesNotContain("\\sam\\", clean);
        Assert.DoesNotContain("0f1e2d3c", clean);
        Assert.Contains("[mac]", clean);
        Assert.Contains("[ip]", clean);
        Assert.Contains("[user]", clean);
        Assert.Contains("[guid]", clean);

        var v6 = LogSanitiser.Sanitise("link fe80::1a2b:3c4d:5e6f:7a8b and 2001:db8:85a3:0:0:8a2e:370:7334 at 16:55:22.912");
        Assert.DoesNotContain("fe80", v6);
        Assert.DoesNotContain("8a2e", v6);
        Assert.Contains("16:55:22.912", v6);
    }

    [Fact]
    public void Formats_lines_with_exception_type_but_no_stack_trace()
    {
        var line = LogSanitiser.FormatLine(LogLevel.Error, "Storage", "boom", new InvalidOperationException("inner C:\\Users\\sam\\x"));
        Assert.Contains("[Error] Storage: boom", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.DoesNotContain("\\sam\\", line);
        Assert.DoesNotContain("   at ", line);
    }
}
