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
| [iso-14289-1.md](./iso-14289-1.md) | ISO 14289-1:2014 PDF/UA-1 |
| [iso-14289-2.md](./iso-14289-2.md) | ISO 14289-2:2024 PDF/UA-2 |

PDF/A and PDF/X get files when their documents are purchased; see `docs/research/missing-iso-specs.md`. The PDF Association's Tagged PDF Best Practice Guide in `Specs/` is guidance, not a conformance target.

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

## Running the checker

```sh
dotnet run --project tools/ConformanceCheck -- --summary           # from anywhere inside the checkout
dotnet run --project tools/ConformanceCheck -- --root . --summary  # what CI runs, from the repository root
dotnet run --project tools/ConformanceCheck -- --strict            # also require tests and notes on partial rows
```

`--root` defaults to the nearest parent of the current directory that contains `Broadside.slnx`. `--summary` prints a Markdown table of row counts per status for every file plus a total. The exit code is 0 when the map is valid and 1 when any rule fails.

The checker runs in three places: the `conformance` CI job runs `dotnet run --project tools/ConformanceCheck -- --root . --summary` on every pull request; `tests/ConformanceCheck.Tests` runs the same validation against the checkout as part of `dotnet test Broadside.Core.slnf`, so a wrong row also fails the test suite; and the fixture tests in that project pin the exact violation for each rule.
