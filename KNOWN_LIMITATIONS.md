# Known limitations

Honest list of what is partial, estimated or not implemented in 0.1.0-alpha. Items are grouped by how they affect a seller.

## Detection accuracy

- **OEM firmware is the source of truth and is sometimes wrong.** Placeholder strings are discarded, but a laptop whose firmware reports a desktop chassis will be shown as a desktop unless a battery is present (then the form factor is estimated). Report such devices with the raw strings from *Show technical details*.
- **Product names** follow the firmware. HP/Dell/ASUS names are usually marketing-friendly; Lenovo's marketing name is taken from the product version field. Some vendors put the same code in every field, in which case only the code is shown.
- **Memory upgradeability is never confirmed.** SellSpec shows firmware slot counts and module form factor with caveats and always labels the answer "Not verified". Firmware maximum-capacity values are frequently wrong and are shown as firmware reported.
- **Screen size is an estimate** derived from EDID whole-centimetre dimensions, snapped to common panel sizes. 13.3 vs 13.4, 15.3 vs 15.6 and 17.0 vs 17.3 cannot be distinguished; the common size is chosen and labelled Estimated.
- **Touch** is reported system-wide (integrated digitizer → built-in display). Per-monitor touch on external displays is not determined.
- **Refresh rate** is the highest rate the panel or driver lists at native resolution; panels that only advertise high refresh through DisplayID may show 60 Hz.
- **Wi-Fi 6E / 7** band support is inferred from the adapter model name; the WLAN API confirms the 802.11 generation (ax/be) but not the 6 GHz band.
- **Bluetooth version** is not detected; only presence.
- **Dedicated graphics memory** comes from the driver's registry value; a few drivers do not populate it, in which case the Windows `AdapterRAM` value is used if it is below the 4 GB cap, otherwise the size is unavailable. Integrated graphics never show a memory figure.
- **Storage reliability counters** (power-on hours, wear, temperature) usually need administrator rights. Without them SellSpec shows "Not available" and offers an optional elevated rescan. SMART attributes are read only for ATA drives via the storage driver; NVMe drives behind some RAID/Intel RST controllers expose no health data at all.
- **Drive health** relies on Windows' `HealthStatus`; "Healthy (reported)" means Windows did not flag the drive, not that SellSpec ran a surface test.
- **Battery cycle count** is not tracked by many batteries (reported as 0 → shown as not tracked). Health is a reported capacity estimate, not a runtime or safety guarantee.
- **Windows 11 readiness** uses a processor generation heuristic; Microsoft's supported-processor list is not bundled. The result is always worded "not verified".
- **Activation** reads the Software Licensing service; on some enterprise/volume setups the query returns nothing and the status is unavailable.
- **Virtual machines** are detected by firmware signatures. Unusual hypervisors may not be recognised; obvious virtual disks and adapters are still excluded.
- **ARM64 devices** (Snapdragon) are supported by the code paths but have not been tested on hardware.

## Features

- **Webcam test** does not show a preview inside SellSpec; it opens the Windows Camera app. A WinRT-based in-app preview was deliberately left out because it adds ~20 MB of Windows SDK projections (see docs/DECISIONS.md).
- **Keyboard test** cannot observe the Fn key, and Windows intercepts the Windows key and Print Screen on key-up; those are tested by eye.
- **Remembered answers are keyed by device model, not by unit.** Two identical models share one saved set (the seller is asked before it is restored), and a device whose model cannot be identified (placeholder firmware strings, virtual machines) is not remembered at all.
- **Reports are not signed or tamper-proof**, and say so.
- **Listing templates are built in**; custom templates, branded reports, bulk scanning, inventory, CSV, valuations and marketplace posting are roadmap items.
- **Single language** (English, Australian spelling). Currency symbol is configurable; number formatting uses invariant culture in listings.
- **PDF export** uses the standard Helvetica fonts, so characters outside Latin-1 are replaced with "?" in the PDF (the PNG, text and JSON exports keep them).

## Platform

- Windows 10 and 11, 64-bit only. Windows 10 shows Segoe MDL2 icons and Segoe UI instead of the Windows 11 fonts.
- No installer, and the executables are unsigned: SignPath Foundation declined the project's application in November 2026 pending public adoption, and no other signing route is open to the maintainers (see CODE_SIGNING_POLICY.md). SmartScreen may warn on first launch. Releases after 0.1.2-alpha carry GitHub build provenance attestations on every asset instead.
- The self-contained build is large because WPF cannot be trimmed.

## Verified so far

Real-hardware verification: automated integration tests and the capture run on one custom-built desktop (Gigabyte B550M K, Ryzen 5 5600, RX 7700 XT, Windows 11 Pro 25H2), and the manual checklist on laptop hardware on 2026-11-26, reported by the owner as passing in full (device models still to be recorded in TESTING.md). Coverage of specific OEM naming quirks grows with each device added to the verification log in TESTING.md.
