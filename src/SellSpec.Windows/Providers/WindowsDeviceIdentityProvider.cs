using System.Runtime.InteropServices;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Native;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
    private readonly IScanLogger _logger;

    public WindowsDeviceIdentityProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Device";

    public async Task<DeviceIdentityResult> DetectAsync(CancellationToken cancellationToken)
    {
        var csTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber, PCSystemType, PCSystemTypeEx, HypervisorPresent, SystemType FROM Win32_ComputerSystem", cancellationToken, _logger, ComponentName);
        var cspTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Vendor, Name, Version, IdentifyingNumber, UUID FROM Win32_ComputerSystemProduct", cancellationToken, _logger, ComponentName);
        var boardTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard", cancellationToken, _logger, ComponentName);
        var biosTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS", cancellationToken, _logger, ComponentName);
        var enclosureTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure", cancellationToken, _logger, ComponentName);

        await Task.WhenAll(csTask, cspTask, boardTask, biosTask, enclosureTask).ConfigureAwait(false);

        var cs = csTask.Result.FirstOrDefault();
        var csp = cspTask.Result.FirstOrDefault();
        var board = boardTask.Result.FirstOrDefault();
        var bios = biosTask.Result.FirstOrDefault();
        var chassis = enclosureTask.Result.SelectMany(r => r.GetInt32Array("ChassisTypes") ?? Array.Empty<int>()).Distinct().ToList();

        var rawManufacturer = cs?.GetString("Manufacturer") ?? csp?.GetString("Vendor");
        var rawModel = cs?.GetString("Model") ?? csp?.GetString("Name");
        var rawVersion = csp?.GetString("Version");
        var rawFamily = cs?.GetString("SystemFamily");
        var rawSku = cs?.GetString("SystemSKUNumber");

        var brand = ManufacturerNormaliser.Normalise(rawManufacturer);
        var model = GenericValueDetector.Clean(rawModel);
        var version = GenericValueDetector.Clean(rawVersion);
        var family = GenericValueDetector.Clean(rawFamily);
        var sku = GenericValueDetector.Clean(rawSku);

        var boardVendor = ManufacturerNormaliser.Normalise(board?.GetString("Manufacturer"));
        var boardProduct = GenericValueDetector.Clean(board?.GetString("Product"));
        var boardVersion = GenericValueDetector.Clean(board?.GetString("Version"));

        var biosVendor = ManufacturerNormaliser.Normalise(bios?.GetString("Manufacturer"));
        var biosVersion = GenericValueDetector.Clean(bios?.GetString("SMBIOSBIOSVersion"));
        var biosDate = bios?.GetDateTime("ReleaseDate");

        var formFactor = DeviceClassifier.FromChassisTypes(chassis, cs?.GetInt32("PCSystemType"), hasBattery: null);
        var vm = DeviceClassifier.DetectVirtualMachine(rawManufacturer, rawModel, bios?.GetString("Manufacturer"), biosVersion, boardProduct);
        if (!vm.IsVirtual && cs?.GetBool("HypervisorPresent") == true && brand == "Microsoft" && (model?.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            vm = new DeviceClassifier.VirtualMachineVerdict(true, "Microsoft Hyper-V");
        }

        var customBuilt = !vm.IsVirtual && DeviceClassifier.IsCustomBuilt(brand, model, boardVendor, boardProduct, formFactor);

        // Resolve a buyer-facing product name. Lenovo (and some others) put a machine-type code in Model and
        // the marketing name in Version / SystemFamily.
        string? productName = model;
        string? modelNumber = model;
        if (model is not null && LooksLikeCode(model) && (version ?? family) is { } descriptive && !LooksLikeCode(descriptive))
        {
            productName = descriptive;
        }
        else if (model is null && (version ?? family) is { } fallbackName && !LooksLikeCode(fallbackName))
        {
            productName = fallbackName;
        }
        if (productName is not null && brand is not null)
        {
            productName = ManufacturerNormaliser.StripBrand(productName, brand);
            if (productName.Length == 0) productName = null;
        }
        if (productName is not null && brand is null && rawManufacturer is not null)
        {
            productName = ManufacturerNormaliser.StripBrand(productName, rawManufacturer);
        }

        var architecture = cs?.GetString("SystemType") switch
        {
            { } s when s.StartsWith("x64", StringComparison.OrdinalIgnoreCase) => "x64",
            { } s when s.StartsWith("ARM64", StringComparison.OrdinalIgnoreCase) => "ARM64",
            { } s when s.StartsWith("X86", StringComparison.OrdinalIgnoreCase) => "x86",
            _ => RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "ARM64",
                Architecture.X86 => "x86",
                _ => null,
            },
        };

        var identity = new DeviceIdentity
        {
            Manufacturer = brand is null ? Spec<string>.Unavailable(rawManufacturer is null ? "Firmware did not report a manufacturer." : "Firmware reported a placeholder manufacturer name.") : Spec<string>.Firmware(brand),
            ProductName = productName is null ? Spec<string>.Unavailable("Firmware did not report a product name.") : Spec<string>.Firmware(productName),
            ModelNumber = modelNumber is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(modelNumber),
            Sku = sku is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(sku),
            SystemFamily = family is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(family),
            FormFactor = formFactor == DeviceFormFactor.Unknown ? Spec<DeviceFormFactor>.Unavailable("Firmware did not report a chassis type.") : Spec<DeviceFormFactor>.Firmware(formFactor),
            Architecture = architecture is null ? Spec<string>.Unavailable() : Spec<string>.Detected(architecture),
            BoardManufacturer = boardVendor is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(boardVendor),
            BoardModel = boardProduct is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(boardProduct),
            BoardVersion = boardVersion is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(boardVersion),
            BiosVendor = biosVendor is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(biosVendor),
            BiosVersion = biosVersion is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(biosVersion),
            BiosReleaseDate = biosDate is null ? Spec<DateOnly>.Unavailable() : Spec<DateOnly>.Firmware(DateOnly.FromDateTime(biosDate.Value)),
            FirmwareType = NativeInfo.GetFirmwareType() is { } fw ? Spec<string>.Detected(fw) : Spec<string>.Unavailable(),
            IsCustomBuilt = customBuilt,
            IsVirtualMachine = vm.IsVirtual,
            VirtualisationPlatform = vm.Platform,
            ChassisTypeCodes = chassis,
            RawManufacturer = rawManufacturer,
            RawModel = rawModel,
            RawVersion = rawVersion,
        };

        var privateIds = new PrivateIdentifiers
        {
            SystemSerialNumber = GenericValueDetector.Clean(csp?.GetString("IdentifyingNumber")),
            SystemUuid = CleanUuid(csp?.GetString("UUID")),
            BaseBoardSerialNumber = GenericValueDetector.Clean(board?.GetString("SerialNumber")),
            BiosSerialNumber = GenericValueDetector.Clean(bios?.GetString("SerialNumber")),
        };

        return new DeviceIdentityResult(identity, privateIds);
    }

    /// <summary>"82XF", "20U9S0JC00", "HP" style codes versus descriptive names like "IdeaPad Slim 5 14IAH8".</summary>
    internal static bool LooksLikeCode(string value)
    {
        var v = value.Trim();
        if (v.Contains(' ')) return false;
        if (v.Length > 12) return false;
        return v.Any(char.IsDigit) || v.Length <= 4;
    }

    private static string? CleanUuid(string? uuid)
    {
        var cleaned = GenericValueDetector.Clean(uuid);
        if (cleaned is null) return null;
        var hex = cleaned.Replace("-", string.Empty);
        if (hex.All(c => c == '0') || hex.All(c => c is 'F' or 'f')) return null;
        return cleaned;
    }
}
