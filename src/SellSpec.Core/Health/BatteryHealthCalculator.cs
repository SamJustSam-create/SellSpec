using SellSpec.Core.Models;

namespace SellSpec.Core.Health;

/// <summary>
/// Battery Health (%) = Full Charge Capacity / Design Capacity × 100.
/// The result is a reported capacity estimate: it says nothing about runtime, swelling or safety.
/// </summary>
public static class BatteryHealthCalculator
{
    /// <summary>At or above this percentage the battery is shown green.</summary>
    public const int GoodThreshold = 80;

    /// <summary>At or above this percentage (and below <see cref="GoodThreshold"/>) the battery is shown amber.</summary>
    public const int AttentionThreshold = 60;

    /// <summary>Readings above this ratio are treated as firmware errors rather than real capacity.</summary>
    public const double ImplausibleRatio = 1.25;

    public sealed record Result(Spec<int> HealthPercent, HealthStatusKind Status);

    public static Result Calculate(int? designCapacityMwh, int? fullChargeCapacityMwh)
    {
        if (designCapacityMwh is null or <= 0)
        {
            return new Result(Spec<int>.Unavailable("The battery did not report its design capacity."), HealthStatusKind.Unknown);
        }

        if (fullChargeCapacityMwh is null or <= 0)
        {
            return new Result(Spec<int>.Unavailable("The battery did not report its current full-charge capacity."), HealthStatusKind.Unknown);
        }

        var ratio = (double)fullChargeCapacityMwh.Value / designCapacityMwh.Value;
        if (ratio > ImplausibleRatio)
        {
            return new Result(
                Spec<int>.Unavailable("The reported full-charge capacity is far above the design capacity, which suggests a firmware reporting error."),
                HealthStatusKind.Unknown);
        }

        string? note = null;
        var percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        if (percent > 100)
        {
            note = "The battery reports slightly more capacity than its design value; shown as 100%.";
            percent = 100;
        }

        var status = StatusFor(percent);
        return new Result(Spec<int>.Detected(percent, note), status);
    }

    public static HealthStatusKind StatusFor(int percent) => percent switch
    {
        >= GoodThreshold => HealthStatusKind.Good,
        >= AttentionThreshold => HealthStatusKind.Attention,
        _ => HealthStatusKind.Issue,
    };

    public static string Describe(int percent) => percent switch
    {
        >= 90 => "Holds close to its original capacity.",
        >= GoodThreshold => "Normal wear for a used battery.",
        >= AttentionThreshold => "Noticeably reduced capacity; shorter runtime than new.",
        _ => "Significantly degraded; a replacement may be needed soon.",
    };

    /// <summary>Combines several packs. Health is computed from the summed capacities; cycle count is the highest reported.</summary>
    public static BatteryInfo Combine(IReadOnlyList<BatteryPack> packs, Spec<int> chargePercent, Spec<string> chargingStatus, Spec<string> reportedCondition)
    {
        if (packs.Count == 0)
        {
            return BatteryInfo.Unavailable("No battery information was returned.") with
            {
                ChargePercent = chargePercent,
                ChargingStatus = chargingStatus,
            };
        }

        var designs = packs.Where(p => p.DesignCapacityMwh.HasValue).Select(p => p.DesignCapacityMwh.Value!).ToList();
        var fulls = packs.Where(p => p.FullChargeCapacityMwh.HasValue).Select(p => p.FullChargeCapacityMwh.Value!).ToList();

        Spec<int> design = designs.Count == packs.Count ? Spec<int>.Firmware(designs.Sum()) : Spec<int>.Unavailable("Not every battery reported a design capacity.");
        Spec<int> full = fulls.Count == packs.Count ? Spec<int>.Firmware(fulls.Sum()) : Spec<int>.Unavailable("Not every battery reported a full-charge capacity.");

        var health = Calculate(design.HasValue ? design.Value : null, full.HasValue ? full.Value : null);

        var cycles = packs.Where(p => p.CycleCount.HasValue).Select(p => p.CycleCount.Value!).ToList();
        Spec<int> cycleSpec;
        if (cycles.Count == 0)
        {
            cycleSpec = Spec<int>.Unavailable("This battery does not report a cycle count.");
        }
        else if (cycles.All(c => c == 0))
        {
            cycleSpec = Spec<int>.Firmware(0, "A reported count of 0 usually means the firmware does not track cycles.");
        }
        else
        {
            cycleSpec = Spec<int>.Firmware(cycles.Max(), packs.Count > 1 ? "Highest count across batteries." : null);
        }

        var chemistry = packs.Select(p => p.Chemistry).FirstOrDefault(c => c.HasValue) ?? Spec<string>.Unavailable();

        return new BatteryInfo
        {
            Presence = BatteryPresence.Present,
            Packs = packs,
            DesignCapacityMwh = design,
            FullChargeCapacityMwh = full,
            HealthPercent = health.HealthPercent,
            HealthStatus = health.Status,
            CycleCount = cycleSpec,
            ChargePercent = chargePercent,
            ChargingStatus = chargingStatus,
            Chemistry = chemistry,
            ReportedCondition = reportedCondition,
        };
    }
}
