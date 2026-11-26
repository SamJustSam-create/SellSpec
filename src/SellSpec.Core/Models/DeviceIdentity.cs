namespace SellSpec.Core.Models;

/// <summary>Who made the device and what it is. Values are normalised; raw firmware strings are kept for the technical view.</summary>
public sealed record DeviceIdentity
{
    public Spec<string> Manufacturer { get; init; } = Spec<string>.Unavailable();
    public Spec<string> ProductName { get; init; } = Spec<string>.Unavailable();
    public Spec<string> ModelNumber { get; init; } = Spec<string>.Unavailable();
    public Spec<string> Sku { get; init; } = Spec<string>.Unavailable();
    public Spec<string> SystemFamily { get; init; } = Spec<string>.Unavailable();
    public Spec<DeviceFormFactor> FormFactor { get; init; } = Spec<DeviceFormFactor>.Unavailable();
    public Spec<string> Architecture { get; init; } = Spec<string>.Unavailable();

    public Spec<string> BoardManufacturer { get; init; } = Spec<string>.Unavailable();
    public Spec<string> BoardModel { get; init; } = Spec<string>.Unavailable();
    public Spec<string> BoardVersion { get; init; } = Spec<string>.Unavailable();

    public Spec<string> BiosVendor { get; init; } = Spec<string>.Unavailable();
    public Spec<string> BiosVersion { get; init; } = Spec<string>.Unavailable();
    public Spec<DateOnly> BiosReleaseDate { get; init; } = Spec<DateOnly>.Unavailable();

    /// <summary>"UEFI" or "Legacy BIOS".</summary>
    public Spec<string> FirmwareType { get; init; } = Spec<string>.Unavailable();

    /// <summary>True when the system manufacturer/model are generic or equal the motherboard, i.e. a self-built PC.</summary>
    public bool IsCustomBuilt { get; init; }

    /// <summary>True when the device looks like a virtual machine. Listings for VMs make no sense and the UI warns about it.</summary>
    public bool IsVirtualMachine { get; init; }
    public string? VirtualisationPlatform { get; init; }

    /// <summary>SMBIOS chassis type codes as reported (for the technical view).</summary>
    public IReadOnlyList<int> ChassisTypeCodes { get; init; } = Array.Empty<int>();

    public string? RawManufacturer { get; init; }
    public string? RawModel { get; init; }
    public string? RawVersion { get; init; }

    /// <summary>Buyer-facing device name, e.g. "Lenovo IdeaPad Slim 5 14IAH8" or "Custom-built desktop PC".</summary>
    public string DisplayName
    {
        get
        {
            if (IsCustomBuilt)
            {
                return FormFactor.ValueOr(DeviceFormFactor.Desktop) == DeviceFormFactor.Desktop
                    ? "Custom-built desktop PC"
                    : "Custom-built PC";
            }

            var brand = Manufacturer.HasValue ? Manufacturer.Value! : null;
            var product = ProductName.HasValue ? ProductName.Value! : (ModelNumber.HasValue ? ModelNumber.Value! : null);

            if (brand is null && product is null)
            {
                return FormFactorLabel(FormFactor.ValueOr(DeviceFormFactor.Unknown)) is { } ff && ff != "Computer"
                    ? $"Windows {ff}"
                    : "Windows PC";
            }

            if (brand is null)
            {
                return product!;
            }

            if (product is null)
            {
                return $"{brand} {FormFactorLabel(FormFactor.ValueOr(DeviceFormFactor.Unknown))}".Trim();
            }

            return product.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? product : $"{brand} {product}";
        }
    }

    /// <summary>Secondary line for the overview, e.g. "Gigabyte B550M K motherboard" for a custom build.</summary>
    public string? SecondaryName
    {
        get
        {
            if (IsCustomBuilt && BoardModel.HasValue)
            {
                var vendor = BoardManufacturer.HasValue ? BoardManufacturer.Value! + " " : string.Empty;
                var model = BoardModel.Value!;
                return model.StartsWith(vendor.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? $"{model} motherboard"
                    : $"{vendor}{model} motherboard";
            }

            if (ProductName.HasValue && ModelNumber.HasValue &&
                !string.Equals(ProductName.Value, ModelNumber.Value, StringComparison.OrdinalIgnoreCase))
            {
                return $"Model {ModelNumber.Value}";
            }

            return null;
        }
    }

    public static string FormFactorLabel(DeviceFormFactor formFactor) => formFactor switch
    {
        DeviceFormFactor.Laptop => "Laptop",
        DeviceFormFactor.Desktop => "Desktop PC",
        DeviceFormFactor.AllInOne => "All-in-one PC",
        DeviceFormFactor.Tablet => "Tablet",
        DeviceFormFactor.Convertible => "2-in-1 laptop",
        DeviceFormFactor.MiniPc => "Mini PC",
        DeviceFormFactor.Server => "Server",
        _ => "Computer",
    };
}

/// <summary>
/// Identifiers that are collected for the owner's own reference only. They are never written to listings,
/// reports, JSON exports or logs. <see cref="Privacy.Redactor"/> verifies this on every export.
/// </summary>
public sealed record PrivateIdentifiers
{
    public string? SystemSerialNumber { get; init; }
    public string? SystemUuid { get; init; }
    public string? BaseBoardSerialNumber { get; init; }
    public string? BiosSerialNumber { get; init; }
    public IReadOnlyList<string> StorageSerialNumbers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BatterySerialNumbers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DisplaySerialNumbers { get; init; } = Array.Empty<string>();

    /// <summary>Every non-empty private value, used by redaction checks.</summary>
    public IEnumerable<string> AllValues()
    {
        foreach (var v in new[] { SystemSerialNumber, SystemUuid, BaseBoardSerialNumber, BiosSerialNumber })
        {
            if (!string.IsNullOrWhiteSpace(v)) yield return v;
        }
        foreach (var v in StorageSerialNumbers.Concat(BatterySerialNumbers).Concat(DisplaySerialNumbers))
        {
            if (!string.IsNullOrWhiteSpace(v)) yield return v;
        }
    }

    public static PrivateIdentifiers Empty { get; } = new();
}
