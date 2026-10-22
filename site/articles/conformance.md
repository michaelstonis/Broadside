---
title: "Conformance"
---

# Conformance

Broadside tracks what it implements in the conformance map: one Markdown file per specification under [`docs/conformance/`](https://github.com/michaelstonis/Broadside/tree/main/docs/conformance), one row per clause. Whoever implements a clause updates its row in the same pull request as the code.

Each row has six columns: the clause number, its title, a status, the implementing types, the tests, and notes. The status is one of `not started`, `partial`, `done` or `n/a` (no implementable content, or out of scope).

The map is checked, not just written. `tools/ConformanceCheck` runs in CI and in the test suite and fails when a `done` row names no test, when a named test or type does not exist in the source, or when a row is malformed. The rules are in the [conformance README](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/README.md).

Specifications covered:

- ISO 32000-2:2020 (PDF 2.0)
- ISO/TS 32001 to 32005 (hash algorithms, ECC signatures, AES-GCM, integrity protection, structure namespaces)
- ISO 14289-1 and 14289-2 (PDF/UA-1 and PDF/UA-2) and Well-Tagged PDF 1.0
- PDF Declarations and the PDF 2.0 application notes 001 to 003
- ICC.1:2022 (reading colour profiles for ICCBased colour spaces)
- ISO 19005-2, -3 and -4 (PDF/A-2, PDF/A-3 and PDF/A-4 output), cited by veraPDF rule identifiers

## Coverage

The checker's `--summary` gives the counts per file. On the integration branch in October 2026 the map has 646 rows across 17 files: 356 `done`, 115 `partial`, 128 `not started` and 47 `n/a`. ISO 32000-2 alone has 433 rows: 272 `done`, 68 `partial`, 66 `not started` and 27 `n/a`. Most of the `not started` rows in ISO 32000-2 fall into four groups:

- introductions with no implementable content of their own;
- out-of-scope features kept as parse-and-preserve (multimedia, 3D, rich media);
- prepress (separations, trapping, OPI);
- informative annexes.

The rest include unencrypted wrapper documents (§7.6.7), articles, forms data format, measurement and geospatial features. Run the checker for the current numbers:

```sh
dotnet run --project tools/ConformanceCheck -- --summary
```
