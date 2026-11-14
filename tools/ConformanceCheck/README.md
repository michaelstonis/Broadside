# ConformanceCheck

Validates the conformance map in `docs/conformance/`. The rules, the cell formats and how to run it are in [docs/conformance/README.md](../../docs/conformance/README.md); this file is only the tool's own notes.

```sh
dotnet run --project tools/ConformanceCheck -- --summary              # from anywhere inside the checkout
dotnet run --project tools/ConformanceCheck -- --root . --summary     # from the repository root
dotnet run --project tools/ConformanceCheck -- --strict               # also require tests and notes on partial rows
dotnet run --project tools/ConformanceCheck -- --root . --summary --citations   # what CI runs: also check clause citations
```

| Option | Effect |
|---|---|
| `--root <dir>` | Repository root. Default: walk up from the current directory to the folder containing `Broadside.slnx`. |
| `--summary` | Print a Markdown table of row counts per status, one line per spec file plus a total. |
| `--strict` | Also fail `partial` rows whose Tests or Notes cell is empty. |
| `--citations` | Also check clause citations against the packages' XML documentation, by the rules in `docs/conformance/citation-policy.json` (rules 10 to 13 of the README). Build the checked packages first. |
| `--configuration <name>` | The build configuration whose `artifacts/bin/<package>/<name>/<package>.xml` `--citations` reads. Default `debug`. |
| `--site-page <path>` | Also fail when the per-specification counts generated into that page, between its `conformance-counts` markers, differ from the maps. `./check-docs.sh` and CI pass `site/articles/conformance.md`. |
| `--write-site-page` | With `--site-page`, regenerate the counts in place instead of failing. |

Exit code 0 when every rule passes (and the site page is current), 1 when any fails (one line per violation, `docs/conformance/<file>.md:<line>: <message>`, or `src/<package>/PublicAPI.Unshipped.txt:<line>: ...` for an uncited public type), 2 for a bad command line, a root without `docs/conformance`, or (with `--citations`) a missing or invalid citation policy.

## Layout

- `ConformanceMap.cs`: the parser (`ConformanceMapParser`), the code index (`CodeIndex`, a regex scan of `tests/**/*.cs` and `src/**/*.cs`), the rules (`ConformanceChecker`) and the report. Public so `tests/ConformanceCheck.Tests` drives them directly.
- `CitationCheck.cs`: the citation policy (`CitationPolicy`), the public type list read from `PublicAPI.*.txt` (`PublicApiReader`), the XML documentation index (`DocumentationIndex`) and the citation rules (`CitationChecker`).
- `SiteCounts.cs`: the counts block of the site's conformance page (rendering, staleness, regeneration).
- `Program.cs`: argument parsing and console output only.

`tests/ConformanceCheck.Tests` runs the checker against fixture repositories (one per rule; `Fixtures/citations` is copied and edited per citation rule) and against this checkout, citations included, so `dotnet test Broadside.Core.slnf` fails when the map or a citation is wrong.
