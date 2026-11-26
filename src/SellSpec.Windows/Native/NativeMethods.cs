using System.Runtime.InteropServices;

namespace SellSpec.Windows.Native;

/// <summary>Win32 declarations used by the detection layer. All calls are read-only and work for standard users.</summary>
internal static class NativeMethods
{
    // --- Power -------------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // --- Firmware ----------------------------------------------------------------------------------------

    internal const uint FirmwareTypeBios = 1;
    internal const uint FirmwareTypeUefi = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFirmwareType(out uint firmwareType);

    // --- Input -------------------------------------------------------------------------------------------

    internal const int SM_DIGITIZER = 94;
    internal const int NID_INTEGRATED_TOUCH = 0x01;
    internal const int NID_EXTERNAL_TOUCH = 0x02;
    internal const int NID_INTEGRATED_PEN = 0x04;
    internal const int NID_MULTI_INPUT = 0x40;
    internal const int NID_READY = 0x80;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    // --- TPM ---------------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct TPM_DEVICE_INFO
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    internal const uint TBS_SUCCESS = 0;
    internal const uint TBS_E_TPM_NOT_FOUND = 0x8028400F;
    internal const uint TPM_VERSION_12 = 1;
    internal const uint TPM_VERSION_20 = 2;

    [DllImport("tbs.dll")]
    internal static extern uint Tbsi_GetDeviceInfo(uint size, out TPM_DEVICE_INFO info);

    // --- Display -----------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    internal const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    internal const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    internal const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x8;
    internal const uint DISPLAY_DEVICE_ACTIVE = 0x1;
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string? device, uint deviceIndex, ref DISPLAY_DEVICE displayDevice, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    internal const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsEx(string? deviceName, int modeNum, ref DEVMODE devMode, uint flags);

    // --- WLAN --------------------------------------------------------------------------------------------

    [DllImport("wlanapi.dll")]
    internal static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    internal static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    internal static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    internal static extern uint WlanGetInterfaceCapability(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr capability);

    [DllImport("wlanapi.dll")]
    internal static extern void WlanFreeMemory(IntPtr memory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string InterfaceDescription;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WLAN_INTERFACE_CAPABILITY
    {
        public int InterfaceType;
        public int Dot11DSupported;
        public uint MaxDesiredSsidListSize;
        public uint MaxDesiredBssidListSize;
        public uint NumberOfSupportedPhys;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public int[] Dot11PhyTypes;
    }

    // --- Shell -------------------------------------------------------------------------------------------

    [DllImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsUserAnAdmin();
}
