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

The logo's master is `assets\logo.svg`. `build\make-icon.ps1` renders it with WPF into `src\SellSpec.App\Assets\SellSpec.ico` (16 to 256 px frames) and `SellSpec.png` (the sidebar image); no external tool is needed. The script understands only the SVG subset the logo uses (paths with solid or linear-gradient fills, group transforms, `data-min-size` to drop detail from small frames), so keep edits within that or extend the script.

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

If the tag and the version disagree, the tag is malformed, or the tagged commit is not on `main`, the workflow stops before building, so a mislabeled or unreviewed release cannot be published. The signing steps in `release.yml` run only when SignPath is configured for the repository (see below).

## Code signing

Release executables are to be signed by [SignPath Foundation](https://signpath.org) through its free programme for open-source projects, once the project is approved and the repository is configured as described below; until then releases are unsigned. [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) is the public policy that programme requires. There is no certificate in this repository or on any maintainer's machine. Signing happens inside the release workflow, and SignPath signs only builds it can trace to a run of that workflow on this repository (origin verification).

How the workflow signs when it is configured:

1. `build/publish.ps1` produces the artifacts as usual.
2. `build/signing.ps1 -Stage` copies the executables into `signing/unsigned` under fixed names (`SellSpec-singlefile.exe`, `SellSpec-selfcontained.exe`, `portable\SellSpec.exe`), and the workflow uploads that folder as a GitHub Actions artifact.
3. `signpath/github-action-submit-signing-request` submits the artifact. The signing policy requires manual approval, so the step waits (up to an hour) for an approver to approve the request in SignPath.
4. `build/signing.ps1 -Apply` verifies that every staged file came back with a valid, timestamped signature whose certificate common name is exactly "SignPath Foundation", copies the signed files over the unsigned ones, rebuilds the portable zip and regenerates `SHA256SUMS.txt`.
5. The release job checks that the tag still points at the commit that was built (before creating the draft and again before publishing), then publishes. The release notes state that the executables are signed and carry the SignPath attribution.

The steps run only when the repository variable `SIGNPATH_ORGANIZATION_ID` is set. Without it (so also before SignPath Foundation has approved the project) the release is published unsigned and its notes say so. With it set, any signing failure fails the run: a half-signed or accidentally unsigned release cannot be published.

Repository configuration (Settings → Secrets and variables → Actions):

| Kind | Name | Value |
|---|---|---|
| Variable | `SIGNPATH_ORGANIZATION_ID` | Organization id shown on the SignPath organisation page |
| Variable | `SIGNPATH_PROJECT_SLUG` | Project slug, e.g. `SellSpec` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | Signing policy slug, e.g. `release-signing` |
| Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath user with the Submitter role |

SignPath project settings the workflow relies on:

- Repository URL `https://github.com/SamJustSam-create/SellSpec`; trusted build system "GitHub.com"; origin verification on; GitHub-hosted runners required (an open-source programme rule). Install the SignPath GitHub App on the repository if SignPath asks for it.
- Allowed branch names: releases are built from tags, so allow `v*` as well as `main`. If a request is rejected on the branch check, read the branch SignPath recorded on the request and adjust the pattern.
- Artifact configuration (the uploaded artifact is a zip of the staging folder):

  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
    <zip-file>
      <pe-file path="SellSpec-singlefile.exe">
        <authenticode-sign/>
      </pe-file>
      <pe-file path="SellSpec-selfcontained.exe">
        <authenticode-sign/>
      </pe-file>
      <directory path="portable">
        <pe-file path="SellSpec.exe">
          <authenticode-sign/>
        </pe-file>
      </directory>
    </zip-file>
  </artifact-configuration>
  ```

- A signing policy for releases with the approval process enabled; the approvers are the people listed in CODE_SIGNING_POLICY.md. The product name in every executable is `SellSpec` (`Directory.Build.props`), which SignPath may require the configuration to enforce.
- `signing.ps1 -Apply` requires the certificate's common name to be exactly `SignPath Foundation`. If the certificate SignPath issues carries a different common name, the first signed release fails closed with a message naming the actual CN; pass `-ExpectedSigner '<that CN>'` in the workflow step rather than loosening the check.

Verifying a release on any Windows PC:

```powershell
Get-AuthenticodeSignature .\SellSpec-<version>-win-x64-singlefile.exe | Format-List Status, TimeStamperCertificate, SignerCertificate
```

`Status` must be `Valid` and the signer's subject must name SignPath Foundation. `signtool verify /pa /v <file>` (Windows SDK) gives the same answer. The zip and `SHA256SUMS.txt` cannot carry an Authenticode signature; the checksums cover them.

Dry run of the packaging steps without SignPath (this does not produce a signed build):

```powershell
./build/publish.ps1 -SkipTests -SkipSelfContained
./build/signing.ps1 -Stage -StagingDirectory signing\unsigned
Copy-Item -Recurse signing\unsigned signing\signed
./build/signing.ps1 -Apply -SignedDirectory signing\signed -SkipSignatureCheck
```

Never commit certificates or tokens, never describe a release as signed when the signing step did not run, and never pass `-SkipSignatureCheck` in the workflow.

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
