using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed class WindowsMemoryProvider : IMemoryProvider
{
    private readonly IScanLogger _logger;

    public WindowsMemoryProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Memory";

    public async Task<MemoryInfo> DetectAsync(CancellationToken cancellationToken)
    {
        var modulesTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2,
            "SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, MemoryType, FormFactor, DeviceLocator, BankLabel, Manufacturer, PartNumber FROM Win32_PhysicalMemory",
            cancellationToken, _logger, ComponentName);
        var arrayTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT MemoryDevices, MaxCapacity, MaxCapacityEx, Use FROM Win32_PhysicalMemoryArray", cancellationToken, _logger, ComponentName);
        var systemTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", cancellationToken, _logger, ComponentName);
        var osTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem", cancellationToken, _logger, ComponentName);

        await Task.WhenAll(modulesTask, arrayTask, systemTask, osTask).ConfigureAwait(false);

        var modules = modulesTask.Result
            .Select(r => new MemoryModule
            {
                CapacityBytes = r.GetUInt64("Capacity") ?? 0,
                Technology = MemoryTypeMapper.FromSmbiosType(r.GetInt32("SMBIOSMemoryType")) ?? MemoryTypeMapper.FromLegacyType(r.GetInt32("MemoryType")),
                SpeedMT = r.GetInt32("Speed") is > 0 and var sp ? sp : null,
                ConfiguredSpeedMT = r.GetInt32("ConfiguredClockSpeed") is > 0 and var cs ? cs : null,
                FormFactor = MemoryTypeMapper.FormFactor(r.GetInt32("FormFactor")),
                Slot = GenericValueDetector.Clean(r.GetString("DeviceLocator")),
                BankLabel = GenericValueDetector.Clean(r.GetString("BankLabel")),
                Manufacturer = CleanModuleManufacturer(r.GetString("Manufacturer")),
                PartNumber = GenericValueDetector.Clean(r.GetString("PartNumber")),
            })
            .Where(m => m.CapacityBytes > 0)
            .ToList();

        var totalPhysical = systemTask.Result.FirstOrDefault()?.GetUInt64("TotalPhysicalMemory");
        var visibleKb = osTask.Result.FirstOrDefault()?.GetUInt64("TotalVisibleMemorySize");
        var usable = totalPhysical ?? (visibleKb is { } kb ? kb * 1024 : null);

        Spec<ulong> installed;
        if (modules.Count > 0)
        {
            installed = Spec<ulong>.Firmware(modules.Aggregate(0UL, (acc, m) => acc + m.CapacityBytes));
        }
        else if (usable is { } u)
        {
            installed = Spec<ulong>.Detected(RoundToModuleSize(u), "Module details were not available; total memory is as reported by Windows.");
        }
        else
        {
            installed = Spec<ulong>.Unavailable("Installed memory could not be read.");
        }

        var technologies = modules.Select(m => m.Technology).Where(t => t is not null).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key!).ToList();
        var configuredSpeed = modules.Select(m => m.ConfiguredSpeedMT ?? m.SpeedMT).Where(s => s is not null).Max();

        Spec<string> technology;
        if (technologies.Count > 0)
        {
            technology = Spec<string>.Firmware(string.Join(" / ", technologies));
        }
        else if (MemoryTypeMapper.InferFromSpeed(configuredSpeed) is { } inferred)
        {
            technology = Spec<string>.Estimated(inferred, "The firmware did not report a memory type; inferred from the configured speed.");
        }
        else
        {
            technology = Spec<string>.Unavailable("The firmware did not report a memory type.");
        }

        var arrays = arrayTask.Result.Where(a => a.GetInt32("Use") is null or 3).ToList();
        var slots = arrays.Sum(a => a.GetInt32("MemoryDevices") ?? 0);
        ulong maxBytes = 0;
        foreach (var a in arrays)
        {
            var ex = a.GetUInt64("MaxCapacityEx");
            var legacy = a.GetUInt64("MaxCapacity");
            var maxKb = ex is > 0 ? ex.Value : legacy ?? 0;
            maxBytes += maxKb * 1024;
        }

        var hints = new List<string>();
        if (slots > 0)
        {
            hints.Add($"Firmware reports {slots} memory slot{(slots == 1 ? string.Empty : "s")} with {modules.Count} module{(modules.Count == 1 ? string.Empty : "s")} detected.");
        }
        var formFactors = modules.Select(m => m.FormFactor).Where(f => f is not null).Distinct().ToList();
        if (technologies.Any(MemoryTypeMapper.IsLowPowerType))
        {
            hints.Add("LPDDR memory is normally soldered to the mainboard and cannot be upgraded.");
        }
        else if (formFactors.Count > 0)
        {
            hints.Add($"Modules report the {string.Join("/", formFactors)} form factor. Socketed modules are often replaceable, but this has not been verified for this model.");
        }
        hints.Add("Check the manufacturer's documentation before advertising memory as upgradeable.");

        return new MemoryInfo
        {
            InstalledBytes = installed,
            UsableBytes = usable is { } us ? Spec<ulong>.Detected(us) : Spec<ulong>.Unavailable(),
            Technology = technology,
            ConfiguredSpeedMT = configuredSpeed is { } speed ? Spec<int>.Firmware(speed) : Spec<int>.Unavailable(),
            Modules = modules,
            ReportedSlots = slots > 0 ? Spec<int>.Firmware(slots, "A slot count does not prove the memory is physically replaceable.") : Spec<int>.Unavailable(),
            ReportedMaxBytes = maxBytes > 0 ? Spec<ulong>.Firmware(maxBytes, "Firmware-reported maximum; frequently inaccurate.") : Spec<ulong>.Unavailable(),
            Upgradeability = Spec<string>.Estimated("Not verified", string.Join(" ", hints)),
        };
    }

    private static string? CleanModuleManufacturer(string? raw)
    {
        var cleaned = GenericValueDetector.Clean(raw);
        if (cleaned is null) return null;
        // JEDEC bank/ID codes such as "80AD" or "0000" are not useful to a buyer.
        if (cleaned.Length <= 4 && cleaned.All(Uri.IsHexDigit)) return null;
        return ManufacturerNormaliser.Normalise(cleaned);
    }

    /// <summary>Windows reports total memory minus reserved regions (e.g. 16,968,011,776). Round up to the nearest half gigabyte.</summary>
    internal static ulong RoundToModuleSize(ulong usableBytes)
    {
        const ulong halfGiB = 512UL * 1024 * 1024;
        var remainder = usableBytes % halfGiB;
        return remainder == 0 ? usableBytes : usableBytes - remainder + halfGiB;
    }
}
