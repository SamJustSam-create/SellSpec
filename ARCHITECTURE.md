# Architecture

SellSpec is three projects with one direction of dependency:

```
SellSpec.App (WPF, Windows)  ──►  SellSpec.Windows (providers)  ──►  SellSpec.Core (model + logic)
         │                                                                   ▲
         └───────────────────────────────────────────────────────────────────┘
```

- **SellSpec.Core** targets `net10.0` and has no Windows or UI dependencies. It owns the data model, normalisation, health rules, listing generation, report writers and the redaction guard. Everything in it is testable with mocked data.
- **SellSpec.Windows** targets `net10.0-windows` and implements the Core provider interfaces using WMI/CIM (`System.Management`), the registry and a small set of Win32 calls. It also hosts the `waveIn` microphone meter.
- **SellSpec.App** is the WPF application: shell, pages, themes, clipboard/file services, hardware test windows and the PNG renderer. It contains no detection logic and no listing logic.

Future platforms (macOS, Linux) would add a provider package beside `SellSpec.Windows` and a UI beside `SellSpec.App`; Core stays as it is.

## Core concepts

### Provenance-tagged values

Every important specification is a `Spec<T>`: a value plus a `Provenance` (`Detected`, `FirmwareReported`, `ManufacturerVerified`, `SellerProvided`, `Estimated`, `Unavailable`, `NotApplicable`) and an optional note. The UI, listings and reports read the provenance to decide how to label or whether to omit a value. `Unavailable` and `NotApplicable` carry no value and can never be rendered as a pass.

### DeviceSnapshot

The immutable result of one scan: `DeviceIdentity`, `ProcessorInfo`, `MemoryInfo`, `StorageDevice[]`, `GraphicsAdapter[]`, `DisplayInfo[]`, `BatteryInfo`, `ConnectivityInfo`, `OperatingSystemInfo`, plus `PrivateIdentifiers` (serials/UUIDs) that are shown only in-app and are excluded from every export.

Helper properties encode listing policy: `ListingStorage` returns only internal drives, `ListingGraphics` returns real (non-virtual, non-fallback) adapters, `PrimaryDisplay` prefers the built-in panel.

### Providers and the scanner

`IHardwareProvider<T>` is the unit of detection. `HardwareProviderSet` groups the nine providers; `HardwareScanner` runs them concurrently with:

- a per-component timeout (15 s, 30 s for the slow licensing and storage classes) and a 60 s overall budget,
- cooperative cancellation,
- failure isolation: an exception or timeout in one provider produces a `ScanIssue` and the fallback "unavailable" value for that component only,
- progress reporting (`ScanProgress`) for the UI,
- sanitised logging through `IScanLogger`.

`SnapshotEnricher` then performs cross-component reasoning (form factor from battery presence, Windows 11 readiness, warnings for virtual machines and fallback display drivers).

### Normalisation

Pure functions in `SellSpec.Core.Normalisation`:

| Class | Responsibility |
|---|---|
| `GenericValueDetector` | recognises OEM placeholders ("To Be Filled By O.E.M.", "Default string"…) |
| `ManufacturerNormaliser` | "ASUSTeK COMPUTER INC." → "ASUS", motherboard-vendor list |
| `ProcessorNameNormaliser` | strips ®/™/"CPU @ 2.40GHz"/"6-Core Processor", extracts generation and family ("Intel Core i5") |
| `CapacityFormatter` | RAM in binary GB, storage in marketing decimal sizes ("512 GB", "1 TB") |
| `MemoryTypeMapper` | SMBIOS type/form factor codes |
| `DeviceClassifier` | chassis type → form factor, virtual machine signatures, custom-built detection |
| `GraphicsClassifier` | integrated vs dedicated vs virtual, name tidy-up |
| `DisplayNaming` | EDID vendor codes, marketing resolution names, diagonal estimate with snapping |
| `WifiStandardInference` | 802.11 PHY types and adapter names → Wi-Fi generation |

### Health

`BatteryHealthCalculator` (full / design × 100 with validation and thresholds 80 / 60), `StorageHealthAssessor` (reported status, SMART prediction, wear, power-on hours), `Windows11Compatibility` (requirements checklist with a generation heuristic that is always labelled "not verified"), and `HealthDashboardBuilder`, which produces the rows shown on the Device Health page and in reports. There is deliberately **no overall health score**.

### Listing generation

`ListingGenerator` is deterministic and offline. `SpecSummary` produces the buyer-facing one-liners shared by listings, reports and the overview cards. `TitleBuilder` creates candidate titles from most to least detailed and picks the first that fits the platform limit, truncating at a word boundary only as a last resort. `MarketplaceTemplate` holds per-platform formatting (bullets, price in body, tone, title limit). Section rules:

- Specifications: only values that are available.
- Condition: only answered questions; unanswered questions produce no sentence.
- Known issues: explicit "No known faults, as described by the seller" only when the seller said *No*; failed hardware tests are always disclosed.
- Seller-confirmed tests are listed as such, never as SellSpec's findings.

### Reports

`ReportBuilder` turns a snapshot plus seller input into a `DeviceReport`, a public model of pre-formatted strings. Writers: `TextReportWriter`, `JsonReportWriter` (source-generated `System.Text.Json`, string enums, schema version), `PdfReportWriter` on top of a small in-house PDF 1.4 writer (`Reports/Pdf`), and the WPF `ReportDocumentView` rendered to PNG at 2× by the App. The PDF writer uses the standard Helvetica fonts with WinAnsi encoding and embedded width tables, so there is no PDF library and no font embedding.

### Privacy guard

`Redactor.EnsureClean` runs on every clipboard copy and file export. It checks the output for any private identifier captured during the scan, MAC addresses, IPv4 addresses, product-key patterns and user-profile paths, and refuses the export if anything matches. `LogSanitiser` applies the same rules to every log line.

### Remembered answers

`Core.Sessions` keeps the seller's work between sessions without identifying the device. `DeviceKey` derives a key from public identity only (brand, product and model number; the motherboard for a custom build; nothing for virtual machines or placeholder firmware). `JsonSavedAnswersStore` holds one `SavedDeviceAnswers` (questionnaire plus test results) per key, newest first and capped, and refuses any write that fails `Redactor`. `SavedAnswersPolicy` decides, from pure inputs, whether a scan clears the current session (only for a positively different device), offers saved answers (seller chooses) or restores them outright (the first scan of an elevated relaunch). Writes go through `AtomicFile` (random temporary name, create-new, move) under a lock file, because the original and the elevated instance share the folder.

## Application layer

- `ShellViewModel` owns navigation, the scan lifecycle and the shared `AppSession` (snapshot, questionnaire, test results, settings, the saved-answers offer).
- `AnswerPersistence` listens to questionnaire and test changes and writes the current device's answers through the store after a short pause; `ShellViewModel` flushes it before an elevated relaunch and `App` flushes it on exit.
- Page view models (`Overview`, `Specifications`, `DeviceHealth`, `HardwareTests`, `CreateListing`, `ExportReport`, `PrivacyChecklist`, `Settings`, `About`) subscribe to session changes and rebuild their rows; they contain presentation logic only.
- Views are XAML `UserControl`s selected by `DataTemplate`s in `Views/PageTemplates.xaml`.
- Theming: `Themes/Tokens.xaml` (type, spacing, icon glyphs), `Light.xaml` / `Dark.xaml` (brushes, swapped at runtime by `ThemeManager`, following the Windows app theme when set to System), `Controls.xaml` (restyled WPF controls).
- Services: `AppPaths` (portable vs local app data; `settings.json`, `answers.json`, `logs`), `FileScanLogger`, `ExportService` (atomic writes after redaction), `ClipboardService`, `ExternalLauncher` (allow-listed `ms-settings:` and `microsoft.windows.camera:` URIs, folders, elevated relaunch), `SoundService`, `CaptureHelper` (developer screenshots).

## Design rules

1. Never fabricate a value. Missing data is `Unavailable` with a note.
2. Never present `Unavailable` or `NotApplicable` as a pass, in any colour.
3. Every colour indicator has a text label.
4. Manufacturer-specific claims (upgradeability, exact panel size) are estimates or "Not verified" until a verified source exists.
5. Seller input is always attributed to the seller.
6. Exports go through `Redactor`; logs go through `LogSanitiser`.
7. No network calls, no background services, no elevation requirement.
8. Core must stay free of Windows and UI dependencies.

Decision records with the reasoning behind the main choices are in [docs/DECISIONS.md](docs/DECISIONS.md).
