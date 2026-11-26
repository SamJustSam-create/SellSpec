# Roadmap

The MVP stays free and fully functional offline. Everything below is optional and must not change that.

## Release plan

| Version | Focus | Status |
|---|---|---|
| 0.1 alpha | Hardware detection engine, UI, listing generator, reports, hardware tests | **this build** – all implemented; real-hardware verification on one desktop |
| 0.2 alpha | Laptop verification round (Lenovo, HP, Dell, ASUS, Surface), detection fixes from field reports | next |
| 0.3 alpha | Battery and storage diagnostics polish: elevated-rescan flow, NVMe SMART via storage driver, panel database for exact sizes | |
| 0.4 alpha | Listing generator refinements from real seller feedback; more marketplaces; persistent questionnaire/test results | |
| 0.5 alpha | Report design iteration, QR/share options, report language variants | |
| 0.6 alpha | In-app webcam preview (if a lightweight capture path is found), trackpad/touch test | |
| 0.7 beta | Compatibility sweep: 10–20 devices, Windows 10, high DPI, VMs, ARM64 | |
| 0.8 beta | Performance and security review, accessibility audit with Narrator | |
| 0.9 RC | Signed artifacts, installer option (MSIX or Inno Setup), documentation freeze | |
| 1.0 | Public release | |

## Candidate professional features (not in the MVP)

Designed for but not implemented. Each slots in without touching Core's public model:

- Branded business reports (logo, business details, custom disclaimer) – a `ReportTheme` on `ReportBuilder`.
- Bulk scanning and inventory – snapshots are serialisable; an inventory store and a device list page would sit beside the current single-device session.
- CSV export – a `CsvReportWriter` over `DeviceReport`.
- Custom listing templates – `MarketplaceTemplate` is already data; expose it through settings or a template file.
- Manufacturer specification database and verified upgradeability – a `IManufacturerSpecSource` whose results carry `Provenance.ManufacturerVerified`; offline bundle first, online opt-in later.
- Market-based valuations – only with real market data and clear sourcing; never fabricated.
- Marketplace integrations – posting APIs are explicit opt-in network features.
- Cloud sync, multi-device management, business APIs – opt-in, account-based, separate product tier.
- Additional operating systems – new provider packages implementing the same `IHardwareProvider<T>` interfaces, plus a UI per platform.
- ~~Commercial licensing~~ – dropped in November 2026. SellSpec is MIT-licensed and its releases are to be signed under SignPath Foundation's open-source terms, which exclude commercial dual-licensing; any paid offering would have to be a separate product outside the signed releases.

## Explicitly out of scope for now

Automatic price estimates, direct posting to marketplaces, online accounts, subscriptions, comprehensive spec databases, mobile apps, AI-generated listings, cloud storage.
