using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Windows.Native;
using SellSpec.Windows.Providers;

namespace SellSpec.Windows;

/// <summary>Entry point for the Windows detection stack.</summary>
public static class WindowsHardwareProviders
{
    public static bool IsElevated() => NativeInfo.IsElevated();

    public static HardwareProviderSet Create(IScanLogger? logger = null)
    {
        logger ??= NullScanLogger.Instance;
        var elevated = IsElevated();
        return new HardwareProviderSet(
            new WindowsDeviceIdentityProvider(logger),
            new WindowsProcessorProvider(logger),
            new WindowsMemoryProvider(logger),
            new WindowsStorageProvider(logger, elevated),
            new WindowsGraphicsProvider(logger),
            new WindowsDisplayProvider(logger),
            new WindowsBatteryProvider(logger),
            new WindowsConnectivityProvider(logger),
            new WindowsOperatingSystemProvider(logger));
    }

    public static HardwareScanner CreateScanner(IScanLogger? logger = null, ScanOptions? options = null)
    {
        var elevated = IsElevated();
        options ??= ScanOptions.Default with { IsElevated = elevated };
        return new HardwareScanner(Create(logger), options, logger);
    }
}
