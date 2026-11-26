namespace SellSpec.Core.Normalisation;

/// <summary>Maps SMBIOS memory type and form factor codes to names.</summary>
public static class MemoryTypeMapper
{
    /// <summary>SMBIOS 3.x "Memory Device — Type" codes (Win32_PhysicalMemory.SMBIOSMemoryType).</summary>
    public static string? FromSmbiosType(int? code) => code switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x1F => "Logical non-volatile",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        _ => null,
    };

    /// <summary>Legacy Win32_PhysicalMemory.MemoryType codes (often 0 on modern systems).</summary>
    public static string? FromLegacyType(int? code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    public static string? FormFactor(int? code) => code switch
    {
        8 => "DIMM",
        9 => "TSOP",
        11 => "RIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        14 => "SMD",
        15 => "SSMP",
        16 => "QFP",
        17 => "TQFP",
        18 => "SOIC",
        19 => "LCC",
        20 => "PLCC",
        21 => "BGA",
        22 => "FPBGA",
        23 => "LGA",
        24 => "FB-DIMM",
        25 => "Die",
        26 => "CAMM",
        _ => null,
    };

    /// <summary>LPDDR is normally soldered to the board. Used only to add a cautionary note, never to assert replaceability.</summary>
    public static bool IsLowPowerType(string? technology) =>
        technology is not null && technology.StartsWith("LPDDR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Guess technology from a configured speed when SMBIOS type is missing. Returns null when ambiguous.</summary>
    public static string? InferFromSpeed(int? speedMT) => speedMT switch
    {
        null or 0 => null,
        >= 4800 => "DDR5 (inferred from speed)",
        >= 2133 and <= 3600 => "DDR4 (inferred from speed)",
        >= 1066 and < 2133 => "DDR3 (inferred from speed)",
        _ => null,
    };
}
