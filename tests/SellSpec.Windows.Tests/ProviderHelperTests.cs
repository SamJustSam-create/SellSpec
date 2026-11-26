using SellSpec.Core.Models;
using SellSpec.Windows.Providers;
using Xunit;

namespace SellSpec.Windows.Tests;

public sealed class ProviderHelperTests
{
    [Theory]
    [InlineData(17, null, "Samsung SSD 980 PRO", StorageBus.Nvme)]
    [InlineData(11, null, "ST1000LM035", StorageBus.Sata)]
    [InlineData(3, null, "WDC WD10", StorageBus.Sata)]
    [InlineData(7, null, "Seagate Expansion", StorageBus.Usb)]
    [InlineData(12, null, "SD Card", StorageBus.SdCard)]
    [InlineData(15, null, "Msft Virtual Disk", StorageBus.Virtual)]
    [InlineData(null, "IDE", "Crucial CT500MX500", StorageBus.Sata)]
    [InlineData(null, "USB", "Kingston DataTraveler", StorageBus.Usb)]
    [InlineData(null, "SCSI", "NVMe KBG40ZNV512G", StorageBus.Nvme)]
    public void Storage_bus_mapping(int? busType, string? legacy, string model, StorageBus expected)
    {
        Assert.Equal(expected, WindowsStorageProvider.MapBus(busType, legacy, model));
    }

    [Theory]
    [InlineData(4, null, StorageBus.Sata, "Anything", StorageMediaType.Ssd)]
    [InlineData(3, null, StorageBus.Sata, "Anything", StorageMediaType.Hdd)]
    [InlineData(0, null, StorageBus.Nvme, "Anything", StorageMediaType.Ssd)]
    [InlineData(0, null, StorageBus.Sata, "Samsung SSD 860 EVO 500GB", StorageMediaType.Ssd)]
    [InlineData(0, 5400u, StorageBus.Sata, "ST1000LM035-1RK172", StorageMediaType.Hdd)]
    [InlineData(0, null, StorageBus.Usb, "Generic Flash Disk", StorageMediaType.Unknown)]
    public void Storage_media_mapping(int? media, uint? spindle, StorageBus bus, string model, StorageMediaType expected)
    {
        Assert.Equal(expected, WindowsStorageProvider.MapMedia(media, spindle, bus, model));
    }

    [Theory]
    [InlineData(StorageBus.Nvme, 17, "Samsung", null, StorageLocation.Internal)]
    [InlineData(StorageBus.Usb, 7, "Seagate Expansion", "External hard disk media", StorageLocation.External)]
    [InlineData(StorageBus.Usb, 7, "Flash", "Removable Media", StorageLocation.Removable)]
    [InlineData(StorageBus.Virtual, 15, "Msft Virtual Disk", null, StorageLocation.Virtual)]
    [InlineData(StorageBus.SdCard, 12, "SD", null, StorageLocation.Removable)]
    [InlineData(StorageBus.Unknown, null, "Drive", "Fixed hard disk media", StorageLocation.Internal)]
    public void Storage_location_mapping(StorageBus bus, int? busType, string model, string? legacyMedia, StorageLocation expected)
    {
        Assert.Equal(expected, WindowsStorageProvider.MapLocation(bus, busType, model, legacyMedia));
    }

    [Fact]
    public void Smart_power_on_hours_parsed_from_vendor_specific_block()
    {
        var block = new byte[512];
        // attribute record at offset 2: id 9, flags, current, worst, raw (little endian) = 3420
        block[2] = 9;
        block[7] = 0x5C; // 3420 = 0x0D5C
        block[8] = 0x0D;
        Assert.Equal(3420, WindowsStorageProvider.ReadSmartAttributeRaw(block, 9));
        Assert.Null(WindowsStorageProvider.ReadSmartAttributeRaw(block, 12));
    }

    [Fact]
    public void Pnp_instance_names_match_despite_separator_differences()
    {
        Assert.True(WindowsStorageProvider.MatchesPnp(@"SCSI\Disk&Ven_Samsung&Prod_SSD_860\4&2a1b&0&000000_0", @"SCSI\DISK&VEN_SAMSUNG&PROD_SSD_860\4&2A1B&0&000000"));
        Assert.False(WindowsStorageProvider.MatchesPnp(@"SCSI\Disk&Ven_A\1_0", @"SCSI\Disk&Ven_B\2"));
    }

    [Theory]
    [InlineData("Microsoft Windows 11 Pro", "Pro")]
    [InlineData("Microsoft Windows 10 Home", "Home")]
    [InlineData("Microsoft Windows 11 Pro for Workstations", "Pro for Workstations")]
    [InlineData("Microsoft Windows 11 Enterprise", "Enterprise")]
    [InlineData(null, null)]
    public void Edition_from_caption(string? caption, string? expected)
    {
        Assert.Equal(expected, WindowsOperatingSystemProvider.EditionFromCaption(caption));
    }

    [Theory]
    [InlineData("Windows(R) Operating System, OEM_DM channel", "OEM digital")]
    [InlineData("Windows(R) Operating System, RETAIL channel", "Retail")]
    [InlineData("Windows(R) Operating System, VOLUME_KMSCLIENT channel", "Volume")]
    [InlineData("Windows(R) Operating System, OEM_COA_NSLP channel", "OEM")]
    [InlineData("Something unrelated", null)]
    public void Licence_channel_from_description(string description, string? expected)
    {
        Assert.Equal(expected, WindowsOperatingSystemProvider.ChannelFromDescription(description));
    }

    [Theory]
    [InlineData("82XF", true)]
    [InlineData("20U9S0JC00", true)]
    [InlineData("IdeaPad Slim 5 14IAH8", false)]
    [InlineData("XPS 15 9520", false)]
    [InlineData("B550M K", false)]
    [InlineData("HP", true)]
    public void Model_code_detection(string value, bool expected)
    {
        Assert.Equal(expected, WindowsDeviceIdentityProvider.LooksLikeCode(value));
    }

    [Theory]
    [InlineData("Intel(R) Wireless Bluetooth(R)", true)]
    [InlineData("Realtek Bluetooth Adapter", true)]
    [InlineData("MediaTek Bluetooth Adapter", true)]
    [InlineData("Qualcomm FastConnect 6900 Bluetooth Adapter", true)]
    [InlineData("Microsoft Bluetooth Enumerator", false)]
    [InlineData("Microsoft Bluetooth LE Enumerator", false)]
    [InlineData("Bluetooth Device (RFCOMM Protocol TDI)", false)]
    [InlineData("Sam's AirPods", false)]
    [InlineData("Sam’s AirPods Pro", false)]
    [InlineData("Galaxy Buds2", false)]
    [InlineData("JBL Bluetooth Speaker", false)]
    [InlineData("Xbox Wireless Controller", false)]
    [InlineData("Logitech MX Keys", false)]
    public void Bluetooth_radio_names_exclude_paired_devices(string name, bool expected)
    {
        Assert.Equal(expected, WindowsConnectivityProvider.IsBluetoothRadioName(name));
    }

    [Fact]
    public void Usable_memory_rounds_up_to_module_size()
    {
        Assert.Equal(16UL * 1024 * 1024 * 1024, WindowsMemoryProvider.RoundToModuleSize(16_968_011_776UL));
        Assert.Equal(8UL * 1024 * 1024 * 1024, WindowsMemoryProvider.RoundToModuleSize(8UL * 1024 * 1024 * 1024));
    }
}
