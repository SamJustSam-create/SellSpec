using SellSpec.Core.Models;

namespace SellSpec.Core.Detection;

/// <summary>
/// A single hardware detection component. Implementations must be side-effect free, must not require
/// administrative rights, and should honour the cancellation token. Throwing is acceptable: the scanner
/// isolates failures per component.
/// </summary>
public interface IHardwareProvider<T>
{
    /// <summary>Stable, user-facing component name (e.g. "Processor").</summary>
    string ComponentName { get; }

    Task<T> DetectAsync(CancellationToken cancellationToken);
}

public interface IDeviceIdentityProvider : IHardwareProvider<DeviceIdentityResult> { }
public interface IProcessorProvider : IHardwareProvider<ProcessorInfo> { }
public interface IMemoryProvider : IHardwareProvider<MemoryInfo> { }
public interface IStorageProvider : IHardwareProvider<StorageResult> { }
public interface IGraphicsProvider : IHardwareProvider<IReadOnlyList<GraphicsAdapter>> { }
public interface IDisplayProvider : IHardwareProvider<IReadOnlyList<DisplayInfo>> { }
public interface IBatteryProvider : IHardwareProvider<BatteryResult> { }
public interface IConnectivityProvider : IHardwareProvider<ConnectivityInfo> { }
public interface IOperatingSystemProvider : IHardwareProvider<OperatingSystemInfo> { }

/// <summary>Identity plus the private identifiers that were read alongside it.</summary>
public sealed record DeviceIdentityResult(DeviceIdentity Identity, PrivateIdentifiers Private)
{
    public static DeviceIdentityResult Empty { get; } = new(new DeviceIdentity(), PrivateIdentifiers.Empty);
}

public sealed record StorageResult(IReadOnlyList<StorageDevice> Devices, IReadOnlyList<string> SerialNumbers, IReadOnlyList<ScanIssue> Issues)
{
    public static StorageResult Empty { get; } = new(Array.Empty<StorageDevice>(), Array.Empty<string>(), Array.Empty<ScanIssue>());
}

public sealed record BatteryResult(BatteryInfo Battery, IReadOnlyList<string> SerialNumbers)
{
    public static BatteryResult Empty { get; } = new(BatteryInfo.Unavailable(), Array.Empty<string>());
}

/// <summary>The full set of providers the scanner needs. Platform packages (e.g. SellSpec.Windows) supply an instance.</summary>
public sealed record HardwareProviderSet(
    IDeviceIdentityProvider Identity,
    IProcessorProvider Processor,
    IMemoryProvider Memory,
    IStorageProvider Storage,
    IGraphicsProvider Graphics,
    IDisplayProvider Displays,
    IBatteryProvider Battery,
    IConnectivityProvider Connectivity,
    IOperatingSystemProvider OperatingSystem);

public sealed record ScanOptions
{
    /// <summary>Per-component timeout. Slow WMI classes (licensing, storage reliability) get a longer budget.</summary>
    public TimeSpan ComponentTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan SlowComponentTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Overall budget. When exceeded the scan returns what it has and marks the rest unavailable.</summary>
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsElevated { get; init; }

    public static ScanOptions Default { get; } = new();
}
