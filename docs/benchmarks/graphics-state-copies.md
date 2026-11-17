# Graphics-state copies

Issue #257 asked how much it costs to copy the graphics state on `q` (ISO 32000-2 §8.4.2) and for each Type 3 glyph (§9.6.4). The state holds two inline colors of up to 32 floats each. The issue asked to store them compactly or share the rarely changed parts only if the cost shows on the pinned real-world subset, and to record the numbers either way.

## What is measured

`bench/Broadside.Benchmarks/GraphicsStateBenchmarks.cs`:

- **`CopyState`**: one `GraphicsState` copy with both colors set, nothing else (per copy, `OperationsPerInvoke` 1,024).
- **`SaveRestore`**: a page made only of nested `q q q q Q Q Q Q` (20,000 `q`), interpreted by a processor that asks for no state events. The figure is per `q` plus its `Q`, including reading both operators.

The per-glyph cost is `Type3GlyphBenchmarks.RunGlyphDescriptions`: 4,000 Type 3 glyphs, each run as a nested content stream. The real-world effect is `ContentInterpretationBenchmarks.InterpretWarm` over `content-subset.json`.

## Size and share

`GraphicsState` is 816 bytes. Each of its two `PdfColor` values is 208 bytes (32 inline floats, the space, the pattern and the pattern matrix), so the colors are 416 bytes of the 816.

`q` per operator on the subset (every page listed, nested streams included):

| File | Operators | `q` | Share |
|---|---:|---:|---:|
| image-heavy: `pdfjs/bug1799927.pdf` | 9,160 | 2,158 | 23.6 % |
| inline-image-heavy: `qpdf/inline-images.pdf` | 19,898 | 3,152 | 15.8 % |
| tiling-pattern: `pdfjs/tiling_patterns_variations.pdf` | 82 | 9 | 11.0 % |
| shading-pattern: `pdfjs/bug1721218_reduced.pdf` | 162,601 | 14,320 | 8.8 % |
| text-heavy: `pdfjs/tracemonkey.pdf` | 34,417 | 1,201 | 3.5 % |
| page-count: `pdfjs/freeculture.pdf` | 148,475 | 1,134 | 0.8 % |
| cjk: `pdfjs/issue11913.pdf` | 163 | 1 | 0.6 % |
| vector-heavy: `pdfjs/issue12810.pdf` | 222,464 | 3 | 0.0 % |

## Results

All runs use `--job short-inprocess`, Release, Apple M4 Max, .NET SDK 10.0.402. The machine was shared with other build agents (load average 50 to 65), so the times are indicative. The base is `sweep/7b` plus the new benchmark class, and the head is this branch. They ran interleaved as base, head, base, head. Allocations are exact. The few hundred bytes some real-world rows show are the in-process toolchain's noise averaged over a handful of calls, not per-operator allocations, and they appear on both sides.

| Benchmark | Base (round 1 / 2) | Head (round 1 / 2) |
|---|---:|---:|
| `CopyState` | 24.6 / 22.1 ns | 22.8 / 22.2 ns |
| `SaveRestore` (one `q` + `Q`) | 83.6 / 82.5 ns | 86.8 / 83.1 ns |
| `RunGlyphDescriptions` (4,000 Type 3 glyphs) | 4.51 / 4.48 ms | 3.84 / 3.64 ms |
| `InterpretWarm` image-heavy | 3,023 / 2,963 μs | 2,957 / 2,824 μs |
| `InterpretWarm` inline-image-heavy | 7,093 / 6,275 μs | 6,295 / 6,155 μs |
| `InterpretWarm` shading-pattern | 35,727 / 33,059 μs | 33,268 / 34,344 μs |
| `InterpretWarm` text-heavy | 13,118 / 12,743 μs | 12,360 / 13,254 μs |
| `InterpretWarm` page-count | 44,298 / 37,948 μs | 37,813 / 37,909 μs |
| `InterpretWarm` vector-heavy | 31,187 / 33,340 μs | 31,642 / 32,038 μs |

## Decision

**The `q` copy itself was not changed.** A copy costs about 23 ns, roughly a quarter of an 83 ns `q`/`Q` pair. On the real-world pages that is small:

- shading-pattern: 14,320 copies, about 0.33 ms of about 33 ms (1 %).
- image-heavy: 2,158 copies, about 50 μs of about 2.9 ms (1.7 %).
- inline-image-heavy: 3,152 copies, about 72 μs of about 6.2 ms (1.2 %).

The other rows are below 0.5 %. Storing colors by component count would at best halve this (the colors are half the bytes), so it would gain under 1 % on the most q-heavy page. It would also cost something that `PdfColor`'s inline storage avoids: DeviceN colors with more than a few components would need a pooled or shared side store, and that store must stay valid for every copy of the state a processor keeps. Sharing the rarely changed parameters (`ref`-counted blocks, copy on write) would add an indirection to every read of them on the paint path. Neither is worth about 1 %. The issue allows recording that instead of changing.

**Nested runs were made cheaper.** Forms, Type 3 glyphs, pattern cells and soft-mask groups were copying the state about ten times per run where three are needed:

- the `NestedRun` description carried the state as a property, so each read copied it;
- the run frame was built and then added to the list, copying it twice more;
- the recursion guard enumerated the frames by value, copying a whole frame for each running stream.

The initial state now goes to `RunNested` by `in` reference, and frames are grown in place and only reached by reference. What remains is one copy into the caller's local, one onto the stack and one into the frame's start state. With this change the Type 3 benchmark is about 17 % faster (4.49 to 3.74 ms, about 1.12 to 0.94 μs per glyph). The real-world rows stay within the noise, because `InterpretWarm`'s subset runs few forms and no Type 3 descriptions. Nothing allocates. The `q`/`Q` hot path is untouched, and `ContentAllocationTests.Saving_and_restoring_the_graphics_state_allocates_nothing_once_warm` guards it.

## Reproducing

```sh
BROADSIDE_CORPUS_DIR=$PWD/corpus dotnet run -c Release --project bench/Broadside.Benchmarks -- \
  --filter '*GraphicsStateBenchmarks*' '*Type3GlyphBenchmarks*' '*ContentInterpretationBenchmarks.InterpretWarm*' --job short-inprocess
```
