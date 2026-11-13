# Conformance map

One file per specification. One row per clause. Status is one of `not started`, `partial`, `done`, `n/a` (clause has no implementable content or is out of scope by `CLAUDE.md`). A row marked `done` must name at least one test; CI fails otherwise. Whoever implements a clause updates its row in the same PR.

| File | Specification |
|---|---|
| [iso-32000-2.md](./iso-32000-2.md) | ISO 32000-2:2020 PDF 2.0 |
| [iso-ts-32001.md](./iso-ts-32001.md) | ISO/TS 32001:2022 Extensions to hash algorithms |
| [iso-ts-32002.md](./iso-ts-32002.md) | ISO/TS 32002:2022 Extensions to digital signatures (ECC) |
| [iso-ts-32003.md](./iso-ts-32003.md) | ISO/TS 32003:2023 AES-GCM |
| [iso-ts-32004.md](./iso-ts-32004.md) | ISO/TS 32004:2024 Integrity protection in encrypted documents |
| [iso-ts-32005.md](./iso-ts-32005.md) | ISO/TS 32005:2023 PDF 1.7 and 2.0 structure namespace inclusion |
| [wtpdf-1.0.md](./wtpdf-1.0.md) | Well-Tagged PDF 1.0 |
| [pdf-declarations.md](./pdf-declarations.md) | PDF Declarations |
| [pdf20-an001.md](./pdf20-an001.md) | Application Note 001 Black Point Compensation |
| [pdf20-an002.md](./pdf20-an002.md) | Application Note 002 Associated Files |
| [pdf20-an003.md](./pdf20-an003.md) | Application Note 003 Object Metadata Locations |
| [icc-1-2022.md](./icc-1-2022.md) | ICC.1:2022 colour profile format (reading, for ICCBased colour spaces) |
| [iso-14289-1.md](./iso-14289-1.md) | ISO 14289-1:2014 PDF/UA-1 |
| [iso-14289-2.md](./iso-14289-2.md) | ISO 14289-2:2024 PDF/UA-2 |
| [iso-19005-2.md](./iso-19005-2.md) | ISO 19005-2:2011 PDF/A-2 output, cited by veraPDF rule identifiers (open decision 12) |
| [iso-19005-3.md](./iso-19005-3.md) | ISO 19005-3:2012 PDF/A-3 output: what it changes from PDF/A-2, cited by veraPDF rule identifiers (open decision 12) |
| [iso-19005-4.md](./iso-19005-4.md) | ISO 19005-4:2020 PDF/A-4 and PDF/A-4f output, based on PDF 2.0, cited by veraPDF rule identifiers (open decision 12) |

PDF/A rows cite veraPDF validation-profile rule identifiers, the standards not being purchased (open decision 12); PDF/X gets a file when its document is purchased; see `docs/research/missing-iso-specs.md`. The PDF Association's Tagged PDF Best Practice Guide in `Specs/` is guidance, not a conformance target.

## Row format

Every table has exactly these six columns, in this order:

| Cell | Content |
|---|---|
| Clause | The clause number as the specification numbers it (`7.5.4`, `Annex F`, `BPC-1`). Unique within the file. |
| Title | The clause title from the specification's table of contents. |
| Status | `not started`, `partial`, `done` or `n/a`, exactly, in lower case. |
| Implementing types | Empty, or a comma-separated list of backticked simple type names declared under `src/`: `` `CosDictionary`, `CosLexer` ``. No namespace, no type arguments (`` `CosArray` `` for `CosArray<T>`). Each must be the name of a `class`, `struct`, `interface`, `enum` or `record` in `src/**/*.cs`. |
| Tests | Empty, or a comma-separated list of backticked test identifiers, each a fully qualified test method `` `Namespace.Class.Method` `` or a test class `` `Namespace.Class` ``: `` `Broadside.Tests.Cos.LexerTests.Reads_names`, `Broadside.Tests.Cos.FilterTests` ``. The class is the one that directly declares the method, in the namespace its file declares; the method must carry `[Fact]` or `[Theory]`. Each must exist under `tests/**/*.cs`. |
| Notes | Free text. Required on `n/a` rows (why the clause is not applicable) and, under `--strict`, on `partial` rows (what is still missing). |

A `|` inside a cell is written `\|`.

## Rules

`tools/ConformanceCheck` enforces these over every `docs/conformance/*.md` except this README; each failure is printed as `docs/conformance/<file>.md:<line>: <message>`.

1. Every table row has exactly six cells, and the header row is `Clause | Title | Status | Implementing types | Tests | Notes`.
2. Clause is non-empty and unique within the file.
3. Status is one of `not started`, `partial`, `done`, `n/a`.
4. A `done` row names at least one test in the Tests cell.
5. An `n/a` row has a non-empty Notes cell.
6. Every identifier in the Tests cell resolves to a test class or a `[Fact]`/`[Theory]` method under `tests/`.
7. Every name in the Implementing types cell is declared as a `class`, `struct`, `interface`, `enum` or `record` under `src/`.
8. With `--strict` only: a `partial` row names at least one test and has a non-empty Notes cell.

The code lookups are a line-level scan of the `.cs` files (`namespace`, type and attributed method declarations), not a compile, so they run in milliseconds and need no build; `bin`, `obj` and `Fixtures` directories are skipped.

## Clause citations

With `--citations` the checker also reads the XML documentation of the packages and applies the rules in [citation-policy.json](./citation-policy.json). Build the checked packages first; the documentation is read from `artifacts/bin/<package>/<configuration>/<package>.xml` (`--configuration`, default `debug`).

9. Every project under `src/` is listed in the policy's `packages`, either checked or unchecked with a reason (the rendering backends and the viewer are unchecked: they implement no clause).
10. Every public type of a checked package, as its `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` declare it, cites a specification in its own XML documentation, in one of the policy's `citationForms` (`ISO 32000-2 §x.y.z`, an annex or table of ISO 32000-2, another ISO standard or ISO/TS, an ITU-T recommendation, an Adobe technical note, an RFC, ICC.1, the OpenType specification, the Unicode Standard, a PDF Association application note, Well-Tagged PDF, PDF Declarations), unless the policy's `exemptions` list the type or its namespace with a reason (options, engine, hosting, diagnostics infrastructure). A nested type is covered by a citation on a type that encloses it, and members by their declaring type's citation. The violation is reported at the type's line in the API file.
11. Every exemption has a reason and matches at least one public type of a checked package.
12. Every implementing type a row names that is declared in a checked package (public or internal) has XML documentation that cites the row's clause of the map's document, on the type or on one of its members: the document's name from the policy's `maps` (`ISO 32000-2`, `ISO/TS 32003`, `ICC.1:2022`, ...) followed, before any other document is named, by the clause (`§7.3.4`, `Annex F.3`, `clause 9`, or the clause right after the name). A citation of the clause, of one of its sub-clauses or, for a row at the third level or deeper, of its parent clause counts: `ISO 32000-2 §8.9.5` covers the row `8.9.5.1`. Clauses that are not numbered are cited by the row's key: `PDF 2.0 Application Note 003 §Explicit`. Types declared only in unchecked packages are not checked.

## Running the checker

```sh
dotnet run --project tools/ConformanceCheck -- --summary           # from anywhere inside the checkout
dotnet run --project tools/ConformanceCheck -- --root . --summary  # what CI runs, from the repository root
dotnet run --project tools/ConformanceCheck -- --strict            # also require tests and notes on partial rows
dotnet run --project tools/ConformanceCheck -- --citations         # also check clause citations (build the checked packages first)
```

`--root` defaults to the nearest parent of the current directory that contains `Broadside.slnx`. `--summary` prints a Markdown table of row counts per status for every file plus a total. The exit code is 0 when the map is valid and 1 when any rule fails.

The checker runs in three places: the `conformance-map` CI job builds the checked packages and runs `dotnet run --project tools/ConformanceCheck -- --root . --summary --citations` on every pull request; `tests/ConformanceCheck.Tests` runs the same validation, citations included, against the checkout as part of `dotnet test Broadside.Core.slnf` (it references the checked packages, so their XML documentation is next to it), so a wrong row or a missing citation also fails the test suite; and the fixture tests in that project pin the exact violation for each rule.
