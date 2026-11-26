using System.Globalization;
using SellSpec.Core.Models;

namespace SellSpec.Core.Normalisation;

public static class DisplayNaming
{
    /// <summary>
    /// Panel sizes common enough to snap to. EDID centimetre rounding gives roughly ±0.25" of uncertainty, so
    /// near-duplicates (13.4 vs 13.3, 15.3 vs 15.6, 17.0 vs 17.3) are deliberately left out in favour of the
    /// far more common size; the value is labelled as an estimate everywhere it is shown.
    /// </summary>
    private static readonly double[] CommonDiagonals =
    {
        10.1, 11.6, 12.3, 12.5, 13.3, 13.5, 14.0, 15.6, 16.0, 17.3, 18.4, 19.5, 21.5, 22.0, 23.8, 24.0, 27.0, 28.0, 31.5, 32.0,
        34.0, 38.0, 43.0, 49.0, 55.0, 65.0,
    };

    /// <summary>
    /// EDID stores the panel size in whole centimetres, so the computed diagonal is approximate. Snap to a
    /// common panel size when the error is within rounding tolerance; otherwise keep one decimal.
    /// </summary>
    public static double EstimateDiagonalInches(PhysicalSize size)
    {
        var raw = size.DiagonalInches;
        var tolerance = Math.Max(0.35, raw * 0.02);
        var best = CommonDiagonals.MinBy(d => Math.Abs(d - raw));
        if (Math.Abs(best - raw) <= tolerance)
        {
            return best;
        }
        return Math.Round(raw, 1);
    }

    public static string FormatDiagonal(double inches) =>
        inches.ToString(inches % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + "-inch";

    public static string? MarketingName(Resolution r) => (r.Width, r.Height) switch
    {
        (1366, 768) => "HD",
        (1280, 720) => "HD",
        (1600, 900) => "HD+",
        (1920, 1080) => "Full HD",
        (1920, 1200) => "Full HD+ (16:10)",
        (2160, 1440) => "2K (3:2)",
        (2256, 1504) => "2.2K (3:2)",
        (2240, 1400) => "2.2K",
        (2496, 1664) => "2.5K (3:2)",
        (2560, 1440) => "QHD (1440p)",
        (2560, 1600) => "WQXGA (2.5K)",
        (2560, 1080) => "Ultrawide Full HD",
        (2880, 1620) => "2.8K",
        (2880, 1800) => "2.8K (16:10)",
        (2880, 1920) => "2.8K (3:2)",
        (3000, 2000) => "3K (3:2)",
        (3072, 1920) => "3K",
        (3200, 2000) => "3.2K",
        (3440, 1440) => "Ultrawide QHD",
        (3456, 2160) => "3.5K",
        (3840, 2160) => "4K UHD",
        (3840, 2400) => "4K UHD+",
        (5120, 1440) => "Super ultrawide (5K2K)",
        (5120, 2880) => "5K",
        (7680, 4320) => "8K",
        _ => null,
    };

    /// <summary>Decodes the three-letter PnP vendor id from EDID into a readable name.</summary>
    public static string? VendorName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        return c switch
        {
            "AUO" => "AU Optronics",
            "BOE" => "BOE",
            "LGD" => "LG Display",
            "LEN" => "Lenovo",
            "SDC" => "Samsung Display",
            "SAM" => "Samsung",
            "SEC" => "Samsung",
            "CMN" => "Innolux (Chi Mei)",
            "CMO" => "Chi Mei",
            "SHP" => "Sharp",
            "NCP" => "Najing CEC Panda",
            "IVO" => "InfoVision",
            "CSO" => "CSOT",
            "CSW" => "China Star",
            "TMX" => "Tianma",
            "DEL" => "Dell",
            "HPN" => "HP",
            "HWP" => "HP",
            "ACR" => "Acer",
            "ACI" => "ASUS",
            "AUS" => "ASUS",
            "BNQ" => "BenQ",
            "GSM" => "LG",
            "PHL" => "Philips",
            "VSC" => "ViewSonic",
            "AOC" => "AOC",
            "MSI" => "MSI",
            "GBT" => "Gigabyte",
            "IVM" => "Iiyama",
            "EIZ" => "Eizo",
            "NEC" => "NEC",
            "SNY" => "Sony",
            "APP" => "Apple",
            "MEI" => "Panasonic",
            "HSD" => "HannStar",
            "PNR" => "Planar",
            "MSH" => "Microsoft",
            _ => null,
        };
    }

    public static string? ConnectionLabel(uint videoOutputTechnology) => videoOutputTechnology switch
    {
        0x80000000 => "Internal",
        0 => "VGA",
        1 => "S-Video",
        2 => "Composite",
        3 => "Component",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "Embedded DisplayPort",
        12 => "UDI",
        13 => "Embedded UDI",
        14 => "SDTV dongle",
        15 => "Miracast",
        16 => "Indirect wired",
        17 => "Indirect virtual",
        18 => "DisplayPort over USB",
        _ => null,
    };
}
