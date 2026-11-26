using System.Globalization;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;

namespace SellSpec.Core.Listing;

/// <summary>
/// Buyer-facing one-liners derived from a snapshot. Shared by the listing generator, reports and the overview
/// cards so that every surface describes the device the same way. Returns null whenever a value is unavailable:
/// callers must omit the line rather than invent one.
/// </summary>
public static class SpecSummary
{
    public static string? Processor(DeviceSnapshot s, bool detailed)
    {
        var cpu = s.Processor;
        if (!cpu.Name.HasValue) return null;
        var text = cpu.Name.Value!;
        if (cpu.InstalledCount.HasValue && cpu.InstalledCount.Value > 1)
        {
            text = $"{cpu.InstalledCount.Value}× {text}";
        }
        if (detailed && cpu.PhysicalCores.HasValue)
        {
            text += cpu.LogicalProcessors.HasValue && cpu.LogicalProcessors.Value != cpu.PhysicalCores.Value
                ? $" ({cpu.PhysicalCores.Value} cores / {cpu.LogicalProcessors.Value} threads)"
                : $" ({cpu.PhysicalCores.Value} cores)";
        }
        return text;
    }

    /// <summary>"Intel Core i5" for titles; falls back to the full name.</summary>
    public static string? ProcessorShort(DeviceSnapshot s) =>
        s.Processor.FamilyShortName ?? (s.Processor.Name.HasValue ? s.Processor.Name.Value : null);

    public static string? Memory(DeviceSnapshot s, bool includeTechnology = true)
    {
        var m = s.Memory;
        var size = m.InstalledBytes.HasValue ? m.InstalledBytes : m.UsableBytes;
        if (!size.HasValue) return null;
        var text = CapacityFormatter.FormatMemory(size.Value);
        if (includeTechnology && m.Technology.HasValue && !m.Technology.Value!.Contains("inferred", StringComparison.OrdinalIgnoreCase))
        {
            text += $" {m.Technology.Value}";
        }
        return text + " RAM";
    }

    /// <summary>"16GB" for compact titles.</summary>
    public static string? MemoryCompact(DeviceSnapshot s)
    {
        var size = s.Memory.InstalledBytes.HasValue ? s.Memory.InstalledBytes : s.Memory.UsableBytes;
        return size.HasValue ? CapacityFormatter.FormatMemory(size.Value).Replace(" ", string.Empty) : null;
    }

    public static IReadOnlyList<string> StorageLines(DeviceSnapshot s)
    {
        return s.ListingStorage
            .Where(d => d.CapacityBytes.HasValue)
            .OrderByDescending(d => d.IsBootDrive)
            .ThenByDescending(d => d.CapacityBytes.Value)
            .Select(d => $"{CapacityFormatter.FormatStorage(d.CapacityBytes.Value)} {d.ShortTypeLabel}")
            .ToList();
    }

    public static string? Storage(DeviceSnapshot s)
    {
        var lines = StorageLines(s);
        return lines.Count == 0 ? null : CapacityFormatter.Join(lines);
    }

    /// <summary>"512GB SSD", "4TB + 1TB SSD" or "512GB SSD + 1TB HDD" for compact titles.</summary>
    public static string? StorageCompact(DeviceSnapshot s)
    {
        var drives = s.ListingStorage.Where(d => d.CapacityBytes.HasValue).OrderByDescending(d => d.IsBootDrive).ThenByDescending(d => d.CapacityBytes.Value).ToList();
        if (drives.Count == 0) return null;

        static string Type(StorageDevice d) => d.Media switch
        {
            StorageMediaType.Ssd => "SSD",
            StorageMediaType.Hdd => "HDD",
            _ => "storage",
        };
        static string Size(StorageDevice d) => CapacityFormatter.FormatStorage(d.CapacityBytes.Value).Replace(" ", string.Empty);

        if (drives.Count == 1) return $"{Size(drives[0])} {Type(drives[0])}";
        if (drives.Count == 2)
        {
            return Type(drives[0]) == Type(drives[1])
                ? $"{Size(drives[0])} + {Size(drives[1])} {Type(drives[0])}"
                : $"{Size(drives[0])} {Type(drives[0])} + {Size(drives[1])} {Type(drives[1])}";
        }
        return $"{Size(drives[0])} {Type(drives[0])} + {drives.Count - 1} more drives";
    }

    public static IReadOnlyList<string> GraphicsLines(DeviceSnapshot s)
    {
        var list = new List<string>();
        foreach (var g in s.ListingGraphics.OrderByDescending(g => g.Kind == GraphicsKind.Dedicated))
        {
            var text = g.Name;
            if (g.Kind == GraphicsKind.Dedicated && g.DedicatedMemoryBytes.HasValue)
            {
                text += $" ({CapacityFormatter.FormatGraphicsMemory(g.DedicatedMemoryBytes.Value)})";
            }
            else if (g.Kind == GraphicsKind.Integrated)
            {
                text += " (integrated)";
            }
            list.Add(text);
        }
        return list;
    }

    public static string? Graphics(DeviceSnapshot s)
    {
        var lines = GraphicsLines(s);
        return lines.Count == 0 ? null : string.Join(" + ", lines);
    }

    /// <summary>Short dedicated GPU name for titles: "RTX 3060".</summary>
    public static string? DedicatedGraphicsCompact(DeviceSnapshot s)
    {
        var gpu = s.ListingGraphics.FirstOrDefault(g => g.Kind == GraphicsKind.Dedicated);
        if (gpu is null) return null;
        var name = gpu.Name;
        foreach (var prefix in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon ", "AMD ", "Intel Arc ", "Intel " })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }
        name = name.Replace(" Laptop GPU", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" Graphics", string.Empty, StringComparison.OrdinalIgnoreCase);
        return name.Trim();
    }

    public static string? Display(DeviceSnapshot s)
    {
        var d = s.PrimaryDisplay;
        if (d is null) return null;
        if (!d.IsInternal && s.Identity.FormFactor.HasValue && s.Identity.FormFactor.Value is DeviceFormFactor.Desktop or DeviceFormFactor.MiniPc)
        {
            return null; // External monitors are not part of the computer being sold.
        }

        var parts = new List<string>();
        if (d.DiagonalInches.HasValue) parts.Add(DisplayNaming.FormatDiagonal(d.DiagonalInches.Value));
        if (d.NativeResolution.HasValue)
        {
            var res = d.NativeResolution.Value;
            parts.Add(d.ResolutionMarketingName is { } mk ? $"{mk} ({res})" : res.ToString());
        }
        if (d.MaxRefreshRateHz.HasValue && d.MaxRefreshRateHz.Value >= 90)
        {
            parts.Add($"{Math.Round(d.MaxRefreshRateHz.Value)} Hz");
        }
        if (d.Touch is { HasValue: true, Value: true }) parts.Add("touchscreen");
        if (parts.Count == 0) return null;
        return string.Join(" ", parts.Take(2)) + (parts.Count > 2 ? ", " + string.Join(", ", parts.Skip(2)) : string.Empty);
    }

    public static string? OperatingSystem(DeviceSnapshot s, bool includeActivation)
    {
        var os = s.OperatingSystem;
        if (!os.Name.HasValue) return null;
        var text = os.FullName;
        if (includeActivation && os.Activation is { HasValue: true, Value: ActivationStatus.Licensed })
        {
            text += " (activated)";
        }
        return text;
    }

    public static string? BatteryHealth(DeviceSnapshot s)
    {
        var b = s.Battery;
        if (b.Presence != BatteryPresence.Present || !b.HealthPercent.HasValue) return null;
        return $"{b.HealthPercent.Value}% of original capacity (software-reported estimate)";
    }

    public static string? Connectivity(DeviceSnapshot s)
    {
        var c = s.Connectivity;
        var features = new List<string>();
        if (c.WifiStandard.HasValue) features.Add(c.WifiStandard.Value!.Split(" (")[0]);
        else if (c.WifiAdapter.HasValue) features.Add("Wi-Fi");
        if (c.Bluetooth is { HasValue: true, Value: true }) features.Add("Bluetooth");
        if (c.Ethernet is { HasValue: true, Value: true }) features.Add("Ethernet");
        if (c.Webcam is { HasValue: true, Value: true }) features.Add("webcam");
        if (c.FingerprintReader is { HasValue: true, Value: true }) features.Add("fingerprint reader");
        return features.Count == 0 ? null : string.Join(", ", features);
    }

    public static string DeviceTypeNoun(DeviceSnapshot s)
    {
        var ff = s.Identity.FormFactor.ValueOr(DeviceFormFactor.Unknown);
        return ff switch
        {
            DeviceFormFactor.Laptop => "laptop",
            DeviceFormFactor.Desktop => "desktop PC",
            DeviceFormFactor.AllInOne => "all-in-one PC",
            DeviceFormFactor.Tablet => "Windows tablet",
            DeviceFormFactor.Convertible => "2-in-1 laptop",
            DeviceFormFactor.MiniPc => "mini PC",
            DeviceFormFactor.Server => "server",
            _ => "computer",
        };
    }

    public static string FormatPrice(decimal price, string currencySymbol)
    {
        var text = price % 1 == 0 ? price.ToString("N0", CultureInfo.InvariantCulture) : price.ToString("N2", CultureInfo.InvariantCulture);
        return currencySymbol + text;
    }
}
