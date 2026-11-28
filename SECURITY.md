# Security

## Threat model in one paragraph

SellSpec runs on the seller's own machine, reads hardware information and writes text and image files where the seller asks. The assets to protect are the seller's privacy (nothing sensitive in exports or logs) and the integrity of their system (SellSpec must not change anything). There is no server, no account and no network surface.

## Principles applied

- **Least privilege.** The manifest requests `asInvoker`. Nothing in the normal workflow needs elevation. The only elevated path is the optional *Rescan as administrator* button, which relaunches the same executable through the standard UAC prompt to read drive reliability counters; it performs the same read-only queries.
- **Read-only hardware access.** Every WMI/CIM query is a `SELECT`; no methods are invoked, no settings are written. Registry access is read-only (`OpenSubKey` without write access). Win32 calls are informational (`GetSystemPowerStatus`, `GetFirmwareType`, `GetSystemMetrics`, `Tbsi_GetDeviceInfo`, display enumeration, WLAN capability queries).
- **Data minimisation.** WMI queries name their columns; identifiers that are not needed (`MACAddress`, `RegisteredUser`, `PartialProductKey`, Wi-Fi profiles) are never selected. The few serials that are read are kept in `PrivateIdentifiers`, shown only behind an in-app toggle and excluded from every export.
- **Export guard.** `Redactor.EnsureClean` scans every clipboard copy and every written file for captured identifiers, MAC/IP patterns, product-key patterns and user-profile paths, and refuses the operation if anything matches. The same patterns are applied to every log line by `LogSanitiser`, which also strips GUIDs.
- **Safe file handling.** Export paths come from the Windows save dialog; the file name suggestion is sanitised of invalid characters; files are written to a temporary name and moved into place atomically; temporary files are cleaned up on failure. Settings and remembered answers are written the same way, and the remembered answers additionally pass the export redaction check before anything is written (see PRIVACY.md).
- **Controlled process execution.** `ExternalLauncher` opens only allow-listed URIs (`ms-settings:` pages and `microsoft.windows.camera:`), Explorer at a folder, and the elevated relaunch of SellSpec itself. No user-supplied string is ever passed to the shell.
- **No persistence.** No services, scheduled tasks, startup entries, drivers or registry writes. Uninstalling is deleting the folder (and optionally `%LOCALAPPDATA%\SellSpec`).
- **No network.** The code contains no HTTP client and makes no socket calls. Any future online feature must be explicitly opt-in (see ROADMAP.md).
- **Input validation.** Questionnaire text is trimmed and capped (2,000 characters per field); prices are parsed strictly; title-limit settings are clamped to 20–200.
- **Crash handling.** Unhandled UI exceptions are logged (sanitised) and reported with a non-technical dialog; the application keeps running.

## Dependencies

Three runtime dependencies, all from Microsoft / the .NET Foundation under the MIT licence: .NET 10 (WPF), `System.Management`, `CommunityToolkit.Mvvm`. See THIRD_PARTY_NOTICES.md. Keep them updated with `dotnet list package --outdated`; review advisories with `dotnet list package --vulnerable`.

## Hardware tests

- Microphone: audio is captured into fixed in-memory buffers through `waveIn`, reduced to a level number immediately, and the buffers are zeroed before being freed. Nothing is written to disk.
- Webcam: SellSpec never opens the camera itself; it launches the Windows Camera app for the user to look at.
- Speaker: an original sine tone is generated in memory.
- Display and keyboard tests are plain WPF windows; the keyboard test only records which keys were pressed, never text.

## Release integrity

Releases are built only by the tag-driven GitHub Actions workflow, from a commit on `main`, on GitHub-hosted runners, with every action pinned to a commit SHA. `SHA256SUMS.txt` lists the checksum of every asset, and from the release after 0.1.2-alpha every asset also carries a GitHub build provenance attestation, which the workflow verifies before publishing (the `gh attestation verify` command in [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) repeats the check). The executables are not code-signed: SignPath Foundation declined the project's application in November 2026 pending public adoption, and no other signing route is open to the maintainers. The fail-closed signing steps described in [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) stay dormant until one is. Each release's notes state whether its executables are signed.

## Reporting a vulnerability

Open a private report with the maintainers (see CONTRIBUTING.md) rather than a public issue. Include the SellSpec version from the About page and the sanitised log from Settings → *Open log folder* if relevant. Please do not include serial numbers or personal data in reports.

## Not covered

SellSpec does not attempt to detect or resist a compromised operating system, tampered WMI data or a malicious user of the same account. Reports are plain files and are explicitly described as not tamper-proof.
