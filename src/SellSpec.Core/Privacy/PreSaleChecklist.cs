namespace SellSpec.Core.Privacy;

/// <summary>One informational step. <see cref="SettingsUri"/> is an optional ms-settings: deep link the app may open for the user.</summary>
public sealed record ChecklistItem(string Id, string Title, string Description, string? SettingsUri = null, string? SettingsLabel = null);

/// <summary>
/// Informational only. SellSpec never deletes files, resets Windows, changes encryption or touches accounts;
/// it explains what the seller should consider doing and where to find the relevant Windows settings.
/// </summary>
public static class PreSaleChecklist
{
    public static IReadOnlyList<ChecklistItem> Items { get; } = new[]
    {
        new ChecklistItem(
            "backup",
            "Back up important files",
            "Copy documents, photos and anything else you want to keep to an external drive or cloud storage before you reset the device. A reset removes everything.",
            "ms-settings:backup",
            "Open Backup settings"),
        new ChecklistItem(
            "signout",
            "Sign out of personal accounts",
            "Sign out of your Microsoft account, email, browsers, OneDrive, Steam, Adobe, Office and any other apps. Deauthorise software that limits the number of installs.",
            "ms-settings:yourinfo",
            "Open Your info"),
        new ChecklistItem(
            "data",
            "Remove personal data",
            "Clear browser passwords and saved payment methods, delete downloaded files and empty the Recycle Bin. A full reset (below) is the reliable way to remove everything.",
            null,
            null),
        new ChecklistItem(
            "bitlocker",
            "Review device encryption and recovery keys",
            "If BitLocker or Device Encryption is on, make sure you have your recovery key saved elsewhere, then let the reset remove the encrypted data. Do not hand over a device that is still locked to your account.",
            "ms-settings:deviceencryption",
            "Open Device encryption"),
        new ChecklistItem(
            "accounts",
            "Remove the device from your online accounts",
            "Remove this PC from your Microsoft account device list, Find My Device, and any manufacturer or anti-theft services so the buyer can register it.",
            "ms-settings:findmydevice",
            "Open Find my device"),
        new ChecklistItem(
            "reset",
            "Perform a factory reset",
            "Use Windows' Reset this PC with the \"Remove everything\" option and, when offered, the \"Clean data\" option. This reinstalls Windows and removes your files, apps and settings.",
            "ms-settings:recovery",
            "Open Recovery settings"),
        new ChecklistItem(
            "verify",
            "Confirm personal information is no longer accessible",
            "After the reset, Windows should start at the out-of-box setup screen. Do not sign in; the buyer will set up their own account.",
            null,
            null),
        new ChecklistItem(
            "accessories",
            "Include the required accessories",
            "Gather the charger, any cables or adapters, and the original box if you still have it. Mention anything that is missing in your listing.",
            null,
            null),
    };
}
