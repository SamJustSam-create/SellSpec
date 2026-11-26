using System.Text.RegularExpressions;
using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed partial class WindowsStorageProvider : IStorageProvider
{
    private readonly IScanLogger _logger;
    private readonly bool _isElevated;

    public WindowsStorageProvider(IScanLogger logger, bool isElevated)
    {
        _logger = logger;
        _isElevated = isElevated;
    }

    public string ComponentName => "Storage";

    public async Task<StorageResult> DetectAsync(CancellationToken cancellationToken)
    {
        var issues = new List<ScanIssue>();

        var physicalTask = WmiQuery.TryQueryAsync(WmiQuery.Storage,
            "SELECT DeviceId, FriendlyName, Model, Manufacturer, SerialNumber, MediaType, BusType, Size, HealthStatus, SpindleSpeed, FirmwareVersion FROM MSFT_PhysicalDisk",
            cancellationToken, _logger, ComponentName);
        var diskTask = WmiQuery.TryQueryAsync(WmiQuery.Storage,
            "SELECT Number, IsBoot, IsSystem, BusType, Model, Size, SerialNumber FROM MSFT_Disk",
            cancellationToken, _logger, ComponentName);
        var legacyTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2,
            "SELECT Index, Model, Size, InterfaceType, MediaType, PNPDeviceID, SerialNumber, Status FROM Win32_DiskDrive",
            cancellationToken, _logger, ComponentName);
        var lettersTask = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition", cancellationToken, _logger, ComponentName);
        var reliabilityTask = WmiQuery.TryQueryAsync(WmiQuery.Storage,
            "SELECT DeviceId, PowerOnHours, Temperature, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected FROM MSFT_StorageReliabilityCounter",
            cancellationToken, _logger, ComponentName);
        var smartStatusTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", cancellationToken, _logger, ComponentName);
        var smartDataTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", cancellationToken, _logger, ComponentName);

        await Task.WhenAll(physicalTask, diskTask, legacyTask, lettersTask, reliabilityTask, smartStatusTask, smartDataTask).ConfigureAwait(false);

        var lettersByDisk = ParseDriveLetters(lettersTask.Result);
        var legacyByIndex = legacyTask.Result
            .Select(r => (Index: r.GetInt32("Index"), Row: r))
            .Where(x => x.Index is not null)
            .ToDictionary(x => x.Index!.Value, x => x.Row);
        var disksByNumber = diskTask.Result
            .Select(r => (Number: r.GetInt32("Number"), Row: r))
            .Where(x => x.Number is not null)
            .GroupBy(x => x.Number!.Value)
            .ToDictionary(g => g.Key, g => g.First().Row);
        var reliabilityById = reliabilityTask.Result
            .Select(r => (Id: r.GetString("DeviceId"), Row: r))
            .Where(x => x.Id is not null)
            .GroupBy(x => x.Id!)
            .ToDictionary(g => g.Key, g => g.First().Row, StringComparer.OrdinalIgnoreCase);
        var smart = ParseSmart(smartStatusTask.Result, smartDataTask.Result);

        var devices = new List<StorageDevice>();
        var serials = new List<string>();

        if (physicalTask.Result.Count > 0)
        {
            foreach (var pd in physicalTask.Result)
            {
                var idText = pd.GetString("DeviceId");
                int? number = int.TryParse(idText, out var n) ? n : null;
                disksByNumber.TryGetValue(number ?? -1, out var disk);
                legacyByIndex.TryGetValue(number ?? -1, out var legacy);
                reliabilityById.TryGetValue(idText ?? string.Empty, out var reliability);

                var model = GenericValueDetector.Clean(pd.GetString("Model")) ?? GenericValueDetector.Clean(pd.GetString("FriendlyName")) ?? GenericValueDetector.Clean(legacy?.GetString("Model")) ?? "Unknown drive";
                var busCode = pd.GetInt32("BusType") ?? disk?.GetInt32("BusType");
                var bus = MapBus(busCode, legacy?.GetString("InterfaceType"), model);
                var media = MapMedia(pd.GetInt32("MediaType"), pd.GetUInt32("SpindleSpeed"), bus, model);
                var location = MapLocation(bus, busCode, model, legacy?.GetString("MediaType"));
                var size = pd.GetUInt64("Size") ?? disk?.GetUInt64("Size") ?? legacy?.GetUInt64("Size");
                var pnp = legacy?.GetString("PNPDeviceID");
                var smartEntry = pnp is null ? null : smart.FirstOrDefault(s => MatchesPnp(s.InstanceName, pnp));

                var serial = GenericValueDetector.Clean(pd.GetString("SerialNumber")) ?? GenericValueDetector.Clean(disk?.GetString("SerialNumber")) ?? GenericValueDetector.Clean(legacy?.GetString("SerialNumber"));
                if (serial is not null) serials.Add(serial);

                var spindle = pd.GetUInt32("SpindleSpeed");
                var manufacturer = ManufacturerNormaliser.Normalise(pd.GetString("Manufacturer")) ?? InferManufacturer(model);

                devices.Add(new StorageDevice
                {
                    Model = model,
                    Manufacturer = manufacturer is null ? Spec<string>.Unavailable() : Spec<string>.Detected(manufacturer),
                    CapacityBytes = size is > 0 ? Spec<ulong>.Detected(size.Value) : Spec<ulong>.Unavailable(),
                    Media = media,
                    Bus = bus,
                    Location = location,
                    IsBootDrive = disk?.GetBool("IsBoot") == true || disk?.GetBool("IsSystem") == true,
                    DriveLetters = number is { } num && lettersByDisk.TryGetValue(num, out var letters) ? letters : Array.Empty<string>(),
                    DiskNumber = number,
                    ReportedHealth = MapHealth(pd.GetInt32("HealthStatus")),
                    PowerOnHours = PowerOnHours(reliability, smartEntry),
                    WearPercentUsed = reliability?.GetInt32("Wear") is { } wear and >= 0 and <= 100 ? Spec<int>.Detected(wear) : Spec<int>.Unavailable(),
                    TemperatureC = reliability?.GetInt32("Temperature") is { } temp and > 0 and < 120 ? Spec<int>.Detected(temp) : Spec<int>.Unavailable(),
                    PredictedFailure = smartEntry is { } se ? Spec<bool>.Detected(se.PredictFailure) : Spec<bool>.Unavailable(),
                    SpindleRpm = media == StorageMediaType.Hdd && spindle is > 0 and < 100_000 ? Spec<int>.Firmware((int)spindle.Value) : Spec<int>.Unavailable(),
                    FirmwareVersion = GenericValueDetector.Clean(pd.GetString("FirmwareVersion")),
                });
            }
        }
        else if (legacyTask.Result.Count > 0)
        {
            issues.Add(new ScanIssue(ComponentName, "Detailed storage information was unavailable; basic drive details were read instead.", ScanIssueSeverity.Info));
            foreach (var legacy in legacyTask.Result)
            {
                var index = legacy.GetInt32("Index");
                var model = GenericValueDetector.Clean(legacy.GetString("Model")) ?? "Unknown drive";
                var bus = MapBus(null, legacy.GetString("InterfaceType"), model);
                var media = MapMedia(null, null, bus, model);
                var pnp = legacy.GetString("PNPDeviceID");
                var smartEntry = pnp is null ? null : smart.FirstOrDefault(s => MatchesPnp(s.InstanceName, pnp));
                var serial = GenericValueDetector.Clean(legacy.GetString("SerialNumber"));
                if (serial is not null) serials.Add(serial);

                devices.Add(new StorageDevice
                {
                    Model = model,
                    Manufacturer = InferManufacturer(model) is { } mf ? Spec<string>.Estimated(mf) : Spec<string>.Unavailable(),
                    CapacityBytes = legacy.GetUInt64("Size") is > 0 and var size ? Spec<ulong>.Detected(size) : Spec<ulong>.Unavailable(),
                    Media = media,
                    Bus = bus,
                    Location = MapLocation(bus, null, model, legacy.GetString("MediaType")),
                    IsBootDrive = index is { } i && lettersByDisk.TryGetValue(i, out var l) && l.Contains("C:", StringComparer.OrdinalIgnoreCase),
                    DriveLetters = index is { } idx && lettersByDisk.TryGetValue(idx, out var letters) ? letters : Array.Empty<string>(),
                    DiskNumber = index,
                    ReportedHealth = legacy.GetString("Status") is "OK" ? Spec<HealthStatusKind>.Unavailable("Windows reports the device as OK but no detailed health data is available.") : Spec<HealthStatusKind>.Unavailable(),
                    PowerOnHours = PowerOnHours(null, smartEntry),
                    PredictedFailure = smartEntry is { } se ? Spec<bool>.Detected(se.PredictFailure) : Spec<bool>.Unavailable(),
                });
            }
        }

        if (devices.Count > 0 && reliabilityTask.Result.Count == 0 && devices.Any(d => d.Location == StorageLocation.Internal && !d.PowerOnHours.HasValue))
        {
            issues.Add(new ScanIssue(ComponentName,
                _isElevated
                    ? "Drive power-on hours and wear were not reported by this system."
                    : "Drive power-on hours and wear may need SellSpec to be run as administrator on this system.",
                ScanIssueSeverity.Info));
        }

        return new StorageResult(devices.OrderBy(d => d.DiskNumber ?? int.MaxValue).ToList(), serials, issues);
    }

    private static Spec<int> PowerOnHours(WmiRow? reliability, SmartEntry? smart)
    {
        var hours = reliability?.GetInt64("PowerOnHours");
        if (hours is > 0 and < 2_000_000) return Spec<int>.Detected((int)hours.Value);
        if (smart?.PowerOnHours is > 0 and < 2_000_000) return Spec<int>.Detected((int)smart.PowerOnHours.Value, "Read from the drive's SMART attributes.");
        if (hours == 0) return Spec<int>.Unavailable("The drive reports 0 power-on hours, which usually means the counter is not supported.");
        return Spec<int>.Unavailable("Power-on hours were not available for this drive.");
    }

    internal static StorageBus MapBus(int? busType, string? legacyInterface, string model)
    {
        switch (busType)
        {
            case 3: // ATA
            case 11: // SATA
                return StorageBus.Sata;
            case 17: return StorageBus.Nvme;
            case 7: return StorageBus.Usb;
            case 12: // SD
            case 13: // MMC
                return StorageBus.SdCard;
            case 8: return StorageBus.Raid;
            case 10: return StorageBus.Sas;
            case 1: return StorageBus.Scsi;
            case 14: // Virtual
            case 15: // File backed virtual
            case 16: // Storage Spaces
                return StorageBus.Virtual;
            case 18: // SCM
            case 19: // UFS
                return StorageBus.Other;
        }

        if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return StorageBus.Nvme;
        return legacyInterface?.ToUpperInvariant() switch
        {
            "IDE" => StorageBus.Sata,
            "USB" => StorageBus.Usb,
            "SCSI" => StorageBus.Scsi,
            "1394" => StorageBus.Other,
            _ => StorageBus.Unknown,
        };
    }

    internal static StorageMediaType MapMedia(int? mediaType, uint? spindleSpeed, StorageBus bus, string model)
    {
        switch (mediaType)
        {
            case 3: return StorageMediaType.Hdd;
            case 4: return StorageMediaType.Ssd;
            case 5: return StorageMediaType.Scm;
        }
        if (bus == StorageBus.Nvme) return StorageMediaType.Ssd;
        if (Regex.IsMatch(model, @"\bSSD\b|NVMe|Solid State|\bM\.2\b|\b(SN|WDS|MZ|CT|KXG|KBG|PM9|PM8|SA\d|BX\d|MX\d|NV\d|KC\d|A2000|970|980|990|860|870|850|840)\b", RegexOptions.IgnoreCase)) return StorageMediaType.Ssd;
        if (Regex.IsMatch(model, @"\bHDD\b|\bST\d{3,}|\bWD\d{2,}|\bHGST\b|\bTOSHIBA (MQ|DT|HD|MG)|\bHTS\d|\bMQ0|\bDT0|\bBarracuda|\bBlue\b|\bBlack\b|\bRed\b|\bPurple\b", RegexOptions.IgnoreCase) && !model.Contains("SSD", StringComparison.OrdinalIgnoreCase)) return StorageMediaType.Hdd;
        if (spindleSpeed is > 0 and < 100_000) return StorageMediaType.Hdd;
        return StorageMediaType.Unknown;
    }

    internal static StorageLocation MapLocation(StorageBus bus, int? busType, string model, string? legacyMediaType)
    {
        if (bus == StorageBus.Virtual || Regex.IsMatch(model, @"Virtual|VHD|Storage Space|Msft Virtual|Veeam|ImDisk", RegexOptions.IgnoreCase)) return StorageLocation.Virtual;
        if (bus == StorageBus.SdCard) return StorageLocation.Removable;
        if (bus == StorageBus.Usb) return legacyMediaType?.Contains("Removable", StringComparison.OrdinalIgnoreCase) == true ? StorageLocation.Removable : StorageLocation.External;
        if (legacyMediaType?.Contains("Removable", StringComparison.OrdinalIgnoreCase) == true) return StorageLocation.Removable;
        if (legacyMediaType?.Contains("External", StringComparison.OrdinalIgnoreCase) == true) return StorageLocation.External;
        if (bus is StorageBus.Sata or StorageBus.Nvme or StorageBus.Raid or StorageBus.Sas or StorageBus.Scsi) return StorageLocation.Internal;
        if (busType == 18 || busType == 19) return StorageLocation.Internal;
        return legacyMediaType?.Contains("Fixed", StringComparison.OrdinalIgnoreCase) == true ? StorageLocation.Internal : StorageLocation.Unknown;
    }

    internal static Spec<HealthStatusKind> MapHealth(int? healthStatus) => healthStatus switch
    {
        0 => Spec<HealthStatusKind>.Detected(HealthStatusKind.Good),
        1 => Spec<HealthStatusKind>.Detected(HealthStatusKind.Attention),
        2 => Spec<HealthStatusKind>.Detected(HealthStatusKind.Issue),
        _ => Spec<HealthStatusKind>.Unavailable("Windows did not report a health status for this drive."),
    };

    internal static string? InferManufacturer(string model)
    {
        var m = model.ToUpperInvariant();
        if (m.StartsWith("SAMSUNG") || Regex.IsMatch(m, @"\bMZ[A-Z0-9]{3,}|\b9[6-9]0 (EVO|PRO)|\b8[4-7]0 (EVO|PRO|QVO)")) return "Samsung";
        if (m.StartsWith("WDC") || m.StartsWith("WD ") || m.StartsWith("WDS") || Regex.IsMatch(m, @"\bSN\d{3}\b|\bWD BLUE|\bWD BLACK|\bWD GREEN")) return "Western Digital";
        if (m.StartsWith("ST") && m.Length > 4 && char.IsDigit(m[2])) return "Seagate";
        if (m.Contains("KINGSTON") || Regex.IsMatch(m, @"\bKC\d{3,4}\b|\bNV\d\b|\bSA400|\bSKC")) return "Kingston";
        if (m.Contains("CRUCIAL") || Regex.IsMatch(m, @"\bCT\d{3,4}|\bMX\d{3}\b|\bBX\d{3}\b|\bP\d (PLUS)?\b")) return "Crucial";
        if (m.Contains("MICRON")) return "Micron";
        if (m.Contains("HYNIX") || m.StartsWith("HFM") || m.StartsWith("HFS") || m.StartsWith("BC") && m.Contains("HYNIX")) return "SK hynix";
        if (m.Contains("KIOXIA") || m.StartsWith("KBG") || m.StartsWith("KXG")) return "Kioxia";
        if (m.Contains("TOSHIBA")) return "Toshiba";
        if (m.Contains("INTEL") || m.StartsWith("INTEL SSDPE") || m.StartsWith("SSDPE") || m.StartsWith("SSDSC")) return "Intel";
        if (m.Contains("SANDISK")) return "SanDisk";
        if (m.Contains("ADATA")) return "ADATA";
        if (m.Contains("CORSAIR")) return "Corsair";
        if (m.Contains("SABRENT")) return "Sabrent";
        if (m.Contains("SOLIDIGM")) return "Solidigm";
        if (m.Contains("HGST") || m.StartsWith("HTS") || m.StartsWith("HUS")) return "HGST";
        if (m.Contains("HITACHI")) return "Hitachi";
        if (m.Contains("SEAGATE")) return "Seagate";
        if (m.Contains("LEXAR")) return "Lexar";
        if (m.Contains("PNY")) return "PNY";
        if (m.Contains("PATRIOT")) return "Patriot";
        if (m.Contains("TEAM")) return "TeamGroup";
        if (m.Contains("GIGABYTE")) return "Gigabyte";
        if (m.Contains("UMIS") || m.Contains("UNION MEMORY")) return "Union Memory";
        if (m.Contains("SSSTC")) return "Solid State Storage Technology";
        if (m.Contains("PHISON")) return "Phison";
        return null;
    }

    private static Dictionary<int, string[]> ParseDriveLetters(IReadOnlyList<WmiRow> rows)
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var row in rows)
        {
            var partition = row.GetString("Antecedent");
            var logical = row.GetString("Dependent");
            if (partition is null || logical is null) continue;
            var diskMatch = DiskNumberInPartition().Match(partition);
            var letterMatch = DriveLetter().Match(logical);
            if (!diskMatch.Success || !letterMatch.Success) continue;
            var disk = int.Parse(diskMatch.Groups["n"].Value);
            if (!map.TryGetValue(disk, out var list))
            {
                list = new List<string>();
                map[disk] = list;
            }
            list.Add(letterMatch.Groups["letter"].Value.ToUpperInvariant() + ":");
        }
        return map.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().OrderBy(l => l).ToArray());
    }

    private sealed record SmartEntry(string InstanceName, bool PredictFailure, long? PowerOnHours);

    private static List<SmartEntry> ParseSmart(IReadOnlyList<WmiRow> statusRows, IReadOnlyList<WmiRow> dataRows)
    {
        var list = new List<SmartEntry>();
        var dataByInstance = dataRows.Where(r => r.GetString("InstanceName") is not null)
            .GroupBy(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var status in statusRows)
        {
            var instance = status.GetString("InstanceName");
            if (instance is null) continue;
            long? poh = null;
            if (dataByInstance.TryGetValue(instance, out var data) && data.GetBytes("VendorSpecific") is { Length: >= 362 } bytes)
            {
                poh = ReadSmartAttributeRaw(bytes, 9);
            }
            list.Add(new SmartEntry(instance, status.GetBool("PredictFailure") ?? false, poh));
        }
        return list;
    }

    /// <summary>ATA SMART data: 2 header bytes followed by 30 × 12-byte attribute records (id, flags×2, current, worst, raw×6, reserved).</summary>
    internal static long? ReadSmartAttributeRaw(byte[] vendorSpecific, int attributeId)
    {
        for (var i = 2; i + 12 <= vendorSpecific.Length && i < 2 + 30 * 12; i += 12)
        {
            if (vendorSpecific[i] != attributeId) continue;
            long raw = 0;
            for (var b = 5; b >= 0; b--)
            {
                raw = (raw << 8) | vendorSpecific[i + 5 + b];
            }
            // Some vendors pack hours into the low 4 bytes and minutes/milliseconds into the upper bytes.
            if (attributeId == 9 && raw > 1_000_000) raw &= 0xFFFFFFFF;
            return raw;
        }
        return null;
    }

    /// <summary>MSStorageDriver instance names are the PNP id plus "_0"; both sides use different separators.</summary>
    internal static bool MatchesPnp(string instanceName, string pnpDeviceId)
    {
        static string Norm(string s) => s.Replace('\\', '#').TrimEnd('0').TrimEnd('_').ToUpperInvariant();
        var a = Norm(instanceName);
        var b = Norm(pnpDeviceId);
        return a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"Disk #(?<n>\d+), Partition #\d+")]
    private static partial Regex DiskNumberInPartition();

    [GeneratedRegex(@"DeviceID=""(?<letter>[A-Za-z]):""")]
    private static partial Regex DriveLetter();
}
