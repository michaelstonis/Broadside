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
