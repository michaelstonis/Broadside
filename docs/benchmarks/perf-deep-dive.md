# Rendering performance deep dive (2026-10-19)

The owner tested the MAUI viewer (#226, Skia backend) and the Blazor demo (#338, software rasterizer / WebGL #353): output accurate, Android
acceptable, iOS "terribly slow" (Release and Debug alike), scrolling interrupted on every shell. Moving work off the UI thread is #357's;
this is about the work itself: content interpretation, display-list build, image decode and colour conversion, the software rasterizer
and the Skia backend. Every change below keeps the output bit for bit (one intended exception on Skia, below) and allocates nothing new
per tile, glyph or pixel.

## Method

- Machine: Apple M4 Max (Arm64), macOS 27, .NET 10.0.12, shared with other build agents (load average 14–25 throughout), so
  times are indicative to 10–20 %; every comparison ran both sides back to back on the same machine.
- Profiles: `dotnet-trace` (sampled thread time, Release, tiered compilation on and off) over a scratch harness that renders a page,
  every 256-pixel tile of a cached display list (as `TileScheduler` asks), or the 21 pinned pages of the rendering gate
  (`tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt`) in a loop; percentages are of samples on the render thread.
- **Phone case** (asked for explicitly: separate "the work is too heavy" from "presentation is slow"): the page fitted to an iPhone's
  width at 3x (1206 device pixels), 256-pixel tiles, `SkiaBackend.Render(list, canvas, tileTransform)` as `SkiaTileBackend` calls it,
  in a **NativeAOT osx-arm64** build (the closest desktop stand-in for iOS's AOT; a phone core is roughly 3–5x slower than an M4 core).
- Before/after: `bench/Broadside.Benchmarks`, `--job short-inprocess`, base `origin/spec/33-broadside-v1` (824ba39e) against this branch.
- Correctness: besides the golden and snapshot tests, page 0 of every pdf.js corpus file (982 files) rendered at 72 DPI on both
  sides, SHA-256 of the pixels compared: software rasterizer 982/982 identical; Skia 980/982 identical (the two differences are the
  intended one below); the Ghent CMYK patch suite (27 files) identical.

## Phone case: Skia tile cost, NativeAOT Arm64

Per 256-pixel tile, page 1206 px wide, warm (the display list and the backend's caches already built). Display-list build is in a
fresh document (it includes decoding the page's images).

| Page | Tiles | Display-list build | Per tile, before | Per tile, after | Pass over the page, before → after |
|---|---:|---:|---:|---:|---:|
| `issue12841_reduced.pdf` (DCT photo 5280x3792) | 35 | 158 ms | 254 ms | 0.26 ms | 8.9 s → 9 ms |
| `tracemonkey.pdf` p1 (embedded Type 1 text) | 35 | 3 ms | 1.73 ms | 0.36 ms | 61 ms → 13 ms |
| `issue5567.pdf` (JPX 1500x1125) | 20 | 517 → 486 ms | 0.23 ms | 0.03 ms | 4.6 ms → 0.6 ms |
| `cmykjpeg.pdf` (DCT CMYK) | 35 | 0.3 ms | 0.01 ms | 0.01 ms | — |

What it says: on image pages the Skia work was far too heavy (a quarter of a second per tile); after the fixes no page here costs
more than ~1 ms per tile on an M4 core, a few ms on a phone core, so **text and vector pages that still feel slow on iOS are a
presentation problem** (OpenGL ES surface, UI-thread work: #357), not render cost. What remains heavy is the one-off build of a page
with a large JPX image (520 ms on the M4, seconds on a phone), which #357 should keep off the UI thread and which is item 1 of the plan.

## Hotspots found

| # | Where | Share | Root cause |
|---|---|---:|---|
| H1 | Skia tiles of an image page: `ImageConverter.Convert` | 96 % of a tile pass (`ColorPipeline` 85 %) | An image larger than the image cache budget (64 MB; a 20-megapixel photo is 80 MB as Skia pixels) was never kept, so **every tile converted the whole image again** (253 ms per tile). |
| H2 | Skia tiles of any page: `DisplayList.Replay` | 92–95 % in glyph fills on text pages | `SkiaBackend.Render(list, canvas, …)` **replayed the whole list for every tile**; the software rasterizer already culls to the tile (`Replay(backend, region)`). |
| H3 | Skia tiles of an image page: `ImageCache.Entry.Admit` | — | Resamplings are keyed by the full image-to-canvas transform, which includes each tile's offset, so **the first four tiles each resampled the whole image** and the rest fell back to Skia's sampling. |
| H4 | Colour: `ManagedPipeline` → `DeviceConverter` for DeviceRGB images | 85 % of H1's conversion | 8-bit DeviceRGB samples went byte → float → clip → byte per component: an identity at ~100 ns a pixel. |
| H5 | Colour: `CmykCharacterization.ToRgb` + `Dot` | ~25 % of the pinned pages at 150 DPI | The SWOP polynomial (34 terms x 3) through a stackalloc'd term buffer per colour, once per pixel of every CMYK group result, CMYK image and shading. |
| H6 | `SoftwareRasterizer.ConvertResult` (group results to RGB) | 32 % inclusive of the pinned pages | Every pixel of a CMYK (or ICC) group converted on its own, though group results are mostly flat runs. |
| H7 | `MeshLayer.FillTriangle` | 13 % of the pinned pages | Per-pixel branches on the bounding box and the colour table. |
| H8 | Software text: `AddPath` (cubic flattening, `AddLine`), cell sort | 49 % + 17 % of a text page's tiles | No glyph cache: every glyph is flattened and scan converted at every show; glyphs of a run outside the tile were still converted. |
| H9 | JPX tier-1 (`JpxCodeBlockDecoder` passes) | 75 % of `issue5567.pdf`'s 500 ms build | A straightforward EBCOT: every pass visits every sample of the block and reads eight neighbours for each context. |
| H10 | Skia: `personwithdog.pdf` (Coons mesh, 62 ms), `tiling-pattern-large-steps.pdf` (35 ms, 4x the software rasterizer) | native Skia time | Mesh triangles drawn as vertices in a layer; a large tiling cell drawn through a picture shader. |
| H11 | Function-based shading (`PostScriptEvaluator`) | 8 % of the pinned pages | A Type 4 function evaluated per pixel. |

## Fixes made

| Fix | Hotspot | Benchmark | Before → after |
|---|---|---|---:|
| Skia keeps the two images larger than the budget used last, outside it (as `ImageSource` keeps its last image); budget 0 still keeps nothing | H1 | `PhoneTileBenchmarks.Tiles(image-over-budget, Skia)` | 470 ms → 1.0 ms (465x) |
| Skia list renders replay only the paints that reach the canvas's clip (`TryGetRegion`, 2-pixel margin as the rasterizer) | H2 | `PhoneTileBenchmarks.Tiles(text-heavy, Skia)` | 91.6 ms → 26.8 ms (3.4x) |
| Image resamplings shared across whole-pixel offsets (each tile reuses the page's), always made for the slot's own transform so the pixels never depend on the budget | H3 | AOT phone case `issue5567.pdf` | 0.24 → 0.03 ms a tile |
| 8-bit DeviceRGB → RGB and DeviceCMYK → CMYK copied (`ManagedPipeline`, `DeviceConverter.IsIdentity`) | H4 | `ColorConversionBenchmarks.ConvertBytes(Rgb)`, `(RgbTransfer)` | 9.36 → 0.04 ms (234x); 10.3 → 1.0 ms |
| CMYK characterisation unrolled with the same terms and sums in the same order (verified bit-identical over 20 M colours, tiering on and off); runs of one colour converted once | H5 | `ColorConversionBenchmarks.ConvertBytes(Cmyk)`, `ConvertFloats(Cmyk)`, `ConvertFloats(Separation)` | 54.9 → 28.6 ms; 59.9 → 24.6 ms; 87.9 → 50.7 ms |
| Group results: each run of equal pixels converted and packed once | H6 | `GroupRenderingBenchmarks.Replay(72/300)` | 7.9 → 5.7 ms; 126 → 94 ms |
| Mesh triangles: the common case (no box) specialised, with and without a colour table | H7 | `MeshRenderingBenchmarks.Replay(72)` | 9.3 → 8.6 ms |
| A filled glyph wholly outside the clip is skipped (software and Skia) | H8 (partly) | `PhoneTileBenchmarks.Tiles(text-heavy, Software)` | 83.7 → 79.6 ms |
| JPX: neighbour-significance flag per sample (skips the eight-neighbour read where the context is 0), magnitudes no longer cleared per block | H9 (partly) | `JpxBenchmarks.Decode(*)`; `issue5567.pdf` build | 1.01–1.12x; 506 → 481 ms |

Whole pinned set (21 pages, 150 DPI, minimum of ≥ 33 runs each, display-list cache off so each render interprets again):

| Backend | Sum of pages, before → after | Geometric mean | Biggest pages |
|---|---:|---:|---|
| Software rasterizer | 493 → 277 ms (-44 %) | 2.99 → 2.44 ms (-18 %) | Ghent CMYK test page 192 → 88 ms; `personwithdog.pdf` 162 → 67 ms; GWG160 blend modes 11.4 → 4.2 ms |
| Skia | 344 → 230 ms (-33 %) | 1.61 → 1.46 ms (-9 %) | Ghent CMYK test page 196 → 90 ms; GWG160 11.9 → 4.3 ms |

Against #140's PDFium figures this moves the 150 DPI geometric mean from about 1.7x towards 1.4x PDFium on the software rasterizer
(the two worst pages, both CMYK, halved); the remaining gap is text (H8) and shadings (H10, H11). #336 stays open for that.

**The one intended output change** (Skia only): an image drawn several times at whole-pixel offsets of one another now shares one
resampling. Before, the first four positions each made their own and the fifth onwards fell back to Skia's sampling, so copies of one
image could differ by a pixel row; now they are identical. It changes `issue16263.pdf` (one image drawn 40 times through a form) and
`issue269_2.pdf` (images drawn twice) in the pdf.js corpus; no golden image changes. The shared resampling is the one made for the
first position the backend drew the image at, so where a pixel centre falls exactly on a sample boundary the tie resolves as it did
there: tiles of a page agree with each other and, when the backend drew the page first, with the page (`SkiaTileTests`); a tile drawn
first by a fresh backend can resolve such a tie one sample over. Skia's own edge and glyph anti-aliasing already differs by a level or
two along tile seams, before and after this change.

## Ranked plan for what is left

1. **JPX tier-1 rewrite** (H9; #352). The build of a page with a large JPX image is 0.5 s on an M4, several seconds on a phone, and it
   is one operation (#97). Stripe-column state words with pre-propagated neighbour significance (OpenJPEG's T1 layout), MQ state in
   locals per pass, and decoding only the resolution levels the display needs (a reduced-resolution decode for a page shown at phone
   size skips the top levels entirely, often 4x fewer samples). Expected 3–5x; exact, verifiable by the decoder's corpus hashes.
2. **Software glyph coverage cache** (H8). The largest remaining cost of software text (6 µs a glyph, 3x Skia): cache each glyph's
   coverage mask per (outline, 2x2 matrix, sub-pixel offset quantised to 1/4 pixel), as PDFium and pdf.js do. Needs an owner decision:
   quantising the offset changes text pixels slightly (golden images re-accepted), in exchange for roughly 5x on text pages and tiles.
3. **Downsampled image levels at decode** (#352): decode DCT at 1/2, 1/4, 1/8 scale (the IDCT's DC/low-frequency shortcut) when the
   page is shown far below the image's resolution, so a 20-megapixel photo on a phone never converts 20 M pixels. Changes pixels
   (averaging before conversion), so it needs a threshold and owner sign-off.
4. **Skia native time** (H10): draw Coons/tensor meshes without a layer when the group allows it; record large tiling cells at device
   resolution once instead of through a picture shader each draw.
5. **Strokes** (#335): the union sampling of #310 doubles stroking. An exact fast path when a stroke's pieces cannot overlap (one segment,
   butt caps, no dashes) would recover most of it on stroke-heavy pages without changing pixels.
6. **Function-based shadings** (H11): evaluate the function on a grid and interpolate where the function is smooth (pdf.js does), behind
   an accuracy bound.

Run the harness numbers again at each phase end (owner tracks the trend): `PhoneTileBenchmarks`, `ColorConversionBenchmarks`,
`GroupRenderingBenchmarks`, and #140's `RealWorldRenderBenchmarks` with the real-world corpus.
