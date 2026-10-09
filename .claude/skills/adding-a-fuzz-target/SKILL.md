---
name: adding-a-fuzz-target
description: "Use in the same PR that adds or changes any parser or codec (lexer, object parser, xref reader, filters, font program parsers, image decoders, CMaps, content interpreter): register a SharpFuzz target in tests/Broadside.Fuzz and run the smoke mode."
---

# Adding a fuzz target

`CLAUDE.md` ("Code conventions") requires a fuzz target for every parser and codec. The harness is `tests/Broadside.Fuzz`, a console application built on SharpFuzz; its README documents the modes and how to run libFuzzer or AFL. The rule is simple: the PR that adds the parser adds the target.

## How targets are registered

`tests/Broadside.Fuzz/FuzzTargets.cs` holds a dictionary from a stable kebab-case name to a `ReadOnlySpanAction` (SharpFuzz's `void (ReadOnlySpan<byte>)` delegate):

```csharp
public static IReadOnlyDictionary<string, ReadOnlySpanAction> All { get; } = new Dictionary<string, ReadOnlySpanAction>(StringComparer.Ordinal)
{
    ["pdf-header"] = PdfHeader,
};
```

`Program.cs` resolves the name with `TryGetTarget` for every mode and prints `All.Keys` for `--list`, so registration is the whole wiring: add a method, add an entry. The project references `src/Broadside` and can call `internal` members (`InternalsVisibleTo` in `src/Directory.Build.props`), links `CorpusLocator.cs` for the seed directory, and must not reference `Broadside.TestSupport`.

## The contract

- Input: one `ReadOnlySpan<byte>`. The target decides what it is (a whole file, a raw stream body, a font program).
- Call the parser in lenient mode and let everything escape. Any exception that leaves the target is a finding; the harness catches and reports it. Under ADR 0005 lenient reading records a `Diagnostic` instead of throwing, so an exception is a bug by definition. Do not wrap the call in `try`/`catch`.
- Assert only invariants that must hold for every input (decoded length within the declared bound, no token past the end of input), and throw when they do not.
- Keep the target small and deterministic: no file I/O, no randomness, no shared mutable state between calls, because libFuzzer calls it millions of times in one process.
- Cite the clause the target exercises in `<remarks>`.

Names: `lexer`, `object-parser`, `xref`, `filter-flate`, `filter-lzw`, `filter-dct`, `font-truetype`, `font-cff`, `cmap`, `content-stream`. One target per parser; a filter with several decode parameters still gets one target that reads the parameters from the input.

## Seeds

`--smoke` seeds every target with every file in `tests/Corpus/` (`CorpusLocator.CorpusFiles()`), then mutates them. That is right for whole-file targets. For a target that takes a raw stream body or a font program:

- the corpus already contains the bytes inside a file (`lzw-stream.pdf`, `text-truetype-embedded.pdf`); the smoke runner still feeds whole files, which is a weak seed for a raw-body target, so the target should tolerate arbitrary bytes and the real seeds go to libFuzzer and AFL in `artifacts/fuzz/seeds/` (extract the stream with `qpdf --show-object=4 --raw-stream-data tests/Corpus/lzw-stream.pdf > artifacts/fuzz/seeds/lzw.bin`);
- say so in `tests/Broadside.Fuzz/README.md` under "Adding a target", step 4.

## Modes

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --list                              # target names
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke pdf-header 60               # corpus + mutations for 60 s, random seed (what CI runs)
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke pdf-header 5 12345          # fixed seed to replay a run
dotnet run -c Release --project tests/Broadside.Fuzz -- --run pdf-header <file>             # one input once; replays a finding
```

Smoke output ends with `no failures` and exit 0; a finding is written to `artifacts/fuzz/<target>/` with the `--run` command to replay it, exit 1. `--fuzz` (libFuzzer through the libfuzzer-dotnet driver, target name from the argument or `BROADSIDE_FUZZ_TARGET`) and `--afl` (afl-fuzz) need an instrumented `Broadside.dll` (`sharpfuzz artifacts/bin/Broadside.Fuzz/release/Broadside.dll`) and the fuzzer binary; the README has the per-platform steps. Smoke mode has no coverage feedback: run a real fuzzer for anything that parses before calling the parser done, and the Phase 1 exit gate (#25) is a 24 h run with no findings.

## Worked example: `pdf-header`

The only target today, in `FuzzTargets.cs`:

```csharp
/// <summary>Placeholder until the COS parser lands (#36): decides whether the input starts with the <c>%PDF-</c> header marker.</summary>
/// <remarks>ISO 32000-2 §7.5.2.</remarks>
private static void PdfHeader(ReadOnlySpan<byte> data)
{
    bool hasHeader = data.StartsWith("%PDF-"u8);
    if (hasHeader && data.Length < "%PDF-"u8.Length)
    {
        throw new InvalidOperationException("StartsWith claimed a header shorter than the marker; the input cannot be both.");
    }
}
```

It shows the shape: a span in, a call, an invariant that throws. `--smoke pdf-header 5 12345` reports all 30 corpus files passing and roughly 20 million mutated inputs in 5 s with no failures. When the lexer lands, a `lexer` target replaces it: construct the lexer over `data` in lenient mode, loop until end of input, and throw if a token's range lies outside the input; the method and the dictionary entry are the whole change.

## Checklist

- [ ] Method `private static void <Name>(ReadOnlySpan<byte> data)` in `FuzzTargets.cs` with `<remarks>` citing the clause
- [ ] Registered in `FuzzTargets.All` under a stable kebab-case name
- [ ] Lenient mode, no `try`/`catch`, only always-true invariants asserted
- [ ] Extra seeds documented in `tests/Broadside.Fuzz/README.md` when whole corpus files are not the right input
- [ ] `--smoke <name> 60` run locally: `no failures`, exit 0; output quoted in the PR
- [ ] Same PR as the parser or codec
