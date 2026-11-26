namespace SellSpec.Core.Models;

public sealed record BatteryPack
{
    public string? Name { get; init; }
    public string? Manufacturer { get; init; }
    public Spec<int> DesignCapacityMwh { get; init; } = Spec<int>.Unavailable();
    public Spec<int> FullChargeCapacityMwh { get; init; } = Spec<int>.Unavailable();
    public Spec<int> CycleCount { get; init; } = Spec<int>.Unavailable();
    public Spec<string> Chemistry { get; init; } = Spec<string>.Unavailable();
}

public sealed record BatteryInfo
{
    public BatteryPresence Presence { get; init; } = BatteryPresence.Unknown;
    public IReadOnlyList<BatteryPack> Packs { get; init; } = Array.Empty<BatteryPack>();

    /// <summary>Combined design capacity across packs (mWh).</summary>
    public Spec<int> DesignCapacityMwh { get; init; } = Spec<int>.Unavailable();
    public Spec<int> FullChargeCapacityMwh { get; init; } = Spec<int>.Unavailable();

    /// <summary>Full charge capacity / design capacity × 100, validated. A reported capacity estimate, not a runtime guarantee.</summary>
    public Spec<int> HealthPercent { get; init; } = Spec<int>.Unavailable();
    public Spec<int> CycleCount { get; init; } = Spec<int>.Unavailable();
    public Spec<int> ChargePercent { get; init; } = Spec<int>.Unavailable();
    public Spec<string> ChargingStatus { get; init; } = Spec<string>.Unavailable();
    public Spec<string> Chemistry { get; init; } = Spec<string>.Unavailable();

    /// <summary>Windows-reported condition string (e.g. "OK") where available.</summary>
    public Spec<string> ReportedCondition { get; init; } = Spec<string>.Unavailable();

    public HealthStatusKind HealthStatus { get; init; } = HealthStatusKind.Unknown;

    public static BatteryInfo NotApplicable(string note = "No battery detected. This is normal for desktop computers.") => new()
    {
        Presence = BatteryPresence.NotPresent,
        DesignCapacityMwh = Spec<int>.NotApplicable(note),
        FullChargeCapacityMwh = Spec<int>.NotApplicable(note),
        HealthPercent = Spec<int>.NotApplicable(note),
        CycleCount = Spec<int>.NotApplicable(note),
        ChargePercent = Spec<int>.NotApplicable(note),
        ChargingStatus = Spec<string>.NotApplicable(note),
        Chemistry = Spec<string>.NotApplicable(note),
        ReportedCondition = Spec<string>.NotApplicable(note),
        HealthStatus = HealthStatusKind.NotApplicable,
    };

    public static BatteryInfo Unavailable(string? note = null) => new()
    {
        Presence = BatteryPresence.Unknown,
        HealthPercent = Spec<int>.Unavailable(note),
        HealthStatus = HealthStatusKind.Unknown,
    };
}
