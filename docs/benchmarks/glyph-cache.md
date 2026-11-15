# Software rasterizer glyph cache (issue #370, 2026-11-14)

Hotspot H8 of the [performance deep dive](perf-deep-dive.md): the software rasterizer flattened and scan converted every glyph each
time it was shown. Issue #370 adds a glyph cache in two tiers and asked for three measurements before deciding on the default: hit
rates, time per text tile, and the pixel differences the quantized tier makes. This page records them. The exact tier ships on by
default; the quantized tier is opt-in (`RenderOptions.WithGlyphPositioning(GlyphPositioning.Quantized)`,
`SoftwareRasterizer.GlyphPositioning`) until the owner decides on making it the default.

## Design

- Every filled glyph (text rendering modes 0 to 2, a glyph under 512 pixels either way, an invertible transform) is scan converted in
  its own small mask, with the glyph origin moved to the top-left corner of the pixel it falls in, and swept into runs of coverage.
  The runs are painted moved by whole pixels through the same clip, group hook and compositor as any other fill
  (`ScanlineRasterizer.PaintSpans`).
- The runs are kept per (font program, glyph id, the device matrix's 2x2 part, the origin's offset inside its pixel, coverage rule),
  compared bit for bit, in a per-rasterizer LRU with a byte budget (`SoftwareRasterizer.GlyphCacheBudget`, 4 MiB by default; 0 keeps
  nothing) and counters (`SoftwareRasterizer.GlyphCacheStatistics`). Runs are 8 bytes; an entry is charged its runs plus 96 bytes.
- **Exact tier** (`GlyphPositioning.Exact`, the default): the offset is kept exactly. A mask is a function of its key alone and every
  glyph the cache takes is painted from one, stored or not, so the pixels never depend on the budget or on what was drawn before.
- **Quantized tier** (`GlyphPositioning.Quantized`): the offset is rounded to the nearest quarter pixel horizontally and vertically,
  so a glyph moves by at most 1/8 pixel each way.
- Not cached (filled in place as before): glyphs in the clipping modes 4 to 7, glyphs larger than 512 pixels, non-finite or singular
  transforms, origins beyond 2^30 pixels; Type 3 glyphs are content, not outlines, and are not affected. Strokes are unchanged.
- Thread safety: each rasterizer owns its cache and renders one thing at a time; the viewer's tile workers each have their own
  rasterizer, so nothing is shared. Nested backends (pattern cells, groups, soft masks, overprint previews) share their parent's cache.
- Memory: the runs live in one pooled arena per rasterizer; eviction drops entries to three quarters of the budget and the arena is
  compacted into a pooled buffer when its end is reached. A hit allocates nothing, and once the arena and the table have grown a miss
  allocates nothing either (`RasterizerAllocationTests.Drawing_glyphs_through_the_glyph_cache_allocates_nothing_once_warm`, with the
  default budget, a 16 KB budget that evicts on every render, and none).

## Method

Apple M-series (Arm64), macOS 27, .NET 10, Release, shared with other build agents (times indicative to about 10 %; both sides of each
comparison ran back to back). A scratch harness renders through the public API with the rendering gate's engine (Standard 14 and
predefined CMaps bundled, no system fonts). Pages: all 14 pages of `pdfjs/tracemonkey.pdf`, the synthetic text-heavy page of
`PhoneTileBenchmarks` (10,000 glyphs of an embedded TrueType font), every `tests/Corpus/text*.pdf`, and the CJK and composite-font
files in the local corpora (`pdfjs/vertical.pdf`, `pdfjs/arial_unicode_ab_cidfont.pdf`, `pdfjs/cid_cff.pdf`,
`pdfjs/ArabicCIDTrueType.pdf`, `pdfbox/input/PDFBOX-3833-reduced.pdf`; the local corpora hold no large embedded-CJK document, so the
CJK rows have few glyphs). Tiles: the page fitted to 1206 pixels (an iPhone at 3x), 256-pixel tiles, as `PhoneTileBenchmarks`.

## Hit rates

A lookup is counted per glyph painted from the cache (glyphs another tile culls are not counted). Page: one render at 150 DPI by a
fresh rasterizer. Tiles: every tile of the phone-width page once, by one fresh rasterizer.

| Page | Exact, page | Quantized, page | Exact, tiles | Quantized, tiles | Entries exact / quantized (150 DPI) |
|---|---:|---:|---:|---:|---:|
| tracemonkey, 14 pages (range; mean) | 0 % | 59–90 %; 81 % | 8.5–12.8 %; 10 % | 64–91 %; 84 % | 2,700–6,600 (1.2–3.3 MB) / 650–1,200 (0.34–0.62 MB) |
| text-heavy synthetic (10,000 glyphs) | 92.8 % | 98.4 % | 53.2 % | 94.1 % | 471 / 104 |
| `pdfjs/cid_cff.pdf` (CID CFF) | 0 % | 39 % | 8 % | 32 % | 23 / 14 |
| `pdfjs/ArabicCIDTrueType.pdf` | 0 % | 8.8 % | 21.8 % | 29.9 % | 68 / 62 |
| `pdfjs/vertical.pdf` (vertical CJK) | 0 % | 0 % | 0 % | 11 % | 8 / 8 |
| hand-written `text*.pdf` corpus | 0–40 % | 0–60 % | 0–50 % | 0–75 % | 2–67 |

Within one render of real text, exact positions do not repeat (TeX and most producers place glyphs at arbitrary fractional offsets),
so the exact tier's hits come from drawing the same glyphs again: a glyph straddling two tiles (about 10 % of a tracemonkey tile's
glyphs), a tile or page redrawn at the same zoom (every glyph hits), and aligned text such as the synthetic page. A redraw of a page
after the first is the case it is for: a viewer redrawing tiles after scrolling back, a color filter toggled, a progressive render
restarted.

## Time per text tile

Per 256-pixel tile, minimum of 12 passes over the page's 35 tiles, geometric mean over the 14 tracemonkey pages. "Base" is the commit
before this change. "Cold" is a fresh rasterizer for each pass (the cache starts empty); "warm" is one rasterizer for every pass (the
second pass on hits every glyph it kept).

| Configuration | ms per tile | vs. base |
|---|---:|---:|
| Base (no cache) | 0.503 | 1.00x |
| Cache off (`GlyphCacheBudget = 0`) | 0.573 | 0.88x |
| Exact, cold | 0.566 | 0.89x |
| Quantized, cold | 0.297 | 1.69x |
| Exact, warm | 0.225 | 2.23x |
| Quantized, warm | 0.226 | 2.23x |

Whole pages at 150 DPI (one render of the list into a page bitmap, minimum of 12), tracemonkey: base 7.0–20.7 ms (page 0: 11.9 ms);
exact cold 7.7–18.7 ms (page 0: 13.2 ms, 5–11 % slower than base); quantized cold 3.5–10.9 ms (page 0: 5.0 ms, 2.4x); exact or
quantized warm 0.9–9.0 ms (page 0: 1.7 ms, 6.8x). The hand-written corpus pages hold a few glyphs each and do not move (0.09 ms a
tile either way). The synthetic text-heavy page is dominated by painting its large rectangle glyphs, not by scan converting them:
2.6 ms a tile cached off, 1.9 exact cold, 1.7 quantized, 2.1 / 1.7 warm.

**The cost on a miss.** Scanning a glyph in its own mask, collecting its runs and storing them costs about 5–10 % over drawing it in
place: a render that hits nothing (the exact tier on a first render of real text) is that much slower than before. A glyph straddling
a tile edge is scan converted whole where the old path converted only its part in the tile; the next tile then paints it from the
cache.

## Pixels

### Exact tier against the previous rasterizer

With or without the cache, cold or warm, the exact tier draws the same bytes (`GlyphCacheTests`). Against the rasterizer before this
change it is the same up to rounding: a glyph is now scan converted relative to its origin's pixel instead of in page coordinates, so
the floating-point arithmetic differs in the last bits and a coverage that falls on a rounding boundary can move by one level.

| Set | Identical | Different |
|---|---:|---|
| tracemonkey, 14 pages at 72 DPI, 150 DPI and as phone tiles | 42 / 42 | none |
| `tests/Corpus/text*.pdf`, the synthetic page and the CJK files at 72 DPI, 150 DPI and as phone tiles | 126 / 135 | `text-cff-embedded`, `text-opentype-cff-embedded`, `text-truetype-composite`: 1 pixel by one level at 72 DPI, 1–19 pixels by one level at 150 DPI; the tile renders of the synthetic page, `text-render-modes` and `text-truetype-composite` (glyphs crossing a tile edge are no longer cut by it; magnitude not measured) |
| pdf.js corpus, page 0 at 72 DPI | 942 / 973 | 31 pages: 1–49 pixels each by one level, one pixel by two |

Every golden image test passes unchanged (none re-accepted), as do the Verify rendering snapshots, the pinned real-world fingerprints
and the tile-equals-page tests. Tiles of a page now agree with the whole page exactly for cached glyphs (each glyph's mask does not
depend on the tile).

### Quantized tier against the exact tier

Every page of the text set at 72 and 150 DPI, each pixel's largest channel difference (8-bit), 146.5 million pixels:

| Difference | Pixels | Share |
|---|---:|---:|
| 0 (unchanged) | 141,860,964 | 96.88 % |
| 1 | 356,978 | 0.24 % |
| 2 | 289,975 | 0.20 % |
| 3–4 | 500,292 | 0.34 % |
| 5–8 | 824,390 | 0.56 % |
| 9–16 | 1,168,559 | 0.80 % |
| 17–32 | 1,316,109 | 0.90 % |
| 33–64 | 110,691 | 0.08 % |
| over 64 | 0 | 0 % |

On tracemonkey (dense 10-point text) 9–24 % of a page's pixels change at 72 DPI and 6–15 % at 150 DPI; of those, the mean change is
11–13 levels, the 99th percentile 34–39 and the largest 51–58. The bound is 64 (a glyph moved by at most 1/8 pixel each way sweeps at
most a quarter of any pixel), and nothing measured exceeds 63. The golden images' threshold for the software rasterizer (0.01) is far
below these differences, so making the quantized tier the default means re-accepting every golden image with text.

## Decision for the owner

- Exact (default): no pixel change beyond one-level rounding on a few pixels, 2.2x per tile and 5–7x per page on redraws, about
  5–10 % slower on a first render of real text (no hits within a page).
- Quantized (opt-in): 1.7x per tile and 2.4x per page on a first render, 2.2x / 6–7x on redraws; edge pixels of about 3 % of all
  pixels (up to a quarter of a dense text page's) change by up to 64 levels, mean about 12.
- If quantized becomes the default, the golden images with text need re-accepting; the cache, budget and statistics stay as they are.

Benchmark: `GlyphCacheBenchmarks.Tiles(Page, Cache)` (`NoCache`, `Exact`, `Quantized`; warm, so it measures the hit path and must
allocate nothing).
