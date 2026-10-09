# Broadside.Fuzz

The fuzz harness: one console application, one target per parser or codec, selected by name. It is built on [SharpFuzz](https://github.com/Metalnem/sharpfuzz), which instruments a .NET assembly for coverage and bridges it to libFuzzer or AFL.

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --list
```

Targets live in `FuzzTargets.cs` as a dictionary from name to `ReadOnlySpanAction`. A target takes one input, calls into `Broadside`, and either returns or throws; any exception that escapes is a finding. The targets today are `lexer` (the COS lexer over the whole input: every token inside the input, always making progress) and `object-parser` (the lenient COS object parser scanning the input as a file body, checking that every object it reads writes back to syntax that parses, without repair, to an equal object; it also runs the strict public `CosObject.TryParse`) and `document` (opens the input as a whole file in lenient mode and reads the version, trailer and every page's boxes, rotation, user unit and resources, and reads every font of each page's resources through the font model (each simple font's 256 glyph names and widths, which must be non-empty and finite, and its descriptor); it also reads the revisions, a linearized file's parameter dictionary and hint tables, and the outline and named destinations, then walks every object reachable from the trailer or numbered below `Size` and decodes every stream through its filters with `DocumentWalker`, the walk the real-world corpus gate runs; a `DiagnosticException` is the one allowed outcome besides the encryption exceptions below, for a file in which even a scan finds no catalog (`CatalogNotFound`)) and `windowed-document` (opens the input from memory and through a seekable stream read in place in windows, once with the default first window and once with windows that start at 16 bytes and double, and requires all three to give the same pages, objects and diagnostics, or fail the same way) and `hint-tables` (the linearization hint table decoder of Annex F over raw hint stream data: byte 0 selects the page count, bytes 1 and 2 the position of the shared object table, the rest is the data; it either fails with a reason or returns one entry per page). `xref-stream` reads the input as cross-reference stream data (bytes 0 to 2 are the W field widths, byte 3 picks the default Index or two subsections, the rest is the data) and checks the entries against the data; `repair` scans the input as a damaged whole file and rebuilds its cross-reference information from the scan, as a lenient open does when the file's own cannot be read (#41): every position the scan reports lies inside the input in file order, every rebuilt in-use entry is an object header the scan found, and the rebuilt trailer has Root (whole corpus files are the right seeds); `object-stream` reads the input as decoded object stream data (bytes 0 and 1 are N and First) and parses every member the header names, at its index and at a wrong one; whole files with cross-reference and object streams go through `document`. `save` opens the input as a whole file, saves it in the layout its length selects (length mod 3: table, cross-reference stream, cross-reference stream with object streams) and reopens the output, which must open with the same page count; `NotSupportedException` (encrypted input, object numbers above the save limit) is the one allowed outcome besides an unreadable input. The filter targets `filter-asciihex`, `filter-ascii85`, `filter-lzw` (first byte selects EarlyChange), `filter-flate` and `filter-runlength` decode the input as a raw stream body and check the output against the most the encoding can expand to; `filter-predictor` reads Predictor, Colors, BitsPerComponent and Columns from the first four bytes and checks that the output is whole rows. `encrypted-document` opens the input with the owner password `owner` (so mutations of the encrypted corpus files get past authentication), decodes every page's content streams and reads the Info strings; `PdfPasswordException`, `PdfCertificateException` and `PdfEncryptionNotSupportedException` are allowed outcomes, as they are now for `document`, `windowed-document` and `save`. `public-key` wraps the input in a document encrypted for certificate recipients (ISO 32000-2 §7.6.5): byte 0 selects where it goes (an extra recipient list before a valid one for a fixed certificate, in an `adbe.pkcs7.s4` or `adbe.pkcs7.s5` dictionary, or the `Recipients` string of a stream's own crypt filter), and the document is opened with that certificate; it must open, digest the input into the key, and decode its content streams (any input is a valid seed). `decrypt` decrypts the input after its first byte as a string with the crypt filter method that byte selects (RC4, AES-128 with Algorithm 1's object key, AES-256-CBC, AES-256-GCM) and checks the plaintext is no longer than the ciphertext; `mac-token` parses the input as a DER PDF MAC token (ISO/TS 32004 AuthenticatedData) and checks its structure. `xmp` reads the input as an XMP packet (ISO 16684-1) through the reader the document uses, and again from its first `<?xpacket` when it has one (so mutated corpus files reach the XML): the packet must be null exactly when an Error says why, and every property, item, field, qualifier and typed getter must read; `pdf-date` reads the input as Latin-1 text with the PDF date parser (§7.9.4) and the XMP date parser, which must not throw, must keep the text and must keep offsets `DateTimeOffset` can hold. `document` also reads every document-level entry of #69 (extensions, requirements, viewer preferences, every page label, Info, the XMP packet, the file identifier). For real fuzzing seed `xmp` with the packets of `metadata-xmp.pdf` and `metadata-xmp-forms.pdf` (`qpdf --show-object=4 --filtered-stream-data`). `structure-tree` (ISO 32000-2 §14.7, §14.8) reads a structure tree completely (every element through `Elements` and through `Children`, role mappings, attributes, languages, and every MCID, ID and object lookup): an input starting with `%PDF-` is opened as a file (the corpus seeds, `tagged-structure.pdf` foremost), any other input is split at line feeds into a synthetic tagged file whose first line is the inside of the structure tree root and whose following lines are objects 5, 6 and so on, so mutations wire elements, K arrays, role maps, namespaces, attributes and the parent and ID trees together at random; it checks that `Elements` lists each element once and every parent chain ends. For libFuzzer and AFL seed it with line-per-object texts as well as the corpus, for instance the root line `/K [5 0 R] /ParentTree << /Nums [0 [5 0 R]] >> /RoleMap << /A /B /B /A >>` followed by `<< /S /A /P 4 0 R /Pg 3 0 R /K [0 5 0 R] /A [1 << /O /Layout >> 2] >>`. `content-lexer` reads the input as decoded content stream bytes with the content reader (ISO 32000-2 §7.8.2, #55): every operator must lie inside the input, after the previous one, with a keyword of at least one byte, and every operand, nested arrays and dictionaries included, must be readable; `content-interpreter` runs the input as a page's content through the interpreter with a processor that asks for every event and checks that path verbs and points agree, that clip handles chain back to the initial clip and that the state stack is balanced at the end; `document` now also interprets every page with that processor. Whole files are weak seeds for the two content targets; for libFuzzer and AFL seed them with decoded content streams (`qpdf --show-object=4 --filtered-stream-data tests/Corpus/filter-chain.pdf`) and with `ContentSamples.PathHeavy` output. `function-type4` compiles the input after its first byte as a Type 4 (PostScript calculator) program (byte 0 picks 1 to 4 inputs and 1 to 4 outputs) and `function-sampled` builds a Type 0 (sampled) function whose Size, BitsPerSample, Order and Encode come from bytes 0 to 4 and whose samples are the rest (ISO 32000-2 §7.10); both evaluate at fixed points inside and outside the domain, require every output inside the range, and require warm evaluations to allocate nothing. `optional-content` (#76) opens an input that starts with `%PDF-` as a whole file and otherwise uses the bytes as choices for a generated file with optional content properties, groups, membership dictionaries and visibility expressions that may be cyclic, deep or mistyped; it computes default and alternate states, auto and usage states, every object's visibility (which must be the same twice) and a marked-content tracker over every object (whose nesting must return to zero); any input is a valid seed, and `optional-content.pdf` is the whole-file seed. Every parser and codec after them adds its own (CLAUDE.md, "Code conventions").

Exit codes: 0 no finding, 1 a finding, 2 usage error.

## Smoke mode: no fuzzer needed

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke object-parser 10          # 10 seconds, random seed
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke object-parser 60 12345    # 60 seconds, fixed seed
```

`--smoke <target> [seconds] [seed]` runs the target over every file in `tests/Corpus/`, then for the given time (default 10 s) over random mutations of those files: bit flips, byte overwrites, truncation, insertion, deletion and chunk copies, one to four per input. It needs no instrumentation and no external binary, so it is what CI runs (60 s per target, issue #4) and what you run before opening a PR. The seed is printed; pass it back to replay a run. On a finding the input is written to `artifacts/fuzz/<target>/` and the command to replay it is printed:

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --run object-parser artifacts/fuzz/object-parser/smoke-seed12345-iter1234.bin
```

`--run <target> <file>` executes the target once on one input, which is also how to replay a crash found by libFuzzer or AFL.

`--seeds <target> <directory> [pdf-directory...]` writes the target's seed inputs for libFuzzer or AFL: whole files for the targets that read whole files, and for the others the parts of the files they read, cut out with the library and prefixed with the control bytes that select each file's own parameters (`filter-*`: the raw bodies of the streams whose first filter it is, `filter-lzw` behind its EarlyChange byte; `filter-predictor`: the Flate output of predicted streams behind the bytes that select their Predictor, Colors, BitsPerComponent and Columns; `xref-stream` and `object-stream`: decoded cross-reference and object streams behind W or N and First; `hint-tables`: the hint streams of linearized files behind their page count and shared table position; `mac-token`: standalone PDF MAC tokens; `decrypt`: the raw bodies of encrypted streams behind each selector byte). The sources are `tests/Corpus/*.pdf` plus every PDF under the extra directories, at most 256 KB each; seeds are named by their SHA-256. This replaces the hand-made seed recipes below for real fuzzing.

Whole corpus files are the right seeds for `lexer` and `object-parser`, which read any bytes as a file body, and for `document` and `save`, which read whole files. `hint-tables` reads raw hint stream data, for which whole files are a weak seed; for libFuzzer and AFL seed it with the hint stream of `linearized.pdf` behind its three control bytes (two pages, shared table at 44):

```sh
mkdir -p artifacts/fuzz/seeds-hints
{ printf '\001\000\054'; qpdf --show-object=7 --raw-stream-data tests/Corpus/linearized.pdf; } > artifacts/fuzz/seeds-hints/linearized.bin
```

`xref-stream` and `object-stream` read raw stream data behind control bytes, so whole files are weak seeds for them too; for libFuzzer and AFL seed them from the corpus:

```sh
mkdir -p artifacts/fuzz/seeds-xref-stream artifacts/fuzz/seeds-object-stream
{ printf '\001\002\001\000'; qpdf --show-object=4 --filtered-stream-data tests/Corpus/png-predictor.pdf; } > artifacts/fuzz/seeds-xref-stream/png-predictor.bin
{ printf '\003\016'; qpdf --show-object=4 --filtered-stream-data tests/Corpus/object-stream.pdf; } > artifacts/fuzz/seeds-object-stream/object-stream.bin
```

`mac-token` takes a DER token: seed it with the one in `encrypted-mac.pdf` (`python3 -c "import re,sys; d=open('tests/Corpus/encrypted-mac.pdf','rb').read(); sys.stdout.buffer.write(bytes.fromhex(re.search(rb'/MAC <([0-9A-F]+)>', d).group(1).decode()))" > artifacts/fuzz/seeds/mac-token.bin`). `decrypt` takes a selector byte and ciphertext: prefix the content stream of an encrypted corpus file with `\001` (AES-128) or `\003` (GCM).

`function-type4` and `function-sampled` take control bytes and a raw program or sample table; for libFuzzer and AFL seed them from `functions.pdf`: `{ printf '\014'; qpdf --show-object=17 --filtered-stream-data tests/Corpus/functions.pdf; } > artifacts/fuzz/seeds-type4/logo-green.bin` (1 input, 4 outputs) and `{ printf '\014\003\002\000\000'; qpdf --show-object=11 --filtered-stream-data tests/Corpus/functions.pdf; } > artifacts/fuzz/seeds-sampled/sampled.bin` (1 input, 4 outputs, 8 bits, Size 3).

For the `filter-*` targets whole files are weak seeds too (a whole PDF as a stream body); for real fuzzing extract stream bodies, for example `qpdf --show-object=4 --raw-stream-data tests/Corpus/lzw-stream.pdf > artifacts/fuzz/seeds/lzw.bin`.

Smoke mode is a regression net, not a fuzzer: it has no coverage feedback and finds only shallow bugs. Use a real fuzzer for anything that parses.

## Real fuzzing

### Scheduled runs in CI

`.github/workflows/fuzz.yml` fuzzes every target under libFuzzer once a week (Sundays 03:17 UTC) and on the "Run workflow" button, one parallel job per target: 80 minutes on every core of the runner by default (the `minutes` input changes it, up to 300; `targets` picks a subset), which adds up to more than 24 hours of fuzzing per run. Each job builds the driver from pinned source (`build-libfuzzer-dotnet.sh`), installs SharpFuzz.CommandLine, builds the harness, fetches the `pdf20examples` and `qpdf` corpora with `tools/CorpusFetcher` as extra seeds, restores the target's corpus from the previous run's cache, and runs `run-libfuzzer.sh`. Findings fail the job and are uploaded as `fuzz-findings-<target>` with a replay of each (`<finding>.txt`); the summary job tabulates minutes, CPU-hours, executions, corpus size and findings per target.

The same scripts run on any Linux machine (or a Docker container from `mcr.microsoft.com/dotnet/sdk:10.0-noble` with `clang libclang-rt-dev jq` installed):

```sh
tests/Broadside.Fuzz/build-libfuzzer-dotnet.sh
dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0
dotnet build -c Release tests/Broadside.Fuzz
tests/Broadside.Fuzz/run-libfuzzer.sh document 600 corpus/pdf20examples corpus/qpdf     # 10 minutes on every core
```

`run-libfuzzer.sh` writes `--seeds` into `artifacts/fuzz/<target>/seeds/` with the plain build, instruments a copy of the build in `artifacts/fuzz/instrumented/`, runs libFuzzer on that copy with one worker per core (`FUZZ_WORKERS` overrides) on `artifacts/fuzz/<target>/corpus/` plus the seeds, with the PDF dictionary `pdf.dict`, a 10 s timeout per input (hangs are findings), inputs up to 256 KB and a 3 GiB managed heap cap per worker (`DOTNET_GCHeapHardLimit`, so a decompression bomb is an `OutOfMemoryException` finding rather than a swapping machine), then minimizes the corpus with `-merge=1`, replays every `crash-*`, `timeout-*` and `oom-*` input into a `.txt` next to it, and writes `stats.json`. A stack overflow kills the .NET process; the driver then exits during the input, which libFuzzer reports as a crash and saves.

### 1. Instrument the library

SharpFuzz rewrites the IL of the assembly under test so the fuzzer sees which branches each input reaches. Instrument `Broadside.dll` (and any other `src/` assembly the target exercises), never the harness itself:

```sh
dotnet tool install --global SharpFuzz.CommandLine
dotnet build -c Release tests/Broadside.Fuzz
sharpfuzz artifacts/bin/Broadside.Fuzz/release/Broadside.dll
```

The instrumented file replaces the original in place; rebuild to get a clean one back. Instrumented code writes coverage into libFuzzer's shared memory, so it faults when started any other way: `--smoke`, `--run` and `--seeds` need the plain build (`run-libfuzzer.sh` keeps both). For the same reason the harness must not run library code before `Fuzzer.LibFuzzer.Run` (keep `Cos*` values out of `FuzzTargets`' static constructor).

### 2a. libFuzzer (`--fuzz`): the primary driver

`--fuzz [target]` calls `SharpFuzz.Fuzzer.LibFuzzer.Run`. It must be started by the [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) driver, a small native program that hosts libFuzzer and talks to the .NET process over shared memory; started any other way it exits 2 with a message. The driver passes one argument to the target, so when `--target_path` is `dotnet` that argument is the harness DLL, the harness takes a start with no mode under the driver as `--fuzz`, and the target name comes from the `BROADSIDE_FUZZ_TARGET` environment variable.

Getting the driver:

- Linux: download `libfuzzer-dotnet-ubuntu` from the libfuzzer-dotnet releases page and `chmod +x` it, or build it with `clang -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet` (`apt install clang`).
- macOS: there is no prebuilt binary and Apple's clang does not ship libFuzzer. Install LLVM from Homebrew and build from source:

  ```sh
  brew install llvm
  curl -O https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/master/libfuzzer-dotnet.cc
  "$(brew --prefix llvm)/bin/clang++" -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
  ```

Run, with the corpus as the seed directory (any directory of inputs works; libFuzzer adds what it finds to it):

```sh
mkdir -p artifacts/fuzz/seeds && cp tests/Corpus/*.pdf artifacts/fuzz/seeds/
BROADSIDE_FUZZ_TARGET=object-parser ./libfuzzer-dotnet \
  --target_path=dotnet \
  --target_arg=artifacts/bin/Broadside.Fuzz/release/Broadside.Fuzz.dll \
  artifacts/fuzz/seeds
```

Crashes land as `crash-<hash>` files in the working directory; replay them with `--run`.

### 2b. AFL (`--afl`): the alternative

`--afl <target>` calls `SharpFuzz.Fuzzer.Run`, SharpFuzz's AFL fork-server mode. afl-fuzz passes arguments through unchanged, so the target name goes on the command line. Install AFL++ (`brew install afl++` on macOS, `apt install afl++` on Debian and Ubuntu), then:

```sh
mkdir -p artifacts/fuzz/seeds && cp tests/Corpus/*.pdf artifacts/fuzz/seeds/
afl-fuzz -i artifacts/fuzz/seeds -o artifacts/fuzz/findings -t 5000 -m none \
  dotnet artifacts/bin/Broadside.Fuzz/release/Broadside.Fuzz.dll --afl object-parser
```

On macOS afl-fuzz refuses to start until the crash reporter is disabled; it prints the `launchctl` commands to run. Findings land in `artifacts/fuzz/findings/crashes/`.

Run outside afl-fuzz, `--afl` executes the target once on standard input and exits.

## Adding a target

1. Add a method `private static void <Name>(ReadOnlySpan<byte> data)` to `FuzzTargets.cs` that calls the parser or codec in lenient mode and lets exceptions escape. Cite the clause it exercises in `<remarks>`.
2. Register it in `FuzzTargets.All` under a stable kebab-case name.
3. Run `--smoke <name> 60` locally; CI runs the same for every registered target.
4. If the target needs seed inputs beyond `tests/Corpus/`, say so in this file.
