# Stroke union speed

Issue #335: #310 made the software rasterizer paint each stroke as the union of its outline pieces (ISO 32000-2 §8.5.3.1), so that overlapping anti-aliased edges are not added up. Each edge piece is kept per pixel, and each pixel row is walked on 4 sample lines in order of position, clamping the winding to the line's height. That was correct and about 2x slower: the stroke-heavy page went from 43 to 88 ms at 72 DPI and `paths-strokes.pdf` at 300 DPI from 0.56 to 1.0 ms. The issue asked for the speed back with the pixels unchanged.

## What did not work

- **A fast path for strokes that cannot overlap** (one segment, no dashes; item 5 of [perf-deep-dive.md](perf-deep-dive.md)), sending them through the exact-area cells instead of the walk. It changes pixels. The cells and the walk compute the same area in exact arithmetic but round differently: the walk stores each piece in `float` relative to its pixel and sums per sample line. With that path, 191 of 328 test renders differed from the current output by a few pixels each (the random stroke pages described under "Checks" below). It also gained little, about 10 % on the stroke-heavy page at 72 DPI, because single-segment strokes are a small share of its edge pixels. The two attempts the issue mentions likely failed for the same reasons.
- **Dropping the union.** Without it, every one of the 328 renders changed, because ordinary joins and caps overlap.

So the walk stays, and it is made cheaper without changing a single bit of its arithmetic.

## What changed (`ScanlineRasterizer`)

Profile of the base at 72 DPI (EventPipe, stroke-heavy page): the walk `Resolve` took 62 % of the time. The sort of the pieces took 21 % of that, and the walk itself about 38 %. Pixels with one edge are 72 % of edge pixels at 72 DPI and 86 % at 300 DPI. Pixels with two edges are 18 % and 11 %.

1. **Sort indices, not pieces.** The comparison sort ran over the keys with the 16-byte pieces. It now runs over the keys with 4-byte indices. A sort compares only keys, so the order is the same.
2. **The walk state in registers.** Each sample line's winding and clamped coverage were two 4-element stack arrays, read and written for every crossing. They are now eight locals. The one-edge and two-edge cases are unrolled per line. The two-edge case orders its crossings as `Span.Sort` would (NaN first, then a swap only when strictly greater). Three or more crossings use a copy of `Span.Sort`'s small-array algorithm: a compare-and-swap network for 3, insertion sort up to 16, and `Span.Sort` itself beyond that or with a NaN. Every addition happens in the same order as before.
3. **Radix sort for shapes of 64 pieces or more.** A stable least-significant-digit radix sort runs over the pixel's row and column relative to the shape's bounds, in 8-bit passes, usually 2 or 3. Pieces in different pixels resolve the same in any order. Within one pixel, the order of its pieces matters only when two of them cross a sample line at exactly the same position as different crossings: then the order of the additions can change the last bit. So the radix order is checked rather than trusted. Such a tie is walked in every order (up to 4 tied crossings, Heap's algorithm), and all orders must end bit for bit the same. If any differs, or a tie is larger, or a crossing is NaN, the shape is resolved again from the comparison sort, the order it always had. On the stroke-heavy page that fallback runs for 0.2 % of shapes at 72 DPI and 0.05 % at 300 DPI.

Working arrays are rented from `ArrayPool` on first use and kept, so a warm rasterizer allocates nothing per stroke (`RasterizerAllocationTests.Stroking_allocates_nothing_per_segment_once_warm` passes). `ScanlineRasterizer` has 5 more reference fields (40 bytes per instance). Benchmarks that build a rasterizer per call (`FirstPaint`) may move by that much in the allocation gate.

## Checks

- Pixels: 328 renders compared by SHA-256 against the base build: the 64 KB stroke-heavy page, `paths-strokes.pdf`, and 80 random pages of 150 strokes each, at 72, 96, 150 and 300 DPI. The random pages mix every cap, join and miter limit, widths 0 to 20, stroke adjustment, dashes, transforms, constant alpha, and lines on whole, half and quarter pixels. All 328 are identical. The golden images, the cross-platform render hashes and `RasterizerAgreementTests` pass unchanged.
- Cells: in a temporary build, every shape that took the radix path was also resolved through the comparison sort, and the two sets of cells (keys, cover and area) were compared bit for bit. There were 0 mismatches over 32,066 shapes on the random pages and 144,550 on the benchmark pages.
- `StrokeRenderingTests.Strokes_paint_the_same_pixels_however_their_pieces_are_sorted` pins the SHA-256 of the stroke-heavy page and of a page of strokes whose pieces meet at the same positions, at 72 and 300 DPI. The hashes were recorded from the base build.

## Results

`StrokeBenchmarks.Replay`, `--job short-inprocess`, Release, Apple M4 Max, .NET SDK 10.0.402. Base is `sweep/7b` (577eca6b) and head is this branch, run interleaved as base, head, base, head. Other build agents shared the machine (load average 60 to 80), so the times are indicative. `Allocated` shows a few hundred bytes of in-process toolchain noise on both sides. The steady-state gate measures 0 for these cases.

| Case | Base (round 1 / 2) | Head (round 1 / 2) | Change |
|---|---:|---:|---:|
| stroke-heavy, 72 DPI | 89.8 / 91.8 ms | 60.2 / 61.4 ms | −33 % |
| stroke-heavy, 300 DPI | 369.9 / 367.2 ms | 247.9 / 251.1 ms | −32 % |
| `paths-strokes.pdf`, 72 DPI | 218.8 / 224.2 µs | 181.2 / 178.5 µs | −19 % |
| `paths-strokes.pdf`, 300 DPI | 1,033 / 1,066 µs | 751.5 / 752.7 µs | −28 % |

For scale, the same page with the union turned off (the pixels before #310, measured as process CPU time in a separate harness under the same load) took about 47 ms at 72 DPI and 250 ms at 300 DPI, against about 97 and 375 ms for the base. The head is at 63 ms at 72 DPI, which recovers about two thirds of what #310 cost there. At 300 DPI, where the sort dominated, it is level with the no-union figure. Most of what remains at 72 DPI is the walk over 4 sample lines per edge pixel. That walk is the price of exact unions, and its additions cannot be reordered or skipped without changing the low bits of the coverage.
