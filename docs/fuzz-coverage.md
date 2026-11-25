# Fuzzing coverage report

Issue #274, for v1.0. Every parser and codec has a fuzz target (`tests/Broadside.Fuzz`); this report says how much of each one its
seed corpus reaches, and records the last full run of every target. It sits next to the [threat model](threat-model.md) and the
[unsafe code and pooled buffer review](unsafe-code-review.md).

## Which code each target owns

`tests/Broadside.Fuzz/parsers.txt` maps every parser and codec of `Broadside` and `Broadside.Rendering` to the targets that
exercise it. A parser or codec is any type behind the filter or font program parser extension point, any type whose name ends in
`Parser`, `Reader`, `Decoder`, `Tokenizer` or `Lexer`, and the parsers added in Phases 3 to 5 that follow neither rule: ICC
profiles (`IccProfile`), the default appearance tokenizer (`PdfDefaultAppearance`), the OpenType layout tables (`LayoutTable`,
`GdefTable`, `KernTable`), CMS signed data, time-stamp responses, OCSP responses and CRLs (`CmsSignature`, `TimestampProtocol`,
`OcspResponse`, `RevocationList`), PNG and JPEG input to the canvas (`PngReader`, `JpegInfo`) and range responses
(`HttpRangeSource`). `FuzzTargetMapTests` (in `tests/Broadside.Rendering.Tests`, part of every CI run) fails when a parser or codec
has no line in the map, when a line names a target that is not registered, and when a line names a type that no longer exists.

The viewer packages parse no untrusted bytes themselves: documents, files and addresses go to the core, and the Blazor viewer hands
JPEG data to the browser's decoder (`BrowserImageDecoder`, whose eligibility checks are the core's, fuzz target
`platform-image-eligibility`), so they add no target; the [review](unsafe-code-review.md#viewers) lists what they do with buffers.

This pass added the two targets the map found missing: `jpeg-reader` (JPEG input to the canvas) and `range-response` (a remote file
against an HTTP server whose answers the input controls). See `tests/Broadside.Fuzz/README.md`.

## Threshold and method

**Threshold: 60 % line coverage of the parser scope**, where the parser scope of a target is every line of the types it owns: the
types whose line in `parsers.txt` names it first (nested types and lambdas included). Other targets on a line also reach the type;
they are not judged on it. A fuzzer starts from these seeds and grows the corpus by coverage, so the seeds
must already reach well into the parser; error branches for each malformed structure are what the fuzzer itself adds. Targets that
own no parser (the whole-document, rendering, writing and extraction targets) exercise code that is not a parser; their
row shows the coverage of the two assemblies only and has no threshold.

`tests/Broadside.Fuzz/Coverage/coverage.sh` measures each target: it writes the target's seeds with `--seeds` (exactly what the
scheduled libFuzzer run starts from, here from `tests/Corpus` alone), then runs the target once over those seeds, its regression
seeds (`tests/Broadside.Fuzz/Seeds/<target>`) and `tests/Corpus`, under coverlet (`coverlet.console`, a local tool in
`.config/dotnet-tools.json`), and `report.py` writes the table. Coverage is of the corpus, not of a fuzzing run: what libFuzzer
reaches beyond it only adds to these numbers.

COVERAGE_TABLE

## Full run

FULL_RUN
