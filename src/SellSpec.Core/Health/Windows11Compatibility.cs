using System.Text.RegularExpressions;
using SellSpec.Core.Models;

namespace SellSpec.Core.Health;

/// <summary>
/// Basic Windows 11 readiness assessment. Microsoft's processor list is not bundled, so the processor check is a
/// generation heuristic and is always labelled as not verified. TPM alone never produces a "compatible" verdict.
/// </summary>
public static partial class Windows11Compatibility
{
    private const ulong MinimumRamBytes = 4UL * 1024 * 1024 * 1024;
    private const ulong MinimumStorageBytes = 64UL * 1_000_000_000;

    public static Windows11Assessment Assess(DeviceSnapshot snapshot)
    {
        var os = snapshot.OperatingSystem;
        if (os.BuildNumber is >= 22000)
        {
            return new Windows11Assessment
            {
                Summary = "Already running Windows 11.",
                Status = HealthStatusKind.Good,
                Checks = new[] { new Windows11Check("Operating system", HealthStatusKind.Good, $"{os.FullName} is installed and running.") },
            };
        }

        var checks = new List<Windows11Check>();

        // TPM 2.0
        if (os.TpmVersion.HasValue)
        {
            var ok = os.TpmVersion.Value!.StartsWith("2", StringComparison.Ordinal);
            checks.Add(new Windows11Check("TPM 2.0", ok ? HealthStatusKind.Good : HealthStatusKind.Issue, ok ? "TPM 2.0 present." : $"TPM {os.TpmVersion.Value} found; Windows 11 requires TPM 2.0."));
        }
        else if (os.TpmVersion.IsNotApplicable || (os.TpmVersion.Note?.Contains("No TPM", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            checks.Add(new Windows11Check("TPM 2.0", HealthStatusKind.Issue, "No TPM was found. It may be disabled in firmware settings."));
        }
        else
        {
            checks.Add(new Windows11Check("TPM 2.0", HealthStatusKind.Unknown, "TPM status could not be read."));
        }

        // UEFI + Secure Boot
        if (os.FirmwareType.HasValue && os.FirmwareType.Value!.Contains("Legacy", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new Windows11Check("UEFI with Secure Boot", HealthStatusKind.Issue, "Windows is installed in legacy BIOS mode. Windows 11 requires UEFI."));
        }
        else if (os.SecureBootEnabled.HasValue)
        {
            checks.Add(new Windows11Check("UEFI with Secure Boot", HealthStatusKind.Good,
                os.SecureBootEnabled.Value ? "UEFI firmware with Secure Boot enabled." : "UEFI firmware; Secure Boot is currently off but the system is Secure Boot capable."));
        }
        else if (os.FirmwareType.HasValue)
        {
            checks.Add(new Windows11Check("UEFI with Secure Boot", HealthStatusKind.Attention, "UEFI firmware detected; Secure Boot state could not be read."));
        }
        else
        {
            checks.Add(new Windows11Check("UEFI with Secure Boot", HealthStatusKind.Unknown, "Firmware mode could not be determined."));
        }

        // RAM
        var ram = snapshot.Memory.InstalledBytes.HasValue ? snapshot.Memory.InstalledBytes : snapshot.Memory.UsableBytes;
        if (ram.HasValue)
        {
            var ok = ram.Value >= MinimumRamBytes - 512UL * 1024 * 1024;
            checks.Add(new Windows11Check("4 GB memory", ok ? HealthStatusKind.Good : HealthStatusKind.Issue, $"{Normalisation.CapacityFormatter.FormatMemory(ram.Value)} installed."));
        }
        else
        {
            checks.Add(new Windows11Check("4 GB memory", HealthStatusKind.Unknown, "Installed memory could not be read."));
        }

        // Storage
        var boot = snapshot.Storage.FirstOrDefault(s => s.IsBootDrive) ?? snapshot.Storage.FirstOrDefault(s => s.Location == StorageLocation.Internal);
        if (boot?.CapacityBytes.HasValue == true)
        {
            var ok = boot.CapacityBytes.Value >= MinimumStorageBytes;
            checks.Add(new Windows11Check("64 GB storage", ok ? HealthStatusKind.Good : HealthStatusKind.Issue, $"{Normalisation.CapacityFormatter.FormatStorage(boot.CapacityBytes.Value)} system drive."));
        }
        else
        {
            checks.Add(new Windows11Check("64 GB storage", HealthStatusKind.Unknown, "System drive capacity could not be read."));
        }

        // Processor basics
        var cpu = snapshot.Processor;
        var is64 = (cpu.Architecture.HasValue && (cpu.Architecture.Value!.Contains("64", StringComparison.Ordinal)))
                   || (os.Architecture.HasValue && os.Architecture.Value!.Contains("64", StringComparison.Ordinal));
        var coresOk = cpu.PhysicalCores.HasValue && cpu.PhysicalCores.Value >= 2;
        var clockOk = cpu.ClockSpeedMHz.HasValue && cpu.ClockSpeedMHz.Value >= 1000;
        if (cpu.PhysicalCores.HasValue || cpu.ClockSpeedMHz.HasValue)
        {
            var ok = is64 && coresOk && clockOk;
            checks.Add(new Windows11Check("1 GHz, 2+ core, 64-bit processor", ok ? HealthStatusKind.Good : HealthStatusKind.Issue,
                ok ? "Meets the basic processor requirement." : "Processor does not meet the basic requirement (64-bit, two or more cores, 1 GHz)."));
        }
        else
        {
            checks.Add(new Windows11Check("1 GHz, 2+ core, 64-bit processor", HealthStatusKind.Unknown, "Processor details could not be read."));
        }

        // Processor generation heuristic
        var generation = AssessProcessorGeneration(cpu.Name.HasValue ? cpu.Name.Value : cpu.RawName);
        checks.Add(generation);

        var status = HealthStatusKind.Good;
        if (checks.Any(c => c.Status == HealthStatusKind.Issue)) status = HealthStatusKind.Issue;
        else if (checks.Any(c => c.Status == HealthStatusKind.Unknown)) status = HealthStatusKind.Unknown;
        else if (checks.Any(c => c.Status == HealthStatusKind.Attention)) status = HealthStatusKind.Attention;

        var summary = status switch
        {
            HealthStatusKind.Good => "Meets the checked Windows 11 requirements. The processor was matched by generation, not against Microsoft's official list.",
            HealthStatusKind.Attention => "Likely compatible, but one or more checks need attention.",
            HealthStatusKind.Issue => "Does not meet one or more Windows 11 requirements.",
            _ => "Windows 11 compatibility could not be fully assessed.",
        };

        return new Windows11Assessment { Summary = summary, Status = status, Checks = checks };
    }

    /// <summary>Heuristic generation check. Returns Attention (not verified) whenever the family is unrecognised.</summary>
    public static Windows11Check AssessProcessorGeneration(string? name)
    {
        const string title = "Supported processor generation";
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Windows11Check(title, HealthStatusKind.Unknown, "Processor name not available.");
        }

        if (Regex.IsMatch(name, @"\bCore\s+Ultra\b", RegexOptions.IgnoreCase) || Regex.IsMatch(name, @"\bCore\s+[3579]\s+\d{3}", RegexOptions.IgnoreCase))
        {
            return new Windows11Check(title, HealthStatusKind.Good, "Intel Core Ultra / Core (Series 1+) processors are supported.");
        }

        var intel = IntelCoreModel().Match(name);
        if (intel.Success)
        {
            var gen = IntelGeneration(intel.Groups["model"].Value, name);
            return gen >= 8
                ? new Windows11Check(title, HealthStatusKind.Good, $"Intel Core {gen}th generation or newer is supported (not verified against Microsoft's list).")
                : new Windows11Check(title, HealthStatusKind.Issue, $"Intel Core {gen}th generation processors are not on Microsoft's supported list.");
        }

        var ryzen = RyzenModel().Match(name);
        if (ryzen.Success)
        {
            var series = int.Parse(ryzen.Groups["model"].Value[..1]);
            return series >= 2
                ? new Windows11Check(title, HealthStatusKind.Good, "AMD Ryzen 2000 series or newer is supported (not verified against Microsoft's list).")
                : new Windows11Check(title, HealthStatusKind.Issue, "First-generation AMD Ryzen (1000 series) processors are not on Microsoft's supported list.");
        }

        if (Regex.IsMatch(name, @"\bRyzen\s+AI\b|\bRyzen\s+[3579]\s+\d{3}\b", RegexOptions.IgnoreCase))
        {
            return new Windows11Check(title, HealthStatusKind.Good, "Current AMD Ryzen processors are supported (not verified against Microsoft's list).");
        }

        if (Regex.IsMatch(name, @"Snapdragon\s+(X|8cx|8c|7c)", RegexOptions.IgnoreCase))
        {
            return new Windows11Check(title, HealthStatusKind.Good, "Qualcomm Snapdragon 7c/8c/8cx/X processors are supported.");
        }

        if (Regex.IsMatch(name, @"\bIntel\b.*\bN(?:95|97|100|150|200|250|300|305|350)\b", RegexOptions.IgnoreCase))
        {
            return new Windows11Check(title, HealthStatusKind.Good, "Intel N-series (Alder Lake-N) processors are supported.");
        }

        if (Regex.IsMatch(name, @"\bCore\s*2\b|\bPentium\s+(4|D)\b|\bAthlon\s+(64|II)\b|\bPhenom\b|\bFX-\d{4}\b|\bA\d{1,2}-\d{4}\b", RegexOptions.IgnoreCase))
        {
            return new Windows11Check(title, HealthStatusKind.Issue, "This processor family predates Windows 11 support.");
        }

        return new Windows11Check(title, HealthStatusKind.Attention, "Processor generation not verified. Check Microsoft's supported processor list.");
    }

    /// <summary>
    /// Intel Core model numbers: 3 digits = 1st gen (i7-920), 4 digits starting 2–9 = that generation (i5-8250U),
    /// 4 digits starting with 1 = 10th–13th gen (i7-1165G7, i7-1260P), 5 digits = first two digits (i7-10710U, i7-14700K).
    /// An explicit "(12th Gen)" suffix from the normaliser wins when present.
    /// </summary>
    internal static int IntelGeneration(string model, string fullName)
    {
        var explicitGen = Regex.Match(fullName, @"\((?<n>\d{1,2})(?:st|nd|rd|th)\s+Gen\)", RegexOptions.IgnoreCase);
        if (explicitGen.Success) return int.Parse(explicitGen.Groups["n"].Value);
        return model.Length switch
        {
            5 => int.Parse(model[..2]),
            4 when model[0] == '1' => int.Parse(model[..2]),
            4 => int.Parse(model[..1]),
            _ => 1,
        };
    }

    [GeneratedRegex(@"\bCore\s+i[3579]-(?<model>\d{3,5})[A-Z]{0,3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoreModel();

    [GeneratedRegex(@"\bRyzen\s+(?:[3579]|Threadripper)(?:\s+PRO)?\s+(?<model>\d{4})[A-Z]{0,3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenModel();
}
