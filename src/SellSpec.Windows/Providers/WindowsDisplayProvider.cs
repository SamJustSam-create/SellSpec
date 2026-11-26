using SellSpec.Core.Detection;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;
using SellSpec.Core.Normalisation;
using SellSpec.Windows.Native;
using SellSpec.Windows.Wmi;

namespace SellSpec.Windows.Providers;

public sealed class WindowsDisplayProvider : IDisplayProvider
{
    private static readonly HashSet<string> PanelMakers = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUO", "BOE", "LGD", "SDC", "CMN", "CMO", "SHP", "NCP", "IVO", "CSO", "CSW", "TMX", "LEN", "HSD", "SEC", "MSH",
    };

    private readonly IScanLogger _logger;

    public WindowsDisplayProvider(IScanLogger logger)
    {
        _logger = logger;
    }

    public string ComponentName => "Display";

    public async Task<IReadOnlyList<DisplayInfo>> DetectAsync(CancellationToken cancellationToken)
    {
        var idTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, Active, ManufacturerName, UserFriendlyName, ProductCodeID FROM WmiMonitorID", cancellationToken, _logger, ComponentName);
        var paramsTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", cancellationToken, _logger, ComponentName);
        var connectionTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams", cancellationToken, _logger, ComponentName);
        var modesTask = WmiQuery.TryQueryAsync(WmiQuery.WmiRoot, "SELECT InstanceName, PreferredMonitorSourceModeIndex, MonitorSourceModes FROM WmiMonitorListedSupportedSourceModes", cancellationToken, _logger, ComponentName);
        var nativeTask = Task.Run(NativeInfo.GetDisplayDevices, cancellationToken);
        var digitizer = NativeInfo.GetDigitizer();

        await Task.WhenAll(idTask, paramsTask, connectionTask, modesTask, nativeTask).ConfigureAwait(false);

        var byInstance = new Dictionary<string, WmiRow>(StringComparer.OrdinalIgnoreCase);
        var paramsBy = paramsTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);
        var connBy = connectionTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);
        var modesBy = modesTask.Result.Where(r => r.GetString("InstanceName") is not null).ToDictionary(r => r.GetString("InstanceName")!, StringComparer.OrdinalIgnoreCase);
        var native = nativeTask.Result;

        var displays = new List<DisplayInfo>();
        var usedNative = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in idTask.Result)
        {
            var instance = id.GetString("InstanceName");
            if (instance is null) continue;
            if (id.GetBool("Active") == false) continue;
            byInstance[instance] = id;

            var manufacturerCode = id.GetCharArrayString("ManufacturerName");
            var friendlyName = id.GetCharArrayString("UserFriendlyName");
            var productCode = id.GetCharArrayString("ProductCodeID");

            paramsBy.TryGetValue(instance, out var basic);
            connBy.TryGetValue(instance, out var conn);
            modesBy.TryGetValue(instance, out var modes);

            var outputTech = conn?.GetUInt32("VideoOutputTechnology");
            var isInternal = outputTech is 0x80000000 or 11 or 6 || (outputTech is null && manufacturerCode is not null && PanelMakers.Contains(manufacturerCode));

            var nativeDevice = MatchNative(native, instance, usedNative);
            if (nativeDevice is not null) usedNative.Add(nativeDevice.AdapterDeviceName);

            var (preferred, maxRefresh) = ReadPreferredMode(modes);
            Spec<Resolution> nativeRes;
            if (preferred is { } p)
            {
                nativeRes = Spec<Resolution>.Firmware(p);
            }
            else if (nativeDevice is not null && nativeDevice.Modes.Count > 0)
            {
                var best = nativeDevice.Modes.OrderByDescending(m => (long)m.Width * m.Height).First();
                nativeRes = Spec<Resolution>.Estimated(new Resolution(best.Width, best.Height), "Highest mode offered by the graphics driver; the panel's native mode was not reported.");
            }
            else
            {
                nativeRes = Spec<Resolution>.Unavailable();
            }

            if (nativeDevice is not null && nativeRes.HasValue)
            {
                var driverMax = nativeDevice.Modes.Where(m => m.Width == nativeRes.Value.Width && m.Height == nativeRes.Value.Height).Select(m => m.RefreshHz).DefaultIfEmpty(0).Max();
                if (driverMax > (maxRefresh ?? 0)) maxRefresh = driverMax;
            }

            Spec<PhysicalSize> physical = Spec<PhysicalSize>.Unavailable();
            Spec<double> diagonal = Spec<double>.Unavailable("The display did not report its physical size.");
            var w = basic?.GetInt32("MaxHorizontalImageSize");
            var h = basic?.GetInt32("MaxVerticalImageSize");
            if (w is > 0 and < 300 && h is > 0 and < 300)
            {
                var size = new PhysicalSize(w.Value, h.Value);
                physical = Spec<PhysicalSize>.Firmware(size);
                diagonal = Spec<double>.Estimated(DisplayNaming.EstimateDiagonalInches(size), "Estimated from the panel's reported physical size (EDID stores whole centimetres).");
            }

            Spec<bool> touch;
            if (digitizer is null)
            {
                touch = Spec<bool>.Unavailable();
            }
            else if (isInternal)
            {
                touch = Spec<bool>.Detected(digitizer.IntegratedTouch);
            }
            else
            {
                touch = digitizer.ExternalTouch ? Spec<bool>.Detected(true, "An external touch digitizer is present; it may belong to another monitor.") : Spec<bool>.Unavailable("Touch support cannot be determined per external monitor.");
            }

            var manufacturer = DisplayNaming.VendorName(manufacturerCode);
            displays.Add(new DisplayInfo
            {
                Name = friendlyName is not null && !isInternal ? friendlyName : (friendlyName is not null && !PanelMakers.Contains(manufacturerCode ?? string.Empty) ? friendlyName : null),
                Manufacturer = manufacturer is null ? (manufacturerCode is null ? Spec<string>.Unavailable() : Spec<string>.Firmware(manufacturerCode)) : Spec<string>.Firmware(manufacturer),
                ManufacturerCode = manufacturerCode,
                ProductCode = productCode,
                IsInternal = isInternal,
                IsPrimary = nativeDevice?.IsPrimary ?? false,
                NativeResolution = nativeRes,
                CurrentResolution = nativeDevice?.Current is { } cur ? Spec<Resolution>.Detected(new Resolution(cur.Width, cur.Height)) : Spec<Resolution>.Unavailable(),
                CurrentRefreshRateHz = nativeDevice?.Current is { RefreshHz: > 0 } c2 ? Spec<double>.Detected(c2.RefreshHz) : Spec<double>.Unavailable(),
                MaxRefreshRateHz = maxRefresh is > 0 ? Spec<double>.Firmware(maxRefresh.Value) : Spec<double>.Unavailable(),
                PhysicalSize = physical,
                DiagonalInches = diagonal,
                Touch = touch,
                ResolutionMarketingName = nativeRes.HasValue ? DisplayNaming.MarketingName(nativeRes.Value) : null,
                ConnectionLabel = outputTech is { } ot ? DisplayNaming.ConnectionLabel(ot) : null,
            });
        }

        // Displays known to Windows but missing from the WMI monitor classes (e.g. inside some VMs).
        foreach (var device in native.Where(d => !usedNative.Contains(d.AdapterDeviceName)))
        {
            if (device.Current is null) continue;
            var best = device.Modes.OrderByDescending(m => (long)m.Width * m.Height).FirstOrDefault();
            var res = best is null ? new Resolution(device.Current.Width, device.Current.Height) : new Resolution(best.Width, best.Height);
            displays.Add(new DisplayInfo
            {
                Name = device.MonitorDeviceString,
                Manufacturer = Spec<string>.Unavailable(),
                IsInternal = false,
                IsPrimary = device.IsPrimary,
                NativeResolution = Spec<Resolution>.Estimated(res, "Highest mode offered by the graphics driver."),
                CurrentResolution = Spec<Resolution>.Detected(new Resolution(device.Current.Width, device.Current.Height)),
                CurrentRefreshRateHz = device.Current.RefreshHz > 0 ? Spec<double>.Detected(device.Current.RefreshHz) : Spec<double>.Unavailable(),
                MaxRefreshRateHz = Spec<double>.Unavailable(),
                DiagonalInches = Spec<double>.Unavailable("No display identification data was available."),
                Touch = digitizer is null ? Spec<bool>.Unavailable() : Spec<bool>.Detected(digitizer.IntegratedTouch || digitizer.ExternalTouch),
                ResolutionMarketingName = DisplayNaming.MarketingName(res),
            });
        }

        return displays.OrderByDescending(d => d.IsInternal).ThenByDescending(d => d.IsPrimary).ToList();
    }

    private static (Resolution? Preferred, int? MaxRefresh) ReadPreferredMode(WmiRow? modesRow)
    {
        if (modesRow is null) return (null, null);
        var modes = modesRow.GetRows("MonitorSourceModes");
        if (modes is null || modes.Length == 0) return (null, null);

        var preferredIndex = modesRow.GetInt32("PreferredMonitorSourceModeIndex") ?? 0;
        var parsed = modes.Select(m =>
        {
            var w = m.GetInt32("HorizontalActivePixels") ?? 0;
            var h = m.GetInt32("VerticalActivePixels") ?? 0;
            var num = m.GetInt32("VerticalRefreshRateNumerator") ?? 0;
            var den = m.GetInt32("VerticalRefreshRateDenominator") ?? 1;
            var hz = den == 0 ? 0 : (int)Math.Round((double)num / den);
            return (W: w, H: h, Hz: hz);
        }).Where(m => m.W > 0 && m.H > 0).ToList();
        if (parsed.Count == 0) return (null, null);

        var preferred = preferredIndex >= 0 && preferredIndex < parsed.Count ? parsed[preferredIndex] : parsed.OrderByDescending(m => (long)m.W * m.H).First();
        var maxAtNative = parsed.Where(m => m.W == preferred.W && m.H == preferred.H).Select(m => m.Hz).DefaultIfEmpty(0).Max();
        return (new Resolution(preferred.W, preferred.H), maxAtNative > 0 ? maxAtNative : null);
    }

    /// <summary>Joins "DISPLAY\AUO5D2D\5&1b0b5e8f&0&UID265_0" with "\\?\DISPLAY#AUO5D2D#5&1b0b5e8f&0&UID265#{guid}".</summary>
    private static NativeInfo.DisplayDeviceInfo? MatchNative(IReadOnlyList<NativeInfo.DisplayDeviceInfo> devices, string instanceName, HashSet<string> used)
    {
        var key = Normalise(instanceName);
        foreach (var d in devices)
        {
            if (d.MonitorDeviceId is null || used.Contains(d.AdapterDeviceName)) continue;
            if (Normalise(d.MonitorDeviceId) == key) return d;
        }
        // Single display with no id match: assume it is the one.
        var remaining = devices.Where(d => !used.Contains(d.AdapterDeviceName)).ToList();
        return remaining.Count == 1 ? remaining[0] : null;

        static string Normalise(string s)
        {
            var v = s.Replace("\\\\?\\", string.Empty).Replace('#', '\\');
            var brace = v.IndexOf('{');
            if (brace > 0) v = v[..brace].TrimEnd('\\');
            if (v.EndsWith("_0", StringComparison.Ordinal)) v = v[..^2];
            return v.ToUpperInvariant();
        }
    }
}
