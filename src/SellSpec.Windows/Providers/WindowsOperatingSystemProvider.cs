using System.Text.RegularExpressions;
using Microsoft.Win32;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Native;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed partial class WindowsOperatingSystemProvider : IOperatingSystemProvider
{
    private const string WindowsActivationAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
    private readonly IScanLogger _logger;

    public WindowsOperatingSystemProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Windows";

    public async Task<OperatingSystemInfo> DetectAsync(CancellationToken cancellationToken)
    {
        // Deliberately not selecting RegisteredUser or SerialNumber.
        var osTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate FROM Win32_OperatingSystem", cancellationToken, _logger, ComponentName);
        // Licensing enumeration can be slow; the filter keeps it to the Windows product with a key installed. PartialProductKey is never selected.
        var licenceTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2,
            $"SELECT LicenseStatus, Description FROM SoftwareLicensingProduct WHERE ApplicationID = '{WindowsActivationAppId}' AND PartialProductKey IS NOT NULL",
            cancellationToken, _logger, ComponentName, TimeSpan.FromSeconds(25));

        var registry = ReadRegistry();
        var firmware = NativeInfo.GetFirmwareType();
        var tpm = NativeInfo.GetTpm();
        var secureBoot = ReadSecureBoot();

        await Task.WhenAll(osTask, licenceTask).ConfigureAwait(false);

        var os = osTask.Result.FirstOrDefault();
        var caption = os?.GetString("Caption");
        var buildText = os?.GetString("BuildNumber") ?? registry.CurrentBuild;
        int? build = int.TryParse(buildText, out var b) ? b : null;

        var name = build switch
        {
            >= 22000 => "Windows 11",
            >= 10240 => "Windows 10",
            _ => caption is null ? null : WindowsName().Match(caption) is { Success: true } m ? m.Value : caption,
        };

        var edition = EditionFromCaption(caption) ?? EditionFromId(registry.EditionId);
        var version = registry.DisplayVersion ?? registry.ReleaseId;
        var fullBuild = build is { } bn ? (registry.Ubr is { } ubr ? $"{bn}.{ubr}" : bn.ToString()) : null;
        var architecture = os?.GetString("OSArchitecture") ?? (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

        Spec<ActivationStatus> activation;
        Spec<string> channel = Spec<string>.Unavailable();
        var licence = licenceTask.Result.FirstOrDefault();
        if (licence is null)
        {
            activation = Spec<ActivationStatus>.Unavailable("Activation status could not be read.");
        }
        else
        {
            activation = licence.GetInt32("LicenseStatus") switch
            {
                1 => Spec<ActivationStatus>.Detected(ActivationStatus.Licensed),
                0 => Spec<ActivationStatus>.Detected(ActivationStatus.Unlicensed),
                2 or 3 or 4 or 6 => Spec<ActivationStatus>.Detected(ActivationStatus.GracePeriod),
                5 => Spec<ActivationStatus>.Detected(ActivationStatus.Notification),
                _ => Spec<ActivationStatus>.Unavailable(),
            };
            var channelText = ChannelFromDescription(licence.GetString("Description"));
            if (channelText is not null) channel = Spec<string>.Detected(channelText);
        }

        Spec<string> tpmSpec = tpm switch
        {
            null => Spec<string>.Unavailable("TPM status could not be read."),
            { Present: false } => Spec<string>.NotApplicable("No TPM was found. It may be disabled in firmware settings."),
            { Version: { } v } => Spec<string>.Detected(v),
            _ => Spec<string>.Detected("present", "A TPM is present but its version was not reported."),
        };

        Spec<bool> secureBootSpec = firmware == "Legacy BIOS"
            ? Spec<bool>.NotApplicable("Secure Boot requires UEFI firmware; Windows is installed in legacy BIOS mode.")
            : secureBoot is { } sb ? Spec<bool>.Detected(sb) : Spec<bool>.Unavailable("Secure Boot state was not exposed by the firmware.");

        return new OperatingSystemInfo
        {
            Name = name is null ? Spec<string>.Unavailable("Operating system name could not be read.") : Spec<string>.Detected(name),
            Edition = edition is null ? Spec<string>.Unavailable() : Spec<string>.Detected(edition),
            Version = version is null ? Spec<string>.Unavailable() : Spec<string>.Detected(version),
            Build = fullBuild is null ? Spec<string>.Unavailable() : Spec<string>.Detected(fullBuild),
            BuildNumber = build,
            Architecture = Spec<string>.Detected(architecture),
            InstallDate = os?.GetDateTime("InstallDate") is { } installed ? Spec<DateOnly>.Detected(DateOnly.FromDateTime(installed)) : Spec<DateOnly>.Unavailable(),
            Activation = activation,
            LicenceChannel = channel,
            TpmVersion = tpmSpec,
            SecureBootEnabled = secureBootSpec,
            FirmwareType = firmware is null ? Spec<string>.Unavailable() : Spec<string>.Detected(firmware),
        };
    }

    private sealed record RegistryInfo(string? DisplayVersion, string? ReleaseId, string? EditionId, string? CurrentBuild, int? Ubr);

    private RegistryInfo ReadRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return new RegistryInfo(
                key?.GetValue("DisplayVersion") as string,
                key?.GetValue("ReleaseId") as string,
                key?.GetValue("EditionID") as string,
                key?.GetValue("CurrentBuild") as string,
                key?.GetValue("UBR") as int?);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.Log(LogLevel.Warning, ComponentName, "CurrentVersion registry key could not be read.", ex);
            return new RegistryInfo(null, null, null, null, null);
        }
    }

    private bool? ReadSecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            var value = key?.GetValue("UEFISecureBootEnabled");
            return value is int i ? i == 1 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.Log(LogLevel.Warning, ComponentName, "Secure Boot registry key could not be read.", ex);
            return null;
        }
    }

    internal static string? EditionFromCaption(string? caption)
    {
        if (caption is null) return null;
        var m = EditionInCaption().Match(caption);
        if (!m.Success) return null;
        var edition = GenericValueDetector.CollapseWhitespace(m.Groups["edition"].Value);
        return edition.Length == 0 ? null : edition;
    }

    internal static string? EditionFromId(string? editionId) => editionId switch
    {
        null => null,
        "Core" => "Home",
        "CoreSingleLanguage" => "Home Single Language",
        "CoreCountrySpecific" => "Home China",
        "CoreN" => "Home N",
        "Professional" => "Pro",
        "ProfessionalN" => "Pro N",
        "ProfessionalEducation" => "Pro Education",
        "ProfessionalWorkstation" => "Pro for Workstations",
        "Enterprise" => "Enterprise",
        "EnterpriseN" => "Enterprise N",
        "EnterpriseS" => "Enterprise LTSC",
        "Education" => "Education",
        "EducationN" => "Education N",
        "IoTEnterprise" => "IoT Enterprise",
        "IoTEnterpriseS" => "IoT Enterprise LTSC",
        "Cloud" => "Home in S mode",
        "CloudE" => "SE",
        "ServerStandard" => "Server Standard",
        "ServerDatacenter" => "Server Datacenter",
        _ => editionId,
    };

    internal static string? ChannelFromDescription(string? description)
    {
        if (description is null) return null;
        var m = ChannelInDescription().Match(description);
        if (!m.Success) return null;
        var channel = m.Groups["channel"].Value.ToUpperInvariant();
        if (channel.StartsWith("OEM_DM", StringComparison.Ordinal)) return "OEM digital";
        if (channel.StartsWith("OEM", StringComparison.Ordinal)) return "OEM";
        if (channel.StartsWith("RETAIL", StringComparison.Ordinal) || channel.StartsWith("OCUR", StringComparison.Ordinal)) return "Retail";
        if (channel.StartsWith("VOLUME", StringComparison.Ordinal)) return "Volume";
        if (channel.StartsWith("TIMEBASED", StringComparison.Ordinal)) return "Evaluation";
        return null;
    }

    [GeneratedRegex(@"Windows\s+(?:11|10|8\.1|8|7|Server\s+\d{4}(?:\s+R2)?)")]
    private static partial Regex WindowsName();

    [GeneratedRegex(@"Windows\s+(?:11|10|8\.1|8|7)\s+(?<edition>.+?)\s*$")]
    private static partial Regex EditionInCaption();

    [GeneratedRegex(@"(?<channel>[A-Z_]+)\s+channel", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelInDescription();
}
