namespace SellSpec.Core.Models;

/// <summary>
/// Connectivity features that matter to a buyer. Deliberately excludes IP addresses, MAC addresses,
/// network names and anything else that could identify the seller's network.
/// </summary>
public sealed record ConnectivityInfo
{
    public Spec<string> WifiAdapter { get; init; } = Spec<string>.Unavailable();

    /// <summary>"Wi-Fi 6 (802.11ax)". Detected via WLAN API where possible, otherwise estimated from the adapter name.</summary>
    public Spec<string> WifiStandard { get; init; } = Spec<string>.Unavailable();

    public Spec<bool> Bluetooth { get; init; } = Spec<bool>.Unavailable();
    public Spec<bool> Ethernet { get; init; } = Spec<bool>.Unavailable();
    public Spec<bool> Webcam { get; init; } = Spec<bool>.Unavailable();
    public Spec<bool> Audio { get; init; } = Spec<bool>.Unavailable();
    public Spec<bool> FingerprintReader { get; init; } = Spec<bool>.Unavailable();

    public IReadOnlyList<string> NetworkAdapters { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cameras { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AudioDevices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BluetoothRadios { get; init; } = Array.Empty<string>();

    public static ConnectivityInfo Unavailable(string? note = null) => new()
    {
        WifiAdapter = Spec<string>.Unavailable(note),
        Bluetooth = Spec<bool>.Unavailable(note),
        Ethernet = Spec<bool>.Unavailable(note),
        Webcam = Spec<bool>.Unavailable(note),
        Audio = Spec<bool>.Unavailable(note),
    };
}
