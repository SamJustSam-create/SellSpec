# Privacy

SellSpec is designed around local-first operation and data minimisation.

## What SellSpec reads

Hardware and system facts needed to describe a computer for sale: manufacturer and model, motherboard and firmware version, processor, memory modules, storage drives and their reported health, graphics adapters, displays, battery capacity figures, network/Bluetooth/camera/audio device *names*, Windows edition, build, activation state, TPM and Secure Boot state.

It also reads, for the owner's reference only, the system serial number, system UUID, motherboard and BIOS serials, drive serials and battery serials. These appear in the Specifications page only when *Show private identifiers* is ticked, and are never included in listings, reports, exports or logs.

## What SellSpec never collects

- Personal documents, browser history, cookies, saved passwords or form data
- Windows product keys (including the partial key WMI can expose)
- Account names, email addresses, the registered Windows user
- IP addresses, MAC addresses, network GUIDs, Wi-Fi network names or passwords
- Camera images or microphone audio (the microphone test computes a level in memory and discards the samples; the webcam test opens the Windows Camera app instead of accessing the camera)
- Location, usage statistics or crash dumps

## Where data goes

Nowhere. SellSpec has no network code. There is no telemetry, no analytics, no update check and no account.

Files are written only when you ask: listings and reports go to the location you choose in the save dialog; settings and an optional diagnostic log go to `%LOCALAPPDATA%\SellSpec` (or the `data` folder next to the executable in portable mode).

## The diagnostic log

Optional (Settings → *Keep a local diagnostic log*), kept for seven days, and sanitised: serial numbers captured during the scan, MAC/IP addresses, GUIDs and user-profile paths are replaced with placeholders before a line is written. It records which components were scanned, timing, and the type of any error, so that a detection problem on a particular model can be diagnosed from the log alone.

## Exports

Every clipboard copy and file export passes through a redaction check that refuses the operation if a private identifier, MAC/IP address, product-key pattern or user path is present. The JSON export mirrors the public report exactly.

Reports state that specifications were collected from the device and that condition details and some test results were supplied by the seller. They are not described as certified or tamper-proof.

## The pre-sale checklist

Informational only. SellSpec opens the relevant Windows Settings pages when you press a button; it never deletes files, signs you out, changes encryption, touches accounts or resets Windows.

## Future features

Any feature that would send data elsewhere (cloud sync, marketplace posting, manufacturer lookups) will be opt-in, clearly described at the point of use, and off by default. None exists in this version.
