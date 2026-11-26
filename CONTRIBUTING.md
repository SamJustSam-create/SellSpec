# Contributing

Thanks for helping make SellSpec more accurate. Hardware detection is the hard part, so the most valuable contributions are **field reports from real devices** and **fixes to normalisation rules with tests**.

## Reporting a detection problem

1. Open SellSpec, go to Specifications, tick *Show technical details*.
2. Note the raw manufacturer/model/version strings, the raw processor and graphics names, chassis codes and anything that looks wrong.
3. Export the JSON report (it contains no identifiers) and attach it, together with the device's official spec sheet or a photo of its label (cover serial numbers).
4. Include the sanitised log from Settings → *Open log folder* if the scan reported a problem.

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

The project's distribution licence has not been finalised. Until it is, contributions are accepted on the understanding that they may be included in both free and commercial versions of SellSpec. Please state in the pull request that you agree.

## Security issues

See [SECURITY.md](SECURITY.md); report privately.
