# Phase 3 parse, render and text baselines

The Track 3I baseline (issue #140): Broadside's parse, render and text extraction times and allocations over the rendering gate's
pinned real-world pages, timed beside PDFium and pdf.js on the same machine through RenderDiff's reference renderers. It sits
next to the [Phase 1 baseline](../../bench/baselines/phase1/README.md) (lexer, filters, open-and-walk) and the
[Phase 2 content interpretation baseline](phase-2-content-baseline.md). The raw exports are in
[`bench/baselines/phase3/`](../../bench/baselines/phase3/): BenchmarkDotNet's full JSON for each class (every iteration, statistics,
allocations) and one JSON line per reference case (`pdfium.jsonl`, `pdfjs.jsonl`).

## What is measured

The pages are the 21 pages of 20 files in `tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt` (issue #138; chosen for
coverage of blend modes, spot colours, output intents, every image codec, shadings, patterns, soft masks and embedded text, not
for speed). Broadside opens them as the rendering gate does: Standard 14 and predefined CMaps bundled, no system fonts, and the
display-list cache off so that a warm render interprets the page again.

| Operation | Broadside (`bench/Broadside.Benchmarks`) | PDFium (`tools/RenderDiff/reference/pdfium/bench.py`) | pdf.js (`tools/RenderDiff/reference/pdfjs/bench.mjs`) |
|---|---|---|---|
| open | `RealWorldPageBenchmarks.Open`: open from memory, load the page, read its MediaBox | `FPDF_LoadMemDocument` + `FPDF_LoadPage` + page size | `getDocument` + `getPage` + `view` |
| text | `RealWorldPageBenchmarks.ExtractText`: on an open document, `PdfPage.ExtractText().PlainText` | load the page, its text page and the whole text range | `getPage` + `getTextContent` + `cleanup` |
| render | `RealWorldRenderBenchmarks.Render`: on an open document, build the display list and rasterize into a new bitmap | load the page, render it with annotations and forms | `getPage` + render with annotations + `cleanup` (operator list parsed again) |
| open-render | `RealWorldRenderBenchmarks.OpenAndRender`: open from memory and render the page | open, init forms, load, render | `getDocument` + `getPage` + render + `destroy` |

All three keep the document's font and resource caches across warm runs and parse the page's content again on each one. The
references run in process in their own runtime: PDFium through pypdfium2 (a ctypes call per operation, microseconds, visible only on
the smallest pages), pdf.js in Node with its in-thread worker and `@napi-rs/canvas` (Skia) rather than a browser canvas. Times are
medians: Broadside's from BenchmarkDotNet, the references' from one untimed then ten timed runs (at least three, at most 10 s a case).
Allocations are Broadside's managed bytes per operation (`[MemoryDiagnoser]`); the bitmap's pixels are pooled and not counted.

Run it again with:

```sh
export BROADSIDE_CORPUS_DIR=/path/to/corpus   # tools/CorpusFetcher output
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter 'Broadside.Benchmarks.RealWorldRenderBenchmarks*' \
  'Broadside.Benchmarks.RealWorldPageBenchmarks*' --job short-inprocess --exporters json
tools/RenderDiff/acquire-references.sh && tools/RenderDiff/bench-references.sh 10 72,150
```

Without a corpus both classes fall back to `tests/Corpus/text-standard14.pdf` page 0, so CI's dry run and the regression gate run
them on every machine.

## Environment

| | |
|---|---|
| Commit | `0de11e44` + this change (branch `140-render-baselines`) |
| Machine | Apple M4 Max, 16 logical cores, 64 GB, Arm64 |
| OS | macOS 27.0.1 (26A434) [Darwin 27.0.0] |
| .NET | .NET 10.0.12 (10.0.1226.42308), SDK 10.0.401, Arm64 RyuJIT, Release |
| BenchmarkDotNet | 0.15.8, `--job short-inprocess` (ShortRun: 3 warm-ups, 3 iterations, in-process emit toolchain) |
| PDFium | PDFium 153.0.7999.0 via pypdfium2 5.13.0, Python 3.9.6 |
| pdf.js | pdfjs-dist 6.3.289 (legacy build), @napi-rs/canvas 1.0.9, Node 26.10.0 |
| Corpora | as fetched by `tools/CorpusFetcher` on this machine, pinned to commits in `tools/CorpusFetcher/corpora.json` (`corpus/manifest.lock.json`) |
| Load average (1, 5, 15 min) | Broadside 6.6, 7.8, 13.8 at the start, 9.2, 10.7, 12.1 at the end; references 8.9, 10.5, 12.0 at the start, 19.0, 12.7, 12.8 at the end |

The machine was shared with other build agents. The short job's three iterations and the load make every time indicative: expect
10-30 % run-to-run differences, more on sub-millisecond pages. Allocations do not depend on load; the `open` and `open-render`
figures can still differ by a fraction of a percent between processes until #288 makes the object cache's growth deterministic.

## Summary

Ratio = Broadside's median over the reference's, per page; below 1 is faster. Geometric mean over the 21 pages, and the worst page.

| Operation | DPI | Pages | Broadside / PDFium (geomean) | worst | Broadside / pdf.js (geomean) | worst |
|---|---:|---:|---:|---:|---:|---:|
| open |  | 21 | 0.04x | 0.3x | 0.09x | 0.2x |
| text |  | 21 | 0.13x | 0.6x | 0.03x | 0.2x |
| render | 72 | 21 | 1.16x | 6.3x | 0.29x | 3.5x |
| render | 150 | 21 | 1.70x | 10.7x | 0.53x | 4.1x |
| open-render | 72 | 21 | 1.13x | 4.2x | 0.21x | 5.9x |
| open-render | 150 | 21 | 1.43x | 6.4x | 0.39x | 4.1x |

What the numbers say:

- **Open and text are already ahead of both.** Opening is lazy (the xref and the page tree path only; 0.01-0.14 ms here), and text
  extraction on a warm document is a fraction of PDFium's text page. Their allocations (up to 419 KB to open, 1.2 MB to extract a
  tracemonkey page) are the document model and the extraction's output.
- **Rendering is near PDFium on simple pages and well behind it on dense ones.** At 72 DPI the geometric mean is about PDFium's,
  at 150 DPI about 1.7x; pdf.js (in Node) is slower than both on most pages. The worst pages are the Ghent CMYK test page (84 ms
  against PDFium's 13 ms at 72 DPI: spot colours, overprint and transparency in a DeviceCMYK group), the Ghent blend-mode patch
  file at 150 DPI (10x: non-knockout CMYK blending), tracemonkey's embedded Type 1 text (6x and 4x: glyph rasterization) and the
  function-based shading (1.6x, 1.8 MB allocated: per-pixel function evaluation). `personwithdog.pdf` (a Coons patch mesh in a
  CMYK group) matches PDFium at 72 DPI but scales worse (2.1x at 150 DPI) and is the slowest page against pdf.js (4.1x).
- **Warm render allocations** are small except where a page decodes or converts per render: 625 B to 750 KB at 72 DPI, the
  function-based shading 1.8 MB (4.0 MB at 150 DPI).

## Proposed performance targets (for the owner to accept or reject)

Measured on this subset with this procedure, against the pinned PDFium and pdf.js above, on one machine in one session:

1. **Render, warm, 72 and 150 DPI:** geometric mean at most 1.0x PDFium at v1 (today 1.16x and 1.70x), and no page slower than
   3x PDFium (today 6.3x and 10.7x).
2. **First render (open-render):** geometric mean at most 1.25x PDFium at 72 and 150 DPI (today 1.13x and 1.43x), no page beyond 4x.
3. **Never slower than pdf.js** on any page and operation (today render is 0.29x/0.53x geometric mean, worst page 3.5x/4.1x:
   tiling-pattern-large-steps and personwithdog).
4. **Open and text:** stay below 0.25x and 0.5x PDFium's geometric mean (today 0.04x and 0.13x), so later work does not make
   opening eager.
5. **Allocations:** a warm render allocates at most 1 MB per page at 72 DPI on this subset (today all but function_based_shading),
   and allocations are exact per case in the regression gate (#139), so any change shows.

## Regression gate

The gate of #139 (on its branch, not yet integrated when this was written) enumerates every benchmark (`--list flat`), so `RealWorldRenderBenchmarks` and `RealWorldPageBenchmarks` are in
it with no list to edit: on the CI runner, which has no real-world corpus, they run their fallback page (6 cases: `Render` and
`OpenAndRender` at 72 and 150 DPI, `Open`, `ExtractText`). Their allocation lines in `bench/baselines/gate/allocations.json` are
recorded from the CI runner by #139's documented update procedure when it lands; the real-world figures above are this report's, not the gate's.

## Per page

Broadside's median and allocation per operation, with PDFium's and pdf.js's medians (ms).

| File | Page | Operation | DPI | Broadside ms | Allocated | PDFium ms | pdf.js ms |
|---|---:|---|---:|---:|---:|---:|---:|
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | open |  | 0.04 | 99.7 KB | 1.87 | 0.35 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | text |  | 0.17 | 136.1 KB | 0.33 | 3.32 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | render | 72 | 3.12 | 141.8 KB | 0.62 | 7.89 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | render | 150 | 10.45 | 142.0 KB | 0.98 | 6.93 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | open-render | 72 | 3.65 | 623.1 KB | 2.66 | 10.69 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Patches/GWG160_Transp_Basic_BM_DeviceCMYK_Non-knockout_X4.pdf` | 0 | open-render | 150 | 11.10 | 623.2 KB | 3.03 | 10.01 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | open |  | 0.08 | 389.8 KB | 8.50 | 0.56 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | text |  | 0.98 | 802.7 KB | 2.19 | 4.99 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | render | 72 | 83.69 | 563.2 KB | 13.25 | 63.32 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | render | 150 | 186 | 565.4 KB | 19.90 | 124 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | open-render | 72 | 96.45 | 7.3 MB | 22.71 | 135 |
| `gwg-output-suite/Ghent_PDF_Output_Suite_V50_Patches/Categories/1-CMYK/Test pages/Ghent_PDF-Output-Test-V50_CMYK_X4.pdf` | 0 | open-render | 150 | 196 | 7.3 MB | 30.52 | 200 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | open |  | 0.01 | 30.1 KB | 0.79 | 0.26 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | text |  | 0.02 | 45.5 KB | 0.11 | 0.17 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | render | 72 | 0.60 | 39.1 KB | 0.81 | 1.64 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | render | 150 | 1.08 | 39.1 KB | 0.58 | 1.78 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | open-render | 72 | 0.78 | 442.8 KB | 0.44 | 7.92 |
| `pdf20examples/PDF 2.0 image with BPC.pdf` | 0 | open-render | 150 | 1.31 | 442.8 KB | 0.73 | 7.81 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | open |  | 0.01 | 35.1 KB | 0.77 | 0.29 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | text |  | 0.01 | 26.9 KB | 0.08 | 0.32 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | render | 72 | 0.17 | 29.0 KB | 0.12 | 0.48 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | render | 150 | 0.44 | 29.0 KB | 0.38 | 0.76 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | open-render | 72 | 0.29 | 392.5 KB | 0.68 | 4.34 |
| `pdf20examples/PDF 2.0 with page level output intent.pdf` | 0 | open-render | 150 | 0.58 | 392.5 KB | 0.47 | 4.04 |
| `pdfjs/alphatrans.pdf` | 0 | open |  | 0.01 | 37.2 KB | 0.87 | 0.10 |
| `pdfjs/alphatrans.pdf` | 0 | text |  | 0.02 | 29.2 KB | 0.13 | 0.70 |
| `pdfjs/alphatrans.pdf` | 0 | render | 72 | 1.44 | 36.3 KB | 1.05 | 5.52 |
| `pdfjs/alphatrans.pdf` | 0 | render | 150 | 5.50 | 36.3 KB | 2.44 | 5.68 |
| `pdfjs/alphatrans.pdf` | 0 | open-render | 72 | 2.15 | 554.1 KB | 0.93 | 8.67 |
| `pdfjs/alphatrans.pdf` | 0 | open-render | 150 | 6.35 | 553.9 KB | 2.60 | 8.15 |
| `pdfjs/annotation-highlight.pdf` | 0 | open |  | 0.02 | 45.7 KB | 0.69 | 0.16 |
| `pdfjs/annotation-highlight.pdf` | 0 | text |  | 0.01 | 10.3 KB | 0.12 | 0.48 |
| `pdfjs/annotation-highlight.pdf` | 0 | render | 72 | 0.10 | 29.8 KB | 0.26 | 1.34 |
| `pdfjs/annotation-highlight.pdf` | 0 | render | 150 | 0.36 | 29.8 KB | 0.40 | 1.60 |
| `pdfjs/annotation-highlight.pdf` | 0 | open-render | 72 | 0.43 | 352.3 KB | 0.78 | 6.16 |
| `pdfjs/annotation-highlight.pdf` | 0 | open-render | 150 | 0.70 | 352.0 KB | 0.91 | 3.88 |
| `pdfjs/bug_jpx.pdf` | 0 | open |  | 0.02 | 45.9 KB | 0.24 | 0.09 |
| `pdfjs/bug_jpx.pdf` | 0 | text |  | 0.00 | 120 B | 0.03 | 0.04 |
| `pdfjs/bug_jpx.pdf` | 0 | render | 72 | 0.06 | 625 B | 0.20 | 0.27 |
| `pdfjs/bug_jpx.pdf` | 0 | render | 150 | 0.32 | 630 B | 0.36 | 1.64 |
| `pdfjs/bug_jpx.pdf` | 0 | open-render | 72 | 0.48 | 76.4 KB | 0.28 | 2.39 |
| `pdfjs/bug_jpx.pdf` | 0 | open-render | 150 | 0.76 | 76.4 KB | 1.14 | 3.44 |
| `pdfjs/calgray.pdf` | 0 | open |  | 0.01 | 35.9 KB | 0.79 | 0.18 |
| `pdfjs/calgray.pdf` | 0 | text |  | 0.03 | 54.3 KB | 0.18 | 0.23 |
| `pdfjs/calgray.pdf` | 0 | render | 72 | 0.45 | 45.6 KB | 0.24 | 0.86 |
| `pdfjs/calgray.pdf` | 0 | render | 150 | 1.67 | 45.6 KB | 0.79 | 2.21 |
| `pdfjs/calgray.pdf` | 0 | open-render | 72 | 0.56 | 396.0 KB | 0.39 | 5.66 |
| `pdfjs/calgray.pdf` | 0 | open-render | 150 | 1.72 | 396.0 KB | 0.93 | 7.04 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | open |  | 0.01 | 29.4 KB | 0.06 | 0.10 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | text |  | 0.00 | 120 B | 0.02 | 0.07 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | render | 72 | 0.70 | 2.0 KB | 0.49 | 0.62 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | render | 150 | 2.82 | 2.0 KB | 1.80 | 1.78 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | open-render | 72 | 0.75 | 72.3 KB | 0.53 | 4.91 |
| `pdfjs/ccitt_EndOfBlock_false.pdf` | 0 | open-render | 150 | 2.84 | 72.3 KB | 1.99 | 2.88 |
| `pdfjs/cmykjpeg.pdf` | 0 | open |  | 0.02 | 45.1 KB | 1.22 | 0.08 |
| `pdfjs/cmykjpeg.pdf` | 0 | text |  | 0.00 | 120 B | 0.02 | 0.08 |
| `pdfjs/cmykjpeg.pdf` | 0 | render | 72 | 0.12 | 753 B | 0.48 | 2.33 |
| `pdfjs/cmykjpeg.pdf` | 0 | render | 150 | 0.56 | 764 B | 0.93 | 3.63 |
| `pdfjs/cmykjpeg.pdf` | 0 | open-render | 72 | 1.95 | 196.7 KB | 1.12 | 2.11 |
| `pdfjs/cmykjpeg.pdf` | 0 | open-render | 150 | 2.42 | 196.5 KB | 1.59 | 2.77 |
| `pdfjs/devicen.pdf` | 0 | open |  | 0.01 | 42.7 KB | 0.28 | 0.08 |
| `pdfjs/devicen.pdf` | 0 | text |  | 0.00 | 120 B | 0.05 | 0.40 |
| `pdfjs/devicen.pdf` | 0 | render | 72 | 0.07 | 1.4 KB | 0.26 | 1.15 |
| `pdfjs/devicen.pdf` | 0 | render | 150 | 0.31 | 1.4 KB | 0.32 | 0.56 |
| `pdfjs/devicen.pdf` | 0 | open-render | 72 | 0.12 | 96.4 KB | 0.52 | 0.68 |
| `pdfjs/devicen.pdf` | 0 | open-render | 150 | 0.36 | 96.4 KB | 0.47 | 0.59 |
| `pdfjs/function_based_shading.pdf` | 0 | open |  | 0.01 | 32.9 KB | 0.10 | 0.09 |
| `pdfjs/function_based_shading.pdf` | 0 | text |  | 0.00 | 120 B | 0.05 | 0.04 |
| `pdfjs/function_based_shading.pdf` | 0 | render | 72 | 19.95 | 1.8 MB | 12.44 | 97.79 |
| `pdfjs/function_based_shading.pdf` | 0 | render | 150 | 54.24 | 4.0 MB | 46.73 | 192 |
| `pdfjs/function_based_shading.pdf` | 0 | open-render | 72 | 20.12 | 1.9 MB | 11.13 | 122 |
| `pdfjs/function_based_shading.pdf` | 0 | open-render | 150 | 54.41 | 4.1 MB | 46.60 | 155 |
| `pdfjs/issue2948.pdf` | 0 | open |  | 0.01 | 32.9 KB | 0.25 | 0.22 |
| `pdfjs/issue2948.pdf` | 0 | text |  | 0.06 | 38.6 KB | 0.12 | 2.07 |
| `pdfjs/issue2948.pdf` | 0 | render | 72 | 2.57 | 49.8 KB | 0.82 | 10.96 |
| `pdfjs/issue2948.pdf` | 0 | render | 150 | 10.40 | 49.9 KB | 2.88 | 51.96 |
| `pdfjs/issue2948.pdf` | 0 | open-render | 72 | 2.78 | 288.9 KB | 1.21 | 12.67 |
| `pdfjs/issue2948.pdf` | 0 | open-render | 150 | 10.98 | 289.0 KB | 3.59 | 70.72 |
| `pdfjs/issue3584.pdf` | 0 | open |  | 0.01 | 28.1 KB | 0.31 | 0.10 |
| `pdfjs/issue3584.pdf` | 0 | text |  | 0.00 | 1.5 KB | 0.02 | 0.07 |
| `pdfjs/issue3584.pdf` | 0 | render | 72 | 0.02 | 10.9 KB | 0.04 | 0.16 |
| `pdfjs/issue3584.pdf` | 0 | render | 150 | 0.04 | 10.9 KB | 0.05 | 0.43 |
| `pdfjs/issue3584.pdf` | 0 | open-render | 72 | 0.19 | 144.5 KB | 0.33 | 3.22 |
| `pdfjs/issue3584.pdf` | 0 | open-render | 150 | 0.22 | 144.5 KB | 0.34 | 3.07 |
| `pdfjs/issue6296.pdf` | 0 | open |  | 0.01 | 29.5 KB | 0.06 | 0.23 |
| `pdfjs/issue6296.pdf` | 0 | text |  | 0.00 | 120 B | 0.02 | 0.21 |
| `pdfjs/issue6296.pdf` | 0 | render | 72 | 0.06 | 873 B | 0.06 | 0.90 |
| `pdfjs/issue6296.pdf` | 0 | render | 150 | 0.23 | 875 B | 0.15 | 0.98 |
| `pdfjs/issue6296.pdf` | 0 | open-render | 72 | 0.08 | 47.8 KB | 0.11 | 1.36 |
| `pdfjs/issue6296.pdf` | 0 | open-render | 150 | 0.25 | 47.8 KB | 0.20 | 1.20 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | open |  | 0.01 | 27.1 KB | 0.03 | 0.27 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | text |  | 0.00 | 120 B | 0.01 | 0.04 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | render | 72 | 0.11 | 625 B | 0.12 | 0.79 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | render | 150 | 0.57 | 636 B | 0.37 | 0.97 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | open-render | 72 | 0.15 | 42.1 KB | 0.14 | 3.11 |
| `pdfjs/jbig2_symbol_offset.pdf` | 0 | open-render | 150 | 0.58 | 42.1 KB | 0.41 | 3.58 |
| `pdfjs/personwithdog.pdf` | 0 | open |  | 0.02 | 49.3 KB | 1.03 | 0.08 |
| `pdfjs/personwithdog.pdf` | 0 | text |  | 0.09 | 121 B | 0.32 | 1.28 |
| `pdfjs/personwithdog.pdf` | 0 | render | 72 | 48.82 | 18.9 KB | 50.21 | 26.01 |
| `pdfjs/personwithdog.pdf` | 0 | render | 150 | 158 | 20.8 KB | 73.72 | 38.57 |
| `pdfjs/personwithdog.pdf` | 0 | open-render | 72 | 50.84 | 806.1 KB | 51.11 | 24.74 |
| `pdfjs/personwithdog.pdf` | 0 | open-render | 150 | 158 | 807.7 KB | 73.75 | 38.90 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | open |  | 0.01 | 26.3 KB | 0.03 | 0.08 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | text |  | 0.00 | 120 B | 0.02 | 0.04 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | render | 72 | 0.09 | 2.2 KB | 0.38 | 0.99 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | render | 150 | 0.32 | 2.2 KB | 1.52 | 2.11 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | open-render | 72 | 0.10 | 40.3 KB | 0.40 | 1.36 |
| `pdfjs/smask_alpha_oob.pdf` | 0 | open-render | 150 | 0.33 | 40.3 KB | 1.57 | 2.71 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | open |  | 0.01 | 28.1 KB | 0.03 | 0.15 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | text |  | 0.00 | 712 B | 0.01 | 0.03 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | render | 72 | 2.78 | 2.1 KB | 2.39 | 0.80 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | render | 150 | 11.99 | 2.2 KB | 10.19 | 3.81 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | open-render | 72 | 2.81 | 41.3 KB | 2.42 | 0.48 |
| `pdfjs/tiling-pattern-large-steps.pdf` | 0 | open-render | 150 | 11.82 | 41.4 KB | 10.43 | 4.09 |
| `pdfjs/tracemonkey.pdf` | 0 | open |  | 0.14 | 418.8 KB | 2.65 | 0.85 |
| `pdfjs/tracemonkey.pdf` | 0 | text |  | 0.46 | 987.5 KB | 0.81 | 6.57 |
| `pdfjs/tracemonkey.pdf` | 0 | render | 72 | 7.57 | 619.7 KB | 1.26 | 16.04 |
| `pdfjs/tracemonkey.pdf` | 0 | render | 150 | 12.62 | 619.5 KB | 2.75 | 20.19 |
| `pdfjs/tracemonkey.pdf` | 0 | open-render | 72 | 9.07 | 2.0 MB | 5.11 | 47.67 |
| `pdfjs/tracemonkey.pdf` | 0 | open-render | 150 | 14.17 | 2.0 MB | 6.82 | 42.33 |
| `pdfjs/tracemonkey.pdf` | 1 | open |  | 0.14 | 418.8 KB | 3.64 | 0.73 |
| `pdfjs/tracemonkey.pdf` | 1 | text |  | 0.73 | 1.2 MB | 1.15 | 8.18 |
| `pdfjs/tracemonkey.pdf` | 1 | render | 72 | 10.96 | 746.5 KB | 2.63 | 30.95 |
| `pdfjs/tracemonkey.pdf` | 1 | render | 150 | 17.77 | 746.7 KB | 4.54 | 33.43 |
| `pdfjs/tracemonkey.pdf` | 1 | open-render | 72 | 12.77 | 2.4 MB | 7.05 | 46.29 |
| `pdfjs/tracemonkey.pdf` | 1 | open-render | 150 | 19.56 | 2.4 MB | 9.28 | 44.58 |
