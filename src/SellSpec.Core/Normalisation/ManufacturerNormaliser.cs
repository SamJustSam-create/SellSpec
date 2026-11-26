namespace SellSpec.Core.Normalisation;

/// <summary>Maps the many ways firmware spells a brand to the name a buyer expects to read.</summary>
public static class ManufacturerNormaliser
{
    private static readonly (string Match, string Brand)[] Prefixes =
    {
        ("lenovo", "Lenovo"),
        ("hewlett-packard", "HP"),
        ("hewlett packard", "HP"),
        ("hp inc", "HP"),
        ("hp", "HP"),
        ("dell", "Dell"),
        ("alienware", "Alienware"),
        ("asustek", "ASUS"),
        ("asus", "ASUS"),
        ("acer", "Acer"),
        ("gateway", "Gateway"),
        ("packard bell", "Packard Bell"),
        ("micro-star", "MSI"),
        ("msi", "MSI"),
        ("gigabyte", "Gigabyte"),
        ("asrock", "ASRock"),
        ("biostar", "Biostar"),
        ("evga", "EVGA"),
        ("nzxt", "NZXT"),
        ("supermicro", "Supermicro"),
        ("super micro", "Supermicro"),
        ("intel corporation", "Intel"),
        ("intel", "Intel"),
        ("microsoft corporation", "Microsoft"),
        ("microsoft", "Microsoft"),
        ("samsung", "Samsung"),
        ("lg electronics", "LG"),
        ("toshiba", "Toshiba"),
        ("dynabook", "Dynabook"),
        ("fujitsu", "Fujitsu"),
        ("sony", "Sony"),
        ("vaio", "VAIO"),
        ("panasonic", "Panasonic"),
        ("razer", "Razer"),
        ("huawei", "Huawei"),
        ("honor", "Honor"),
        ("xiaomi", "Xiaomi"),
        ("timi", "Xiaomi"),
        ("google", "Google"),
        ("framework", "Framework"),
        ("system76", "System76"),
        ("tuxedo", "TUXEDO"),
        ("schenker", "Schenker"),
        ("clevo", "Clevo"),
        ("tongfang", "Tongfang"),
        ("medion", "Medion"),
        ("chuwi", "Chuwi"),
        ("beelink", "Beelink"),
        ("minisforum", "Minisforum"),
        ("apple", "Apple"),
        ("vmware", "VMware"),
        ("innotek", "Oracle VirtualBox"),
        ("qemu", "QEMU"),
        ("parallels", "Parallels"),
        ("red hat", "Red Hat"),
        ("xen", "Xen"),
        ("american megatrends", "American Megatrends"),
        ("ami", "American Megatrends"),
        ("insyde", "Insyde"),
        ("phoenix", "Phoenix"),
        ("nvidia", "NVIDIA"),
        ("advanced micro devices", "AMD"),
        ("amd", "AMD"),
        ("qualcomm", "Qualcomm"),
        ("western digital", "Western Digital"),
        ("wdc", "Western Digital"),
        ("wd", "Western Digital"),
        ("seagate", "Seagate"),
        ("st", "Seagate"),
        ("kingston", "Kingston"),
        ("crucial", "Crucial"),
        ("micron", "Micron"),
        ("sk hynix", "SK hynix"),
        ("hynix", "SK hynix"),
        ("skhynix", "SK hynix"),
        ("sandisk", "SanDisk"),
        ("toshiba memory", "Kioxia"),
        ("kioxia", "Kioxia"),
        ("kbg", "Kioxia"),
        ("corsair", "Corsair"),
        ("g.skill", "G.Skill"),
        ("gskill", "G.Skill"),
        ("team group", "TeamGroup"),
        ("teamgroup", "TeamGroup"),
        ("adata", "ADATA"),
        ("patriot", "Patriot"),
        ("pny", "PNY"),
        ("sabrent", "Sabrent"),
        ("solidigm", "Solidigm"),
        ("hitachi", "Hitachi"),
        ("hgst", "HGST"),
    };

    public static string? Normalise(string? raw)
    {
        var cleaned = GenericValueDetector.Clean(raw);
        if (cleaned is null) return null;

        var lower = cleaned.ToLowerInvariant();
        foreach (var (match, brand) in Prefixes)
        {
            if (lower == match || lower.StartsWith(match + " ", StringComparison.Ordinal) ||
                lower.StartsWith(match + ",", StringComparison.Ordinal) || lower.StartsWith(match + ".", StringComparison.Ordinal))
            {
                return brand;
            }
        }

        // Strip corporate suffixes and tidy the casing of whatever is left.
        var stripped = StripCorporateSuffixes(cleaned);
        return stripped.Length == 0 ? cleaned : stripped;
    }

    public static string StripCorporateSuffixes(string value)
    {
        var result = value;
        foreach (var suffix in new[] { " co., ltd.", " co., ltd", " co.,ltd.", " co.,ltd", " co. ltd", " ltd.", " ltd", " inc.", " inc", " corporation", " corp.", " corp", " gmbh", " limited", " computer", " computers", " technology", " technologies", " international", " electronics", " llc", " s.a.", " pty" })
        {
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^suffix.Length].TrimEnd(',', '.', ' ');
            }
        }
        return result.Trim();
    }

    /// <summary>True for vendors that primarily sell motherboards to system builders.</summary>
    public static bool IsMotherboardVendor(string? brand) =>
        brand is "ASUS" or "Gigabyte" or "MSI" or "ASRock" or "Biostar" or "EVGA" or "NZXT" or "Supermicro";

    /// <summary>Removes a leading brand name from a product string ("Dell XPS 15 9520" with brand "Dell" → "XPS 15 9520").</summary>
    public static string StripBrand(string product, string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return product;
        return product.StartsWith(brand + " ", StringComparison.OrdinalIgnoreCase) ? product[(brand.Length + 1)..].Trim() : product;
    }
}
