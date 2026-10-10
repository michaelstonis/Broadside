# Phase 2 content interpretation baseline

The Track 2D exit benchmark (issue #80): `bench/Broadside.Benchmarks/ContentInterpretationBenchmarks.cs` over the real-world subset pinned in `bench/Broadside.Benchmarks/content-subset.json`. The full BenchmarkDotNet JSON export (every iteration, statistics and allocation figures) is [`bench/baselines/phase2/ContentInterpretationBenchmarks.json`](../../bench/baselines/phase2/ContentInterpretationBenchmarks.json), next to the Phase 1 reports of #48 (`bench/baselines/phase1/`) for the Phase 3I regression gate.

## What is measured

- **`InterpretWarm`**: every listed page of a document opened from memory and interpreted once in `GlobalSetup`, so fonts, colour spaces, shadings, decoded form content and image views are cached. The processor requests every event (`ContentEvents.All`) and does nothing with them, so the number is the interpreter's own cost: operators read, paths built, glyphs decoded and positioned, forms run, images and shadings resolved. Pattern cells, Type 3 glyph procedures and soft-mask groups run only on a processor's request, so they are not in it.
- **`OpenAndInterpretFirstPage`**: open the document from bytes and interpret page 1: the cost of resolving the page's resources on first use (font programs, colour spaces, functions, shadings).

## Subset

Picked by measurement from the content corpus gate's per-file detail (`artifacts/corpus-gate/<id>.json`): the well-formed file with the most of each category per page. Counts are from the gate's processor (nested streams included).

| Category | File | Pages | Operators | Glyphs | Path verbs | Images |
|---|---|---:|---:|---:|---:|---:|
| text-heavy | `pdfjs/tracemonkey.pdf` (pdf.js's own benchmark paper) | 14 | 34,867 | 69,284 | 23,714 | 140 |
| vector-heavy | `pdfjs/issue12810.pdf` | 1 | 222,464 | 15 | 145,897 | 0 |
| image-heavy | `pdfjs/bug1799927.pdf` (2,156 inline stencil masks) | 1 | 9,160 | 0 | 355 | 2,156 |
| inline-image-heavy | `qpdf/inline-images.pdf` | 2 | 19,898 | 0 | 7,140 | 3,141 |
| cjk | `pdfjs/issue11913.pdf` (Type 0, 2-byte codes) | 1 | 163 | 4,779 | 11 | 0 |
| page-count | `pdfjs/freeculture.pdf` | 352 | 148,475 | 671,012 | 47,436 | 22 |
| shading-pattern | `pdfjs/bug1721218_reduced.pdf` (3,525 axial/radial `sh`, 49 groups, 14 soft masks) | 1 | 162,908 | 0 | 105,766 | 0 |
| tiling-pattern | `pdfjs/tiling_patterns_variations.pdf` | 1 | 98 | 0 | 130 | 0 |

The largest page count among well-formed files is a synthetic 10,000-page Isartor implementation-limit file with no content; `freeculture.pdf` (352 pages) is the largest real document, so it stands for the page-count category.

## Results

`--job default` (BenchmarkDotNet's default job), Release build. Code at commit `0bd4c53` (branch `80-content-corpus-gate`), corpora: pdf.js `c53f395e4057b501f478a7e98ed03d47c7c62a04`, qpdf as fetched by `tools/CorpusFetcher` on 2026-10-09 (`corpus/manifest.lock.json`).

| Method | File | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| InterpretWarm | cjk: pdfjs/issue11913.pdf | 665.6 μs | 15.4 μs | 58,008 B |
| OpenAndInterpretFirstPage | cjk: pdfjs/issue11913.pdf | 789.7 μs | 13.9 μs | 196,960 B |
| InterpretWarm | image-heavy: pdfjs/bug1799927.pdf | 3,249.3 μs | 281.0 μs | 4,315,288 B |
| OpenAndInterpretFirstPage | image-heavy: pdfjs/bug1799927.pdf | 3,086.0 μs | 22.0 μs | 4,337,921 B |
| InterpretWarm | inline-image-heavy: qpdf/inline-images.pdf | 7,167.8 μs | 71.6 μs | 8,196,514 B |
| OpenAndInterpretFirstPage | inline-image-heavy: qpdf/inline-images.pdf | 3,362.0 μs | 42.6 μs | 3,509,384 B |
| InterpretWarm | page-count: pdfjs/freeculture.pdf | 38,358.8 μs | 353.7 μs | 2,834,784 B |
| OpenAndInterpretFirstPage | page-count: pdfjs/freeculture.pdf | 1,726.0 μs | 15.7 μs | 2,472,472 B |
| InterpretWarm | shading-pattern: pdfjs/bug1721218_reduced.pdf | 30,661.3 μs | 257.3 μs | 968 B |
| OpenAndInterpretFirstPage | shading-pattern: pdfjs/bug1721218_reduced.pdf | 35,786.4 μs | 337.3 μs | 4,931,508 B |
| InterpretWarm | text-heavy: pdfjs/tracemonkey.pdf | 11,804.5 μs | 75.4 μs | 222,696 B |
| OpenAndInterpretFirstPage | text-heavy: pdfjs/tracemonkey.pdf | 1,418.6 μs | 15.6 μs | 1,135,578 B |
| InterpretWarm | tiling-pattern: pdfjs/tiling_patterns_variations.pdf | 7.5 μs | 0.03 μs | - |
| OpenAndInterpretFirstPage | tiling-pattern: pdfjs/tiling_patterns_variations.pdf | 22.8 μs | 0.26 μs | 38,264 B |
| InterpretWarm | vector-heavy: pdfjs/issue12810.pdf | 33,540.8 μs | 369.9 μs | 2,795,920 B |
| OpenAndInterpretFirstPage | vector-heavy: pdfjs/issue12810.pdf | 32,429.6 μs | 481.2 μs | 2,851,208 B |

Derived, warm: about 150 ns per operator on path-dominated pages (issue12810: 33.5 ms for 222k operators; bug1721218: 30.7 ms for 163k), about 110 ns per glyph on text pages (freeculture: 38.4 ms for 671k glyphs on 352 pages, i.e. 109 μs per page).

### Reading the Allocated column

The warm pass allocates nothing per operator, glyph or path: `ContentAllocationTests.Interpreting_a_page_with_every_event_requested_allocates_nothing_once_warm` proves it on 16 minimal-corpus pages (text in four font kinds, forms, transparency and graphics states, marked and optional content, every colour space family, shadings including meshes, patterns, image XObjects), and `bug1721218_reduced.pdf` shows 968 B for 163k operators. What the column does show, per run:

- **Decoding the page's content stream.** Page content is decoded on every run (forms are cached per document; pages are not, to keep memory flat on large documents). `issue12810.pdf` decodes 2.8 MB of content, `freeculture.pdf` 352 Flate streams, `tracemonkey.pdf` 14; `issue11913.pdf`'s 58 KB, whatever the events requested, is the decoding of its 7 KB of content (decoder buffers and output).
- **Inline images.** The image view of an inline image (`ImageEvent.Image`) owns a copy of its dictionary and data (#56), built only for a processor that requests `ContentEvents.Images`: the image-heavy and inline-image-heavy rows.

`OpenAndInterpretFirstPage` adds what opening allocates (cross-reference, page tree) and the first resolution of every resource the page uses.

## Machine

- Apple M4 Max, 16 cores (16 logical, 16 physical), 64 GB, macOS 27.0.1 (26A434), arm64.
- BenchmarkDotNet v0.15.8; .NET SDK 10.0.401 (Homebrew), runtime .NET 10.0.12 (10.0.1226.42308), Arm64 RyuJIT armv8.0-a.
- Not a quiet machine: other build agents shared it (load average about 32 just after the run). As for the Phase 1 baseline (`bench/baselines/phase1/README.md`), treat the times as indicative and compare them with a tolerance; the allocation figures do not depend on load and are exact.

`dotnet --info` (abridged):

```
.NET SDK:
 Version:           10.0.401
 Commit:            e34a38d2ae
 MSBuild version:   18.9.11+e34a38d2a
Runtime Environment:
 OS Name:     Mac OS X
 OS Version:  27.0
 OS Platform: Darwin
 RID:         osx-arm64
Host:
  Version:      10.0.12
  Architecture: arm64
  Commit:       95017c711e
```

## Reproducing

```sh
dotnet run -c Release --project tools/CorpusFetcher -- --only pdfjs,qpdf
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*ContentInterpretationBenchmarks*' --exporters json
```

Without `corpus/` (CI's `bench-dry`), the benchmark runs the minimal-corpus fallback listed in `content-subset.json` instead; those numbers are not comparable with this baseline. A pinned file whose SHA-256 no longer matches is skipped, so a refetched corpus cannot silently change the subset.
