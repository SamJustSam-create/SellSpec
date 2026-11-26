namespace SellSpec.Core.Models;

/// <summary>Traffic-light style status used by the device health dashboard. Every status is paired with a text label in the UI.</summary>
public enum HealthStatusKind
{
    /// <summary>Grey: unavailable, unsupported or not tested. Never a pass.</summary>
    Unknown,

    /// <summary>Green: confirmed passing result or healthy reported value.</summary>
    Good,

    /// <summary>Amber: attention recommended.</summary>
    Attention,

    /// <summary>Red: reported issue or failed test.</summary>
    Issue,

    /// <summary>Grey: does not apply to this device type.</summary>
    NotApplicable,
}

public enum DeviceFormFactor
{
    Unknown,
    Laptop,
    Desktop,
    AllInOne,
    Tablet,
    Convertible,
    MiniPc,
    Server,
    Other,
}

public enum StorageMediaType
{
    Unknown,
    Ssd,
    Hdd,
    /// <summary>Storage-class memory, e.g. Intel Optane.</summary>
    Scm,
}

public enum StorageBus
{
    Unknown,
    Sata,
    Nvme,
    Usb,
    SdCard,
    Raid,
    Sas,
    Scsi,
    Virtual,
    Other,
}

public enum StorageLocation
{
    Unknown,
    Internal,
    External,
    Removable,
    Virtual,
}

public enum GraphicsKind
{
    Unknown,
    Integrated,
    Dedicated,
    Virtual,
}

public enum BatteryPresence
{
    Unknown,
    Present,
    NotPresent,
}

public enum ActivationStatus
{
    Unknown,
    Licensed,
    Unlicensed,
    GracePeriod,
    Notification,
}
