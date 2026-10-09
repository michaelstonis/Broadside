# Broadside.Fuzz

The fuzz harness: one console application, one target per parser or codec, selected by name. It is built on [SharpFuzz](https://github.com/Metalnem/sharpfuzz), which instruments a .NET assembly for coverage and bridges it to libFuzzer or AFL.

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --list
```

Targets live in `FuzzTargets.cs` as a dictionary from name to `ReadOnlySpanAction`. A target takes one input, calls into `Broadside`, and either returns or throws; any exception that escapes is a finding. The targets today are `lexer` (the COS lexer over the whole input: every token inside the input, always making progress) and `object-parser` (the lenient COS object parser scanning the input as a file body, checking that every object it reads writes back to syntax that parses, without repair, to an equal object; it also runs the strict public `CosObject.TryParse`) and `document` (opens the input as a whole file in lenient mode and reads the version, trailer and every page's boxes, rotation, user unit and resources, and decodes each page's content stream through its filters; a `DiagnosticException` for a file whose cross-reference information cannot be read is the one allowed outcome until #41 reconstructs it; it also reads the revisions and a linearized file's parameter dictionary and hint tables) and `hint-tables` (the linearization hint table decoder of Annex F over raw hint stream data: byte 0 selects the page count, bytes 1 and 2 the position of the shared object table, the rest is the data; it either fails with a reason or returns one entry per page). `xref-stream` reads the input as cross-reference stream data (bytes 0 to 2 are the W field widths, byte 3 picks the default Index or two subsections, the rest is the data) and checks the entries against the data; `object-stream` reads the input as decoded object stream data (bytes 0 and 1 are N and First) and parses every member the header names, at its index and at a wrong one; whole files with cross-reference and object streams go through `document`. The filter targets `filter-asciihex`, `filter-ascii85`, `filter-lzw` (first byte selects EarlyChange), `filter-flate` and `filter-runlength` decode the input as a raw stream body and check the output against the most the encoding can expand to; `filter-predictor` reads Predictor, Colors, BitsPerComponent and Columns from the first four bytes and checks that the output is whole rows. Every parser and codec after them adds its own (CLAUDE.md, "Code conventions").

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

Whole corpus files are the right seeds for `lexer` and `object-parser`, which read any bytes as a file body, and for `document`, which reads whole files. `hint-tables` reads raw hint stream data, for which whole files are a weak seed; for libFuzzer and AFL seed it with the hint stream of `linearized.pdf` behind its three control bytes (two pages, shared table at 44):

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

For the `filter-*` targets whole files are weak seeds too (a whole PDF as a stream body); for real fuzzing extract stream bodies, for example `qpdf --show-object=4 --raw-stream-data tests/Corpus/lzw-stream.pdf > artifacts/fuzz/seeds/lzw.bin`.

Smoke mode is a regression net, not a fuzzer: it has no coverage feedback and finds only shallow bugs. Use a real fuzzer for anything that parses.

## Real fuzzing

### 1. Instrument the library

SharpFuzz rewrites the IL of the assembly under test so the fuzzer sees which branches each input reaches. Instrument `Broadside.dll` (and any other `src/` assembly the target exercises), never the harness itself:

```sh
dotnet tool install --global SharpFuzz.CommandLine
dotnet build -c Release tests/Broadside.Fuzz
sharpfuzz artifacts/bin/Broadside.Fuzz/release/Broadside.dll
```

The instrumented file replaces the original in place; rebuild to get a clean one back.

### 2a. libFuzzer (`--fuzz`): the primary driver

`--fuzz [target]` calls `SharpFuzz.Fuzzer.LibFuzzer.Run`. It must be started by the [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) driver, a small native program that hosts libFuzzer and talks to the .NET process over shared memory; started any other way it exits 2 with a message. The driver passes one argument to the target, so when `--target_path` is `dotnet` that argument is the harness DLL and the target name comes from the `BROADSIDE_FUZZ_TARGET` environment variable.

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
