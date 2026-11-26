# Code signing policy

SellSpec's release executables are to be code-signed through [SignPath Foundation](https://signpath.org)'s free programme for open-source projects. Signing happens only after SignPath Foundation has approved the project and the repository has been configured for it; until then every release is unsigned, and this page describes how signing will work once it is active.

> Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

**Status.** Not yet signed. SellSpec is applying to SignPath Foundation; every release's notes state whether its executables are signed, and this page will name the first signed release when it ships. Releases up to and including 0.1.2-alpha are unsigned.

## What is signed

| File | Signed once signing is active |
|---|---|
| `SellSpec-<version>-win-x64-singlefile.exe` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec-<version>-win-x64-selfcontained.exe` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec.exe` inside `SellSpec-<version>-win-x64-portable.zip` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec-<version>-win-x64-portable.zip`, `SHA256SUMS.txt` | No. A zip cannot carry an Authenticode signature; `SHA256SUMS.txt` covers every asset. |

The certificate is issued to SignPath Foundation, so Windows will show **SignPath Foundation** as the publisher. A signature confirms that the file is an unmodified, automated build of the source code in this repository, including its build scripts and workflow, at the tagged commit.

## How a release is built and signed

Steps 3 to 5 run only once signing is active (project approved, repository configured). Until then a release goes straight from step 2 to step 6 and its notes say the executables are unsigned.

1. A maintainer tags a reviewed commit on `main` with `v<major>.<minor>.<patch>[-<suffix>]`, equal to the version in `Directory.Build.props`.
2. The [release workflow](.github/workflows/release.yml) runs on GitHub-hosted runners only. It refuses tags that are malformed, do not match the project version, or are not on `main`; then it builds from source, runs the full test suite and publishes the artifacts with `build/publish.ps1`.
3. `build/signing.ps1 -Stage` copies the executables into a staging folder, which is uploaded as a workflow artifact and submitted to SignPath with origin verification: SignPath checks the repository, the commit and the workflow run before it will sign anything.
4. An approver reviews the signing request in SignPath, checking the tag, the commit and the build, and approves it by hand. Nothing is signed automatically.
5. `build/signing.ps1 -Apply` verifies that every file came back with a valid, timestamped signature from SignPath Foundation, replaces the unsigned executables, rebuilds the portable zip and regenerates `SHA256SUMS.txt`.
6. A separate, minimal job checks that the tag still points at the commit that was built, attaches the assets to a draft GitHub release, checks the tag again and only then publishes the release.

Signing fails closed. If any step fails, no release is published. The signing steps run only when SignPath is configured for the repository; a release built without them is published unsigned and its notes say so.

## Team and roles

| Role | Members | Responsibility |
|---|---|---|
| Committers (authors) | [@SamJustSam-create](https://github.com/SamJustSam-create) | May change source code, build scripts and workflows in the repository. |
| Reviewers | [@SamJustSam-create](https://github.com/SamJustSam-create) | Every change from a non-committer (pull request) is reviewed by a committer before it is merged. Automated review (Sourcery) assists but never replaces this review. |
| Approvers | [@SamJustSam-create](https://github.com/SamJustSam-create) (repository owner) | Decides whether a release may be signed and approves each signing request in SignPath. |

All team members use multi-factor authentication for GitHub and for SignPath. `main` is protected: changes land through pull requests, and releases are built only from commits on `main`.

## Privacy

SellSpec does not transfer information to other networked systems unless the user asks it to. It contains no network code, no telemetry, no analytics, no update check and no account. The only outbound actions are ones the user chooses explicitly: opening the GitHub issue form from *Report a detection problem*, and launching Windows Settings or the Windows Camera app from the hardware tests. Exports are written only where the user chooses in the save dialog.

The full privacy policy is [PRIVACY.md](PRIVACY.md). Third-party components (the .NET runtime, `System.Management`, `CommunityToolkit.Mvvm`) send no data; they are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Installation is a copy or an unzip, and uninstalling is deleting the folder (and, optionally, `%LOCALAPPDATA%\SellSpec`).

## Verifying a signature

On any Windows PC:

```powershell
Get-AuthenticodeSignature .\SellSpec-<version>-win-x64-singlefile.exe | Format-List Status, TimeStamperCertificate, SignerCertificate
```

`Status` must be `Valid` and the signer's subject must name SignPath Foundation. The same information is under *Properties → Digital Signatures* in Explorer, and `signtool verify /pa /v <file>` (Windows SDK) gives the same answer. Compare every download against `SHA256SUMS.txt` as well.

## Reporting a problem

If a signed SellSpec release behaves unexpectedly, or you believe a signed file is not what this policy describes, report it privately as described in [SECURITY.md](SECURITY.md). Suspected misuse of the certificate may also be reported to SignPath Foundation at support@signpath.io.
