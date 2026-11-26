namespace SellSpec.Core.Models;

public sealed record Windows11Check(string Requirement, HealthStatusKind Status, string Detail);

public sealed record Windows11Assessment
{
    /// <summary>Overall text such as "Already running Windows 11" or "Meets the checked requirements (processor not verified)".</summary>
    public string Summary { get; init; } = string.Empty;
    public HealthStatusKind Status { get; init; } = HealthStatusKind.Unknown;
    public IReadOnlyList<Windows11Check> Checks { get; init; } = Array.Empty<Windows11Check>();
}

public sealed record OperatingSystemInfo
{
    /// <summary>"Windows 11" / "Windows 10".</summary>
    public Spec<string> Name { get; init; } = Spec<string>.Unavailable();

    /// <summary>"Home", "Pro", "Education"...</summary>
    public Spec<string> Edition { get; init; } = Spec<string>.Unavailable();

    /// <summary>Feature update name such as "24H2".</summary>
    public Spec<string> Version { get; init; } = Spec<string>.Unavailable();

    /// <summary>"26100.4061" style build.revision.</summary>
    public Spec<string> Build { get; init; } = Spec<string>.Unavailable();
    public int? BuildNumber { get; init; }
    public Spec<string> Architecture { get; init; } = Spec<string>.Unavailable();
    public Spec<DateOnly> InstallDate { get; init; } = Spec<DateOnly>.Unavailable();

    public Spec<ActivationStatus> Activation { get; init; } = Spec<ActivationStatus>.Unavailable();

    /// <summary>"OEM", "Retail", "Volume" where detectable. Never includes product keys.</summary>
    public Spec<string> LicenceChannel { get; init; } = Spec<string>.Unavailable();

    public Spec<string> TpmVersion { get; init; } = Spec<string>.Unavailable();
    public Spec<bool> SecureBootEnabled { get; init; } = Spec<bool>.Unavailable();
    public Spec<string> FirmwareType { get; init; } = Spec<string>.Unavailable();

    public Windows11Assessment? Windows11Compatibility { get; init; }

    public string FullName
    {
        get
        {
            if (!Name.HasValue) return "Windows";
            return Edition.HasValue ? $"{Name.Value} {Edition.Value}" : Name.Value!;
        }
    }

    public static OperatingSystemInfo Unavailable(string? note = null) => new() { Name = Spec<string>.Unavailable(note) };
}
