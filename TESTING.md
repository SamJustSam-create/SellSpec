# Testing

## Automated tests

```powershell
dotnet test SellSpec.slnx
```

| Project | Count | What it covers |
|---|---|---|
| `tests/SellSpec.Core.Tests` | 287 | normalisation (processor names, brands, placeholders, capacities, memory types, display names, Wi-Fi inference, graphics and device classification), battery health calculation (missing/invalid/multiple packs/zero cycles), storage health, Windows 11 assessment, the scanner (failing, slow and cancelled providers, progress, private-value registration), snapshot enrichment, log sanitising, title building and platform limits, listing generation for every scenario and marketplace, questionnaire handling and defect disclosure, report building, text/JSON/PDF writers (structure, xref offsets, pagination, encoding), redaction, diagnostics bundle (entries, sanitised log, identifier refusal across all scenarios), remembered answers (device key from public identity only with separator escaping, store round-trip, newest-first cap, corrupt and damaged files, locked file reported, identifier refusal with no temp file left, notes count as answers, offer/restore policy), settings store, test tone, checklist, demo device |
| `tests/SellSpec.Windows.Tests` | 56 | provider helper mappings (bus/media/location, SMART parsing, PnP matching, edition and licence parsing, model-code detection, memory rounding, Bluetooth radio-name filtering) and **integration tests on the real machine** (full scan completes within budget with no error-level issues, cancellation returns quickly, native calls do not throw, exports of the real scan contain no private identifiers) |

The ten device scenarios required by the brief are fixtures in `tests/SellSpec.Core.Tests/Scenarios.cs` and are exercised by the listing and report tests:

1. Standard Windows laptop (Lenovo IdeaPad, i5-1335U, 16 GB DDR5, 512 GB NVMe)
2. Gaming laptop with integrated and dedicated graphics (Ryzen 7 5800H, RTX 3060 6 GB, 144 Hz, two drives)
3. Custom-built desktop (Gigabyte B550M K, RTX 3070, SSD + HDD, external monitor, no battery)
4. Laptop with soldered LPDDR5 memory (upgradeability must read "Not verified")
5. Device with multiple drives including an external SSD and a virtual disk (excluded from listings)
6. Device without a battery (same as 3)
7. Laptop whose battery does not report design capacity (health unavailable, not shown as good)
8. Device with placeholder manufacturer strings (title falls back to "Windows laptop")
9. Hyper-V virtual machine (warnings, virtual adapters excluded)
10. Non-administrator scan (power-on hours unavailable with the elevation note)

Mocked hardware never reaches the production application. The only non-real data path is the `--demo` switch, which loads a device manufactured by "SellSpec Demo" and shows a banner on every page.

Last run (development machine, 2026-11-27): **343 passed, 0 failed, 0 skipped.** CI runs the same suite on `windows-latest` for every push and pull request.

## Performance measurements

Measured on the development machine: custom-built desktop, AMD Ryzen 5 5600, 32 GB DDR4, two NVMe SSDs, Windows 11 Pro 25H2, standard user, Release build. Methods are in `docs/MANUAL_TEST_CHECKLIST.md`.

| Target (brief) | Measured | Notes |
|---|---|---|
| Scan ≤ 10 s | **0.5–1.3 s** | all nine components concurrently; the first scan after boot is slowest (Windows licensing query); `Scan finished` log lines: 1327, 517, 529 ms |
| Startup ≤ 3 s | **0.54–0.69 s** | wall clock from process start until the main window handle exists, three cold-ish runs: 686, 541, 550 ms; the in-app stopwatch reports ~490 ms to window shown |
| Idle memory low | **~160 MB working set, ~111 MB private bytes** | measured 18 s after launch with the scan complete and the overview visible. This is normal for a WPF application (the WPF/.NET runtime accounts for most of it) but is not "tiny"; see notes |
| Idle CPU ~0 | **0 ms of CPU over a 10 s idle window** | no timers or background work after the scan; total CPU for launch + scan ≈ 1.9–2.2 s |
| No internet traffic | **none** | no network code exists; verified by inspection |
| Download < 20 MB | see artifact table below | framework-dependent builds meet it; the self-contained build is larger by design |

Measurement script: `docs/MANUAL_TEST_CHECKLIST.md` section I (a PowerShell loop starting the Release build three times and sampling `WorkingSet64`, `PrivateMemorySize64` and `TotalProcessorTime`).

Notes on memory: the working set includes the shared WPF and .NET runtime pages; private bytes (memory attributable to SellSpec's own state and the WPF visual tree) are about 111 MB. Reducing this further would mean leaving WPF, which was a deliberate trade-off (docs/DECISIONS.md, ADR-1).

### Release artifacts

Sizes of the v0.1.2-alpha assets as published on GitHub Releases by the release workflow (Release, win-x64, all tests passing first):

| Artifact | Size | Runtime requirement |
|---|---|---|
| `SellSpec-0.1.2-alpha-win-x64-portable.zip` | **772 KB** | .NET Desktop Runtime 10 |
| `SellSpec-0.1.2-alpha-win-x64-singlefile.exe` | **1.8 MB** | .NET Desktop Runtime 10 |
| `SellSpec-0.1.2-alpha-win-x64-selfcontained.exe` | **61.9 MB** (compressed single file) | none |

For comparison, 0.1.0-alpha measured 546 KB, 1.4 MB and 60.5 MB. The executables grew with the field-report and remembered-answers code; the portable zip additionally gained the bundled licence, code signing policy and privacy policy, which are not part of either executable.

Both framework-dependent artifacts are far below the 20 MB target. The self-contained build cannot meet it because WPF is not trimmable; it exists for users who do not want to install the runtime. After publishing, the self-contained executable was started in `--capture` mode from a clean launch and produced the full page set and exports. The portable executable, started the same way on this machine (which has no machine-wide .NET runtime), correctly showed the standard ".NET must be installed" prompt; with `DOTNET_ROOT` pointed at a .NET 10 runtime it ran and produced the same output, and wrote its settings and logs to the `data` folder beside the executable as portable mode requires. SHA-256 checksums are in `artifacts\SHA256SUMS.txt`. Nothing is code-signed.

## Manual Windows testing checklist

Items the development environment could not exercise automatically. The full checklist with steps and expected results is in [docs/MANUAL_TEST_CHECKLIST.md](docs/MANUAL_TEST_CHECKLIST.md). Summary of what still needs human verification on real devices:

- Laptops: battery health/cycles, built-in display size and touch, chassis-based form factor, Lenovo/HP/Dell/ASUS naming.
- Keyboard test on a physical keyboard, display colour test on the built-in panel, speaker tone audible left/right, microphone meter moves, Windows Camera opens.
- Save dialogs and exports to removable or network locations; clipboard paste into Facebook/eBay/Gumtree forms.
- Elevated rescan via UAC and the resulting power-on hours.
- Windows 10 (fonts fall back to Segoe UI / MDL2 icons), 125–200 % display scaling, 1366×768 screens.
- Virtual machines (Hyper-V, VMware, VirtualBox) show the warning and produce no misleading listing.

## What the development environment did verify

- Build of all five projects from a clean clone with the .NET 10 SDK.
- All automated tests, including the real-hardware integration tests on a custom-built desktop.
- The application launches, scans real hardware, renders every page in light and dark themes (captured to PNG), and writes PDF/PNG/text/JSON reports and listings for all four marketplaces, with redaction checks passing on real data.
- The keyboard test window layout (rendered), the PDF structure (parsed back), the PNG dimensions.

Not verified on the development machine because it has no battery, built-in display, touch digitizer or fingerprint reader: those code paths are covered by unit tests with fixtures.

## Manual verification log

| Date | Device | Windows | Result |
|---|---|---|---|
| 2026-11-25 | Custom-built desktop: Gigabyte B550M K, Ryzen 5 5600, RX 7700 XT, 2× NVMe | 11 Pro 25H2 | Automated integration tests and `--capture` run passed; see "Performance measurements" |
| 2026-11-26 | Laptop hardware (models to be recorded) | — | Owner ran `docs/MANUAL_TEST_CHECKLIST.md` and reported every item passing |

Add a row for every device tested, with the model name from the Overview page and the Windows version from Settings → System → About.
