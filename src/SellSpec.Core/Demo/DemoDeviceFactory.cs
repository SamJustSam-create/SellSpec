using SellSpec.Core.Health;
using SellSpec.Core.Models;

namespace SellSpec.Core.Demo;

/// <summary>
/// A clearly labelled fictional laptop used only when SellSpec is started with the --demo switch. It lets the UI,
/// listing and report code be exercised on machines (such as desktops) that lack batteries or built-in displays.
/// The manufacturer name makes it impossible to mistake for a real device.
/// </summary>
public static class DemoDeviceFactory
{
    public const string Manufacturer = "SellSpec Demo";

    public static DeviceSnapshot CreateLaptop()
    {
        var battery = BatteryHealthCalculator.Combine(
            new[]
            {
                new BatteryPack
                {
                    Name = "Demo battery",
                    DesignCapacityMwh = Spec<int>.Firmware(56_500),
                    FullChargeCapacityMwh = Spec<int>.Firmware(49_200),
                    CycleCount = Spec<int>.Firmware(212),
                    Chemistry = Spec<string>.Firmware("Lithium-ion polymer"),
                },
            },
            Spec<int>.Detected(74),
            Spec<string>.Detected("Charging"),
            Spec<string>.Detected("OK"));

        var snapshot = new DeviceSnapshot
        {
            Identity = new DeviceIdentity
            {
                Manufacturer = Spec<string>.Firmware(Manufacturer),
                ProductName = Spec<string>.Firmware("Demo Laptop 14"),
                ModelNumber = Spec<string>.Firmware("DL14-2024"),
                Sku = Spec<string>.Firmware("DEMO-SKU-0001"),
                FormFactor = Spec<DeviceFormFactor>.Firmware(DeviceFormFactor.Laptop),
                Architecture = Spec<string>.Detected("x64"),
                BoardManufacturer = Spec<string>.Firmware(Manufacturer),
                BoardModel = Spec<string>.Firmware("DL14 mainboard"),
                BiosVendor = Spec<string>.Firmware("Demo Firmware"),
                BiosVersion = Spec<string>.Firmware("1.08"),
                BiosReleaseDate = Spec<DateOnly>.Firmware(new DateOnly(2024, 6, 12)),
                FirmwareType = Spec<string>.Detected("UEFI"),
                ChassisTypeCodes = new[] { 10 },
                RawManufacturer = Manufacturer,
                RawModel = "DL14-2024",
            },
            Processor = new ProcessorInfo
            {
                Manufacturer = Spec<string>.Detected("Intel"),
                Name = Spec<string>.Detected("Intel Core i5-1335U (13th Gen)"),
                FamilyShortName = "Intel Core i5",
                RawName = "13th Gen Intel(R) Core(TM) i5-1335U",
                PhysicalCores = Spec<int>.Detected(10),
                LogicalProcessors = Spec<int>.Detected(12),
                ClockSpeedMHz = Spec<int>.Firmware(1300),
                Architecture = Spec<string>.Detected("x64"),
                InstalledCount = Spec<int>.Detected(1),
            },
            Memory = new MemoryInfo
            {
                InstalledBytes = Spec<ulong>.Firmware(16UL * 1024 * 1024 * 1024),
                UsableBytes = Spec<ulong>.Detected(16_968_011_776),
                Technology = Spec<string>.Firmware("LPDDR5"),
                ConfiguredSpeedMT = Spec<int>.Firmware(5200),
                Modules = new[]
                {
                    new MemoryModule { CapacityBytes = 8UL * 1024 * 1024 * 1024, Technology = "LPDDR5", SpeedMT = 5200, FormFactor = "Row of chips", Slot = "Controller0-ChannelA" },
                    new MemoryModule { CapacityBytes = 8UL * 1024 * 1024 * 1024, Technology = "LPDDR5", SpeedMT = 5200, FormFactor = "Row of chips", Slot = "Controller1-ChannelA" },
                },
                ReportedSlots = Spec<int>.Firmware(2, "Firmware slot counts do not prove the memory is replaceable."),
                ReportedMaxBytes = Spec<ulong>.Firmware(16UL * 1024 * 1024 * 1024),
                Upgradeability = Spec<string>.Estimated("Not verified", "LPDDR5 memory is normally soldered to the mainboard and cannot be upgraded."),
            },
            Storage = new[]
            {
                new StorageDevice
                {
                    Model = "Demo NVMe SSD 512GB",
                    Manufacturer = Spec<string>.Detected("SellSpec Demo"),
                    CapacityBytes = Spec<ulong>.Detected(512_110_190_592),
                    Media = StorageMediaType.Ssd,
                    Bus = StorageBus.Nvme,
                    Location = StorageLocation.Internal,
                    IsBootDrive = true,
                    DriveLetters = new[] { "C:" },
                    PowerOnHours = Spec<int>.Detected(3_420),
                    ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good),
                    WearPercentUsed = Spec<int>.Detected(4),
                    TemperatureC = Spec<int>.Detected(38),
                    PredictedFailure = Spec<bool>.Detected(false),
                    DiskNumber = 0,
                },
                new StorageDevice
                {
                    Model = "Demo USB Flash 64GB",
                    CapacityBytes = Spec<ulong>.Detected(62_000_000_000),
                    Media = StorageMediaType.Unknown,
                    Bus = StorageBus.Usb,
                    Location = StorageLocation.Removable,
                    DriveLetters = new[] { "E:" },
                    DiskNumber = 1,
                },
            },
            Graphics = new[]
            {
                new GraphicsAdapter
                {
                    Name = "Intel Iris Xe Graphics",
                    RawName = "Intel(R) Iris(R) Xe Graphics",
                    Vendor = Spec<string>.Detected("Intel"),
                    Kind = GraphicsKind.Integrated,
                    DriverVersion = "31.0.101.5186",
                },
            },
            Displays = new[]
            {
                new DisplayInfo
                {
                    Manufacturer = Spec<string>.Detected("AU Optronics"),
                    ManufacturerCode = "AUO",
                    ProductCode = "DEMO",
                    IsInternal = true,
                    IsPrimary = true,
                    NativeResolution = Spec<Resolution>.Detected(new Resolution(1920, 1200)),
                    CurrentResolution = Spec<Resolution>.Detected(new Resolution(1920, 1200)),
                    CurrentRefreshRateHz = Spec<double>.Detected(60),
                    MaxRefreshRateHz = Spec<double>.Detected(60),
                    PhysicalSize = Spec<PhysicalSize>.Firmware(new PhysicalSize(30, 19)),
                    DiagonalInches = Spec<double>.Estimated(14.0, "Estimated from the panel's reported physical size."),
                    Touch = Spec<bool>.Detected(false),
                    ResolutionMarketingName = "Full HD+ (16:10)",
                    ConnectionLabel = "Internal",
                },
            },
            Battery = battery,
            Connectivity = new ConnectivityInfo
            {
                WifiAdapter = Spec<string>.Detected("Demo Wi-Fi 6E AX211"),
                WifiStandard = Spec<string>.Detected("Wi-Fi 6E (802.11ax, 6 GHz)"),
                Bluetooth = Spec<bool>.Detected(true),
                Ethernet = Spec<bool>.Detected(false),
                Webcam = Spec<bool>.Detected(true),
                Audio = Spec<bool>.Detected(true),
                FingerprintReader = Spec<bool>.Detected(true),
                NetworkAdapters = new[] { "Demo Wi-Fi 6E AX211" },
                Cameras = new[] { "Demo Integrated Camera" },
                AudioDevices = new[] { "Demo Smart Sound Technology" },
                BluetoothRadios = new[] { "Demo Wireless Bluetooth" },
            },
            OperatingSystem = new OperatingSystemInfo
            {
                Name = Spec<string>.Detected("Windows 11"),
                Edition = Spec<string>.Detected("Home"),
                Version = Spec<string>.Detected("24H2"),
                Build = Spec<string>.Detected("26100.4061"),
                BuildNumber = 26100,
                Architecture = Spec<string>.Detected("64-bit"),
                InstallDate = Spec<DateOnly>.Detected(new DateOnly(2024, 9, 3)),
                Activation = Spec<ActivationStatus>.Detected(ActivationStatus.Licensed),
                LicenceChannel = Spec<string>.Detected("OEM"),
                TpmVersion = Spec<string>.Detected("2.0"),
                SecureBootEnabled = Spec<bool>.Detected(true),
                FirmwareType = Spec<string>.Detected("UEFI"),
            },
            Private = new PrivateIdentifiers
            {
                SystemSerialNumber = "DEMO-SERIAL-00000001",
                SystemUuid = "00000000-0000-4000-8000-000000000001",
                StorageSerialNumbers = new[] { "DEMO-SSD-SERIAL-01" },
            },
        };

        return Detection.SnapshotEnricher.Enrich(snapshot);
    }
}
