# Architectural decision records

Short records of the choices that shaped the MVP, with the alternatives considered.

## ADR-1: .NET 10 LTS, WPF, MVVM

**Decision.** C# on .NET 10 (LTS, supported to November 2028) with WPF and the MVVM pattern, as the brief preferred.
**Alternatives.** .NET 8 was the previous LTS but reached end of support in November 2026, so starting a new product on it made no sense. WinUI 3 / Windows App SDK gives native Windows 11 visuals but adds a large runtime dependency, has a heavier deployment story and weaker tooling for a lightweight portable exe. Avalonia would ease future macOS/Linux ports but is a third-party framework with its own look; the brief asked for native Windows feel and a Windows-only MVP. WPF with a hand-written Windows 11 design system meets the look-and-feel requirement at zero dependency cost.
**Consequence.** Future platforms need a separate UI project; Core and the provider interfaces are already platform-neutral.

## ADR-2: Provenance on every value

**Decision.** `Spec<T>` carries value + provenance + note; `Unavailable` and `NotApplicable` have no value.
**Why.** The brief's accuracy requirements ("never fabricate", "never present unavailable as passing", "firmware vs verified") are impossible to enforce after the fact. Encoding the state in the type makes every consumer handle it and makes the honesty rules testable.

## ADR-3: Concurrent, isolated scanning with timeouts

**Decision.** Nine providers run concurrently; each has its own timeout and exception boundary; a 60 s overall budget caps the scan.
**Why.** WMI classes are independently flaky (missing on some systems, slow on others, access-denied without elevation). Isolation turns each failure into an "unavailable" value with a note instead of a failed scan, and concurrency keeps the scan near one second on typical hardware.

## ADR-4: System.Management rather than Microsoft.Management.Infrastructure

**Decision.** Use the `System.Management` package for WMI/CIM.
**Why.** It covers every namespace needed (`root\CIMV2`, `root\WMI`, `root\Microsoft\Windows\Storage`, `root\StandardCimv2`), is maintained by Microsoft under MIT, and needs no extra setup. MI/CIM would be marginally faster but brings a more complex API for no practical benefit at this scale. PowerShell invocation was rejected because spawning processes is slow, fragile and a security smell.

## ADR-5: Win32 calls for TPM, Secure Boot, power, display and WLAN

**Decision.** P/Invoke `Tbsi_GetDeviceInfo`, `GetFirmwareType`, `GetSystemPowerStatus`, `GetSystemMetrics`, `EnumDisplayDevices`/`EnumDisplaySettingsEx` and the WLAN capability API.
**Why.** The WMI equivalents either require elevation (`Win32_Tpm`, `Confirm-SecureBootUEFI`) or do not exist (current display mode, 802.11 PHY types). These calls work for standard users, which is a hard requirement.

## ADR-6: In-house PDF writer

**Decision.** A ~400-line PDF 1.4 writer using the base-14 Helvetica fonts with WinAnsi encoding and embedded width tables.
**Alternatives.** QuestPDF (community licence conditions for commercial use), PdfSharp (MIT but ~1 MB and font-resolver setup on .NET Core), iText (AGPL), printing through "Microsoft Print to PDF" (requires the printer and a dialog). None justified a dependency and a licence review for a single-template report.
**Consequence.** Non-Latin-1 characters become "?" in the PDF only; the writer is unit-tested (xref offsets, pagination, encoding).

## ADR-7: PNG export via WPF rendering

**Decision.** The report preview control is rendered with `RenderTargetBitmap` at 2× for the PNG export.
**Why.** It guarantees the image matches the preview exactly and costs nothing. The control uses fixed colours so the export looks the same in dark mode.

## ADR-8: Webcam check through the Windows Camera app

**Decision.** No in-app camera preview in the MVP.
**Why.** An in-app preview in WPF needs the Windows SDK projections (`Microsoft.Windows.SDK.NET.dll`, tens of MB) or a DirectShow/Media Foundation interop layer of several hundred lines. Both conflict with the lightweight target. Launching `microsoft.windows.camera:` lets the user verify the camera without SellSpec touching it, which is also the stronger privacy story. Revisit when a small capture path is available.

## ADR-9: Microphone meter with waveIn

**Decision.** ~150 lines of `winmm` P/Invoke instead of NAudio.
**Why.** A level meter only needs one input format and a buffer callback. Buffers are zeroed on release; nothing is written to disk.

## ADR-10: Redaction as a guard, not a filter

**Decision.** Exports are built from public models only; `Redactor` then verifies the final text and refuses the export on a hit.
**Why.** A filter that silently removes identifiers would hide bugs. Refusing makes a leak impossible and visible.

## ADR-11: Custom-built detection rule

**Decision.** A desktop is custom-built when the system manufacturer is a motherboard vendor and the system model equals, or is contained in, the baseboard product, or when the system strings are placeholders. Laptops are never custom-built.
**Why.** Verified against a real Gigabyte board (system model "B550M K" = board product) and known MSI behaviour (system model "MS-7C56" inside "MAG B550 TOMAHAWK (MS-7C56)"), while keeping branded ASUS/MSI desktops ("ROG Strix G15DK") as branded. The earlier "model contains board" rule was dropped because it misclassified branded desktops.

## ADR-12: Screen-size snapping

**Decision.** Estimate the diagonal from EDID centimetres and snap to a pruned list of common panel sizes; always label it Estimated.
**Why.** EDID stores whole centimetres, giving about ±0.25" uncertainty; a long list with 13.3/13.4/15.3/15.6/17.0/17.3 all present produced wrong snaps in tests. The pruned list picks the common size, which is right far more often and is still labelled an estimate.

## ADR-13: No overall health score

**Decision.** Individual health rows only.
**Why.** The brief required a defensible methodology for any score; none exists that is honest across batteries, drives and seller-confirmed tests. Buyers are better served by the rows.

## ADR-14: CommunityToolkit.Mvvm as the one UI dependency

**Decision.** Use the toolkit's source generators for observable properties and commands.
**Why.** It removes hundreds of lines of boilerplate, is MIT-licensed, maintained by Microsoft/.NET Foundation and adds a small assembly. Hand-rolling was considered and rejected as a maintainability cost with no privacy or size benefit.

## ADR-15: Developer capture mode in the product binary

**Decision.** `--capture` renders every page and writes sample exports, then exits.
**Why.** It lets UI changes be reviewed without a screen or a GUI automation stack, and produced the documentation screenshots. It runs the production code paths; it does not inject data.

## ADR-16: MIT licence

**Decision.** SellSpec is released under the MIT Licence; contributions are accepted under the same terms. The earlier "licence not chosen, commercial version possible" stance is withdrawn.
**Why.** Free code signing through SignPath Foundation requires an OSI-approved licence with no commercial dual-licensing, and SellSpec's maintainers cannot obtain a certificate of their own (a certificate authority validates an adult individual or an organisation; Azure Artifact Signing's individual route is limited to the United States and Canada). MIT is the licence of the project's only dependencies, is the most common choice for .NET desktop tools, and keeps forks and packaging (winget, Scoop, Chocolatey) frictionless.

## ADR-17: Code signing by SignPath Foundation, failing closed

**Decision.** Executables are signed inside the release workflow by SignPath Foundation with origin verification and manual approval. `build/signing.ps1` stages the files under fixed names, then verifies every returned file (valid, timestamped, certificate common name exactly "SignPath Foundation") before replacing the executables and rebuilding the zip and checksums. The release job re-checks that the tag still points at the built commit before creating and before publishing the release. The steps run only when the SignPath repository variables are set; once set, any signing failure fails the release.
**Why.** No private key exists anywhere the project controls, so nothing can leak from the repository or a maintainer's machine. Fixed staging names keep SignPath's artifact configuration independent of the version. Failing closed means a release can never be published half-signed or silently unsigned; a release without signing is possible only while signing is not configured, and its notes say so.

## ADR-18: Remembered answers keyed by model, saved automatically, failing closed

**Decision.** Questionnaire answers and hardware test results are written to `answers.json` in the data folder a moment after they change, under a key built from public identity only (brand, product and model number; the motherboard for a custom build). Nothing is kept for virtual machines, placeholder firmware or the demonstration device. On the next scan of the same model the answers are offered back and the seller chooses Restore or Discard; the first scan of an administrator relaunch restores them without asking. Every write passes the export redaction check first; a serial number typed into a note stops the write and the UI says so.
**Why.** Sellers lost their work on every restart and on the administrator rescan, which is a relaunch. Keying by model rather than by serial keeps the file free of identifiers and lets a refurbisher reuse answers across identical units, while the explicit offer stops one unit's defects being attributed to another by accident. Automatic saving avoids a "save" button nobody would press before the UAC prompt; the redaction guard and the per-user data folder keep the file as safe as a settings file.
