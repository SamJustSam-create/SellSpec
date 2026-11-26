using System.Text.RegularExpressions;
using Microsoft.Win32;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed partial class WindowsGraphicsProvider : IGraphicsProvider
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const ulong AdapterRamCap = 4_293_918_720; // 4 GiB - 1 MiB: Win32_VideoController.AdapterRAM saturates here.

    private readonly IScanLogger _logger;

    public WindowsGraphicsProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Graphics";

    public async Task<IReadOnlyList<GraphicsAdapter>> DetectAsync(CancellationToken cancellationToken)
    {
        var rows = await WmiQuery.TryQueryAsync(WmiQuery.Cimv2,
            "SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, DriverDate, PNPDeviceID, Availability, ConfigManagerErrorCode FROM Win32_VideoController",
            cancellationToken, _logger, ComponentName).ConfigureAwait(false);

        var registryMemory = ReadRegistryMemorySizes();
        var adapters = new List<GraphicsAdapter>();

        foreach (var row in rows)
        {
            var name = GraphicsClassifier.NormaliseName(row.GetString("Name"));
            var vendor = GraphicsClassifier.VendorFromName(name, row.GetString("AdapterCompatibility"));
            var pnp = row.GetString("PNPDeviceID");
            ulong? registryBytes = pnp is null ? null : LookupMemory(registryMemory, pnp);
            var kind = GraphicsClassifier.Classify(name, vendor, registryBytes);
            var fallback = GraphicsClassifier.IsFallbackDriver(name);

            Spec<ulong> dedicated = Spec<ulong>.NotApplicable();
            if (kind == GraphicsKind.Dedicated)
            {
                var adapterRam = row.GetUInt64("AdapterRAM");
                if (registryBytes is >= 256UL * 1024 * 1024)
                {
                    dedicated = Spec<ulong>.Detected(registryBytes.Value);
                }
                else if (adapterRam is { } ram && ram > 0 && ram < AdapterRamCap)
                {
                    dedicated = Spec<ulong>.Firmware(ram, "Reported by Windows; this value is capped at 4 GB on some systems.");
                }
                else if (adapterRam is >= AdapterRamCap)
                {
                    dedicated = Spec<ulong>.Unavailable("Windows caps the reported value at 4 GB; the actual amount is 4 GB or more.");
                }
                else
                {
                    dedicated = Spec<ulong>.Unavailable("Dedicated memory size was not reported.");
                }
            }

            var availability = row.GetInt32("Availability");
            var errorCode = row.GetInt32("ConfigManagerErrorCode") ?? 0;
            adapters.Add(new GraphicsAdapter
            {
                Name = name,
                RawName = row.GetString("Name"),
                Vendor = vendor is null ? Spec<string>.Unavailable() : Spec<string>.Detected(vendor),
                Kind = kind,
                DedicatedMemoryBytes = dedicated,
                DriverVersion = row.GetString("DriverVersion"),
                DriverDate = row.GetDateTime("DriverDate") is { } d ? DateOnly.FromDateTime(d) : null,
                IsActive = availability is null or 3 && errorCode == 0,
                IsFallbackDriver = fallback,
            });
        }

        // De-duplicate identical entries that some drivers register twice.
        return adapters
            .GroupBy(a => (a.Name, a.Kind))
            .Select(g => g.First())
            .OrderByDescending(a => a.Kind == GraphicsKind.Dedicated)
            .ThenBy(a => a.Name)
            .ToList();
    }

    private sealed record RegistryAdapter(string MatchingDeviceId, ulong MemoryBytes);

    private List<RegistryAdapter> ReadRegistryMemorySizes()
    {
        var list = new List<RegistryAdapter>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null) return list;
            foreach (var subName in classKey.GetSubKeyNames())
            {
                if (!subName.All(char.IsDigit)) continue;
                try
                {
                    using var sub = classKey.OpenSubKey(subName);
                    if (sub is null) continue;
                    var matching = sub.GetValue("MatchingDeviceId") as string;
                    if (string.IsNullOrWhiteSpace(matching)) continue;

                    ulong bytes = 0;
                    var qw = sub.GetValue("HardwareInformation.qwMemorySize");
                    if (qw is long l && l > 0) bytes = (ulong)l;
                    else if (qw is byte[] qb && qb.Length >= 8) bytes = BitConverter.ToUInt64(qb, 0);
                    if (bytes == 0)
                    {
                        var legacy = sub.GetValue("HardwareInformation.MemorySize");
                        if (legacy is int li && li > 0) bytes = (uint)li;
                        else if (legacy is byte[] lb && lb.Length >= 4) bytes = lb.Length >= 8 ? BitConverter.ToUInt64(lb, 0) : BitConverter.ToUInt32(lb, 0);
                    }
                    if (bytes > 0) list.Add(new RegistryAdapter(matching, bytes));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Individual subkeys can be locked down; skip them.
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.Log(LogLevel.Warning, ComponentName, "Display class registry key could not be read.", ex);
        }
        return list;
    }

    private static ulong? LookupMemory(List<RegistryAdapter> entries, string pnpDeviceId)
    {
        var ven = VenDev().Match(pnpDeviceId);
        if (!ven.Success) return null;
        var key = ven.Value.ToUpperInvariant();
        var subsys = Subsys().Match(pnpDeviceId);

        RegistryAdapter? best = null;
        foreach (var entry in entries)
        {
            var upper = entry.MatchingDeviceId.ToUpperInvariant();
            if (!upper.Contains(key, StringComparison.Ordinal)) continue;
            if (subsys.Success && upper.Contains(subsys.Value.ToUpperInvariant(), StringComparison.Ordinal))
            {
                return entry.MemoryBytes;
            }
            best ??= entry;
        }
        return best?.MemoryBytes;
    }

    [GeneratedRegex(@"VEN_[0-9A-Fa-f]{4}&DEV_[0-9A-Fa-f]{4}")]
    private static partial Regex VenDev();

    [GeneratedRegex(@"SUBSYS_[0-9A-Fa-f]{8}")]
    private static partial Regex Subsys();
}
