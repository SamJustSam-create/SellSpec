using SellSpec.Core.Health;
using SellSpec.Core.Models;
using SellSpec.Core.Testing;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class BatteryHealthCalculatorTests
{
    [Theory]
    [InlineData(60_000, 52_200, 87, HealthStatusKind.Good)]
    [InlineData(50_000, 40_000, 80, HealthStatusKind.Good)]
    [InlineData(50_000, 39_999, 80, HealthStatusKind.Good)]
    [InlineData(50_000, 35_000, 70, HealthStatusKind.Attention)]
    [InlineData(50_000, 25_000, 50, HealthStatusKind.Issue)]
    public void Health_percentage_and_status(int design, int full, int expectedPercent, HealthStatusKind expectedStatus)
    {
        var result = BatteryHealthCalculator.Calculate(design, full);
        Assert.True(result.HealthPercent.HasValue);
        Assert.Equal(expectedPercent, result.HealthPercent.Value);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public void Missing_design_capacity_is_unavailable()
    {
        var result = BatteryHealthCalculator.Calculate(null, 40_000);
        Assert.False(result.HealthPercent.HasValue);
        Assert.Equal(Provenance.Unavailable, result.HealthPercent.Provenance);
        Assert.Equal(HealthStatusKind.Unknown, result.Status);
    }

    [Fact]
    public void Zero_or_negative_capacities_are_unavailable()
    {
        Assert.False(BatteryHealthCalculator.Calculate(0, 40_000).HealthPercent.HasValue);
        Assert.False(BatteryHealthCalculator.Calculate(50_000, 0).HealthPercent.HasValue);
        Assert.False(BatteryHealthCalculator.Calculate(50_000, -5).HealthPercent.HasValue);
    }

    [Fact]
    public void Implausible_ratio_is_rejected()
    {
        var result = BatteryHealthCalculator.Calculate(10_000, 50_000);
        Assert.False(result.HealthPercent.HasValue);
        Assert.Contains("firmware", result.HealthPercent.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Slightly_above_design_is_capped_at_100()
    {
        var result = BatteryHealthCalculator.Calculate(50_000, 52_000);
        Assert.Equal(100, result.HealthPercent.Value);
        Assert.NotNull(result.HealthPercent.Note);
        Assert.Equal(HealthStatusKind.Good, result.Status);
    }

    [Fact]
    public void Multiple_packs_are_combined()
    {
        var packs = new[]
        {
            new BatteryPack { DesignCapacityMwh = Spec<int>.Firmware(40_000), FullChargeCapacityMwh = Spec<int>.Firmware(36_000), CycleCount = Spec<int>.Firmware(120) },
            new BatteryPack { DesignCapacityMwh = Spec<int>.Firmware(20_000), FullChargeCapacityMwh = Spec<int>.Firmware(12_000), CycleCount = Spec<int>.Firmware(300) },
        };
        var info = BatteryHealthCalculator.Combine(packs, Spec<int>.Detected(50), Spec<string>.Detected("Charging"), Spec<string>.Detected("OK"));

        Assert.Equal(BatteryPresence.Present, info.Presence);
        Assert.Equal(60_000, info.DesignCapacityMwh.Value);
        Assert.Equal(48_000, info.FullChargeCapacityMwh.Value);
        Assert.Equal(80, info.HealthPercent.Value);
        Assert.Equal(300, info.CycleCount.Value);
        Assert.Contains("Highest", info.CycleCount.Note);
    }

    [Fact]
    public void Pack_with_missing_design_makes_combined_health_unavailable()
    {
        var packs = new[]
        {
            new BatteryPack { DesignCapacityMwh = Spec<int>.Firmware(40_000), FullChargeCapacityMwh = Spec<int>.Firmware(36_000) },
            new BatteryPack { DesignCapacityMwh = Spec<int>.Unavailable(), FullChargeCapacityMwh = Spec<int>.Firmware(12_000) },
        };
        var info = BatteryHealthCalculator.Combine(packs, Spec<int>.Unavailable(), Spec<string>.Unavailable(), Spec<string>.Unavailable());
        Assert.False(info.HealthPercent.HasValue);
        Assert.Equal(HealthStatusKind.Unknown, info.HealthStatus);
    }

    [Fact]
    public void Zero_cycle_count_is_explained_not_trusted()
    {
        var packs = new[] { new BatteryPack { DesignCapacityMwh = Spec<int>.Firmware(40_000), FullChargeCapacityMwh = Spec<int>.Firmware(36_000), CycleCount = Spec<int>.Firmware(0) } };
        var info = BatteryHealthCalculator.Combine(packs, Spec<int>.Unavailable(), Spec<string>.Unavailable(), Spec<string>.Unavailable());
        Assert.Equal(0, info.CycleCount.Value);
        Assert.Contains("does not track", info.CycleCount.Note);
    }

    [Fact]
    public void No_packs_is_unavailable_not_absent()
    {
        var info = BatteryHealthCalculator.Combine(Array.Empty<BatteryPack>(), Spec<int>.Detected(70), Spec<string>.Unavailable(), Spec<string>.Unavailable());
        Assert.Equal(BatteryPresence.Unknown, info.Presence);
        Assert.Equal(70, info.ChargePercent.Value);
    }

    [Fact]
    public void Desktop_battery_is_not_applicable()
    {
        var info = BatteryInfo.NotApplicable();
        Assert.Equal(BatteryPresence.NotPresent, info.Presence);
        Assert.Equal(HealthStatusKind.NotApplicable, info.HealthStatus);
        Assert.True(info.HealthPercent.IsNotApplicable);
    }
}

public sealed class StorageHealthAssessorTests
{
    [Fact]
    public void Unavailable_health_is_unknown_not_good()
    {
        var drive = new StorageDevice { Model = "X" };
        var a = StorageHealthAssessor.Assess(drive);
        Assert.Equal(HealthStatusKind.Unknown, a.Status);
        Assert.Contains("could not be checked", a.Detail);
    }

    [Fact]
    public void Predicted_failure_is_an_issue()
    {
        var drive = new StorageDevice { Model = "X", PredictedFailure = Spec<bool>.Detected(true), ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good) };
        Assert.Equal(HealthStatusKind.Issue, StorageHealthAssessor.Assess(drive).Status);
    }

    [Fact]
    public void High_wear_or_hours_need_attention()
    {
        var worn = new StorageDevice { Model = "X", ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good), WearPercentUsed = Spec<int>.Detected(85) };
        Assert.Equal(HealthStatusKind.Attention, StorageHealthAssessor.Assess(worn).Status);

        var old = new StorageDevice { Model = "X", ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good), PowerOnHours = Spec<int>.Detected(40_000) };
        Assert.Equal(HealthStatusKind.Attention, StorageHealthAssessor.Assess(old).Status);
    }

    [Fact]
    public void Healthy_drive_is_good()
    {
        var drive = Scenarios.Nvme512(true);
        var a = StorageHealthAssessor.Assess(drive);
        Assert.Equal(HealthStatusKind.Good, a.Status);
        Assert.Contains("healthy", a.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formats_hours_with_years()
    {
        Assert.Equal("3,420 hours", StorageHealthAssessor.FormatHours(3_420));
        Assert.Contains("about 1.4 years", StorageHealthAssessor.FormatHours(12_000));
    }
}

public sealed class Windows11CompatibilityTests
{
    [Fact]
    public void Running_windows_11_is_compatible()
    {
        var a = Windows11Compatibility.Assess(Scenarios.StandardLaptop());
        Assert.Equal(HealthStatusKind.Good, a.Status);
        Assert.Contains("Already running", a.Summary);
    }

    [Fact]
    public void Windows_10_with_supported_hardware_is_good_but_processor_not_verified()
    {
        var s = Scenarios.StandardLaptop();
        s = s with { OperatingSystem = s.OperatingSystem with { Name = Spec<string>.Detected("Windows 10"), BuildNumber = 19045 } };
        var a = Windows11Compatibility.Assess(s);
        Assert.Equal(HealthStatusKind.Good, a.Status);
        Assert.Contains("not against Microsoft's official list", a.Summary);
    }

    [Fact]
    public void Old_processor_fails()
    {
        var s = Scenarios.StandardLaptop();
        s = s with
        {
            OperatingSystem = s.OperatingSystem with { Name = Spec<string>.Detected("Windows 10"), BuildNumber = 19045 },
            Processor = s.Processor with { Name = Spec<string>.Detected("Intel Core i7-7700HQ") },
        };
        var a = Windows11Compatibility.Assess(s);
        Assert.Equal(HealthStatusKind.Issue, a.Status);
    }

    [Fact]
    public void Missing_tpm_fails_and_tpm_alone_never_passes()
    {
        var s = Scenarios.StandardLaptop();
        s = s with
        {
            OperatingSystem = s.OperatingSystem with { Name = Spec<string>.Detected("Windows 10"), BuildNumber = 19045, TpmVersion = Spec<string>.NotApplicable("No TPM was found.") },
        };
        Assert.Equal(HealthStatusKind.Issue, Windows11Compatibility.Assess(s).Status);

        var unknownCpu = Scenarios.StandardLaptop();
        unknownCpu = unknownCpu with
        {
            OperatingSystem = unknownCpu.OperatingSystem with { Name = Spec<string>.Detected("Windows 10"), BuildNumber = 19045 },
            Processor = unknownCpu.Processor with { Name = Spec<string>.Detected("Mystery CPU 9000") },
        };
        Assert.Equal(HealthStatusKind.Attention, Windows11Compatibility.Assess(unknownCpu).Status);
    }

    [Theory]
    [InlineData("Intel Core i5-8250U", HealthStatusKind.Good)]
    [InlineData("Intel Core i7-1260P (12th Gen)", HealthStatusKind.Good)]
    [InlineData("Intel Core i5-7200U", HealthStatusKind.Issue)]
    [InlineData("AMD Ryzen 5 2500U", HealthStatusKind.Good)]
    [InlineData("AMD Ryzen 7 1700", HealthStatusKind.Issue)]
    [InlineData("Intel Core Ultra 7 155H", HealthStatusKind.Good)]
    [InlineData("Snapdragon X Elite", HealthStatusKind.Good)]
    [InlineData("Intel Celeron N4020", HealthStatusKind.Attention)]
    [InlineData("Intel Core 2 Duo E8400", HealthStatusKind.Issue)]
    public void Processor_generation_heuristic(string name, HealthStatusKind expected)
    {
        Assert.Equal(expected, Windows11Compatibility.AssessProcessorGeneration(name).Status);
    }
}

public sealed class HealthDashboardBuilderTests
{
    [Fact]
    public void Unavailable_values_are_never_shown_as_passing()
    {
        var items = HealthDashboardBuilder.Build(Scenarios.UnavailableBatteryHealth(), Array.Empty<HardwareTestResult>());
        var battery = items.Single(i => i.Title == "Reported battery health");
        Assert.Equal(HealthStatusKind.Unknown, battery.Status);
        Assert.Equal("Unavailable", battery.Value);

        var tests = items.Where(i => i.Group == "Hardware tests").ToList();
        Assert.Equal(6, tests.Count);
        Assert.All(tests, t => Assert.Equal(HealthStatusKind.Unknown, t.Status));
    }

    [Fact]
    public void Desktop_shows_not_applicable_battery()
    {
        var items = HealthDashboardBuilder.Build(Scenarios.CustomDesktop(), Array.Empty<HardwareTestResult>());
        var battery = items.Single(i => i.Title == "Battery health");
        Assert.Equal(HealthStatusKind.NotApplicable, battery.Status);
        Assert.Equal("Not applicable", battery.StatusLabel);
    }

    [Fact]
    public void Seller_confirmed_tests_are_labelled()
    {
        var tests = new[]
        {
            new HardwareTestResult(HardwareTestKind.Keyboard, TestOutcome.Passed, TestSource.SellerConfirmed, Evidence: "84 of 84 keys registered"),
            new HardwareTestResult(HardwareTestKind.Speakers, TestOutcome.Failed, TestSource.SellerConfirmed, Notes: "Left speaker crackles"),
        };
        var items = HealthDashboardBuilder.Build(Scenarios.StandardLaptop(), tests);
        var keyboard = items.Single(i => i.Title == "Keyboard test");
        Assert.Equal(HealthStatusKind.Good, keyboard.Status);
        Assert.Contains("Seller-confirmed", keyboard.Detail);
        Assert.Contains("84 of 84", keyboard.Detail);
        var speakers = items.Single(i => i.Title == "Speakers test");
        Assert.Equal(HealthStatusKind.Issue, speakers.Status);
    }

    [Fact]
    public void Virtual_machine_warning_is_present()
    {
        var items = HealthDashboardBuilder.Build(Scenarios.VirtualMachine(), Array.Empty<HardwareTestResult>());
        Assert.Contains(items, i => i.Title == "Virtual machine" && i.Status == HealthStatusKind.Attention);
    }
}
