# Sample documents: per-page render baseline (#363)

How long the software rasterizer takes to render each page of the representative sample documents in `samples/documents/pdf`. The
set is described in `samples/documents/MANIFEST.md`: a catalogue, two invoices, a textbook chapter, a newsletter, a paper, a
dictionary, a brochure, a novel in three editions and a feature showcase, made by WeasyPrint, headless Chrome, Ghostscript, qpdf and
Broadside. The numbers are the "before" for #362 (decoding images at the resolution they are drawn) and for later rendering work.
They are not a gate: page times on real documents vary too much between runs to fail a pull request on. Re-run them at the end of
each phase.

## Method

- **What is timed.** Each document is opened once and every page is rendered twice at 96 DPI, all layers. Only the second pass is
  timed. The engine has no display-list cache, so the timed pass interprets each page again. Fonts, images and colour spaces stay
  cached, as in a viewer that has already shown the page once. Each time covers building the display list and rasterizing it into a
  new bitmap.
- **Engine.** Standard 14 fonts and predefined CMaps bundled, no system fonts. All the sample documents embed their fonts.
- **Native (JIT and Native AOT).** `bench/Broadside.SampleTimings`. JIT: `dotnet run -c Release --project bench/Broadside.SampleTimings`.
  Native AOT: `dotnet publish bench/Broadside.SampleTimings -c Release --use-current-runtime -o <folder>`, then run the executable.
  Each of them ran four times on an otherwise busy machine (other builds were running). For each document the table keeps the run
  with the smallest total.
- **Browser.** `RenderStats.RenderDocumentPagesAsync` in the Blazor sample does the same thing inside the demo. The test
  `SampleDocumentTests` (`tests/Broadside.Viewer.Blazor.BrowserTests`) calls it for every sample, fails if any page takes longer
  than 20 s, and writes the table to the measurement log. The local column is headless Chromium on the `--no-aot` build, so the IL
  runs in the WebAssembly interpreter. The AOT column comes from CI's `blazor-wasm` job, an `ubuntu-latest` runner with the image's
  Chrome, which runs the test on the AOT build of every pull request. The AOT column is from the `blazor-browser-measurements`
  artifact of CI run 42861231464 (pull request #482's branch, commit ead4ac8, 2026-11-10); the runner is a shared one, so repeat
  runs differ by a few tens of percent on the image-heavy documents.
- **Machine.** 2026-10-25, MacBook Pro, Apple M4 Max (16 cores), macOS 27.0.1, .NET 10.0.13 (Arm64), Playwright's Chromium.
- **Benchmarks.** `bench/Broadside.Benchmarks.Realistic` is the realistic tier. It is a BenchmarkDotNet project outside the regression
  gate, and CI dry-runs it in `bench-dry`. For page 0 and the slowest page of each document it measures `Render` (warm) and
  `OpenAndRender` (cold) at 72 and 150 DPI. Run it with `dotnet run -c Release --project bench/Broadside.Benchmarks.Realistic --
  --filter '*'`.

## Summary

Milliseconds per page: mean / 95th percentile / slowest page.

| Document | Pages | JIT mean / p95 / max | Native AOT mean / p95 / max | Browser, interpreter: mean / p95 / max | Browser, AOT (CI): mean / p95 / max |
|---|---:|---|---|---|---|
| `brochure.pdf` | 4 | 71.1 / 130.5 / 130.5 | 52.5 / 106.0 / 106.0 | 1425.2 / 2912.0 / 2912.0 | 345.3 / 714.3 / 714.3 |
| `catalogue.pdf` | 11 | 108.8 / 481.8 / 481.8 | 111.2 / 456.7 / 456.7 | 3089.1 / 14222.3 / 14222.3 | 520.4 / 1910.0 / 1910.0 |
| `dictionary.pdf` | 17 | 12.0 / 13.1 / 13.1 | 15.1 / 16.7 / 16.7 | 232.2 / 262.8 / 262.8 | 48.0 / 54.2 / 54.2 |
| `invoice-colourful.pdf` | 2 | 18.9 / 25.7 / 25.7 | 23.2 / 31.0 / 31.0 | 549.7 / 702.5 / 702.5 | 53.0 / 66.4 / 66.4 |
| `invoice-conservative-filled.pdf` | 3 | 3.9 / 5.6 / 5.6 | 3.7 / 6.3 / 6.3 | 57.0 / 99.7 / 99.7 | 12.5 / 22.0 / 22.0 |
| `invoice-conservative.pdf` | 3 | 3.1 / 5.6 / 5.6 | 3.7 / 6.5 / 6.5 | 55.7 / 99.1 / 99.1 | 11.9 / 21.5 / 21.5 |
| `newsletter.pdf` | 4 | 72.7 / 95.7 / 95.7 | 81.9 / 109.5 / 109.5 | 2240.9 / 2950.4 / 2950.4 | 259.7 / 337.8 / 337.8 |
| `novel-hardback.pdf` | 180 | 4.7 / 5.9 / 8.0 | 5.8 / 7.1 / 7.5 | 80.9 / 99.6 / 102.0 | 17.9 / 22.3 / 28.5 |
| `novel-large-print.pdf` | 219 | 3.0 / 3.4 / 3.7 | 3.8 / 4.4 / 4.6 | 55.2 / 63.8 / 66.0 | 12.9 / 15.0 / 15.7 |
| `novel-pocket.pdf` | 170 | 4.5 / 5.1 / 5.4 | 5.6 / 6.4 / 6.6 | 80.4 / 91.8 / 94.4 | 17.7 / 20.2 / 24.1 |
| `paper-ghostscript.pdf` | 2 | 6.8 / 7.5 / 7.5 | 9.2 / 10.2 / 10.2 | 146.7 / 163.8 / 163.8 | 30.3 / 34.2 / 34.2 |
| `paper.pdf` | 2 | 7.1 / 7.8 / 7.8 | 9.4 / 10.5 / 10.5 | 157.5 / 177.1 / 177.1 | 31.4 / 35.6 / 35.6 |
| `showcase.pdf` | 5 | 6.9 / 18.8 / 18.8 | 7.9 / 22.1 / 22.1 | 153.0 / 486.4 / 486.4 | 18.7 / 44.5 / 44.5 |
| `textbook-linearized.pdf` | 8 | 6.7 / 22.1 / 22.1 | 7.0 / 19.3 / 19.3 | 114.8 / 423.7 / 423.7 | 18.6 / 32.4 / 32.4 |
| `textbook.pdf` | 8 | 6.7 / 22.0 / 22.0 | 7.0 / 19.3 / 19.3 | 117.8 / 436.3 / 436.3 | 18.5 / 32.1 / 32.1 |

## What stands out

- **Images dominate.** The catalogue's small-prints page (page 10) is the slowest page in the set: about 460 ms native and 14 s in
  the browser interpreter. It draws nine photographs of 2 to 4 MP as thumbnails about 50 mm wide, and every one is decoded at full
  resolution first. That is #362's problem, and this page is the one to watch. The full-bleed pages of the catalogue (60 to 110 ms)
  and the newsletter's photographs (70 to 110 ms) come next. The brochure's third page adds an image with an SMask under sixteen
  blend modes.
- **Text is cheap and even.** All three editions of the novel render at 3 to 6 ms a page, with a p95 within a millisecond or two of
  the median. The dictionary's dense 6.9 pt pages, with three columns and six fonts, take 12 to 15 ms. The PDF/UA large-print
  edition is the fastest per page: it has the fewest glyphs.
- **The JIT is ahead of Native AOT on text.** Native AOT is 20 to 30% slower on text-heavy pages here: the timed pass runs on
  the JIT's tiered, profile-guided code for the rasterizer's hot loops. On the image pages the two are within a few percent, and on
  the brochure Native AOT is faster (53 against 71 ms a page). That one has not been looked into.
- **The interpreter costs 15 to 30 times native.** That is why the demo ships AOT-compiled, and why `--no-aot` builds are only for
  hosts with a per-file size limit.
- **Producers barely matter for speed.** The Ghostscript redistillation of the paper renders like Chrome's original, and the
  linearized textbook like the plain one.

## Results

### JIT

| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |
|---|---:|---:|---:|---:|---:|---|
| `brochure.pdf` | 4 | 284 | 71.1 | 21.5 | 130.5 | 130.5 (page 1) |
| `catalogue.pdf` | 11 | 1197 | 108.8 | 65.8 | 481.8 | 481.8 (page 10) |
| `dictionary.pdf` | 17 | 205 | 12.0 | 12.2 | 13.1 | 13.1 (page 7) |
| `invoice-colourful.pdf` | 2 | 38 | 18.9 | 12.2 | 25.7 | 25.7 (page 1) |
| `invoice-conservative-filled.pdf` | 3 | 12 | 3.9 | 3.8 | 5.6 | 5.6 (page 1) |
| `invoice-conservative.pdf` | 3 | 9 | 3.1 | 2.8 | 5.6 | 5.6 (page 1) |
| `newsletter.pdf` | 4 | 291 | 72.7 | 50.9 | 95.7 | 95.7 (page 4) |
| `novel-hardback.pdf` | 180 | 853 | 4.7 | 5.3 | 5.9 | 8.0 (page 125) |
| `novel-large-print.pdf` | 219 | 658 | 3.0 | 3.1 | 3.4 | 3.7 (page 138) |
| `novel-pocket.pdf` | 170 | 757 | 4.5 | 4.7 | 5.1 | 5.4 (page 68) |
| `paper-ghostscript.pdf` | 2 | 14 | 6.8 | 6.1 | 7.5 | 7.5 (page 2) |
| `paper.pdf` | 2 | 14 | 7.1 | 6.4 | 7.8 | 7.8 (page 2) |
| `showcase.pdf` | 5 | 35 | 6.9 | 2.9 | 18.8 | 18.8 (page 1) |
| `textbook-linearized.pdf` | 8 | 53 | 6.7 | 3.9 | 22.1 | 22.1 (page 7) |
| `textbook.pdf` | 8 | 54 | 6.7 | 3.9 | 22.0 | 22.0 (page 7) |

### Native AOT

| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |
|---|---:|---:|---:|---:|---:|---|
| `brochure.pdf` | 4 | 210 | 52.5 | 5.5 | 106.0 | 106.0 (page 3) |
| `catalogue.pdf` | 11 | 1223 | 111.2 | 70.6 | 456.7 | 456.7 (page 10) |
| `dictionary.pdf` | 17 | 256 | 15.1 | 15.4 | 16.7 | 16.7 (page 8) |
| `invoice-colourful.pdf` | 2 | 46 | 23.2 | 15.3 | 31.0 | 31.0 (page 1) |
| `invoice-conservative-filled.pdf` | 3 | 11 | 3.7 | 3.3 | 6.3 | 6.3 (page 1) |
| `invoice-conservative.pdf` | 3 | 11 | 3.7 | 3.3 | 6.5 | 6.5 (page 1) |
| `newsletter.pdf` | 4 | 328 | 81.9 | 59.9 | 109.5 | 109.5 (page 4) |
| `novel-hardback.pdf` | 180 | 1037 | 5.8 | 6.5 | 7.1 | 7.5 (page 62) |
| `novel-large-print.pdf` | 219 | 837 | 3.8 | 3.9 | 4.4 | 4.6 (page 38) |
| `novel-pocket.pdf` | 170 | 944 | 5.6 | 5.9 | 6.4 | 6.6 (page 147) |
| `paper-ghostscript.pdf` | 2 | 18 | 9.2 | 8.1 | 10.2 | 10.2 (page 2) |
| `paper.pdf` | 2 | 19 | 9.4 | 8.4 | 10.5 | 10.5 (page 2) |
| `showcase.pdf` | 5 | 39 | 7.9 | 3.4 | 22.1 | 22.1 (page 1) |
| `textbook-linearized.pdf` | 8 | 56 | 7.0 | 4.7 | 19.3 | 19.3 (page 7) |
| `textbook.pdf` | 8 | 56 | 7.0 | 4.9 | 19.3 | 19.3 (page 7) |

### Browser, WebAssembly interpreter (`--no-aot`, local)

| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |
|---|---:|---:|---:|---:|---:|---|
| `brochure.pdf` | 4 | 5701 | 1425.2 | 120.2 | 2912.0 | 2912.0 (page 3) |
| `catalogue.pdf` | 11 | 33980 | 3089.1 | 1884.6 | 14222.3 | 14222.3 (page 10) |
| `dictionary.pdf` | 17 | 3948 | 232.2 | 235.7 | 262.8 | 262.8 (page 8) |
| `invoice-colourful.pdf` | 2 | 1099 | 549.7 | 396.8 | 702.5 | 702.5 (page 1) |
| `invoice-conservative-filled.pdf` | 3 | 171 | 57.0 | 49.8 | 99.7 | 99.7 (page 1) |
| `invoice-conservative.pdf` | 3 | 167 | 55.7 | 52.2 | 99.1 | 99.1 (page 1) |
| `newsletter.pdf` | 4 | 8963 | 2240.9 | 1623.2 | 2950.4 | 2950.4 (page 4) |
| `novel-hardback.pdf` | 180 | 14563 | 80.9 | 91.3 | 99.6 | 102.0 (page 129) |
| `novel-large-print.pdf` | 219 | 12079 | 55.2 | 57.0 | 63.8 | 66.0 (page 181) |
| `novel-pocket.pdf` | 170 | 13667 | 80.4 | 84.2 | 91.8 | 94.4 (page 119) |
| `paper-ghostscript.pdf` | 2 | 293 | 146.7 | 129.6 | 163.8 | 163.8 (page 2) |
| `paper.pdf` | 2 | 315 | 157.5 | 137.8 | 177.1 | 177.1 (page 2) |
| `showcase.pdf` | 5 | 765 | 153.0 | 55.1 | 486.4 | 486.4 (page 1) |
| `textbook-linearized.pdf` | 8 | 919 | 114.8 | 65.3 | 423.7 | 423.7 (page 7) |
| `textbook.pdf` | 8 | 943 | 117.8 | 68.3 | 436.3 | 436.3 (page 7) |

### Browser, WebAssembly AOT (CI, `ubuntu-24.04`, Chrome)

| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |
|---|---:|---:|---:|---:|---:|---|
| `brochure.pdf` | 4 | 1381 | 345.3 | 47.0 | 714.3 | 714.3 (page 3) |
| `catalogue.pdf` | 11 | 5725 | 520.4 | 387.9 | 1910.0 | 1910.0 (page 10) |
| `dictionary.pdf` | 17 | 817 | 48.0 | 48.6 | 54.2 | 54.2 (page 11) |
| `invoice-colourful.pdf` | 2 | 106 | 53.0 | 39.5 | 66.4 | 66.4 (page 1) |
| `invoice-conservative-filled.pdf` | 3 | 38 | 12.5 | 11.1 | 22.0 | 22.0 (page 1) |
| `invoice-conservative.pdf` | 3 | 36 | 11.9 | 10.8 | 21.5 | 21.5 (page 1) |
| `newsletter.pdf` | 4 | 1039 | 259.7 | 191.8 | 337.8 | 337.8 (page 4) |
| `novel-hardback.pdf` | 180 | 3218 | 17.9 | 20.0 | 22.3 | 28.5 (page 174) |
| `novel-large-print.pdf` | 219 | 2833 | 12.9 | 13.3 | 15.0 | 15.7 (page 207) |
| `novel-pocket.pdf` | 170 | 3003 | 17.7 | 18.5 | 20.2 | 24.1 (page 119) |
| `paper-ghostscript.pdf` | 2 | 61 | 30.3 | 26.3 | 34.2 | 34.2 (page 2) |
| `paper.pdf` | 2 | 63 | 31.4 | 27.2 | 35.6 | 35.6 (page 2) |
| `showcase.pdf` | 5 | 94 | 18.7 | 10.4 | 44.5 | 44.5 (page 1) |
| `textbook-linearized.pdf` | 8 | 149 | 18.6 | 14.9 | 32.4 | 32.4 (page 7) |
| `textbook.pdf` | 8 | 148 | 18.5 | 14.8 | 32.1 | 32.1 (page 7) |
