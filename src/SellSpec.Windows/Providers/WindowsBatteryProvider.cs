using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Health;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Native;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed class WindowsBatteryProvider : IBatteryProvider
{
    private readonly IScanLogger _logger;

    public WindowsBatteryProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Battery";

    public async Task<BatteryResult> DetectAsync(CancellationToken cancellationToken)
    {
        var power = NativeInfo.GetPowerStatus();

        var win32Task = WmiQuery.TryQueryAsync(WmiQuery.Cimv2, "SELECT Name, BatteryStatus, EstimatedChargeRemaining, DesignCapacity, FullChargeCapacity, Chemistry, Status FROM Win32_Battery", cancellationToken, _logger, ComponentName);
        var staticTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, DesignedCapacity, Chemistry, ManufactureName, DeviceName, SerialNumber FROM BatteryStaticData", cancellationToken, _logger, ComponentName);
        var fullTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, FullChargedCapacity FROM BatteryFullChargedCapacity", cancellationToken, _logger, ComponentName);
        var cycleTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, CycleCount FROM BatteryCycleCount", cancellationToken, _logger, ComponentName);
        var statusTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, Charging, Discharging, PowerOnline, RemainingCapacity FROM BatteryStatus", cancellationToken, _logger, ComponentName);

        await Task.WhenAll(win32Task, staticTask, fullTask, cycleTask, statusTask).ConfigureAwait(false);

        var win32 = win32Task.Result;
        var statics = staticTask.Result;

        if (win32.Count == 0 && statics.Count == 0)
        {
            if (power?.HasBattery == false || power is null)
            {
                return new BatteryResult(BatteryInfo.NotApplicable(), Array.Empty<string>());
            }
            return new BatteryResult(BatteryInfo.Unavailable("Windows reports a battery but no details were available."), Array.Empty<string>());
        }

        var fullBy = fullTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);
        var cycleBy = cycleTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);
        var statusBy = statusTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);

        var packs = new List<BatteryPack>();
        var serials = new List<string>();

        foreach (var s in statics)
        {
            var instance = s.GetString("InstanceName") ?? string.Empty;
            fullBy.TryGetValue(instance, out var full);
            cycleBy.TryGetValue(instance, out var cycle);

            var design = s.GetInt32("DesignedCapacity");
            var fullCharged = full?.GetInt32("FullChargedCapacity");
            var cycles = cycle?.GetInt32("CycleCount");
            var serial = GenericValueDetector.Clean(s.GetString("SerialNumber"));
            if (serial is not null) serials.Add(serial);

            packs.Add(new BatteryPack
            {
                Name = GenericValueDetector.Clean(s.GetString("DeviceName")),
                Manufacturer = GenericValueDetector.Clean(s.GetString("ManufactureName")),
                DesignCapacityMwh = design is > 0 ? Spec<int>.Firmware(design.Value) : Spec<int>.Unavailable("Design capacity not reported."),
                FullChargeCapacityMwh = fullCharged is > 0 ? Spec<int>.Firmware(fullCharged.Value) : Spec<int>.Unavailable("Full-charge capacity not reported."),
                CycleCount = cycles is { } c and >= 0 ? Spec<int>.Firmware(c) : Spec<int>.Unavailable(),
                Chemistry = ChemistryFromString(s.GetString("Chemistry")) is { } chem ? Spec<string>.Firmware(chem) : Spec<string>.Unavailable(),
            });
        }

        // Fallback: Win32_Battery occasionally carries capacities when root\WMI classes are missing.
        if (packs.Count == 0)
        {
            foreach (var b in win32)
            {
                var design = b.GetInt32("DesignCapacity");
                var fullCharged = b.GetInt32("FullChargeCapacity");
                packs.Add(new BatteryPack
                {
                    Name = GenericValueDetector.Clean(b.GetString("Name")),
                    DesignCapacityMwh = design is > 0 ? Spec<int>.Firmware(design.Value) : Spec<int>.Unavailable("Design capacity not reported."),
                    FullChargeCapacityMwh = fullCharged is > 0 ? Spec<int>.Firmware(fullCharged.Value) : Spec<int>.Unavailable("Full-charge capacity not reported."),
                    CycleCount = Spec<int>.Unavailable("Cycle count not reported."),
                    Chemistry = ChemistryFromCode(b.GetInt32("Chemistry")) is { } chem ? Spec<string>.Firmware(chem) : Spec<string>.Unavailable(),
                });
            }
        }

        // Charge level and charging state.
        var firstWin32 = win32.FirstOrDefault();
        Spec<int> charge = power?.ChargePercent is { } pct
            ? Spec<int>.Detected(pct)
            : firstWin32?.GetInt32("EstimatedChargeRemaining") is { } est and >= 0 and <= 100 ? Spec<int>.Detected(est) : Spec<int>.Unavailable();

        var statusRow = statusBy.Values.FirstOrDefault();
        string? chargingText = null;
        if (statusRow is not null)
        {
            var charging = statusRow.GetBool("Charging") == true;
            var discharging = statusRow.GetBool("Discharging") == true;
            var online = statusRow.GetBool("PowerOnline") == true;
            chargingText = charging ? "Charging" : discharging ? "Discharging (on battery)" : online ? (charge.HasValue && charge.Value >= 95 ? "Fully charged, on AC power" : "On AC power, not charging") : null;
        }
        chargingText ??= firstWin32?.GetInt32("BatteryStatus") switch
        {
            1 => "Discharging (on battery)",
            2 => "On AC power",
            3 => "Fully charged",
            4 => "Low",
            5 => "Critical",
            6 or 7 or 8 or 9 => "Charging",
            11 => "Partially charged",
            _ => power?.OnAcPower switch { true => "On AC power", false => "On battery", _ => null },
        };

        var condition = firstWin32?.GetString("Status");

        var info = BatteryHealthCalculator.Combine(
            packs,
            charge,
            chargingText is null ? Spec<string>.Unavailable() : Spec<string>.Detected(chargingText),
            condition is null ? Spec<string>.Unavailable() : Spec<string>.Detected(condition));

        return new BatteryResult(info, serials);
    }

    private static string? ChemistryFromString(string? raw)
    {
        var s = GenericValueDetector.Clean(raw)?.ToUpperInvariant();
        return s switch
        {
            null => null,
            "LION" or "LI-I" or "LIION" or "LI-ION" => "Lithium-ion",
            "LIP" or "LIPO" or "LI-P" => "Lithium-ion polymer",
            "NIMH" => "Nickel-metal hydride",
            "NICD" => "Nickel-cadmium",
            "PBAC" => "Lead acid",
            "RAM" => "Rechargeable alkaline",
            _ => raw,
        };
    }

    private static string? ChemistryFromCode(int? code) => code switch
    {
        3 => "Lead acid",
        4 => "Nickel-cadmium",
        5 => "Nickel-metal hydride",
        6 => "Lithium-ion",
        7 => "Zinc air",
        8 => "Lithium-ion polymer",
        _ => null,
    };
}
