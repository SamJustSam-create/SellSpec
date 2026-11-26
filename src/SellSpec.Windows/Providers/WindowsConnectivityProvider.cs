using System.Text.RegularExpressions;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Native;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed partial class WindowsConnectivityProvider : IConnectivityProvider
{
    private readonly IScanLogger _logger;

    public WindowsConnectivityProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Connectivity";

    public async Task<ConnectivityInfo> DetectAsync(CancellationToken cancellationToken)
    {
        // Only descriptive columns are selected: no addresses, GUIDs or connection names.
        var adaptersTask = WmiQuery.TryQueryAsync(WmiQuery.StandardCimv2, "SELECT InterfaceDescription, NdisPhysicalMedium, HardwareInterface, Virtual, Hidden FROM MSFT_NetAdapter", cancellationToken, _logger, ComponentName);
        var legacyTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Name, AdapterTypeID, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", cancellationToken, _logger, ComponentName);
        var pnpTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Name, PNPClass, Service, Status FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' OR PNPClass = 'Camera' OR PNPClass = 'Image' OR PNPClass = 'Biometric'", cancellationToken, _logger, ComponentName);
        var soundTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Name, Status FROM Win32_SoundDevice", cancellationToken, _logger, ComponentName);
        var wlanTask = Task.Run(NativeInfo.GetWlanInterfaces, cancellationToken);

        await Task.WhenAll(adaptersTask, legacyTask, pnpTask, soundTask, wlanTask).ConfigureAwait(false);

        var adapters = new List<(string Name, bool IsWifi, bool IsEthernet)>();
        foreach (var row in adaptersTask.Result)
        {
            var name = GenericValueDetector.Clean(row.GetString("InterfaceDescription"));
            if (name is null) continue;
            if (row.GetBool("Virtual") == true || row.GetBool("HardwareInterface") == false || row.GetBool("Hidden") == true) continue;
            if (VirtualAdapterName().IsMatch(name)) continue;
            var medium = row.GetInt32("NdisPhysicalMedium");
            var isWifi = medium is 9 or 1 || WifiName().IsMatch(name);
            var isEthernet = medium is 14 && !isWifi;
            if (medium is 10) continue; // Bluetooth PAN
            adapters.Add((name, isWifi, isEthernet));
        }

        if (adapters.Count == 0)
        {
            foreach (var row in legacyTask.Result)
            {
                var name = GenericValueDetector.Clean(row.GetString("Name"));
                if (name is null || VirtualAdapterName().IsMatch(name) || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) continue;
                var typeId = row.GetInt32("AdapterTypeID");
                var isWifi = typeId == 9 || WifiName().IsMatch(name);
                adapters.Add((name, isWifi, typeId == 0 && !isWifi));
            }
        }

        var wifiAdapters = adapters.Where(a => a.IsWifi).Select(a => a.Name).Distinct().ToList();
        var ethernetAdapters = adapters.Where(a => a.IsEthernet).Select(a => a.Name).Distinct().ToList();

        // Wi-Fi generation: WLAN API first (detected), name heuristics second (estimated).
        Spec<string> wifiStandard = Spec<string>.Unavailable();
        var wlan = wlanTask.Result;
        var phyLabel = wlan.Select(w => WifiStandardInference.FromPhyTypes(w.PhyTypes)).FirstOrDefault(l => l is not null);
        var nameLabel = wifiAdapters.Concat(wlan.Select(w => w.Description)).Select(WifiStandardInference.FromAdapterName).FirstOrDefault(l => l is not null);
        if (phyLabel is not null)
        {
            // The PHY list cannot distinguish 6E from 6; prefer the name when it adds the 6 GHz detail for the same generation.
            if (nameLabel is not null && nameLabel.StartsWith("Wi-Fi 6E", StringComparison.Ordinal) && phyLabel.StartsWith("Wi-Fi 6", StringComparison.Ordinal))
            {
                wifiStandard = Spec<string>.Detected(nameLabel, "802.11ax confirmed by the wireless driver; 6 GHz support inferred from the adapter model.");
            }
            else
            {
                wifiStandard = Spec<string>.Detected(phyLabel, "Reported by the wireless driver.");
            }
        }
        else if (nameLabel is not null)
        {
            wifiStandard = Spec<string>.Estimated(nameLabel, "Inferred from the adapter model name.");
        }
        else if (wifiAdapters.Count > 0)
        {
            wifiStandard = Spec<string>.Unavailable("The Wi-Fi generation could not be determined.");
        }

        if (wifiAdapters.Count == 0 && wlan.Count > 0)
        {
            wifiAdapters.AddRange(wlan.Select(w => GenericValueDetector.Clean(w.Description)).Where(d => d is not null)!);
        }

        var pnp = pnpTask.Result.Where(r => r.GetString("Status") is null or "OK").ToList();
        var bluetoothDevices = pnp.Where(r => string.Equals(r.GetString("PNPClass"), "Bluetooth", StringComparison.OrdinalIgnoreCase)).ToList();
        var bluetoothRadios = bluetoothDevices
            .Select(r => r.GetString("Name"))
            .Where(n => n is not null && IsBluetoothRadioName(n))
            .Select(n => n!)
            .Distinct()
            .ToList();
        var cameras = pnp
            .Where(r => string.Equals(r.GetString("PNPClass"), "Camera", StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(r.GetString("PNPClass"), "Image", StringComparison.OrdinalIgnoreCase) && CameraName().IsMatch(r.GetString("Name") ?? string.Empty)) ||
                        string.Equals(r.GetString("Service"), "usbvideo", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.GetString("Name"))
            .Where(n => n is not null && !VirtualCameraName().IsMatch(n))
            .Select(n => n!)
            .Distinct()
            .ToList();
        var fingerprint = pnp.Any(r => string.Equals(r.GetString("PNPClass"), "Biometric", StringComparison.OrdinalIgnoreCase));

        var audio = soundTask.Result
            .Where(r => r.GetString("Status") is null or "OK")
            .Select(r => GenericValueDetector.Clean(r.GetString("Name")))
            .Where(n => n is not null && !VirtualAudioName().IsMatch(n))
            .Select(n => n!)
            .Distinct()
            .ToList();

        var usbEthernet = ethernetAdapters.Any(e => e.Contains("USB", StringComparison.OrdinalIgnoreCase));

        return new ConnectivityInfo
        {
            WifiAdapter = wifiAdapters.Count > 0 ? Spec<string>.Detected(wifiAdapters[0]) : Spec<string>.Unavailable(),
            WifiStandard = wifiStandard,
            Bluetooth = bluetoothDevices.Count > 0 ? Spec<bool>.Detected(true) : Spec<bool>.Detected(false, "No Bluetooth radio was found."),
            Ethernet = ethernetAdapters.Count > 0
                ? Spec<bool>.Detected(true, usbEthernet ? "A USB Ethernet adapter was detected; it may not be built in." : null)
                : Spec<bool>.Detected(false, "No wired network adapter was found."),
            Webcam = cameras.Count > 0 ? Spec<bool>.Detected(true) : Spec<bool>.Detected(false, "No camera device was found."),
            Audio = audio.Count > 0 ? Spec<bool>.Detected(true) : Spec<bool>.Detected(false, "No audio device was found."),
            FingerprintReader = fingerprint ? Spec<bool>.Detected(true) : Spec<bool>.Detected(false),
            NetworkAdapters = adapters.Select(a => a.Name).Distinct().ToList(),
            Cameras = cameras,
            AudioDevices = audio,
            BluetoothRadios = bluetoothRadios,
        };
    }

    [GeneratedRegex(@"Virtual|VPN|TAP-|Wintun|Hyper-V|VMware|VirtualBox|Loopback|WAN Miniport|Npcap|WSL|ZeroTier|Tailscale|Docker|Teredo|ISATAP|6to4|Kernel Debug|Miniport|Pseudo|Bluetooth Device \(Personal", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualAdapterName();

    [GeneratedRegex(@"Wi-?Fi|Wireless|802\.11|WLAN|\bAX\d{3,4}\b|\bBE\d{3}\b|\bAC\s?\d{3,4}\b|Dual Band|Tri Band", RegexOptions.IgnoreCase)]
    private static partial Regex WifiName();

    /// <summary>
    /// Only hardware radio names are kept. Paired devices also appear under the Bluetooth PnP class and carry
    /// user-assigned names ("Sam's AirPods"), which must never reach a report or a diagnostics bundle, so a name
    /// has to look like a radio (positive match) and not like a peripheral or protocol entry (negative match).
    /// </summary>
    internal static bool IsBluetoothRadioName(string name) =>
        BluetoothRadioName().IsMatch(name) && !BluetoothNonRadio().IsMatch(name);

    [GeneratedRegex(@"Bluetooth|\bBT\b|Radio|Wireless Adapter", RegexOptions.IgnoreCase)]
    private static partial Regex BluetoothRadioName();

    [GeneratedRegex(@"Enumerator|RFCOMM|Personal Area|LE Generic|Service|Transport|Device Information|Protocol|Audio Sink|Audio Source|Hands-Free|Avrcp|HID|Keyboard|Mouse|Headset|Headphones|Speaker|Earbuds|Buds|Controller$|Gamepad|Pen\b|Watch|Phone|'s\b|’s\b", RegexOptions.IgnoreCase)]
    private static partial Regex BluetoothNonRadio();

    [GeneratedRegex(@"camera|webcam|\bcam\b|imaging", RegexOptions.IgnoreCase)]
    private static partial Regex CameraName();

    [GeneratedRegex(@"Virtual|OBS|Snap Camera|XSplit|ManyCam|NVIDIA Broadcast|Camo|DroidCam|Iriun|EpocCam|NDI|Streamlabs", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualCameraName();

    [GeneratedRegex(@"Virtual|Steam Streaming|VB-Audio|Voicemeeter|NVIDIA Broadcast|Remote Audio|Oculus|Meta Quest|Parsec|Moonlight|Sunshine|SteelSeries Sonar|Nahimic|Wave Link|GoXLR", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualAudioName();
}
