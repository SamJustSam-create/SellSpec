# Manual Windows test checklist

Run on each physical test device. Record the device model, Windows version and SellSpec version. Tick each row and note deviations.

## A. Launch and scan

| # | Step | Expected |
|---|---|---|
| A1 | Launch `SellSpec.exe` as a standard user | Window appears within 3 s; scan strip shows progress; no UAC prompt |
| A2 | Wait for the scan | "Scan complete" within 10 s; overview shows manufacturer/model, type, OS, six cards |
| A3 | Compare manufacturer, model and model number with the sticker/firmware | Match; for Lenovo the marketing name (e.g. IdeaPad Slim 5 14IAH8) with "Model 82XF" beneath |
| A4 | Compare processor, RAM size/type, storage sizes and types, GPU(s), display size and resolution with the manufacturer's spec sheet | Match; estimated values labelled; anything wrong recorded as a bug with the raw strings from *Show technical details* |
| A5 | Press *Rescan* | Scan repeats; questionnaire answers remain; hardware test results are cleared |
| A6 | Press *Cancel* during a scan | Scan stops within a few seconds; overview shows what was collected; "Scan cancelled" |
| A7 | Launch with `--demo` | Yellow demo banner; fictional "SellSpec Demo Demo Laptop 14" device |

## B. Laptop-specific

| # | Step | Expected |
|---|---|---|
| B1 | Battery card and Device Health | Reported health %, cycle count (or "not tracked"), charge and charging state; plausible against `powercfg /batteryreport` |
| B2 | Unplug/plug the charger, rescan | Charging status changes |
| B3 | Built-in display | "Built-in display", size estimate within 0.3", native resolution, refresh rate, Touch yes/no correct |
| B4 | Connect an external monitor, rescan | Both displays listed; listing uses the built-in one |
| B5 | Form factor | Laptop / 2-in-1 / Tablet matches the device |
| B6 | Soldered-memory laptop (e.g. XPS 13, Surface) | Memory type LPDDRx; Upgradeable "Not verified" with the soldered note; listing never says "upgradeable" |

## C. Desktop-specific

| # | Step | Expected |
|---|---|---|
| C1 | Custom build | "Custom-built desktop PC" with motherboard named; title starts "Custom gaming PC" when a dedicated GPU exists |
| C2 | Branded desktop (Dell OptiPlex, HP ProDesk) | Brand and model shown, not custom-built |
| C3 | Battery | "Not applicable", grey, no error |
| C4 | External monitor | Shown on cards as "not part of this PC"; omitted from listing and report |

## D. Device Health

| # | Step | Expected |
|---|---|---|
| D1 | Storage power-on hours as a standard user | Either a value or "Not available" with the administrator note and the *Rescan as administrator* button |
| D2 | Press *Rescan as administrator*, accept UAC | New elevated instance starts and scans; power-on hours and wear appear for supported drives |
| D3 | Decline UAC | Original window reports the rescan was not started; nothing else changes |
| D4 | Windows activation | Matches Settings → System → Activation |
| D5 | Windows 10 machine | Windows 11 readiness checklist shown with TPM/Secure Boot/RAM/storage/CPU rows; processor row labelled "not verified" |

## E. Hardware tests

| # | Step | Expected |
|---|---|---|
| E1 | Keyboard test: press every key | Each key lights; count increments; Tab/Enter/Space/arrows do not move focus; Done closes; evidence "n of 84 keys registered" |
| E2 | Display test | Full-screen red, green, blue, white, black; click/Space advances, Left goes back, Esc exits; hint fades |
| E3 | Speakers | Left tone audible on left only, right on right only, both on both; no clicks at start/end |
| E4 | Microphone | Meter moves while speaking; stops on Stop; evidence shows peak %; nothing written to disk (check the data folder) |
| E5 | Microphone with privacy setting off | Readable error, no crash |
| E6 | Webcam | Windows Camera app opens; SellSpec itself does not request camera access |
| E7 | Connectivity | Ticked boxes appear as evidence; result chips update the Device Health page |
| E8 | Mark Failed with notes | Appears under "Known issues" in the listing and report as seller-reported |

## F. Listing

| # | Step | Expected |
|---|---|---|
| F1 | Leave the questionnaire empty | Listing has no condition sentence, no "Included", no "Known issues"; warning shown |
| F2 | Answer every question including a defect and price | All answers appear; defects under "Known issues"; price only on General/Gumtree |
| F3 | Switch marketplace | eBay title ≤ 80, Facebook ≤ 99, Gumtree ≤ 64 characters; counter turns red if you type past the limit |
| F4 | Edit the title and description, then change an answer | Status says answers changed; Regenerate replaces edits |
| F5 | Copy title / description / both; paste into Notepad and into the marketplace form | Exact text, bullets preserved, no serial numbers |
| F6 | Save as text to Documents and to a USB drive | File written; status shows path |

## G. Report

| # | Step | Expected |
|---|---|---|
| G1 | Preview | Matches the current questionnaire and tests; updates when they change |
| G2 | Save PNG, PDF, text, JSON | Files open in Photos/Edge/Notepad; PDF has page numbers and the disclaimer on each page; PNG is crisp at 2×; JSON validates |
| G3 | Search every export for the serial numbers shown under *Show private identifiers* | None found |
| G4 | Save to a read-only location | Friendly error, no crash |

## H. Settings, themes, accessibility

| # | Step | Expected |
|---|---|---|
| H1 | Theme Light/Dark/Follow Windows; switch the Windows app theme while running | Colours update live; all text readable in both themes |
| H2 | Change title limits and currency symbol | Listing regenerates with the new limit/symbol; values persist after restart |
| H3 | Disable diagnostic logging | No new log lines; Open log folder still works |
| H4 | Portable mode (`portable.txt` beside exe) | `data\settings.json` and `data\logs` created beside the exe; nothing in `%LOCALAPPDATA%\SellSpec` |
| H5 | Keyboard-only navigation (Tab, arrows, Space, Enter) | Every page and control reachable; focus ring visible |
| H6 | Narrator | Navigation items, buttons and inputs are announced with their labels |
| H7 | 125 %, 150 %, 200 % scaling and a 1366×768 screen | Layout usable; navigation collapses to icons below 1000 px width; no clipped text |
| H8 | Windows 10 | Icons render (Segoe MDL2 Assets), fonts fall back to Segoe UI |

## I. Performance (record numbers in TESTING.md)

| # | Method |
|---|---|
| I1 | Startup: `Measure-Command { Start-Process SellSpec.exe -PassThru \| ForEach-Object { while ($_.MainWindowHandle -eq 0) { Start-Sleep -m 20; $_.Refresh() } } }` |
| I2 | Scan time: the "Scanned in x s" text on the overview or the `Scan finished` log line |
| I3 | Idle memory/CPU: Task Manager or `Get-Process SellSpec | Select WorkingSet64, CPU` sampled 10 s after the scan and again 30 s later (CPU delta should be ~0) |
| I4 | Network: Resource Monitor → Network while using every feature; expected zero bytes for SellSpec.exe |

## J. Virtual machine

| # | Step | Expected |
|---|---|---|
| J1 | Run inside Hyper-V/VMware/VirtualBox | Overview warning and health "Virtual machine" row; virtual disks/adapters excluded; listing warning |
