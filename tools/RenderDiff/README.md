# RenderDiff

Triage for rendering differences (issue #137). RenderDiff renders each page of a PDF file or a corpus with Broadside's software rasterizer and with reference renderers (PDFium, pdf.js, poppler) at the same DPI, lines the images up, ranks the pages by the golden-image harness's perceptual comparison and writes a report with side-by-side and diff images.

It is a development tool only: never a CI gate, never packed, and no Broadside package references it or anything it uses. The reference renderers run as separate processes from a directory outside the tracked files.

## Install the reference renderers

```sh
tools/RenderDiff/acquire-references.sh           # PDFium and pdf.js
tools/RenderDiff/acquire-references.sh pdfjs     # one of them
```

| Engine | What runs | Needs | Pinned by |
|---|---|---|---|
| `pdfium` | `reference/pdfium/render.py` in a venv with pypdfium2 (a PDFium build in a wheel) | python3 with venv | `reference/pdfium/requirements.txt` (version and wheel hashes) |
| `pdfjs` | `reference/pdfjs/render.mjs` under node with pdfjs-dist and @napi-rs/canvas | node 20+, npm | `reference/pdfjs/package-lock.json` (`npm ci --ignore-scripts`) |
| `poppler` | `pdftoppm` from the `PATH` | `apt install poppler-utils` / `brew install poppler` | your package manager |

The script installs into `artifacts/reference-renderers/` (gitignored), or `$BROADSIDE_REFERENCE_DIR`. Re-running reinstalls. It runs on Linux and macOS. An engine that is not installed is skipped with a message saying how to install it; with none available RenderDiff exits 3.

## Run

```sh
dotnet run --project tools/RenderDiff -c Release -- corpus/pdfjs
dotnet run --project tools/RenderDiff -c Release -- --engines pdfium --max-pages 1 tests/Corpus/paths-fill-rules.pdf
dotnet run --project tools/RenderDiff -c Release -- --reuse --report-pages 100 corpus/pdfjs   # re-rank without re-rendering the references
```

| Option | Effect |
|---|---|
| `--engines <list>` | `pdfium`, `pdfjs`, `poppler`, comma-separated (default all three; empty for none). |
| `--reference <name>=<dir>` | Also compare with renders made earlier, laid out as `<dir>/<file stem>/page<N>.png` (the layout of a report's `references/<engine>/`). Repeatable. |
| `--reference-dir <dir>` | Where the acquisition script installed (default `$BROADSIDE_REFERENCE_DIR`, else `artifacts/reference-renderers`). |
| `--out <dir>` | Report directory (default `artifacts/renderdiff`). |
| `--dpi <dpi>` | Resolution (default 72, the golden images' DPI). |
| `--max-pages <n>` | Pages per file, from the first (default 10). |
| `--max-shift <px>` | Largest alignment offset tried (default 1). |
| `--edge <t>`, `--flat <t>` | The golden-image tolerances (default 0.3 and 0.05, `GoldenThreshold.Default`). |
| `--report-pages <n>` | Worst pages shown with images in `index.html` (default 50). |
| `--triage-pages <n>` | Worst pages listed in `triage.md` (default 20). |
| `--timeout <s>` | Per file and engine (default 120). Broadside counts as one engine. |
| `--jobs <n>` | Files processed at once (default half the processors). |
| `--reuse` | Reuse reference renders already in the report directory. |

## What it does

1. **Render.** Broadside renders through the public API (`PdfDocument.Open`, `SoftwareRasterizer`, `RenderOptions { Dpi, Layers = DisplayLayers.All, Intent = DisplayIntent.View }`): the crop box with `/Rotate` and `UserUnit`, white background, the content then the annotations then the form fields (#98). The references are asked for the same: crop box, white background, annotations and form fields drawn from their appearances (PDFium with `draw_annots` and `may_draw_forms` after `init_forms`, pdf.js with `AnnotationMode.ENABLE`, poppler's default). Reference renders kept from a run before annotations were drawn are stale; do not `--reuse` them. Every image is reduced to `GoldenImage` (opaque RGB over white).
2. **Align.** Both images go on a white canvas as large as the larger one (engines round page sizes differently), then the reference is moved by every whole-pixel offset up to `--max-shift`; the offset with the fewest differing pixels wins, the one nearest to none on a tie, but only when it at least halves the differing pixels of the images as they lie (pages that differ everywhere would otherwise be moved just to push a row and a column of differences off the canvas). A non-zero offset is shown in the report: it is itself a finding.
3. **Compare.** `GoldenComparison` under the threshold, with the reference as the golden image: the same YIQ metric, edge/flat tolerances and diff image the golden-image tests use (#120); the harness's source files are compiled into the tool, so there is one metric. A page's per-engine number is the share of pixels beyond tolerance.
4. **Rank.** A page's score is its smallest share over the engines that drew it, so a page ranks high only when Broadside differs from every reference. "References differ" compares the first two references with each other the same way; a page they disagree on is a candidate for a reference-engine bug.
5. **Report.** In `--out`:
   - `index.html`: every compared page worst first; the worst `--report-pages` with Broadside's image, each aligned reference and each diff image (red beyond tolerance, amber within, the reference faded elsewhere).
   - `triage.md`: the worst `--triage-pages` as a Markdown table with empty Verdict and Notes columns (bug, accepted difference, or reference-engine bug), and the pages not compared. Paste it into the pull request and fill it in.
   - `report.json`: everything, including failures (files Broadside cannot open, pages no engine drew, timeouts) and skipped engines.
   - `references/<engine>/<file stem>/page<N>.png`: the reference renders, reused by `--reuse`.

Known reference-engine differences: PDFium and poppler ignore `UserUnit` (pdf.js and Broadside honour it, ISO 32000-2 §14.11.2).

## Tests

`tests/RenderDiff.Tests` drives the tool through `RenderDiffRun` and `RenderDiffCommand` over hand-written corpus files, with fixture reference directories; the poppler, PDFium and pdf.js cases are skipped where those engines are not installed.

## Timing the references (issue #140)

`bench-references.sh [iterations] [dpis] [engines...]` times PDFium and pdf.js (the same installs) over the rendering gate's pinned pages
with `reference/pdfium/bench.py` and `reference/pdfjs/bench.mjs`: open, text, warm render and open-render, one JSON line per case in
`artifacts/reference-bench/<engine>.jsonl`. `docs/benchmarks/phase-3-render-baselines.md` compares them with Broadside's
`RealWorldRenderBenchmarks` and `RealWorldPageBenchmarks`.
