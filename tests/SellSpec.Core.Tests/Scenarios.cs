using SellSpec.Core.Detection;
using SellSpec.Core.Health;
using SellSpec.Core.Models;

namespace SellSpec.Core.Tests;

/// <summary>Realistic device fixtures covering the ten scenarios in the development brief.</summary>
public static class Scenarios
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    public static readonly PrivateIdentifiers PrivateIds = new()
    {
        SystemSerialNumber = "PF3K8QX2",
        SystemUuid = "6ab35c0e-1a1f-4e92-9f0f-3b2b6b9a6a01",
        BaseBoardSerialNumber = "L1HF2CB00E6",
        StorageSerialNumbers = new[] { "S6PENX0T512345A" },
        BatterySerialNumbers = new[] { "1234BAT567" },
    };

    public static DeviceSnapshot StandardLaptop() => Enrich(new DeviceSnapshot
    {
        Identity = new DeviceIdentity
        {
            Manufacturer = Spec<string>.Firmware("Lenovo"),
            ProductName = Spec<string>.Firmware("IdeaPad Slim 5 14IAH8"),
            ModelNumber = Spec<string>.Firmware("82XF"),
            FormFactor = Spec<DeviceFormFactor>.Firmware(DeviceFormFactor.Laptop),
            Architecture = Spec<string>.Detected("x64"),
            BoardManufacturer = Spec<string>.Firmware("Lenovo"),
            BoardModel = Spec<string>.Firmware("LNVNB161216"),
            BiosVersion = Spec<string>.Firmware("LBCN21WW"),
            FirmwareType = Spec<string>.Detected("UEFI"),
            RawManufacturer = "LENOVO",
            RawModel = "82XF",
        },
        Processor = new ProcessorInfo
        {
            Manufacturer = Spec<string>.Detected("Intel"),
            Name = Spec<string>.Detected("Intel Core i5-1335U (13th Gen)"),
            FamilyShortName = "Intel Core i5",
            PhysicalCores = Spec<int>.Detected(10),
            LogicalProcessors = Spec<int>.Detected(12),
            ClockSpeedMHz = Spec<int>.Firmware(1300),
            Architecture = Spec<string>.Detected("x64"),
            InstalledCount = Spec<int>.Detected(1),
        },
        Memory = new MemoryInfo
        {
            InstalledBytes = Spec<ulong>.Firmware(16 * GiB),
            UsableBytes = Spec<ulong>.Detected(16_968_011_776),
            Technology = Spec<string>.Firmware("DDR5"),
            ConfiguredSpeedMT = Spec<int>.Firmware(5200),
            Modules = new[] { new MemoryModule { CapacityBytes = 8 * GiB, FormFactor = "SODIMM" }, new MemoryModule { CapacityBytes = 8 * GiB, FormFactor = "SODIMM" } },
            ReportedSlots = Spec<int>.Firmware(2),
            Upgradeability = Spec<string>.Estimated("Not verified"),
        },
        Storage = new[] { Nvme512(boot: true) },
        Graphics = new[] { IrisXe() },
        Displays = new[] { InternalPanel(1920, 1080, 30.9, 17.4, "Full HD") },
        Battery = Battery(60_000, 52_200, 180),
        Connectivity = Wifi6Connectivity(),
        OperatingSystem = Windows11Home(),
        Private = PrivateIds,
    });

    public static DeviceSnapshot GamingLaptop() => Enrich(StandardLaptop() with
    {
        Identity = StandardLaptop().Identity with
        {
            Manufacturer = Spec<string>.Firmware("ASUS"),
            ProductName = Spec<string>.Firmware("ROG Strix G15 G513QM"),
            ModelNumber = Spec<string>.Firmware("G513QM"),
        },
        Processor = new ProcessorInfo
        {
            Manufacturer = Spec<string>.Detected("AMD"),
            Name = Spec<string>.Detected("AMD Ryzen 7 5800H"),
            FamilyShortName = "AMD Ryzen 7",
            PhysicalCores = Spec<int>.Detected(8),
            LogicalProcessors = Spec<int>.Detected(16),
            ClockSpeedMHz = Spec<int>.Firmware(3200),
            Architecture = Spec<string>.Detected("x64"),
            InstalledCount = Spec<int>.Detected(1),
        },
        Graphics = new[]
        {
            new GraphicsAdapter { Name = "AMD Radeon Graphics", Vendor = Spec<string>.Detected("AMD"), Kind = GraphicsKind.Integrated },
            new GraphicsAdapter { Name = "NVIDIA GeForce RTX 3060 Laptop GPU", Vendor = Spec<string>.Detected("NVIDIA"), Kind = GraphicsKind.Dedicated, DedicatedMemoryBytes = Spec<ulong>.Detected(6 * GiB) },
        },
        Displays = new[] { InternalPanel(1920, 1080, 34.4, 19.4, "Full HD") with { MaxRefreshRateHz = Spec<double>.Firmware(144), CurrentRefreshRateHz = Spec<double>.Detected(144) } },
        Storage = new[] { Nvme512(boot: true), new StorageDevice { Model = "Samsung SSD 870 EVO 1TB", CapacityBytes = Spec<ulong>.Detected(1_000_204_886_016), Media = StorageMediaType.Ssd, Bus = StorageBus.Sata, Location = StorageLocation.Internal, DiskNumber = 1, ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good) } },
    });

    public static DeviceSnapshot CustomDesktop() => Enrich(new DeviceSnapshot
    {
        Identity = new DeviceIdentity
        {
            Manufacturer = Spec<string>.Firmware("Gigabyte"),
            ProductName = Spec<string>.Firmware("B550M K"),
            ModelNumber = Spec<string>.Firmware("B550M K"),
            FormFactor = Spec<DeviceFormFactor>.Firmware(DeviceFormFactor.Desktop),
            Architecture = Spec<string>.Detected("x64"),
            BoardManufacturer = Spec<string>.Firmware("Gigabyte"),
            BoardModel = Spec<string>.Firmware("B550M K"),
            FirmwareType = Spec<string>.Detected("UEFI"),
            IsCustomBuilt = true,
            RawManufacturer = "Gigabyte Technology Co., Ltd.",
            RawModel = "B550M K",
        },
        Processor = new ProcessorInfo
        {
            Manufacturer = Spec<string>.Detected("AMD"),
            Name = Spec<string>.Detected("AMD Ryzen 5 5600X"),
            FamilyShortName = "AMD Ryzen 5",
            PhysicalCores = Spec<int>.Detected(6),
            LogicalProcessors = Spec<int>.Detected(12),
            ClockSpeedMHz = Spec<int>.Firmware(3700),
            Architecture = Spec<string>.Detected("x64"),
            InstalledCount = Spec<int>.Detected(1),
        },
        Memory = new MemoryInfo
        {
            InstalledBytes = Spec<ulong>.Firmware(32 * GiB),
            UsableBytes = Spec<ulong>.Detected(34_225_520_640),
            Technology = Spec<string>.Firmware("DDR4"),
            ConfiguredSpeedMT = Spec<int>.Firmware(3200),
            Modules = new[] { new MemoryModule { CapacityBytes = 16 * GiB, FormFactor = "DIMM" }, new MemoryModule { CapacityBytes = 16 * GiB, FormFactor = "DIMM" } },
            ReportedSlots = Spec<int>.Firmware(4),
            Upgradeability = Spec<string>.Estimated("Not verified"),
        },
        Storage = new[] { Nvme512(boot: true), new StorageDevice { Model = "ST2000DM008-2FR102", CapacityBytes = Spec<ulong>.Detected(2_000_398_934_016), Media = StorageMediaType.Hdd, Bus = StorageBus.Sata, Location = StorageLocation.Internal, DiskNumber = 1, SpindleRpm = Spec<int>.Firmware(7200), PowerOnHours = Spec<int>.Detected(12_000), ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good) } },
        Graphics = new[] { new GraphicsAdapter { Name = "NVIDIA GeForce RTX 3070", Vendor = Spec<string>.Detected("NVIDIA"), Kind = GraphicsKind.Dedicated, DedicatedMemoryBytes = Spec<ulong>.Detected(8 * GiB) } },
        Displays = new[] { new DisplayInfo { Name = "DELL U2722D", Manufacturer = Spec<string>.Firmware("Dell"), IsInternal = false, IsPrimary = true, NativeResolution = Spec<Resolution>.Firmware(new Resolution(2560, 1440)), DiagonalInches = Spec<double>.Estimated(27), ResolutionMarketingName = "QHD (1440p)" } },
        Battery = BatteryInfo.NotApplicable(),
        Connectivity = new ConnectivityInfo { Ethernet = Spec<bool>.Detected(true), Bluetooth = Spec<bool>.Detected(false), Webcam = Spec<bool>.Detected(false), Audio = Spec<bool>.Detected(true), WifiAdapter = Spec<string>.Unavailable() },
        OperatingSystem = Windows11Home() with { Edition = Spec<string>.Detected("Pro"), LicenceChannel = Spec<string>.Detected("Retail") },
        Private = PrivateIds,
    });

    public static DeviceSnapshot SolderedRamLaptop() => Enrich(StandardLaptop() with
    {
        Identity = StandardLaptop().Identity with { Manufacturer = Spec<string>.Firmware("Dell"), ProductName = Spec<string>.Firmware("XPS 13 9315"), ModelNumber = Spec<string>.Firmware("XPS 13 9315") },
        Memory = new MemoryInfo
        {
            InstalledBytes = Spec<ulong>.Firmware(16 * GiB),
            UsableBytes = Spec<ulong>.Detected(16_900_000_000),
            Technology = Spec<string>.Firmware("LPDDR5"),
            ConfiguredSpeedMT = Spec<int>.Firmware(5200),
            Modules = new[] { new MemoryModule { CapacityBytes = 8 * GiB, Technology = "LPDDR5", FormFactor = "Row of chips" }, new MemoryModule { CapacityBytes = 8 * GiB, Technology = "LPDDR5", FormFactor = "Row of chips" } },
            ReportedSlots = Spec<int>.Firmware(2, "Firmware slot counts do not prove the memory is replaceable."),
            Upgradeability = Spec<string>.Estimated("Not verified", "LPDDR5 memory is normally soldered to the mainboard and cannot be upgraded."),
        },
    });

    public static DeviceSnapshot MultiDriveDevice() => Enrich(StandardLaptop() with
    {
        Storage = new[]
        {
            Nvme512(boot: true),
            new StorageDevice { Model = "WDC WD10SPZX-24Z10", CapacityBytes = Spec<ulong>.Detected(1_000_204_886_016), Media = StorageMediaType.Hdd, Bus = StorageBus.Sata, Location = StorageLocation.Internal, DiskNumber = 1, SpindleRpm = Spec<int>.Firmware(5400) },
            new StorageDevice { Model = "SanDisk Extreme Portable", CapacityBytes = Spec<ulong>.Detected(1_000_000_000_000), Media = StorageMediaType.Ssd, Bus = StorageBus.Usb, Location = StorageLocation.External, DiskNumber = 2 },
            new StorageDevice { Model = "Msft Virtual Disk", CapacityBytes = Spec<ulong>.Detected(40_000_000_000), Media = StorageMediaType.Unknown, Bus = StorageBus.Virtual, Location = StorageLocation.Virtual, DiskNumber = 3 },
        },
    });

    public static DeviceSnapshot NoBatteryDevice() => CustomDesktop();

    public static DeviceSnapshot UnavailableBatteryHealth() => Enrich(StandardLaptop() with
    {
        Battery = BatteryHealthCalculator.Combine(
            new[] { new BatteryPack { Name = "Battery", DesignCapacityMwh = Spec<int>.Unavailable("Design capacity not reported."), FullChargeCapacityMwh = Spec<int>.Firmware(40_000), CycleCount = Spec<int>.Unavailable() } },
            Spec<int>.Detected(88), Spec<string>.Detected("Charging"), Spec<string>.Detected("OK")),
    });

    public static DeviceSnapshot IncompleteManufacturer() => Enrich(StandardLaptop() with
    {
        Identity = new DeviceIdentity
        {
            Manufacturer = Spec<string>.Unavailable("Firmware reported a placeholder manufacturer name."),
            ProductName = Spec<string>.Unavailable(),
            ModelNumber = Spec<string>.Unavailable(),
            FormFactor = Spec<DeviceFormFactor>.Firmware(DeviceFormFactor.Laptop),
            Architecture = Spec<string>.Detected("x64"),
            RawManufacturer = "To Be Filled By O.E.M.",
            RawModel = "To Be Filled By O.E.M.",
        },
    });

    public static DeviceSnapshot VirtualMachine() => Enrich(new DeviceSnapshot
    {
        Identity = new DeviceIdentity
        {
            Manufacturer = Spec<string>.Firmware("Microsoft"),
            ProductName = Spec<string>.Firmware("Virtual Machine"),
            ModelNumber = Spec<string>.Firmware("Virtual Machine"),
            FormFactor = Spec<DeviceFormFactor>.Firmware(DeviceFormFactor.Desktop),
            IsVirtualMachine = true,
            VirtualisationPlatform = "Microsoft Hyper-V",
            RawManufacturer = "Microsoft Corporation",
            RawModel = "Virtual Machine",
        },
        Processor = new ProcessorInfo { Name = Spec<string>.Detected("Intel Core i7-12700K (12th Gen)"), FamilyShortName = "Intel Core i7", PhysicalCores = Spec<int>.Detected(4), LogicalProcessors = Spec<int>.Detected(4), Architecture = Spec<string>.Detected("x64"), InstalledCount = Spec<int>.Detected(1) },
        Memory = new MemoryInfo { InstalledBytes = Spec<ulong>.Detected(8 * GiB), UsableBytes = Spec<ulong>.Detected(8 * GiB), Technology = Spec<string>.Unavailable(), Upgradeability = Spec<string>.Estimated("Not verified") },
        Storage = new[] { new StorageDevice { Model = "Msft Virtual Disk", CapacityBytes = Spec<ulong>.Detected(127 * 1_000_000_000UL), Bus = StorageBus.Virtual, Location = StorageLocation.Virtual, IsBootDrive = true, DiskNumber = 0 } },
        Graphics = new[] { new GraphicsAdapter { Name = "Microsoft Hyper-V Video", Vendor = Spec<string>.Detected("Microsoft"), Kind = GraphicsKind.Virtual } },
        Displays = Array.Empty<DisplayInfo>(),
        Battery = BatteryInfo.NotApplicable(),
        Connectivity = new ConnectivityInfo { Ethernet = Spec<bool>.Detected(true) },
        OperatingSystem = Windows11Home() with { Activation = Spec<ActivationStatus>.Detected(ActivationStatus.Notification) },
        Private = PrivateIdentifiers.Empty,
    });

    /// <summary>Non-elevated scan: reliability counters unavailable, so power-on hours and wear must read as unavailable.</summary>
    public static DeviceSnapshot NonAdministrator() => Enrich(StandardLaptop() with
    {
        Storage = new[]
        {
            Nvme512(boot: true) with
            {
                PowerOnHours = Spec<int>.Unavailable("Power-on hours may need SellSpec to be run as administrator on this system."),
                WearPercentUsed = Spec<int>.Unavailable(),
                ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good),
            },
        },
    });

    public static IEnumerable<object[]> All()
    {
        yield return new object[] { "StandardLaptop", StandardLaptop() };
        yield return new object[] { "GamingLaptop", GamingLaptop() };
        yield return new object[] { "CustomDesktop", CustomDesktop() };
        yield return new object[] { "SolderedRamLaptop", SolderedRamLaptop() };
        yield return new object[] { "MultiDriveDevice", MultiDriveDevice() };
        yield return new object[] { "NoBatteryDevice", NoBatteryDevice() };
        yield return new object[] { "UnavailableBatteryHealth", UnavailableBatteryHealth() };
        yield return new object[] { "IncompleteManufacturer", IncompleteManufacturer() };
        yield return new object[] { "VirtualMachine", VirtualMachine() };
        yield return new object[] { "NonAdministrator", NonAdministrator() };
    }

    private static DeviceSnapshot Enrich(DeviceSnapshot s) => SnapshotEnricher.Enrich(s);

    public static StorageDevice Nvme512(bool boot) => new()
    {
        Model = "Samsung MZVL2512HCJQ-00B00",
        Manufacturer = Spec<string>.Detected("Samsung"),
        CapacityBytes = Spec<ulong>.Detected(512_110_190_592),
        Media = StorageMediaType.Ssd,
        Bus = StorageBus.Nvme,
        Location = StorageLocation.Internal,
        IsBootDrive = boot,
        DriveLetters = new[] { "C:" },
        DiskNumber = 0,
        PowerOnHours = Spec<int>.Detected(3_420),
        ReportedHealth = Spec<HealthStatusKind>.Detected(HealthStatusKind.Good),
        WearPercentUsed = Spec<int>.Detected(3),
        PredictedFailure = Spec<bool>.Detected(false),
    };

    public static GraphicsAdapter IrisXe() => new()
    {
        Name = "Intel Iris Xe Graphics",
        Vendor = Spec<string>.Detected("Intel"),
        Kind = GraphicsKind.Integrated,
    };

    public static DisplayInfo InternalPanel(int w, int h, double widthCm, double heightCm, string? marketing) => new()
    {
        Manufacturer = Spec<string>.Firmware("AU Optronics"),
        ManufacturerCode = "AUO",
        IsInternal = true,
        IsPrimary = true,
        NativeResolution = Spec<Resolution>.Firmware(new Resolution(w, h)),
        CurrentResolution = Spec<Resolution>.Detected(new Resolution(w, h)),
        CurrentRefreshRateHz = Spec<double>.Detected(60),
        MaxRefreshRateHz = Spec<double>.Firmware(60),
        PhysicalSize = Spec<PhysicalSize>.Firmware(new PhysicalSize(widthCm, heightCm)),
        DiagonalInches = Spec<double>.Estimated(Normalisation.DisplayNaming.EstimateDiagonalInches(new PhysicalSize(widthCm, heightCm))),
        Touch = Spec<bool>.Detected(false),
        ResolutionMarketingName = marketing,
    };

    public static BatteryInfo Battery(int design, int full, int cycles) => BatteryHealthCalculator.Combine(
        new[] { new BatteryPack { Name = "Battery", DesignCapacityMwh = Spec<int>.Firmware(design), FullChargeCapacityMwh = Spec<int>.Firmware(full), CycleCount = Spec<int>.Firmware(cycles), Chemistry = Spec<string>.Firmware("Lithium-ion polymer") } },
        Spec<int>.Detected(64), Spec<string>.Detected("Charging"), Spec<string>.Detected("OK"));

    public static ConnectivityInfo Wifi6Connectivity() => new()
    {
        WifiAdapter = Spec<string>.Detected("Intel(R) Wi-Fi 6E AX211 160MHz"),
        WifiStandard = Spec<string>.Detected("Wi-Fi 6E (802.11ax, 6 GHz)"),
        Bluetooth = Spec<bool>.Detected(true),
        Ethernet = Spec<bool>.Detected(false),
        Webcam = Spec<bool>.Detected(true),
        Audio = Spec<bool>.Detected(true),
        FingerprintReader = Spec<bool>.Detected(true),
        Cameras = new[] { "Integrated Camera" },
    };

    public static OperatingSystemInfo Windows11Home() => new()
    {
        Name = Spec<string>.Detected("Windows 11"),
        Edition = Spec<string>.Detected("Home"),
        Version = Spec<string>.Detected("24H2"),
        Build = Spec<string>.Detected("26100.4061"),
        BuildNumber = 26100,
        Architecture = Spec<string>.Detected("64-bit"),
        Activation = Spec<ActivationStatus>.Detected(ActivationStatus.Licensed),
        LicenceChannel = Spec<string>.Detected("OEM digital"),
        TpmVersion = Spec<string>.Detected("2.0"),
        SecureBootEnabled = Spec<bool>.Detected(true),
        FirmwareType = Spec<string>.Detected("UEFI"),
    };
}

/// <summary>Wraps a prepared snapshot as a provider set so the scanner pipeline can be exercised.</summary>
public static class FakeProviders
{
    public class Fake<T> : IHardwareProvider<T>
    {
        private readonly Func<CancellationToken, Task<T>> _factory;
        public Fake(string name, Func<CancellationToken, Task<T>> factory) { ComponentName = name; _factory = factory; }
        public string ComponentName { get; }
        public Task<T> DetectAsync(CancellationToken cancellationToken) => _factory(cancellationToken);
    }

    private sealed class FakeIdentity : Fake<DeviceIdentityResult>, IDeviceIdentityProvider { public FakeIdentity(Func<CancellationToken, Task<DeviceIdentityResult>> f) : base("Device", f) { } }
    private sealed class FakeProcessor : Fake<ProcessorInfo>, IProcessorProvider { public FakeProcessor(Func<CancellationToken, Task<ProcessorInfo>> f) : base("Processor", f) { } }
    private sealed class FakeMemory : Fake<MemoryInfo>, IMemoryProvider { public FakeMemory(Func<CancellationToken, Task<MemoryInfo>> f) : base("Memory", f) { } }
    private sealed class FakeStorage : Fake<StorageResult>, IStorageProvider { public FakeStorage(Func<CancellationToken, Task<StorageResult>> f) : base("Storage", f) { } }
    private sealed class FakeGraphics : Fake<IReadOnlyList<GraphicsAdapter>>, IGraphicsProvider { public FakeGraphics(Func<CancellationToken, Task<IReadOnlyList<GraphicsAdapter>>> f) : base("Graphics", f) { } }
    private sealed class FakeDisplays : Fake<IReadOnlyList<DisplayInfo>>, IDisplayProvider { public FakeDisplays(Func<CancellationToken, Task<IReadOnlyList<DisplayInfo>>> f) : base("Display", f) { } }
    private sealed class FakeBattery : Fake<BatteryResult>, IBatteryProvider { public FakeBattery(Func<CancellationToken, Task<BatteryResult>> f) : base("Battery", f) { } }
    private sealed class FakeConnectivity : Fake<ConnectivityInfo>, IConnectivityProvider { public FakeConnectivity(Func<CancellationToken, Task<ConnectivityInfo>> f) : base("Connectivity", f) { } }
    private sealed class FakeOs : Fake<OperatingSystemInfo>, IOperatingSystemProvider { public FakeOs(Func<CancellationToken, Task<OperatingSystemInfo>> f) : base("Windows", f) { } }

    public static HardwareProviderSet FromSnapshot(DeviceSnapshot s,
        Func<CancellationToken, Task<ProcessorInfo>>? processor = null,
        Func<CancellationToken, Task<StorageResult>>? storage = null,
        Func<CancellationToken, Task<BatteryResult>>? battery = null)
    {
        return new HardwareProviderSet(
            new FakeIdentity(_ => Task.FromResult(new DeviceIdentityResult(s.Identity, s.Private))),
            new FakeProcessor(processor ?? (_ => Task.FromResult(s.Processor))),
            new FakeMemory(_ => Task.FromResult(s.Memory)),
            new FakeStorage(storage ?? (_ => Task.FromResult(new StorageResult(s.Storage, s.Private.StorageSerialNumbers, Array.Empty<ScanIssue>())))),
            new FakeGraphics(_ => Task.FromResult(s.Graphics)),
            new FakeDisplays(_ => Task.FromResult(s.Displays)),
            new FakeBattery(battery ?? (_ => Task.FromResult(new BatteryResult(s.Battery, s.Private.BatterySerialNumbers)))),
            new FakeConnectivity(_ => Task.FromResult(s.Connectivity)),
            new FakeOs(_ => Task.FromResult(s.OperatingSystem)));
    }
}
