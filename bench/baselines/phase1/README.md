# Phase 1 benchmark baseline

The performance baseline that closes Phase 1 (issue #48) and that the Phase 3I regression gate compares against: every Phase 1
benchmark of `bench/Broadside.Benchmarks` (144 cases) run once with BenchmarkDotNet's default job (`Job.Default`: pilot,
warm-up and 15 to 100 measured iterations) and `[MemoryDiagnoser]`. One JSON report per class is next to this file
(`<Class>-report-full-compressed.json`, BenchmarkDotNet's full JSON exporter: every iteration, statistics and allocation figures);
the tables below are their summary.

## How it was run

```sh
export BROADSIDE_CORPUS_DIR=/path/to/corpus      # tools/CorpusFetcher output; enables OpenAndWalkBenchmarks' real-world subset
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter \
  'Broadside.Benchmarks.Ascii85Benchmarks*' 'Broadside.Benchmarks.AsciiHexBenchmarks*' 'Broadside.Benchmarks.CorpusBenchmarks*' \
  'Broadside.Benchmarks.DocumentOpenBenchmarks*' 'Broadside.Benchmarks.DocumentSaveBenchmarks*' 'Broadside.Benchmarks.EncryptedDocumentBenchmarks*' \
  'Broadside.Benchmarks.FlateBenchmarks*' 'Broadside.Benchmarks.LazyLoadingBenchmarks*' 'Broadside.Benchmarks.LexerBenchmarks*' \
  'Broadside.Benchmarks.LzwBenchmarks*' 'Broadside.Benchmarks.OpenAndWalkBenchmarks*' 'Broadside.Benchmarks.PredictorBenchmarks*' \
  'Broadside.Benchmarks.ReconstructionBenchmarks*' 'Broadside.Benchmarks.RepairBenchmarks*' 'Broadside.Benchmarks.RunLengthBenchmarks*' \
  'Broadside.Benchmarks.SecurityBenchmarks*' --exporters json github
```

| | |
|---|---|
| Commit | `de00556` (branch `48-fuzz-baseline`) |
| Machine | Apple M4 Max, 16 logical cores, Arm64 |
| OS | macOS 27.0.1 (26A434) [Darwin 27.0.0] |
| .NET | .NET 10.0.12 (10.0.12, 10.0.1226.42308), SDK 10.0.401, RELEASE |
| BenchmarkDotNet | 0.15.8 |
| Load average (1, 5, 15 min) | 10.0, 11.4, 32.7 at the start, 10.3, 14.0, 15.8 at the end |

The machine was not quiet: other build agents shared it (the load averages above). Treat the times as indicative and compare them
with care; the allocation figures do not depend on load and are exact. The regression gate should gate allocations hard and
times with a tolerance, from runs on one kind of runner.

## What the open-and-walk numbers measure

`OpenAndWalkBenchmarks.OpenAndWalk` opens a file from memory and reads everything the document model exposes through
`tests/Broadside.TestSupport/DocumentWalker.cs`, the walk of the real-world corpus gate (#47): version, catalog, security, revisions,
linearization and hint tables, every page's boxes and resources, every action and annotation, every object reachable from the
trailer or numbered below `Size`, and every stream decoded through its filters into a discarding writer. Its parameter is either a
file of `tests/Corpus/` (all of them; the two password-protected ones are opened with their user password) or a path in the fixed
real-world subset (14 files from the pdf20examples, qpdf, pdf.js and PDFBox corpora that `tools/CorpusFetcher` pins to commits,
each pinned again by SHA-256 in `bench/Broadside.Benchmarks/RealWorldSubset.cs`; the benchmark refuses a file whose hash differs).
Opening allocates the document model by design, so `Allocated` there is a baseline to watch, not a zero to assert; the hot paths
underneath (lexer, filters, decryption into a buffer) are the classes whose `Allocated` reads 0 B.

## Results

### Ascii85Benchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode |  | 250.19 us | 2.79 us | 0 B |

### AsciiHexBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode |  | 345.02 us | 15.20 us | 0 B |

### CorpusBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| ReadAllCorpusFiles |  | 2.49 ms | 65.39 us | 123.39 KB |

### DocumentOpenBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| OpenAndWalkPages | File: empty-page.pdf | 5.02 us | 115.4 ns | 13.44 KB |
| OpenAndReadFileStructure | File: empty-page.pdf | 3.49 us | 178.5 ns | 9.89 KB |
| OpenAndWalkPages | File: hybrid-xref.pdf | 7.54 us | 58.5 ns | 18.87 KB |
| OpenAndReadFileStructure | File: hybrid-xref.pdf | 5.76 us | 37.5 ns | 15.32 KB |
| OpenAndWalkPages | File: incremental-update.pdf | 5.76 us | 63.8 ns | 15.07 KB |
| OpenAndReadFileStructure | File: incremental-update.pdf | 4.10 us | 32.4 ns | 11.52 KB |
| OpenAndWalkPages | File: linearized.pdf | 11.17 us | 74.8 ns | 25.67 KB |
| OpenAndReadFileStructure | File: linearized.pdf | 8.33 us | 99.0 ns | 19.06 KB |
| OpenAndWalkPages | File: object-stream.pdf | 6.18 us | 62.0 ns | 17.67 KB |
| OpenAndReadFileStructure | File: object-stream.pdf | 4.59 us | 48.8 ns | 14.28 KB |
| OpenAndWalkPages | File: page-tree-inherited.pdf | 8.01 us | 112.5 ns | 19.20 KB |
| OpenAndReadFileStructure | File: page-tree-inherited.pdf | 3.98 us | 33.4 ns | 12.16 KB |
| OpenAndWalkPages | File: png-predictor.pdf | 6.63 us | 99.1 ns | 16.72 KB |
| OpenAndReadFileStructure | File: png-predictor.pdf | 4.84 us | 110.8 ns | 13.17 KB |
| OpenAndWalkPages | File: xref-stream.pdf | 5.16 us | 63.2 ns | 15.12 KB |
| OpenAndReadFileStructure | File: xref-stream.pdf | 3.69 us | 61.9 ns | 11.58 KB |

### DocumentSaveBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| OpenAndSave | File: empty-page.pdf, Layout: Table | 6.18 us | 125.8 ns | 16.24 KB |
| OpenAndSave | File: empty-page.pdf, Layout: StreamWithObjectStreams | 14.32 us | 168.2 ns | 20.42 KB |
| OpenAndSave | File: object-stream.pdf, Layout: Table | 8.16 us | 134.6 ns | 22.84 KB |
| OpenAndSave | File: object-stream.pdf, Layout: StreamWithObjectStreams | 16.43 us | 130.3 ns | 27.03 KB |
| OpenAndSave | File: text-truetype-embedded.pdf, Layout: Table | 11.83 us | 323.5 ns | 25.99 KB |
| OpenAndSave | File: text-truetype-embedded.pdf, Layout: StreamWithObjectStreams | 23.67 us | 336.5 ns | 31.81 KB |

### EncryptedDocumentBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| OpenAndDecryptContent | File: encrypted-aes-128.pdf | 30.96 us | 409.7 ns | 21.97 KB |
| OpenAndDecryptContent | File: encrypted-aes-256.pdf | 496.49 us | 4.28 us | 43.42 KB |
| OpenAndDecryptContent | File: encrypted-aes-gcm.pdf | 533.41 us | 50.41 us | 45.42 KB |
| OpenAndDecryptContent | File: encrypted-mac.pdf | 538.60 us | 4.00 us | 119.00 KB |
| OpenAndDecryptContent | File: encrypted-rc4-128.pdf | 30.58 us | 205.0 ns | 19.69 KB |

### FlateBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode |  | 229.67 us | 7.16 us | 264 B |

### LazyLoadingBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| ResolveCached | File: object-stream.pdf | 20.0 ns | 0.3 ns | 0 B |
| OpenFromBytesAndResolveAll | File: object-stream.pdf | 6.54 us | 126.5 ns | 18.27 KB |
| OpenFromPathAndResolveAll | File: object-stream.pdf | 60.96 us | 4.17 us | 19.02 KB |
| ResolveCached | File: page-tree-inherited.pdf | 31.6 ns | 0.4 ns | 0 B |
| OpenFromBytesAndResolveAll | File: page-tree-inherited.pdf | 8.34 us | 126.5 ns | 20.38 KB |
| OpenFromPathAndResolveAll | File: page-tree-inherited.pdf | 61.53 us | 2.30 us | 20.95 KB |

### LexerBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Tokenize | File: empty-page.pdf | 1.60 us | 27.8 ns | 0 B |
| Tokenize | File: text-truetype-embedded.pdf | 5.76 us | 113.8 ns | 0 B |

### LzwBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode |  | 491.33 us | 11.89 us | 0 B |

### OpenAndWalkBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| OpenAndWalk | File: actions-all.pdf | 194.69 us | 3.38 us | 305.39 KB |
| OpenAndWalk | File: actions-preserved.pdf | 58.61 us | 310.5 ns | 113.08 KB |
| OpenAndWalk | File: annotations-appearance.pdf | 31.43 us | 509.1 ns | 46.90 KB |
| OpenAndWalk | File: annotations-link.pdf | 9.66 us | 94.9 ns | 22.23 KB |
| OpenAndWalk | File: annotations-malformed.pdf | 47.91 us | 878.6 ns | 79.16 KB |
| OpenAndWalk | File: annotations-subtypes.pdf | 418.23 us | 5.32 us | 282.48 KB |
| OpenAndWalk | File: ascii85-stream.pdf | 8.26 us | 166.9 ns | 20.39 KB |
| OpenAndWalk | File: asciihex-stream.pdf | 8.26 us | 109.1 ns | 20.39 KB |
| OpenAndWalk | File: associated-files.pdf | 38.29 us | 544.4 ns | 88.75 KB |
| OpenAndWalk | File: broken-xref-offsets.pdf | 7.45 us | 49.5 ns | 18.71 KB |
| OpenAndWalk | File: catalog-version-extensions.pdf | 9.45 us | 103.7 ns | 22.07 KB |
| OpenAndWalk | File: collection-portfolio.pdf | 21.86 us | 243.4 ns | 47.86 KB |
| OpenAndWalk | File: declarations.pdf | 15.96 us | 650.9 ns | 43.83 KB |
| OpenAndWalk | File: destinations-all.pdf | 23.12 us | 1.95 us | 45.08 KB |
| OpenAndWalk | File: embedded-files.pdf | 17.59 us | 3.08 us | 32.80 KB |
| OpenAndWalk | File: empty-page.pdf | 6.56 us | 280.3 ns | 15.54 KB |
| OpenAndWalk | File: encrypted-aes-128.pdf | 35.29 us | 654.8 ns | 26.67 KB |
| OpenAndWalk | File: encrypted-aes-256.pdf | 497.51 us | 4.54 us | 48.12 KB |
| OpenAndWalk | File: encrypted-aes-gcm.pdf | 503.04 us | 1.72 us | 52.65 KB |
| OpenAndWalk | File: encrypted-crypt-filters.pdf | 46.24 us | 637.4 ns | 53.88 KB |
| OpenAndWalk | File: encrypted-empty-owner-password.pdf | 1.16 ms | 19.25 us | 57.92 KB |
| OpenAndWalk | File: encrypted-mac-tampered.pdf | 544.67 us | 4.88 us | 125.40 KB |
| OpenAndWalk | File: encrypted-mac.pdf | 540.12 us | 3.46 us | 125.10 KB |
| OpenAndWalk | File: encrypted-owner-key-variant.pdf | 35.41 us | 777.7 ns | 24.27 KB |
| OpenAndWalk | File: encrypted-rc4-128.pdf | 33.27 us | 277.1 ns | 24.27 KB |
| OpenAndWalk | File: encrypted-rc4-40-r3.pdf | 34.68 us | 345.6 ns | 24.25 KB |
| OpenAndWalk | File: encrypted-rc4-40.pdf | 12.18 us | 93.8 ns | 24.25 KB |
| OpenAndWalk | File: encrypted-rc4-length-missing.pdf | 150.55 us | 1.23 us | 26.27 KB |
| OpenAndWalk | File: encrypted-rc4-user-password.pdf | 35.57 us | 956.9 ns | 25.74 KB |
| OpenAndWalk | File: encrypted-user-password.pdf | 555.05 us | 3.82 us | 49.33 KB |
| OpenAndWalk | File: filter-chain.pdf | 9.48 us | 417.4 ns | 21.31 KB |
| OpenAndWalk | File: flate-stream.pdf | 9.02 us | 115.4 ns | 20.80 KB |
| OpenAndWalk | File: functions.pdf | 22.01 us | 265.2 ns | 46.98 KB |
| OpenAndWalk | File: hybrid-xref.pdf | 11.80 us | 135.4 ns | 27.23 KB |
| OpenAndWalk | File: incremental-update.pdf | 7.09 us | 258.2 ns | 17.17 KB |
| OpenAndWalk | File: info-dictionary.pdf | 8.72 us | 109.7 ns | 20.84 KB |
| OpenAndWalk | File: inline-image.pdf | 7.08 us | 159.2 ns | 17.76 KB |
| OpenAndWalk | File: linearized-xref-stream.pdf | 21.22 us | 268.1 ns | 50.16 KB |
| OpenAndWalk | File: linearized.pdf | 16.92 us | 205.0 ns | 35.28 KB |
| OpenAndWalk | File: lzw-stream.pdf | 8.60 us | 152.4 ns | 20.54 KB |
| OpenAndWalk | File: metadata-xmp-forms.pdf | 18.38 us | 263.3 ns | 54.53 KB |
| OpenAndWalk | File: metadata-xmp.pdf | 12.04 us | 247.7 ns | 35.50 KB |
| OpenAndWalk | File: missing-endobj.pdf | 6.15 us | 110.5 ns | 15.77 KB |
| OpenAndWalk | File: name-tree-broken.pdf | 19.55 us | 308.8 ns | 44.67 KB |
| OpenAndWalk | File: name-tree-deep.pdf | 215.08 us | 1.06 us | 321.04 KB |
| OpenAndWalk | File: name-tree-dests.pdf | 11.68 us | 165.9 ns | 25.69 KB |
| OpenAndWalk | File: no-xref.pdf | 7.50 us | 152.7 ns | 21.80 KB |
| OpenAndWalk | File: number-tree-deep.pdf | 33.37 us | 566.2 ns | 71.74 KB |
| OpenAndWalk | File: object-metadata.pdf | 50.85 us | 3.11 us | 114.69 KB |
| OpenAndWalk | File: object-stream.pdf | 8.67 us | 77.7 ns | 22.17 KB |
| OpenAndWalk | File: optional-content.pdf | 21.40 us | 203.5 ns | 47.52 KB |
| OpenAndWalk | File: outline-broken.pdf | 16.63 us | 139.4 ns | 36.60 KB |
| OpenAndWalk | File: outline-full.pdf | 23.80 us | 103.6 ns | 50.79 KB |
| OpenAndWalk | File: outline.pdf | 11.08 us | 205.8 ns | 25.16 KB |
| OpenAndWalk | File: page-labels.pdf | 26.62 us | 502.3 ns | 57.39 KB |
| OpenAndWalk | File: page-tree-inherited.pdf | 11.12 us | 109.6 ns | 25.67 KB |
| OpenAndWalk | File: pdf20-header.pdf | 6.93 us | 178.7 ns | 16.55 KB |
| OpenAndWalk | File: pdf20examples/PDF 2.0 via incremental save.pdf | 34.43 us | 228.0 ns | 76.45 KB |
| OpenAndWalk | File: pdf20examples/Simple PDF 2.0 file.pdf | 33.55 us | 499.9 ns | 74.10 KB |
| OpenAndWalk | File: pdf20examples/pdf20-utf8-test.pdf | 101.60 us | 1.27 us | 81.74 KB |
| OpenAndWalk | File: pdfbox/input/rendering/survey.pdf | 718.17 us | 10.93 us | 404.45 KB |
| OpenAndWalk | File: pdfjs/160F-2019.pdf | 3.14 ms | 66.49 us | 2.37 MB |
| OpenAndWalk | File: pdfjs/bug900822.pdf | 765.64 us | 8.69 us | 299.42 KB |
| OpenAndWalk | File: pdfjs/issue17808.pdf | 3.30 ms | 41.15 us | 632.62 KB |
| OpenAndWalk | File: pdfjs/issue18911.pdf | 5.88 ms | 47.87 us | 3.08 MB |
| OpenAndWalk | File: pdfjs/issue7665.pdf | 484.64 us | 2.75 us | 51.85 KB |
| OpenAndWalk | File: png-predictor.pdf | 10.19 us | 35.2 ns | 22.63 KB |
| OpenAndWalk | File: qpdf/deterministic-id-in.pdf | 1.35 ms | 21.44 us | 1.36 MB |
| OpenAndWalk | File: qpdf/large-inline-image.pdf | 3.91 ms | 71.64 us | 37.00 KB |
| OpenAndWalk | File: qpdf/lin9.pdf | 30.15 us | 370.1 ns | 61.15 KB |
| OpenAndWalk | File: qpdf/merge-implicit-ranges.pdf | 205.66 us | 2.23 us | 376.17 KB |
| OpenAndWalk | File: qpdf/minimal-linearized.pdf | 14.16 us | 1.14 us | 28.56 KB |
| OpenAndWalk | File: runlength-stream.pdf | 8.08 us | 113.3 ns | 20.39 KB |
| OpenAndWalk | File: startxref-wrong.pdf | 6.71 us | 150.6 ns | 16.72 KB |
| OpenAndWalk | File: tagged-structure.pdf | 44.39 us | 542.6 ns | 87.34 KB |
| OpenAndWalk | File: text-standard14-alias.pdf | 8.06 us | 137.5 ns | 19.84 KB |
| OpenAndWalk | File: text-standard14-differences.pdf | 8.61 us | 166.0 ns | 21.15 KB |
| OpenAndWalk | File: text-standard14-macroman.pdf | 8.08 us | 122.1 ns | 19.84 KB |
| OpenAndWalk | File: text-standard14-symbol-differences.pdf | 8.89 us | 418.2 ns | 20.51 KB |
| OpenAndWalk | File: text-standard14-symbol.pdf | 8.83 us | 247.5 ns | 21.02 KB |
| OpenAndWalk | File: text-standard14-widths.pdf | 10.57 us | 140.1 ns | 24.09 KB |
| OpenAndWalk | File: text-standard14-winansi-quirks.pdf | 7.83 us | 69.9 ns | 19.84 KB |
| OpenAndWalk | File: text-standard14-zapfdingbats.pdf | 7.81 us | 56.7 ns | 19.45 KB |
| OpenAndWalk | File: text-standard14.pdf | 8.06 us | 94.1 ns | 19.84 KB |
| OpenAndWalk | File: text-truetype-embedded.pdf | 11.18 us | 161.2 ns | 24.87 KB |
| OpenAndWalk | File: text-type1-symbolic-noencoding.pdf | 11.04 us | 452.6 ns | 23.20 KB |
| OpenAndWalk | File: viewer-preferences.pdf | 9.73 us | 291.2 ns | 22.48 KB |
| OpenAndWalk | File: wrong-stream-length.pdf | 8.26 us | 43.6 ns | 20.41 KB |
| OpenAndWalk | File: xref-stream.pdf | 7.67 us | 123.6 ns | 19.52 KB |

### PredictorBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode | Predictor: 2 | 262.65 us | 5.04 us | 0 B |
| Decode | Predictor: 15 | 404.24 us | 19.31 us | 0 B |

### ReconstructionBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Scan |  | 347.55 us | 6.87 us | 512.81 KB |
| OpenAndRebuild |  | 927.66 us | 19.38 us | 5.65 MB |

### RepairBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| OpenBrokenFile | File: broken-xref-offsets.pdf | 6.19 us | 55.4 ns | 16.62 KB |
| OpenBrokenFile | File: missing-endobj.pdf | 5.14 us | 115.6 ns | 13.66 KB |
| OpenBrokenFile | File: no-xref.pdf | 5.97 us | 83.6 ns | 19.70 KB |
| OpenBrokenFile | File: startxref-wrong.pdf | 5.16 us | 34.3 ns | 14.61 KB |
| OpenBrokenFile | File: wrong-stream-length.pdf | 5.60 us | 100.8 ns | 14.77 KB |

### RunLengthBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Decode |  | 174.24 us | 10.09 us | 0 B |

### SecurityBenchmarks

| Method | Parameters | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| Rc4Decrypt |  | 70.68 us | 946.3 ns | 0 B |
| AesCbcDecrypt |  | 4.62 us | 59.8 ns | 72 B |
| ManagedAesCbcDecrypt |  | 1.28 ms | 15.03 us | 0 B |
| AesGcmDecrypt |  | 12.59 us | 328.5 ns | 40 B |
| Revision6Hash |  | 235.22 us | 395.1 ns | 9.24 KB |
