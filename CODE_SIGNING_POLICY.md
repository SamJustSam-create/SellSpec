# Code signing policy

SellSpec's release executables are not code-signed yet. This page says why, what is verified instead, and how signing will work when it becomes available. It is kept in the shape [SignPath Foundation](https://signpath.org)'s open-source programme asks of a project's policy page, so that it can serve as one when the project reapplies.

**Status.** Not signed. SellSpec applied to SignPath Foundation's free programme for open-source projects in November 2026 and was declined for now: the programme requires established public adoption (stars, forks, contributors, independent articles or discussions), which a project this new cannot show. The project will reapply once it has that record. A certificate of the project's own is not available either, because certificate authorities validate an organisation or an adult individual and SellSpec has neither to offer. Releases up to and including 0.1.2-alpha are unsigned; every release's notes state whether its executables are signed, and this page will name the first signed release when it ships.

## What is verified today: build provenance

From the release after 0.1.2-alpha onwards, every asset of every release (the two executables, the portable zip and `SHA256SUMS.txt`) carries a GitHub [artifact attestation](https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds): a Sigstore-signed statement, recorded by GitHub, that a file with this exact SHA-256 digest was produced by this repository's release workflow, from the tagged commit, on a GitHub-hosted runner. The release workflow verifies the attestation of every asset before it publishes the release, so an asset GitHub cannot vouch for is never released.

An attestation does not replace a code signature. Windows does not read it, so SmartScreen may still warn on first launch. It answers a different question, "did this file really come out of the SellSpec repository, unmodified?", and it answers it for the zip and the checksum file too, which an Authenticode signature cannot cover.

Verify a download with a current [GitHub CLI](https://cli.github.com) (`gh`):

```powershell
gh attestation verify .\SellSpec-<version>-win-x64-singlefile.exe --repo SamJustSam-create/SellSpec
```

The command succeeds, naming the workflow and the commit that built the file, only if GitHub holds a matching attestation for that exact file; a modified or re-packed file fails. The attestations of every release are listed at `https://github.com/SamJustSam-create/SellSpec/attestations`. Compare every download against `SHA256SUMS.txt` as well.

## What is signed once signing is active

| File | Signed once signing is active |
|---|---|
| `SellSpec-<version>-win-x64-singlefile.exe` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec-<version>-win-x64-selfcontained.exe` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec.exe` inside `SellSpec-<version>-win-x64-portable.zip` | Yes (Authenticode, SHA-256, timestamped) |
| `SellSpec-<version>-win-x64-portable.zip`, `SHA256SUMS.txt` | No. A zip cannot carry an Authenticode signature; `SHA256SUMS.txt` and the build provenance attestations cover every asset. |

Under the SignPath Foundation programme the certificate would be issued to SignPath Foundation, so Windows would show **SignPath Foundation** as the publisher. A signature confirms that the file is an unmodified, automated build of the source code in this repository, including its build scripts and workflow, at the tagged commit.

## How a release is built, attested and signed

Steps 3 to 5 run only once signing is active (project approved, repository configured). Until then a release goes from step 2 straight to step 6 and its notes say the executables are unsigned.

1. A maintainer tags a reviewed commit on `main` with `v<major>.<minor>.<patch>[-<suffix>]`, equal to the version in `Directory.Build.props`.
2. The [release workflow](.github/workflows/release.yml) runs on GitHub-hosted runners only. It refuses tags that are malformed, do not match the project version, or are not on `main`; then it builds from source, runs the full test suite and publishes the artifacts with `build/publish.ps1`.
3. `build/signing.ps1 -Stage` copies the executables into a staging folder, which is uploaded as a workflow artifact and submitted to SignPath with origin verification: SignPath checks the repository, the commit and the workflow run before it will sign anything.
4. An approver reviews the signing request in SignPath, checking the tag, the commit and the build, and approves it by hand. Nothing is signed automatically.
5. `build/signing.ps1 -Apply` verifies that every file came back with a valid, timestamped signature from SignPath Foundation, replaces the unsigned executables, rebuilds the portable zip and regenerates `SHA256SUMS.txt`.
6. The build job attests every final asset (the executables and the zip, signed or not, and `SHA256SUMS.txt`) with GitHub's `actions/attest`, which records the attestation with GitHub.
7. A separate, minimal job verifies the attestation of every downloaded asset against this repository, this workflow file and the tag, checks that the tag still points at the commit that was built, attaches the assets to a draft GitHub release, checks the tag again and only then publishes the release.

Signing and attestation both fail closed. If any step fails, no release is published. The signing steps run only when SignPath is configured for the repository; a release built without them is published unsigned and its notes say so.

## Team and roles

| Role | Members | Responsibility |
|---|---|---|
| Committers (authors) | [@SamJustSam-create](https://github.com/SamJustSam-create) | May change source code, build scripts and workflows in the repository. |
| Reviewers | [@SamJustSam-create](https://github.com/SamJustSam-create) | Every change from a non-committer (pull request) is reviewed by a committer before it is merged. Automated review (Sourcery) assists but never replaces this review. |
| Approvers | [@SamJustSam-create](https://github.com/SamJustSam-create) (repository owner) | Decides whether a release may be signed and approves each signing request in the signing service. |

All team members use multi-factor authentication for GitHub and for any signing service the project uses. `main` is protected: changes land through pull requests, and releases are built only from commits on `main`.

## Privacy

SellSpec does not transfer information to other networked systems unless the user asks it to. It contains no network code, no telemetry, no analytics, no update check and no account. The only outbound actions are ones the user chooses explicitly: opening the GitHub issue form from *Report a detection problem*, and launching Windows Settings or the Windows Camera app from the hardware tests. Exports are written only where the user chooses in the save dialog.

The full privacy policy is [PRIVACY.md](PRIVACY.md). Third-party components (the .NET runtime, `System.Management`, `CommunityToolkit.Mvvm`) send no data; they are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Installation is a copy or an unzip, and uninstalling is deleting the folder (and, optionally, `%LOCALAPPDATA%\SellSpec`).

## Verifying a signature

Once releases are signed, on any Windows PC:

```powershell
Get-AuthenticodeSignature .\SellSpec-<version>-win-x64-singlefile.exe | Format-List Status, TimeStamperCertificate, SignerCertificate
```

`Status` must be `Valid` and the signer's subject must name the signer given in that release's notes. The same information is under *Properties → Digital Signatures* in Explorer, and `signtool verify /pa /v <file>` (Windows SDK) gives the same answer. Until then, use the attestation check above and `SHA256SUMS.txt`.

## Reporting a problem

If a SellSpec release behaves unexpectedly, or you believe a release asset is not what this policy describes, report it privately as described in [SECURITY.md](SECURITY.md). Once releases are signed, suspected misuse of the certificate may also be reported to its issuer, named in the release notes.
