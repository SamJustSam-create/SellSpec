using System.Runtime.InteropServices;
using System.Security.Principal;
using SellSpec.Core.Normalisation;

namespace SellSpec.Windows.Native;

/// <summary>Safe wrappers around the Win32 calls. Every method swallows platform errors and returns null.</summary>
public static class NativeInfo
{
    public sealed record PowerStatus(bool? HasBattery, bool? OnAcPower, int? ChargePercent);

    public static PowerStatus? GetPowerStatus()
    {
        try
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status)) return null;
            bool? hasBattery = status.BatteryFlag switch
            {
                128 => false,
                255 => null,
                _ => true,
            };
            bool? onAc = status.ACLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null,
            };
            int? percent = status.BatteryLifePercent is <= 100 ? status.BatteryLifePercent : null;
            return new PowerStatus(hasBattery, onAc, percent);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>"UEFI", "Legacy BIOS" or null.</summary>
    public static string? GetFirmwareType()
    {
        try
        {
            if (!NativeMethods.GetFirmwareType(out var type)) return null;
            return type switch
            {
                NativeMethods.FirmwareTypeUefi => "UEFI",
                NativeMethods.FirmwareTypeBios => "Legacy BIOS",
                _ => null,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public sealed record DigitizerInfo(bool IntegratedTouch, bool ExternalTouch, bool IntegratedPen, bool Ready);

    public static DigitizerInfo? GetDigitizer()
    {
        try
        {
            var value = NativeMethods.GetSystemMetrics(NativeMethods.SM_DIGITIZER);
            return new DigitizerInfo(
                (value & NativeMethods.NID_INTEGRATED_TOUCH) != 0,
                (value & NativeMethods.NID_EXTERNAL_TOUCH) != 0,
                (value & NativeMethods.NID_INTEGRATED_PEN) != 0,
                (value & NativeMethods.NID_READY) != 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public sealed record TpmInfo(bool Present, string? Version);

    /// <summary>Uses the TPM Base Services API, which works for standard users (the WMI Win32_Tpm class needs elevation).</summary>
    public static TpmInfo? GetTpm()
    {
        try
        {
            var size = (uint)Marshal.SizeOf<NativeMethods.TPM_DEVICE_INFO>();
            var result = NativeMethods.Tbsi_GetDeviceInfo(size, out var info);
            if (result == NativeMethods.TBS_E_TPM_NOT_FOUND) return new TpmInfo(false, null);
            if (result != NativeMethods.TBS_SUCCESS) return null;
            var version = info.TpmVersion switch
            {
                NativeMethods.TPM_VERSION_20 => "2.0",
                NativeMethods.TPM_VERSION_12 => "1.2",
                _ => null,
            };
            return new TpmInfo(true, version);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public sealed record DisplayMode(int Width, int Height, int RefreshHz);

    public sealed record DisplayDeviceInfo(
        string AdapterDeviceName,
        string? MonitorDeviceId,
        string? MonitorDeviceString,
        bool IsPrimary,
        DisplayMode? Current,
        IReadOnlyList<DisplayMode> Modes);

    /// <summary>Enumerates active display outputs with their current mode and supported modes.</summary>
    public static IReadOnlyList<DisplayDeviceInfo> GetDisplayDevices()
    {
        var list = new List<DisplayDeviceInfo>();
        try
        {
            for (uint i = 0; i < 32; i++)
            {
                var adapter = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
                if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0)) break;
                if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
                if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) continue;

                string? monitorId = null;
                string? monitorString = null;
                var monitor = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
                if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    monitorId = monitor.DeviceID;
                    monitorString = monitor.DeviceString;
                }

                DisplayMode? current = null;
                var mode = new NativeMethods.DEVMODE { dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>() };
                if (NativeMethods.EnumDisplaySettingsEx(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode, 0) && mode.dmPelsWidth > 0)
                {
                    current = new DisplayMode((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency);
                }

                var modes = new List<DisplayMode>();
                for (var m = 0; m < 2048; m++)
                {
                    var dm = new NativeMethods.DEVMODE { dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>() };
                    if (!NativeMethods.EnumDisplaySettingsEx(adapter.DeviceName, m, ref dm, 0)) break;
                    if (dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0) continue;
                    modes.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency));
                }

                list.Add(new DisplayDeviceInfo(
                    adapter.DeviceName,
                    GenericValueDetector.Clean(monitorId),
                    GenericValueDetector.Clean(monitorString),
                    (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    current,
                    modes.Distinct().ToList()));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // Return whatever was gathered.
        }
        return list;
    }

    public sealed record WlanInterface(string Description, IReadOnlyList<int> PhyTypes);

    /// <summary>
    /// Reads supported 802.11 PHY types per wireless interface through the WLAN API. Only capability data is
    /// requested: no profiles, SSIDs or credentials are touched.
    /// </summary>
    public static IReadOnlyList<WlanInterface> GetWlanInterfaces()
    {
        var result = new List<WlanInterface>();
        var handle = IntPtr.Zero;
        var listPtr = IntPtr.Zero;
        try
        {
            if (NativeMethods.WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return result;
            if (NativeMethods.WlanEnumInterfaces(handle, IntPtr.Zero, out listPtr) != 0 || listPtr == IntPtr.Zero) return result;

            var count = Marshal.ReadInt32(listPtr);
            var headerSize = 8; // dwNumberOfItems + dwIndex
            var itemSize = Marshal.SizeOf<NativeMethods.WLAN_INTERFACE_INFO>();
            for (var i = 0; i < count && i < 16; i++)
            {
                var itemPtr = IntPtr.Add(listPtr, headerSize + i * itemSize);
                var info = Marshal.PtrToStructure<NativeMethods.WLAN_INTERFACE_INFO>(itemPtr);
                var guid = info.InterfaceGuid;
                var capPtr = IntPtr.Zero;
                try
                {
                    if (NativeMethods.WlanGetInterfaceCapability(handle, ref guid, IntPtr.Zero, out capPtr) != 0 || capPtr == IntPtr.Zero)
                    {
                        result.Add(new WlanInterface(info.InterfaceDescription, Array.Empty<int>()));
                        continue;
                    }
                    var cap = Marshal.PtrToStructure<NativeMethods.WLAN_INTERFACE_CAPABILITY>(capPtr);
                    var phys = cap.Dot11PhyTypes.Take((int)Math.Min(cap.NumberOfSupportedPhys, 64)).ToList();
                    result.Add(new WlanInterface(info.InterfaceDescription, phys));
                }
                finally
                {
                    if (capPtr != IntPtr.Zero) NativeMethods.WlanFreeMemory(capPtr);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or AccessViolationException)
        {
            // WLAN service not running or API unavailable (e.g. Server Core).
        }
        finally
        {
            if (listPtr != IntPtr.Zero) NativeMethods.WlanFreeMemory(listPtr);
            if (handle != IntPtr.Zero) NativeMethods.WlanCloseHandle(handle, IntPtr.Zero);
        }
        return result;
    }
}
