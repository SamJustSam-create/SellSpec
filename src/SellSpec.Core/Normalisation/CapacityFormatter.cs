using System.Globalization;

namespace SellSpec.Core.Normalisation;

/// <summary>
/// Formats byte counts the way buyers read them: RAM in binary gigabytes ("16 GB"), storage in the decimal
/// marketing sizes printed on the drive ("512 GB", "1 TB").
/// </summary>
public static class CapacityFormatter
{
    private const double GiB = 1024d * 1024d * 1024d;
    private const double GB = 1_000_000_000d;

    /// <summary>Formats installed memory. 17,179,869,184 bytes → "16 GB"; 8,589,934,592 → "8 GB"; 6 GiB → "6 GB".</summary>
    public static string FormatMemory(ulong bytes)
    {
        if (bytes == 0) return "0 GB";
        var gib = bytes / GiB;
        if (gib < 1)
        {
            return $"{Math.Round(bytes / (1024d * 1024d)):0} MB";
        }

        // Memory is sold in whole (or half) gigabytes; usable memory may be slightly below.
        var rounded = Math.Round(gib * 2, MidpointRounding.AwayFromZero) / 2;
        return rounded.ToString(rounded % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Formats usable memory with one decimal where it differs from a whole number: "15.8 GB".</summary>
    public static string FormatMemoryPrecise(ulong bytes)
    {
        var gib = bytes / GiB;
        return gib.ToString(gib % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>
    /// Formats a drive size as the marketing size printed on the label.
    /// 512,110,190,592 → "512 GB"; 1,000,204,886,016 → "1 TB"; 240,057,409,536 → "240 GB"; 2,000,398,934,016 → "2 TB".
    /// </summary>
    public static string FormatStorage(ulong bytes)
    {
        if (bytes == 0) return "0 GB";
        var gb = bytes / GB;
        if (gb >= 1000)
        {
            var tb = gb / 1000d;
            // Drives are marketed as 1, 2, 4, 8 TB; unusual sizes keep two decimals (1.92 TB).
            var rounded = Math.Round(tb, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - Math.Round(rounded)) < 0.03)
            {
                rounded = Math.Round(rounded);
            }
            return rounded.ToString("0.##", CultureInfo.InvariantCulture) + " TB";
        }

        if (gb >= 1)
        {
            return Math.Round(gb, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " GB";
        }

        return Math.Round(bytes / 1_000_000d).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>Formats a precise decimal size for technical views: "476.9 GB".</summary>
    public static string FormatStoragePrecise(ulong bytes)
    {
        var gb = bytes / GB;
        return gb >= 1000
            ? (gb / 1000d).ToString("0.00", CultureInfo.InvariantCulture) + " TB"
            : gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Formats dedicated graphics memory (binary, whole GB where possible): 6,442,450,944 → "6 GB".</summary>
    public static string FormatGraphicsMemory(ulong bytes)
    {
        var gib = bytes / GiB;
        if (gib >= 1)
        {
            var rounded = Math.Round(gib * 2, MidpointRounding.AwayFromZero) / 2;
            return rounded.ToString(rounded % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " GB";
        }
        return $"{Math.Round(bytes / (1024d * 1024d)):0} MB";
    }

    /// <summary>Adds together all listing-relevant drives into a single phrase: "512 GB NVMe SSD + 1 TB HDD".</summary>
    public static string Join(IEnumerable<string> parts) => string.Join(" + ", parts);
}
