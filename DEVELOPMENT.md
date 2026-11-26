# Development

## Prerequisites

- Windows 10/11 64-bit.
- .NET SDK 10.0 (LTS). Either install it machine-wide, or user-locally with Microsoft's script (no administrator rights needed):

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
  .\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath
  . .\build\dotnet-env.ps1     # puts the user-local SDK on PATH for this shell
  ```

  `global.json` pins the 10.0 feature band with `latestFeature` roll-forward.
- Any editor. Visual Studio 2022 17.14+ or VS Code with the C# Dev Kit both open `SellSpec.slnx`.

No other tooling is required. NuGet restores three packages: `System.Management`, `CommunityToolkit.Mvvm` and the xUnit test stack.

## Build, test, run

```powershell
dotnet build SellSpec.slnx                      # Debug build of everything
dotnet test SellSpec.slnx                       # all tests (Core unit/scenario + Windows integration)
dotnet test tests\SellSpec.Core.Tests           # fast, hardware-independent tests only
dotnet run --project src\SellSpec.App           # start SellSpec (scans this computer)
dotnet run --project src\SellSpec.App -- --demo # start with the labelled demo laptop
```

The Windows integration tests run the real providers on the machine executing them. They assert structure (nothing throws, timeouts are honoured, redaction holds), not specific hardware values, so they pass on any Windows machine but are skipped by nothing: run them on each test device.

### Developer capture mode

```powershell
.\src\SellSpec.App\bin\Debug\net10.0-windows\win-x64\SellSpec.exe --capture C:\temp\sellspec-capture
```

scans, renders every page in light and dark themes plus the keyboard test window to PNG, writes `report.pdf/png/txt/json` and a listing for each marketplace, then exits. This is how the screenshots in `docs/screenshots` were produced and is the quickest way to review a UI change without clicking through the app.

## Publishing

```powershell
.\build\publish.ps1                 # runs tests, then publishes all variants into artifacts\
.\build\publish.ps1 -SkipTests      # faster iteration
.\build\publish.ps1 -SkipSelfContained
```

Variants:

| Variant | Command used | Requirement |
|---|---|---|
| Portable folder + zip | `dotnet publish --self-contained false` | .NET Desktop Runtime 10 on the target |
| Single-file (framework-dependent) | `-p:PublishSingleFile=true` | .NET Desktop Runtime 10 on the target |
| Single-file self-contained | `--self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true` | none |

WPF cannot be IL-trimmed, so the self-contained build carries the whole desktop runtime. Framework-dependent builds are the way to meet the "under 20 MB" download target; the self-contained build is the "just works" option for users without the runtime. Measured sizes are recorded in [TESTING.md](TESTING.md#performance-measurements).

`build\checksums.ps1` writes `SHA256SUMS.txt` (sha256sum format) for everything in `artifacts\`.

`build\make-icon.ps1` regenerates `src\SellSpec.App\Assets\SellSpec.ico` and `SellSpec.png` with System.Drawing; no external tool is needed.

## Versioning

`Directory.Build.props` holds `VersionPrefix` (e.g. `0.1.0`) and `VersionSuffix` (`alpha`, `beta`, `rc1`, or empty for a release). The value flows into the assembly informational version, the About page, reports and artifact names. Update `CHANGELOG.md` with every version bump.

## Continuous integration

Two GitHub Actions workflows live in `.github/workflows/`:

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | every push and pull request to `main`, or manually | restores, builds in Release and runs the whole test suite on `windows-latest`; uploads the `.trx` results as an artifact. The real-hardware integration tests run against the GitHub runner VM |
| `release.yml` | a pushed tag matching `v*` | two jobs. A **read-only build job** requires the tagged commit to be on `main`, checks that the tag has the exact (case-sensitive) form `v<major>.<minor>.<patch>[-<suffix>]` and equals the version in `Directory.Build.props`, builds and tests, runs `build\publish.ps1 -SkipTests` and uploads the artifacts. A separate **release job**, the only one with write access, downloads those artifacts, creates a *draft* release with the zip, both executables and `SHA256SUMS.txt`, and publishes it only once every asset is attached. Tags containing a hyphen (`v0.2.0-alpha`) become pre-releases; runs for the same tag are serialised |

Both workflows pin every action to a full commit SHA (the version is in a trailing comment) and `.github/dependabot.yml` proposes monthly updates for actions and for the NuGet packages under `src/` and `tests/` (grouped into one pull request). The tag name is passed to scripts through an environment variable, never interpolated into script source.

### Cutting a release

1. Set `VersionPrefix` and `VersionSuffix` in `Directory.Build.props` (empty suffix for a stable release).
2. Add the version's section to `CHANGELOG.md` and commit to `main` (through a pull request so `ci.yml` runs).
3. Tag and push:

   ```powershell
   git tag -a v0.2.0-alpha -m "SellSpec 0.2.0-alpha"
   git push origin v0.2.0-alpha
   ```

4. Watch the *Release* workflow; when it finishes, review the generated release page and edit the notes if needed.

If the tag and the version disagree, the tag is malformed, or the tagged commit is not on `main`, the workflow stops before building, so a mislabeled or unreviewed release cannot be published. The signing step is a commented placeholder in `release.yml` until a certificate exists (see below).

## Code signing

The artifacts are **not signed**. To sign future releases:

1. Obtain a code-signing certificate (an OV certificate works; an EV certificate gives immediate SmartScreen reputation). Store it in a hardware token or Azure Key Vault, never in the repository.
2. Sign each executable after publishing, with timestamping so signatures outlive the certificate:

   ```powershell
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a artifacts\SellSpec-*.exe
   ```

   (`signtool` ships with the Windows SDK. For Key Vault use `AzureSignTool`.)
3. Also sign `SellSpec.exe` inside the portable folder before zipping (run `signtool` on `artifacts\SellSpec-<version>-win-x64-portable\SellSpec.exe` and re-zip), then regenerate `SHA256SUMS.txt`.
4. Verify with `signtool verify /pa /v <file>`.

Add the signing step to `build\publish.ps1` behind a parameter once a certificate exists; do not commit certificate files or passwords.

## Coding conventions

- `.editorconfig` is authoritative: file-scoped namespaces, 4-space C#, 2-space XAML/JSON.
- Nullable reference types are on everywhere. Warnings are not treated as errors but the build is expected to be warning-free; fix rather than suppress.
- Core must not reference WPF, `System.Management`, the registry or anything Windows-specific. If you need a new platform value, add a `Spec<T>` to the model and implement it in `SellSpec.Windows`.
- Providers must: run as a standard user, never modify system state, select only the WMI columns they need, and return `Unavailable` values instead of throwing when something is missing.
- Anything that could be confused for a fact must carry the right `Provenance`.
- Every new export path must call `Redactor.EnsureClean`; every log line goes through `IScanLogger` (which sanitises).
- Keep dependencies at zero unless a package is clearly better than ~200 lines of in-house code *and* its licence is permissive.

## Repository hygiene

`.gitignore` excludes `bin/`, `obj/`, `artifacts/`, `TestResults/`, IDE folders and logs. Nothing in the repository is machine- or user-specific; the user-local SDK path is picked up at runtime by `build\dotnet-env.ps1`.
