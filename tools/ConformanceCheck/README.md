# ConformanceCheck

Validates the conformance map in `docs/conformance/`. The rules, the cell formats and how to run it are in [docs/conformance/README.md](../../docs/conformance/README.md); this file is only the tool's own notes.

```sh
dotnet run --project tools/ConformanceCheck -- --summary              # from anywhere inside the checkout
dotnet run --project tools/ConformanceCheck -- --root . --summary     # what CI runs, from the repository root
dotnet run --project tools/ConformanceCheck -- --strict               # also require tests and notes on partial rows
```

| Option | Effect |
|---|---|
| `--root <dir>` | Repository root. Default: walk up from the current directory to the folder containing `Broadside.slnx`. |
| `--summary` | Print a Markdown table of row counts per status, one line per spec file plus a total. |
| `--strict` | Also fail `partial` rows whose Tests or Notes cell is empty. |

Exit code 0 when every rule passes, 1 when any fails (one line per violation, `docs/conformance/<file>.md:<line>: <message>`), 2 for a bad command line or a root without `docs/conformance`.

## Layout

- `ConformanceMap.cs`: the parser (`ConformanceMapParser`), the code index (`CodeIndex`, a regex scan of `tests/**/*.cs` and `src/**/*.cs`), the rules (`ConformanceChecker`) and the report. Public so `tests/ConformanceCheck.Tests` drives them directly.
- `Program.cs`: argument parsing and console output only.

`tests/ConformanceCheck.Tests` runs the checker against fixture repositories (one per rule) and against this checkout, so `dotnet test Broadside.Core.slnf` fails when the map is wrong.
