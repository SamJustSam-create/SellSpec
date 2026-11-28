# Changelog

All notable changes to SellSpec are recorded here. The format follows Keep a Changelog; versions follow SemVer.

## [Unreleased]

### Added
- Build provenance attestations: the release workflow attests every release asset (executables, portable zip and `SHA256SUMS.txt`) with GitHub's `actions/attest` and verifies each asset's attestation against the repository, the workflow file and the tag before publishing. The release notes and CODE_SIGNING_POLICY.md carry the verification command, pinned to the release workflow and tag, and explain the check; releases up to 0.1.2-alpha have no attestations.

### Changed
- Code signing status: SignPath Foundation declined SellSpec's application in November 2026 pending public adoption. The policy, README, security, limitations and development documents now say that releases are unsigned, that the signing steps stay dormant, and that the project will reapply; the SignPath attribution line was removed.

## [0.1.2-alpha] – 2026-11-27

First release under the MIT licence, with the new logo and the field-report and remembered-answers features. Executables are still unsigned; see CODE_SIGNING_POLICY.md.

### Added
- Remembered answers: questionnaire answers and hardware test results are saved per device model in `answers.json` in the data folder a moment after they change, and offered back (Restore / Discard) when the same model is scanned again; an administrator rescan restores them automatically. The file is keyed by public model identity only, is redaction-checked on every write, and is never written for the demonstration device or an unidentifiable device. Settings gains *Remember questionnaire answers and hardware test results* (turning it off deletes the file) and *Clear remembered answers*.
- Settings → *Report a detection problem*: saves a diagnostics bundle (public JSON report, raw detection dump with firmware strings, codes, provenance and notes, scan issues, today's sanitised log, optional description) as a zip for attaching to a GitHub report. Every entry is redaction-checked; identifiers are never included. A button opens the GitHub issue form, the only web address SellSpec ever opens.
- "Something wrong? Report a detection problem" link on the Overview page.
- GitHub issue forms for detection reports and bug reports, with links to the detection methods and known limitations.
- MIT licence (`LICENSE`). Contributions are accepted under the same licence; the About page and assembly metadata name it.
- Code signing policy (`CODE_SIGNING_POLICY.md`) and SignPath Foundation signing in the release workflow: `build/signing.ps1` stages the executables under fixed names, the SignPath action submits them with origin verification and waits for manual approval, and the script verifies every returned file (valid, timestamped, signed by SignPath Foundation) before replacing the executables and rebuilding the zip and checksums. The steps run only when the SignPath repository variables are set and fail the release on any signing error.

### Changed
- New logo: a tilted price tag carrying a short spec list, drawn as a vector master (`assets/logo.svg`) that `build/make-icon.ps1` now renders with WPF into the application icon and the sidebar image.
- Release notes state whether the executables are signed and link the code signing policy.
- The release job checks that the tag still points at the commit that was built, before creating and again before publishing the release.
- The portable zip now ships `LICENSE`, `CODE_SIGNING_POLICY.md` and `PRIVACY.md` beside the README.
- The commercial-licensing roadmap item was dropped: SellSpec is open source for good.

### Fixed
- A rescan of the same device no longer clears hardware test results and answers; they are cleared only when a different device is scanned. The test cards now update when results change outside the card (rescan, restore).
- The export redaction check no longer flags the sanitiser's own `C:\Users\[user]` placeholder as a leak.
- Redaction now also catches IPv6 addresses, short private identifiers as whole tokens, and the Windows account and computer names in diagnostics bundles.
- Bluetooth device names in the Specifications page and diagnostics bundles are limited to hardware radios; paired devices with personal names are excluded.

## [0.1.1-alpha] – 2026-11-26

Infrastructure release; no functional change to the application.

### Added
- GitHub Actions CI: Release build and the full test suite on every push and pull request to `main`.
- Tag-driven release workflow: a read-only build job validates the tag (must be on `main`, exact `v<major>.<minor>.<patch>[-suffix]`, equal to the project version), builds, tests and publishes; a separate write-only job creates a draft release and publishes it once every asset is attached. Actions are pinned to commit SHAs; Dependabot tracks actions and NuGet packages.
- `.gitattributes` so PDFs, images and executables are never line-ending converted on checkout.
- Manual verification log in TESTING.md, including the 2026-11-26 laptop checklist pass.

### Fixed
- `SHA256SUMS.txt` is written with LF line endings so `sha256sum -c` works on Linux, macOS and Git Bash; the checksum script's default artifacts path works when run on its own.
- Version parsing in the publish script treats an empty `VersionSuffix` as no suffix, so stable releases validate.
- An order-dependent failure in the log sanitiser unit test.

## [0.1.0-alpha] – 2026-11-25

First functional MVP.

### Added
- Hardware detection engine (`SellSpec.Core` + `SellSpec.Windows`): device identity with custom-build and virtual-machine detection, processor, memory, storage (internal/external/removable/virtual classification, reported health, SMART and reliability counters), graphics (integrated/dedicated, registry-based VRAM), displays (EDID, native mode, size estimate, touch), battery (design/full-charge capacity, health %, cycles, status), connectivity (Wi-Fi generation via WLAN API, Bluetooth, Ethernet, webcam, audio, fingerprint), Windows (edition, version, activation and licence channel, TPM via TBS, Secure Boot, Windows 11 readiness).
- Provenance labelling for every value; concurrent scanner with per-component isolation, timeouts, cancellation and progress.
- WPF application with Windows 11-style light and dark themes, nine pages, compact navigation on narrow windows, keyboard navigation and automation names.
- Device health dashboard with text-labelled status indicators and an optional administrator rescan.
- Guided hardware tests: keyboard, full-screen display colours, speaker tone, microphone level meter, Windows Camera launch, connectivity confirmations.
- Physical condition questionnaire; every question skippable.
- Marketplace listing generator for Facebook Marketplace, eBay, Gumtree and general use with configurable title limits, editing, copy and text export.
- Report export to PNG, PDF (in-house writer), plain text and JSON with preview and disclaimer.
- Pre-sale privacy checklist with links to Windows Settings pages.
- Redaction guard on all exports and sanitised diagnostic logging.
- Portable mode, settings persistence, `--demo` and `--capture` developer switches.
- 287 automated tests including ten device scenarios and real-hardware integration tests.
- Publish scripts for portable, single-file and self-contained builds with SHA-256 checksums; icon generator.
- Documentation set.

### Known gaps
See KNOWN_LIMITATIONS.md. Not yet verified on a physical laptop; binaries unsigned.
