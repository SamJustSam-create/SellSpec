using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class ProcessorNameNormaliserTests
{
    [Theory]
    [InlineData("12th Gen Intel(R) Core(TM) i7-1260P", "Intel Core i7-1260P (12th Gen)", "Intel Core i7")]
    [InlineData("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz", "Intel Core i5-8250U", "Intel Core i5")]
    [InlineData("AMD Ryzen 7 5800H with Radeon Graphics", "AMD Ryzen 7 5800H", "AMD Ryzen 7")]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", "AMD Ryzen 5 5600X", "AMD Ryzen 5")]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", "AMD Ryzen 9 7950X", "AMD Ryzen 9")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", "Intel Core Ultra 7 155H", "Intel Core Ultra 7")]
    [InlineData("13th Gen Intel(R) Core(TM) i5-1335U", "Intel Core i5-1335U (13th Gen)", "Intel Core i5")]
    [InlineData("Intel(R) Celeron(R) N4020 CPU @ 1.10GHz", "Intel Celeron N4020", "Intel Celeron")]
    [InlineData("Snapdragon(R) X Elite - X1E78100 - Qualcomm(R) Oryon(TM) CPU", "Snapdragon X Elite - X1E78100 - Qualcomm Oryon", "Snapdragon X Elite")]
    [InlineData("Intel(R) Core(TM) 7 150U", "Intel Core 7 150U", "Intel Core 7")]
    [InlineData("AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "AMD Ryzen AI 9")]
    public void Normalises_firmware_names(string raw, string expectedName, string expectedFamily)
    {
        var result = ProcessorNameNormaliser.Normalise(raw);
        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedFamily, result.FamilyShortName);
    }

    [Fact]
    public void Missing_name_is_reported_not_invented()
    {
        var result = ProcessorNameNormaliser.Normalise("   ");
        Assert.Equal("Unknown processor", result.Name);
        Assert.Null(result.FamilyShortName);
    }

    [Fact]
    public void Generation_is_extracted()
    {
        Assert.Equal(12, ProcessorNameNormaliser.Normalise("12th Gen Intel(R) Core(TM) i7-1260P").Generation);
        Assert.Null(ProcessorNameNormaliser.Normalise("AMD Ryzen 7 5800H").Generation);
    }
}

public sealed class CapacityFormatterTests
{
    [Theory]
    [InlineData(17_179_869_184UL, "16 GB")]
    [InlineData(8_589_934_592UL, "8 GB")]
    [InlineData(16_968_011_776UL, "16 GB")]
    [InlineData(6_442_450_944UL, "6 GB")]
    [InlineData(4_294_967_296UL, "4 GB")]
    [InlineData(34_359_738_368UL, "32 GB")]
    [InlineData(536_870_912UL, "512 MB")]
    public void Memory_sizes(ulong bytes, string expected) => Assert.Equal(expected, CapacityFormatter.FormatMemory(bytes));

    [Theory]
    [InlineData(512_110_190_592UL, "512 GB")]
    [InlineData(500_107_862_016UL, "500 GB")]
    [InlineData(256_060_514_304UL, "256 GB")]
    [InlineData(240_057_409_536UL, "240 GB")]
    [InlineData(128_035_676_160UL, "128 GB")]
    [InlineData(1_000_204_886_016UL, "1 TB")]
    [InlineData(2_000_398_934_016UL, "2 TB")]
    [InlineData(1_920_383_410_176UL, "1.92 TB")]
    [InlineData(4_000_787_030_016UL, "4 TB")]
    [InlineData(32_000_000_000UL, "32 GB")]
    public void Storage_sizes(ulong bytes, string expected) => Assert.Equal(expected, CapacityFormatter.FormatStorage(bytes));

    [Fact]
    public void Graphics_memory_sizes()
    {
        Assert.Equal("6 GB", CapacityFormatter.FormatGraphicsMemory(6_442_450_944));
        Assert.Equal("8 GB", CapacityFormatter.FormatGraphicsMemory(8_589_934_592));
        Assert.Equal("512 MB", CapacityFormatter.FormatGraphicsMemory(536_870_912));
    }
}

public sealed class ManufacturerNormaliserTests
{
    [Theory]
    [InlineData("LENOVO", "Lenovo")]
    [InlineData("Hewlett-Packard", "HP")]
    [InlineData("HP", "HP")]
    [InlineData("Dell Inc.", "Dell")]
    [InlineData("ASUSTeK COMPUTER INC.", "ASUS")]
    [InlineData("Micro-Star International Co., Ltd.", "MSI")]
    [InlineData("Gigabyte Technology Co., Ltd.", "Gigabyte")]
    [InlineData("Acer", "Acer")]
    [InlineData("To Be Filled By O.E.M.", null)]
    [InlineData("System manufacturer", null)]
    [InlineData("", null)]
    [InlineData("Framework", "Framework")]
    [InlineData("Some Unknown Vendor Co., Ltd.", "Some Unknown Vendor")]
    public void Normalises_brands(string raw, string? expected) => Assert.Equal(expected, ManufacturerNormaliser.Normalise(raw));

    [Fact]
    public void Strips_brand_prefix_from_product()
    {
        Assert.Equal("XPS 15 9520", ManufacturerNormaliser.StripBrand("Dell XPS 15 9520", "Dell"));
        Assert.Equal("Pavilion Laptop 15-eg0xxx", ManufacturerNormaliser.StripBrand("HP Pavilion Laptop 15-eg0xxx", "HP"));
        Assert.Equal("IdeaPad Slim 5", ManufacturerNormaliser.StripBrand("IdeaPad Slim 5", "Lenovo"));
    }
}

public sealed class GenericValueDetectorTests
{
    [Theory]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("Default string")]
    [InlineData("System Product Name")]
    [InlineData("N/A")]
    [InlineData("xxxxxxxx")]
    [InlineData("0000000")]
    [InlineData("   ")]
    public void Recognises_placeholders(string value) => Assert.Null(GenericValueDetector.Clean(value));

    [Theory]
    [InlineData("  XPS 15   9520 ", "XPS 15 9520")]
    [InlineData("B550M K", "B550M K")]
    public void Keeps_real_values(string value, string expected) => Assert.Equal(expected, GenericValueDetector.Clean(value));
}

public sealed class DeviceClassifierTests
{
    [Theory]
    [InlineData(new[] { 10 }, null, DeviceFormFactor.Laptop)]
    [InlineData(new[] { 3 }, null, DeviceFormFactor.Desktop)]
    [InlineData(new[] { 31 }, null, DeviceFormFactor.Convertible)]
    [InlineData(new[] { 13 }, null, DeviceFormFactor.AllInOne)]
    [InlineData(new[] { 35 }, null, DeviceFormFactor.MiniPc)]
    [InlineData(new int[0], 2, DeviceFormFactor.Laptop)]
    [InlineData(new int[0], 1, DeviceFormFactor.Desktop)]
    [InlineData(new[] { 1 }, null, DeviceFormFactor.Unknown)]
    public void Chassis_to_form_factor(int[] chassis, int? pcSystemType, DeviceFormFactor expected)
    {
        Assert.Equal(expected, DeviceClassifier.FromChassisTypes(chassis, pcSystemType, null));
    }

    [Fact]
    public void Unknown_chassis_uses_battery_presence()
    {
        Assert.Equal(DeviceFormFactor.Laptop, DeviceClassifier.FromChassisTypes(Array.Empty<int>(), null, true));
        Assert.Equal(DeviceFormFactor.Desktop, DeviceClassifier.FromChassisTypes(Array.Empty<int>(), null, false));
    }

    [Theory]
    [InlineData("Microsoft Corporation", "Virtual Machine", "Microsoft Corporation", "Hyper-V UEFI Release v4.1", true, "Microsoft Hyper-V")]
    [InlineData("VMware, Inc.", "VMware20,1", "VMware, Inc.", "VMW201.00V.21805430", true, "VMware")]
    [InlineData("innotek GmbH", "VirtualBox", "innotek GmbH", "VirtualBox", true, "Oracle VirtualBox")]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", "SeaBIOS", "rel-1.16", true, "QEMU/KVM")]
    [InlineData("LENOVO", "82XF", "LENOVO", "LBCN21WW", false, null)]
    [InlineData("Gigabyte Technology Co., Ltd.", "B550M K", "American Megatrends International, LLC.", "F15", false, null)]
    public void Virtual_machine_detection(string manufacturer, string model, string bios, string biosVersion, bool expected, string? platform)
    {
        var verdict = DeviceClassifier.DetectVirtualMachine(manufacturer, model, bios, biosVersion, null);
        Assert.Equal(expected, verdict.IsVirtual);
        Assert.Equal(platform, verdict.Platform);
    }

    [Fact]
    public void Gigabyte_board_as_system_model_is_custom_built()
    {
        Assert.True(DeviceClassifier.IsCustomBuilt("Gigabyte", "B550M K", "Gigabyte", "B550M K", DeviceFormFactor.Desktop));
    }

    [Fact]
    public void Generic_system_strings_are_custom_built()
    {
        Assert.True(DeviceClassifier.IsCustomBuilt(null, null, "ASUS", "PRIME B550-PLUS", DeviceFormFactor.Desktop));
    }

    [Fact]
    public void Branded_desktop_from_board_vendor_is_not_custom_built()
    {
        Assert.False(DeviceClassifier.IsCustomBuilt("ASUS", "ROG Strix G15DK", "ASUS", "G15DK", DeviceFormFactor.Desktop));
    }

    [Fact]
    public void Msi_board_code_as_system_model_is_custom_built()
    {
        Assert.True(DeviceClassifier.IsCustomBuilt("MSI", "MS-7C56", "MSI", "MAG B550 TOMAHAWK (MS-7C56)", DeviceFormFactor.Desktop));
    }

    [Fact]
    public void Laptops_are_never_custom_built()
    {
        Assert.False(DeviceClassifier.IsCustomBuilt("ASUS", "X515EA", "ASUS", "X515EA", DeviceFormFactor.Laptop));
        Assert.False(DeviceClassifier.IsCustomBuilt(null, null, null, null, DeviceFormFactor.Laptop));
    }
}

public sealed class GraphicsClassifierTests
{
    [Theory]
    [InlineData("Intel(R) Iris(R) Xe Graphics", "Intel", null, GraphicsKind.Integrated)]
    [InlineData("Intel(R) UHD Graphics 620", "Intel", null, GraphicsKind.Integrated)]
    [InlineData("Intel(R) Arc(TM) Graphics", "Intel", null, GraphicsKind.Integrated)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "Intel", null, GraphicsKind.Dedicated)]
    [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", "NVIDIA", 6_442_450_944UL, GraphicsKind.Dedicated)]
    [InlineData("NVIDIA GeForce MX450", "NVIDIA", null, GraphicsKind.Dedicated)]
    [InlineData("AMD Radeon(TM) Graphics", "AMD", 536_870_912UL, GraphicsKind.Integrated)]
    [InlineData("AMD Radeon 780M Graphics", "AMD", null, GraphicsKind.Integrated)]
    [InlineData("AMD Radeon RX 6600 XT", "AMD", 8_589_934_592UL, GraphicsKind.Dedicated)]
    [InlineData("AMD Radeon RX 6500M", "AMD", null, GraphicsKind.Dedicated)]
    [InlineData("Radeon RX Vega 8 Graphics", "AMD", null, GraphicsKind.Integrated)]
    [InlineData("Microsoft Hyper-V Video", "Microsoft", null, GraphicsKind.Virtual)]
    [InlineData("VMware SVGA 3D", "VMware", null, GraphicsKind.Virtual)]
    [InlineData("Microsoft Basic Display Adapter", "Microsoft", null, GraphicsKind.Unknown)]
    [InlineData("Qualcomm(R) Adreno(TM) X1-85 GPU", "Qualcomm", null, GraphicsKind.Integrated)]
    public void Classifies_adapters(string name, string vendor, ulong? dedicated, GraphicsKind expected)
    {
        var normalised = GraphicsClassifier.NormaliseName(name);
        Assert.Equal(expected, GraphicsClassifier.Classify(normalised, vendor, dedicated));
    }

    [Fact]
    public void Removes_trademark_marks()
    {
        Assert.Equal("Intel Iris Xe Graphics", GraphicsClassifier.NormaliseName("Intel(R) Iris(R) Xe Graphics"));
        Assert.Equal("NVIDIA GeForce RTX 4070", GraphicsClassifier.NormaliseName("NVIDIA GeForce RTX 4070"));
    }

    [Fact]
    public void Basic_display_adapter_is_fallback_driver()
    {
        Assert.True(GraphicsClassifier.IsFallbackDriver("Microsoft Basic Display Adapter"));
        Assert.False(GraphicsClassifier.IsFallbackDriver("NVIDIA GeForce RTX 3060"));
    }
}

public sealed class DisplayNamingTests
{
    [Theory]
    [InlineData(30, 19, 14.0)]
    [InlineData(31, 17, 14.0)]
    [InlineData(34, 19, 15.6)]
    [InlineData(38, 21, 17.3)]
    [InlineData(29, 17, 13.3)]
    [InlineData(60, 34, 27.0)]
    [InlineData(53, 30, 24.0)]
    [InlineData(26, 17, 12.3)]
    public void Snaps_diagonal_to_common_sizes(double w, double h, double expected)
    {
        Assert.Equal(expected, DisplayNaming.EstimateDiagonalInches(new PhysicalSize(w, h)));
    }

    [Theory]
    [InlineData(1920, 1080, "Full HD")]
    [InlineData(2560, 1440, "QHD (1440p)")]
    [InlineData(3840, 2160, "4K UHD")]
    [InlineData(1366, 768, "HD")]
    [InlineData(2880, 1800, "2.8K (16:10)")]
    [InlineData(1234, 567, null)]
    public void Marketing_names(int w, int h, string? expected) => Assert.Equal(expected, DisplayNaming.MarketingName(new Resolution(w, h)));

    [Theory]
    [InlineData("AUO", "AU Optronics")]
    [InlineData("LGD", "LG Display")]
    [InlineData("DEL", "Dell")]
    [InlineData("ZZZ", null)]
    public void Vendor_codes(string code, string? expected) => Assert.Equal(expected, DisplayNaming.VendorName(code));

    [Fact]
    public void Formats_diagonal()
    {
        Assert.Equal("14-inch", DisplayNaming.FormatDiagonal(14.0));
        Assert.Equal("15.6-inch", DisplayNaming.FormatDiagonal(15.6));
    }
}

public sealed class WifiStandardInferenceTests
{
    [Theory]
    [InlineData(new[] { 4, 5, 6, 7, 9, 10 }, "Wi-Fi 6 (802.11ax)")]
    [InlineData(new[] { 7, 9 }, "Wi-Fi 5 (802.11ac)")]
    [InlineData(new[] { 7 }, "Wi-Fi 4 (802.11n)")]
    [InlineData(new[] { 11 }, "Wi-Fi 7 (802.11be)")]
    [InlineData(new int[0], null)]
    public void From_phy_types(int[] phys, string? expected) => Assert.Equal(expected, WifiStandardInference.FromPhyTypes(phys));

    [Theory]
    [InlineData("Intel(R) Wi-Fi 6E AX211 160MHz", "Wi-Fi 6E (802.11ax, 6 GHz)")]
    [InlineData("Intel(R) Wi-Fi 6 AX201 160MHz", "Wi-Fi 6 (802.11ax)")]
    [InlineData("Intel(R) Wireless-AC 9560 160MHz", "Wi-Fi 5 (802.11ac)")]
    [InlineData("Realtek RTL8821CE 802.11ac PCIe Adapter", "Wi-Fi 5 (802.11ac)")]
    [InlineData("Qualcomm Atheros AR9485 Wireless Network Adapter", "Wi-Fi 4 (802.11n)")]
    [InlineData("Intel(R) Wi-Fi 7 BE200 320MHz", "Wi-Fi 7 (802.11be)")]
    [InlineData("MediaTek Wi-Fi 6E MT7922 160MHz Wireless LAN Card", "Wi-Fi 6E (802.11ax, 6 GHz)")]
    [InlineData("Some Generic Wireless Adapter", null)]
    public void From_adapter_name(string name, string? expected) => Assert.Equal(expected, WifiStandardInference.FromAdapterName(name));
}

public sealed class MemoryTypeMapperTests
{
    [Theory]
    [InlineData(26, "DDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    [InlineData(30, "LPDDR4")]
    [InlineData(24, "DDR3")]
    [InlineData(0, null)]
    public void Smbios_types(int code, string? expected) => Assert.Equal(expected, MemoryTypeMapper.FromSmbiosType(code));

    [Fact]
    public void Low_power_types_are_flagged()
    {
        Assert.True(MemoryTypeMapper.IsLowPowerType("LPDDR5"));
        Assert.False(MemoryTypeMapper.IsLowPowerType("DDR5"));
    }

    [Fact]
    public void Speed_inference_is_conservative()
    {
        Assert.Equal("DDR5 (inferred from speed)", MemoryTypeMapper.InferFromSpeed(5600));
        Assert.Equal("DDR4 (inferred from speed)", MemoryTypeMapper.InferFromSpeed(3200));
        Assert.Null(MemoryTypeMapper.InferFromSpeed(4000));
    }
}
