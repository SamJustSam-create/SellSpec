using System.Text.RegularExpressions;

namespace SellSpec.Core.Normalisation;

/// <summary>
/// Maps 802.11 PHY types (from the WLAN API) and adapter names to Wi-Fi generation labels.
/// PHY-based results are detected; name-based results are estimates.
/// </summary>
public static class WifiStandardInference
{
    /// <summary>DOT11_PHY_TYPE values from wlanapi.h.</summary>
    public static string? FromPhyTypes(IEnumerable<int> phyTypes)
    {
        var set = phyTypes.ToHashSet();
        if (set.Contains(11)) return "Wi-Fi 7 (802.11be)";
        if (set.Contains(10)) return "Wi-Fi 6 (802.11ax)";
        if (set.Contains(9)) return "Wi-Fi 5 (802.11ac)";
        if (set.Contains(7)) return "Wi-Fi 4 (802.11n)";
        if (set.Contains(6) || set.Contains(4)) return "802.11a/g";
        if (set.Contains(5)) return "802.11b";
        return null;
    }

    /// <summary>Best-effort inference from the adapter's marketing name. Returns null when nothing recognisable is present.</summary>
    public static string? FromAdapterName(string? adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName)) return null;
        var n = adapterName;

        if (Regex.IsMatch(n, @"Wi-?Fi\s*7|802\.11be|\bBE(?:200|201|202|211|411)\b|FastConnect\s*7800|QCNCM865|\bMT7925\b|RZ7\d{2}|\bWCN7850\b", RegexOptions.IgnoreCase))
            return "Wi-Fi 7 (802.11be)";

        if (Regex.IsMatch(n, @"Wi-?Fi\s*6E|\bAX210\b|\bAX211\b|\bAX1675\b|\bAX1690\b|\bRZ616\b|\bRZ608\b|\bMT7922\b|\bWCN6855\b|\bWCN6856\b|FastConnect\s*6900|\bQCNFA765\b", RegexOptions.IgnoreCase))
            return "Wi-Fi 6E (802.11ax, 6 GHz)";

        if (Regex.IsMatch(n, @"Wi-?Fi\s*6|802\.11ax|\bAX\d{3,4}\b|\bMT7921\b|\bRZ60\d\b|\bQCA6390\b|\bWCN685\d\b|FastConnect\s*6[2-8]00|\bRTL8852\b|\bRTL8832\b|\bRTL8851\b", RegexOptions.IgnoreCase))
            return "Wi-Fi 6 (802.11ax)";

        if (Regex.IsMatch(n, @"802\.11ac|\bAC\s*\d{3,4}\b|Wireless-AC|\bRTL882[12]\b|\bRTL8822\b|\bRTL8812\b|\bQCA61x4\b|\bQCA9377\b|\bQCA6174\b|\bBCM43\d{2}\b|\b3165\b|\b3168\b|\b7265\b|\b8260\b|\b8265\b|\b9260\b|\b9461\b|\b9462\b|\b9560\b|\bMT7612\b|\bRZ600\b", RegexOptions.IgnoreCase))
            return "Wi-Fi 5 (802.11ac)";

        if (Regex.IsMatch(n, @"802\.11n|Wireless-N|\bRTL8188\b|\bRTL8723\b|\bRTL8192\b|\bAR9485\b|\bAR9285\b|\bAR9565\b|\bQCA9565\b|\b7260\s*N\b|\b3160\b|\b1030\b|\b6205\b|\b6235\b|\bBCM4313\b", RegexOptions.IgnoreCase))
            return "Wi-Fi 4 (802.11n)";

        return null;
    }
}
