using SellSpec.Core.Models;

namespace SellSpec.Core.Normalisation;

/// <summary>Decides form factor, custom-built status and virtual machine status from firmware clues.</summary>
public static class DeviceClassifier
{
    /// <summary>SMBIOS chassis types that indicate a portable device.</summary>
    public static DeviceFormFactor FromChassisTypes(IEnumerable<int> chassisTypes, int? pcSystemType, bool? hasBattery)
    {
        var types = chassisTypes.ToList();
        foreach (var t in types)
        {
            switch (t)
            {
                case 8:  // Portable
                case 9:  // Laptop
                case 10: // Notebook
                case 14: // Sub Notebook
                    return DeviceFormFactor.Laptop;
                case 11: // Hand Held
                case 30: // Tablet
                    return DeviceFormFactor.Tablet;
                case 31: // Convertible
                case 32: // Detachable
                    return DeviceFormFactor.Convertible;
                case 13: // All in One
                    return DeviceFormFactor.AllInOne;
                case 35: // Mini PC
                case 36: // Stick PC
                    return DeviceFormFactor.MiniPc;
                case 3:  // Desktop
                case 4:  // Low Profile Desktop
                case 5:  // Pizza Box
                case 6:  // Mini Tower
                case 7:  // Tower
                case 15: // Space-saving
                case 16: // Lunch Box
                case 24: // Sealed-case PC
                    return DeviceFormFactor.Desktop;
                case 17: // Main Server Chassis
                case 23: // Rack Mount Chassis
                case 25: // Multi-system chassis
                case 28: // Blade
                case 29: // Blade Enclosure
                    return DeviceFormFactor.Server;
            }
        }

        // Fall back to Win32_ComputerSystem.PCSystemType: 1 Desktop, 2 Mobile, 3 Workstation, 4 Enterprise Server, 5 SOHO Server, 6 Appliance PC, 7 Performance Server, 8 Maximum/Slate.
        switch (pcSystemType)
        {
            case 2:
                return DeviceFormFactor.Laptop;
            case 8:
                return DeviceFormFactor.Tablet;
            case 1:
            case 3:
            case 6:
                return hasBattery == true ? DeviceFormFactor.Laptop : DeviceFormFactor.Desktop;
            case 4:
            case 5:
            case 7:
                return DeviceFormFactor.Server;
        }

        return hasBattery switch
        {
            true => DeviceFormFactor.Laptop,
            false => DeviceFormFactor.Desktop,
            _ => DeviceFormFactor.Unknown,
        };
    }

    public sealed record VirtualMachineVerdict(bool IsVirtual, string? Platform);

    public static VirtualMachineVerdict DetectVirtualMachine(string? manufacturer, string? model, string? biosVendor, string? biosVersion, string? boardProduct)
    {
        var haystack = string.Join(" | ", new[] { manufacturer, model, biosVendor, biosVersion, boardProduct }.Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();
        if (haystack.Length == 0) return new VirtualMachineVerdict(false, null);

        (string Needle, string Platform)[] signatures =
        {
            ("vmware", "VMware"),
            ("virtualbox", "Oracle VirtualBox"),
            ("innotek", "Oracle VirtualBox"),
            ("virtual machine", "Microsoft Hyper-V"),
            ("hyper-v", "Microsoft Hyper-V"),
            ("qemu", "QEMU/KVM"),
            ("kvm", "QEMU/KVM"),
            ("bochs", "QEMU/Bochs"),
            ("seabios", "QEMU/KVM"),
            ("parallels", "Parallels Desktop"),
            ("xen", "Xen"),
            ("bhyve", "bhyve"),
            ("google compute engine", "Google Compute Engine"),
            ("amazon ec2", "Amazon EC2"),
            ("nutanix", "Nutanix AHV"),
            ("proxmox", "Proxmox VE"),
            ("cloud hypervisor", "Cloud Hypervisor"),
            ("utm", "UTM"),
        };

        foreach (var (needle, platform) in signatures)
        {
            // "kvm" and "xen" are short; require word boundaries to avoid matching e.g. "Nexen".
            if (needle is "kvm" or "xen" or "utm")
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(haystack, $@"\b{needle}\b"))
                {
                    return new VirtualMachineVerdict(true, platform);
                }
                continue;
            }

            if (haystack.Contains(needle, StringComparison.Ordinal))
            {
                return new VirtualMachineVerdict(true, platform);
            }
        }

        return new VirtualMachineVerdict(false, null);
    }

    /// <summary>
    /// A PC is treated as custom-built when the system manufacturer/model are placeholders, or when the system
    /// "model" is actually the motherboard model from a motherboard vendor on a desktop-class chassis.
    /// </summary>
    public static bool IsCustomBuilt(string? normalisedManufacturer, string? cleanedModel, string? boardVendor, string? boardProduct, DeviceFormFactor formFactor)
    {
        if (formFactor is DeviceFormFactor.Laptop or DeviceFormFactor.Tablet or DeviceFormFactor.Convertible)
        {
            return false;
        }

        var manufacturerMissing = normalisedManufacturer is null;
        var modelMissing = cleanedModel is null;
        if (manufacturerMissing && modelMissing) return true;

        if (ManufacturerNormaliser.IsMotherboardVendor(normalisedManufacturer))
        {
            if (modelMissing) return true;
            // Gigabyte: system model "B550M K" equals the board. MSI: system model "MS-7C56" is the code inside
            // the board name "MAG B550 TOMAHAWK (MS-7C56)".
            if (boardProduct is not null && string.Equals(cleanedModel, boardProduct, StringComparison.OrdinalIgnoreCase)) return true;
            if (boardProduct is not null && boardProduct.Contains(cleanedModel!, StringComparison.OrdinalIgnoreCase)) return true;
            // ASUS/MSI/Gigabyte also sell complete desktops; those carry product names ("ROG Strix G15DK") that differ from the board.
            return false;
        }

        if (manufacturerMissing && boardVendor is not null && ManufacturerNormaliser.IsMotherboardVendor(boardVendor))
        {
            return true;
        }

        return false;
    }
}
