# Hardware detection methods

All detection runs as a standard user. Every query is read-only. Each component is isolated: if a query fails or times out the component is reported as unavailable and the scan continues.

WMI queries select only the columns listed here; columns holding addresses, keys, user names or serial numbers that are not needed are never requested.

## Device identity

| Value | Source | Notes |
|---|---|---|
| Manufacturer, model, family, SKU, PC system type | `Win32_ComputerSystem` | Placeholders ("To Be Filled By O.E.M.", "System Product Name"…) are discarded |
| Product version (Lenovo marketing name) | `Win32_ComputerSystemProduct.Version` | Used as the product name when the model is a short code such as `82XF` |
| Motherboard | `Win32_BaseBoard` | |
| BIOS vendor, version, release date | `Win32_BIOS` | |
| Chassis types | `Win32_SystemEnclosure.ChassisTypes` | Mapped to laptop / desktop / all-in-one / tablet / convertible / mini PC / server |
| Firmware mode (UEFI / legacy) | `GetFirmwareType` (kernel32) | |
| Architecture | `Win32_ComputerSystem.SystemType`, `RuntimeInformation` | |
| Virtual machine | Signatures in manufacturer/model/BIOS strings (VMware, VirtualBox, Hyper-V "Virtual Machine", QEMU/KVM/SeaBIOS, Parallels, Xen…) | `HypervisorPresent` alone is not used because it is true on hosts with Hyper-V enabled |
| Custom-built PC | System manufacturer is a motherboard vendor (ASUS, Gigabyte, MSI, ASRock, Biostar, EVGA, NZXT, Supermicro) and the system model equals, or is contained in, the baseboard product, or the system strings are placeholders. Never for laptop-class chassis | Verified on a Gigabyte B550M K build |
| Serial number, UUID, board/BIOS serials | `Win32_ComputerSystemProduct`, `Win32_BaseBoard`, `Win32_BIOS` | Stored as private identifiers; shown only behind a toggle; never exported |

## Processor

`Win32_Processor` (name, manufacturer, cores, logical processors, max clock, architecture, caches, socket), summed across sockets. Fallback: `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`. Names are normalised (see `ProcessorNameNormaliser`): trademark marks, "CPU @ x.xxGHz", "with Radeon Graphics", "N-Core Processor" are removed; a leading "12th Gen" becomes a "(12th Gen)" suffix.

## Memory

`Win32_PhysicalMemory` (capacity, speed, configured speed, SMBIOS memory type, form factor, locator, manufacturer, part number), `Win32_PhysicalMemoryArray` (slot count, maximum capacity), `Win32_ComputerSystem.TotalPhysicalMemory`, `Win32_OperatingSystem.TotalVisibleMemorySize`.

- Installed = sum of module capacities (firmware reported). When no modules are reported (some VMs, restricted systems) the Windows total is rounded up to the nearest 512 MB and marked detected with a note.
- Technology from the SMBIOS type code; otherwise the legacy type; otherwise a conservative inference from speed (labelled as inferred).
- Upgradeability is **always "Not verified"**. The note mentions the firmware slot count, the module form factor and, for LPDDR, that such memory is normally soldered. Slot counts are never treated as proof of replaceability.

## Storage

Primary: `root\Microsoft\Windows\Storage` – `MSFT_PhysicalDisk` (model, media type, bus type, size, health status, spindle speed, firmware), `MSFT_Disk` (boot/system flags), `MSFT_StorageReliabilityCounter` (power-on hours, temperature, wear). Drive letters from `Win32_LogicalDiskToPartition`. Fallback: `Win32_DiskDrive`. SMART for ATA drives: `root\WMI` `MSStorageDriver_FailurePredictStatus` / `FailurePredictData` (attribute 9 = power-on hours).

- Location: USB → external (or removable when Windows says removable media), SD/MMC → removable, virtual/file-backed/Storage Spaces → virtual, SATA/NVMe/RAID/SAS/SCSI → internal. Only internal drives appear in listings and reports.
- Media type: Windows media type; else NVMe → SSD; else model-name heuristics (SSD/HDD families); else spindle speed.
- Health: Windows `HealthStatus` (Healthy/Warning/Unhealthy), SMART failure prediction, wear ≥ 80 % → attention, ≥ 95 % → issue, ≥ 35,000 power-on hours → attention. Missing data → "Health not available", never "healthy".
- **Reliability counters usually require administrator rights.** Without them the UI explains this and offers an optional elevated rescan (UAC prompt; same read-only queries).
- Physical disks and partitions are never double-counted: partitions only contribute drive letters.

## Graphics

`Win32_VideoController` (name, vendor, AdapterRAM, driver version/date, PNP id, availability, error code). Dedicated memory is read from the display class registry key `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-…}\00NN\HardwareInformation.qwMemorySize`, matched to the adapter by PCI vendor/device/subsystem id, because `AdapterRAM` is a 32-bit value capped near 4 GB.

Classification by vendor and name (NVIDIA → dedicated; Intel Iris/UHD/HD/Arc-without-model → integrated, Arc A/B-series → dedicated; AMD "Radeon Graphics"/Vega 3-11/6xxM-8xxM → integrated, RX/Pro → dedicated; Hyper-V/VMware/VirtualBox/Remote Display → virtual; "Microsoft Basic Display Adapter" → unknown with a warning). Dedicated memory is reported only for dedicated adapters so that shared system memory is never advertised as VRAM.

## Display

`root\WMI` – `WmiMonitorID` (EDID manufacturer code, product code, friendly name), `WmiMonitorBasicDisplayParams` (physical size in whole centimetres), `WmiMonitorConnectionParams` (output technology: internal / embedded DisplayPort / LVDS = built-in), `WmiMonitorListedSupportedSourceModes` (preferred mode = native resolution, refresh rates). Win32 `EnumDisplayDevices` / `EnumDisplaySettingsEx` supply the current mode, the supported mode list and the primary flag; monitors are matched to adapters by device instance path. Touch: `GetSystemMetrics(SM_DIGITIZER)` (integrated touch is attributed to the built-in display; it cannot be determined per external monitor).

Diagonal size = √(w² + h²) from EDID centimetres, snapped to a list of common panel sizes within tolerance, and **always labelled Estimated**. EDID rounding gives about ±0.25" of uncertainty, so 13.3/13.4 or 15.3/15.6 cannot be told apart; the common size is chosen.

## Battery

`GetSystemPowerStatus` (presence, AC state, charge), `Win32_Battery` (status, charge, chemistry, Windows condition), `root\WMI` `BatteryStaticData` (design capacity, chemistry, manufacturer, serial → private), `BatteryFullChargedCapacity`, `BatteryCycleCount`, `BatteryStatus` (charging/discharging/AC).

- Health = full-charge ÷ design × 100. Missing or non-positive values → unavailable; ratio above 125 % → unavailable with a firmware-error note; 100–125 % → capped at 100 % with a note. Thresholds: ≥ 80 % good, 60–79 % attention, below → issue.
- Multiple packs: capacities are summed; the cycle count shown is the highest. A reported cycle count of 0 is shown with a note that the firmware probably does not track cycles.
- No battery (desktops): "Not applicable", never an error.

## Connectivity

`root\StandardCimv2` `MSFT_NetAdapter` (interface description, physical medium, hardware/virtual flags only), fallback `Win32_NetworkAdapter` (physical adapters only). Virtual, VPN, Hyper-V, WSL, Bluetooth PAN and loopback adapters are filtered by name and flags. **MAC addresses, GUIDs, IP addresses, connection names and Wi-Fi profiles are never requested.**

- Wi-Fi generation: WLAN API `WlanGetInterfaceCapability` PHY types (802.11be → Wi-Fi 7, ax → 6, ac → 5, n → 4) marked Detected; 6E is inferred from the adapter model name because the PHY list does not expose the 6 GHz band. Without the WLAN API the adapter name is used and the value is marked Estimated.
- Bluetooth: any `Win32_PnPEntity` of class Bluetooth with status OK; radio names are filtered for display.
- Webcam: PnP class Camera, or Image with a camera-like name, or the `usbvideo` service; virtual cameras (OBS, Snap Camera…) are excluded.
- Audio: `Win32_SoundDevice`, virtual devices excluded.
- Fingerprint reader: PnP class Biometric.

## Operating system

`Win32_OperatingSystem` (caption, version, build, architecture, install date – not `RegisteredUser` or `SerialNumber`), registry `CurrentVersion` (display version, edition id, UBR). Windows 11 is identified by build ≥ 22000 because the registry product name still says "Windows 10".

- Activation: `SoftwareLicensingProduct` filtered to the Windows application id with a partial key present; `LicenseStatus` 1 = licensed. The partial product key column is **not selected**. The licence channel (OEM digital / OEM / Retail / Volume) is parsed from the description.
- TPM: `Tbsi_GetDeviceInfo` (tbs.dll) – works for standard users, unlike `Win32_Tpm` which needs elevation.
- Secure Boot: `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled`; "Not applicable" in legacy BIOS mode.
- Windows 11 readiness: already running Windows 11, or a checklist of TPM 2.0, UEFI/Secure Boot, 4 GB RAM, 64 GB system drive, 64-bit dual-core ≥ 1 GHz, and a processor generation heuristic (Intel 8th gen+, AMD Ryzen 2000+, Core Ultra, Snapdragon 7c/8c/8cx/X, Intel N-series). The heuristic is always labelled "not verified against Microsoft's list".

## Known weak spots

See [KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md): OEM firmware placeholders, laptops that report a desktop chassis, memory maximums, EDID sizes, Wi-Fi 6E, drive reliability counters without elevation, SMART on NVMe behind some controllers, and virtual machines.
