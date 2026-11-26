# v1 performance comparison

The results behind [`site/articles/performance.md`](../../../site/articles/performance.md) (issue #275): one folder per machine,
each written by one run of [`bench/perf-report.sh`](../../perf-report.sh).

| File | What it holds |
|---|---|
| `environment.json` | The machine, OS, engine versions, settings, commit and load average of the run |
| `broadside.jsonl`, `pdfium.jsonl`, `pdfjs.jsonl` | One line per page and operation: median and minimum milliseconds, runs; Broadside's render lines also carry the bytes it allocated |
| `memory.jsonl` | Each engine's peak resident memory per file, and with the engine loaded and idle (no `file`) |
| `browser.jsonl` | Broadside's WebAssembly build and pdf.js in the same Chromium, one line per engine, page and operation |

## Running it

```sh
dotnet run --project tools/CorpusFetcher -- --only pdfjs,pdf20examples,gwg-output-suite   # once: the files of the pinned pages
bench/perf-report.sh                     # this machine; results and report.md in artifacts/perf-report/<os>-<arch>
bench/perf-report.sh --no-browser        # without the WebAssembly comparison (no wasm-tools workload or Chromium needed)
```

The script installs PDFium (pypdfium2) and pdf.js (pdfjs-dist with `@napi-rs/canvas`) through
`tools/RenderDiff/acquire-references.sh` when they are missing, times the three engines, measures memory, publishes the Blazor sample
and runs `PerfComparisonTests` for the browser, then writes `report.md` for that run alone. Close other heavy programs first: the
numbers are only as quiet as the machine.

For Linux, run the **Performance report** workflow (`.github/workflows/perf-report.yml`) and download its `perf-report-Linux`
artifact.

## Publishing

```sh
bench/perf-report.sh --publish artifacts/perf-report/macos-arm64 ~/Downloads/perf-report-Linux
```

copies each folder here (named by its `slug`) and writes `site/articles/performance.md` from every folder in this directory. Commit
both, with the reason for the new run in the pull request.

## Targets

The report checks the targets proposed with the Phase 3 baseline (issue #140,
[`docs/benchmarks/phase-3-render-baselines.md`](../../../docs/benchmarks/phase-3-render-baselines.md)): render at most 1.0x PDFium
on average and no page over 3x; first render at most 1.25x and no page over 4x; never slower than pdf.js; open under 0.25x and text
under 0.5x PDFium; a 72 DPI render allocating at most 1 MB. A missed target blocks 1.0 unless the owner accepts it in writing in
the report's pull request (issue #275).
