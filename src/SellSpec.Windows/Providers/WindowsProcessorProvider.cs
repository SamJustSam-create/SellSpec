using Microsoft.Win32;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed class WindowsProcessorProvider : IProcessorProvider
{
    private readonly IScanLogger _logger;

    public WindowsProcessorProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Processor";

    public async Task<ProcessorInfo> DetectAsync(CancellationToken cancellationToken)
    {
        var rows = await WmiQuery.TryQueryAsync(WmiQuery.Cimv2,
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Architecture, SocketDesignation, L2CacheSize, L3CacheSize FROM Win32_Processor",
            cancellationToken, _logger, ComponentName).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            _logger.Log(LogLevel.Warning, ComponentName, "Win32_Processor returned nothing; falling back to the registry.");
            return FromRegistry();
        }

        var first = rows[0];
        var normalised = ProcessorNameNormaliser.Normalise(first.GetString("Name"));
        var cores = rows.Sum(r => r.GetInt32("NumberOfCores") ?? 0);
        var logical = rows.Sum(r => r.GetInt32("NumberOfLogicalProcessors") ?? 0);
        if (logical == 0) logical = Environment.ProcessorCount;
        var clock = first.GetInt32("MaxClockSpeed");
        var architecture = ArchitectureName(first.GetInt32("Architecture"));
        var manufacturer = normalised.Manufacturer ?? ManufacturerNormaliser.Normalise(first.GetString("Manufacturer"));

        return new ProcessorInfo
        {
            Manufacturer = manufacturer is null ? Spec<string>.Unavailable() : Spec<string>.Detected(manufacturer),
            Name = normalised.Name == "Unknown processor" ? Spec<string>.Unavailable("The processor did not report a name.") : Spec<string>.Detected(normalised.Name),
            FamilyShortName = normalised.FamilyShortName,
            RawName = first.GetString("Name"),
            PhysicalCores = cores > 0 ? Spec<int>.Detected(cores) : Spec<int>.Unavailable(),
            LogicalProcessors = logical > 0 ? Spec<int>.Detected(logical) : Spec<int>.Unavailable(),
            ClockSpeedMHz = clock is > 0 ? Spec<int>.Firmware(clock.Value, "Reported base/maximum clock; actual speeds vary with boost and power limits.") : Spec<int>.Unavailable(),
            Architecture = architecture is null ? Spec<string>.Unavailable() : Spec<string>.Detected(architecture),
            InstalledCount = Spec<int>.Detected(rows.Count),
            L2CacheKB = first.GetInt32("L2CacheSize") is > 0 and var l2 ? Spec<int>.Detected(l2) : Spec<int>.Unavailable(),
            L3CacheKB = first.GetInt32("L3CacheSize") is > 0 and var l3 ? Spec<int>.Detected(l3) : Spec<int>.Unavailable(),
            SocketDesignation = first.GetString("SocketDesignation"),
        };
    }

    private static ProcessorInfo FromRegistry()
    {
        string? name = null;
        int? mhz = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            name = key?.GetValue("ProcessorNameString") as string;
            mhz = key?.GetValue("~MHz") as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Fall through with whatever we have.
        }

        var normalised = ProcessorNameNormaliser.Normalise(name);
        return new ProcessorInfo
        {
            Manufacturer = normalised.Manufacturer is null ? Spec<string>.Unavailable() : Spec<string>.Detected(normalised.Manufacturer),
            Name = name is null ? Spec<string>.Unavailable("Processor information could not be read.") : Spec<string>.Detected(normalised.Name),
            FamilyShortName = normalised.FamilyShortName,
            RawName = name,
            LogicalProcessors = Spec<int>.Detected(Environment.ProcessorCount),
            PhysicalCores = Spec<int>.Unavailable("Core count not available without WMI."),
            ClockSpeedMHz = mhz is > 0 ? Spec<int>.Firmware(mhz.Value) : Spec<int>.Unavailable(),
            Architecture = Spec<string>.Detected(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()),
            InstalledCount = Spec<int>.Estimated(1),
        };
    }

    private static string? ArchitectureName(int? code) => code switch
    {
        0 => "x86",
        5 => "ARM",
        6 => "Itanium",
        9 => "x64",
        12 => "ARM64",
        _ => null,
    };
}
