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

Today every row is `not started`: no library code exists yet.
