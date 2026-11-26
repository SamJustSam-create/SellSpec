# Contributing

Thanks for helping make SellSpec more accurate. Hardware detection is the hard part, so the most valuable contributions are **field reports from real devices** and **fixes to normalisation rules with tests**.

## Reporting a detection problem

1. In SellSpec go to Settings → *Report a detection problem*, describe what was wrong, and press **Save diagnostics bundle**. The zip holds the public JSON report, a raw detection dump (firmware strings, codes, provenance and notes for every component), the scan notes and today's sanitised log. It never contains serial numbers, addresses, user names or personal files; every file is checked before it is written.
2. Press **Open the report form on GitHub** (or go to [New issue → Detection report](https://github.com/SamJustSam-create/SellSpec/issues/new/choose)), fill in the device, the Windows version, what SellSpec showed and what the correct value is, with a link to the maker's spec sheet if you have one, and attach the bundle.
3. For crashes or other misbehaviour use the **Bug report** form instead.

## Development workflow

- Read [DEVELOPMENT.md](DEVELOPMENT.md) and [ARCHITECTURE.md](ARCHITECTURE.md).
- Branch from `main`; keep pull requests focused.
- Every change to normalisation, classification, listing or report logic needs a unit test in `tests/SellSpec.Core.Tests`. Add a new scenario fixture when a new device class is involved.
- Run `dotnet test SellSpec.slnx` and, for UI changes, `SellSpec.exe --capture <folder>` and look at the PNGs in both themes.
- Do not add dependencies without discussing it first; do not add network calls at all.
- Follow the provenance rules: nothing is a fact unless it was detected; estimates are labelled; seller input is attributed.

## Style

`.editorconfig` governs formatting. Australian/British spelling in user-facing text ("colour", "licence"), concise sentences, no jargon on the main pages.

## Licence of contributions

SellSpec is released under the [MIT Licence](LICENSE). By opening a pull request you agree that your contribution is licensed under the same terms. Only submit code you are entitled to license this way, and do not add dependencies whose licences are not permissive (see DEVELOPMENT.md).

## Maintainers

Maintainers hold the roles listed in [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md). They use multi-factor authentication on GitHub and SignPath, review every pull request from a non-committer before merging it, and approve a release signing request only after checking the tag, the commit and the build.

## Security issues

See [SECURITY.md](SECURITY.md); report privately.
