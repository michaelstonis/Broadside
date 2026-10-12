---
name: adding-a-filter
description: "Use when implementing a stream filter or image codec (FlateDecode, LZWDecode, ASCII85Decode, RunLengthDecode, DCTDecode, CCITTFaxDecode, JBIG2Decode, JPXDecode, Crypt, predictors) behind the filter extension point in Broadside.Filters."
---

# Adding a filter

A filter is "a stream encoding named in a stream's `/Filter` entry, and the codec that decodes or encodes it. Image codecs are filters." (`CONTEXT.md`). Filters are an extension point: a public contract in the core with a fully managed default (`CLAUDE.md`, "Hard rules"; ADR 0001).

## Where it lives

- Contract and standard filters: namespace `Broadside.Filters` in `src/Broadside/`. Image codecs (DCT, CCITT, JBIG2, JPX; Phase 2B) implement the same contract, in the same namespace or a sub-namespace per codec.
- Tests: `tests/Broadside.Tests/Filters/`, namespace `Broadside.Tests.Filters`, one class per filter.
- Corpus: `tests/Corpus/<filter>-stream.pdf` (`flate-stream.pdf`, `lzw-stream.pdf`, `ascii85-stream.pdf`, `asciihex-stream.pdf`, `runlength-stream.pdf`, `filter-chain.pdf`, `png-predictor.pdf` exist); the encoder side of each lives in `tests/Corpus/generate.py` (`lzw_encode`, `ascii85_encode`, `png_up_predict`, ...).
- Benchmarks: a `<Filter>Benchmarks` class deriving from `FilterBenchmark` in `bench/Broadside.Benchmarks/FilterBenchmarks.cs` (256 KB of `FilterEncoders.SampleData`, encoded in setup with `tests/Broadside.TestSupport/FilterEncoders.cs`). Allocation proof: `tests/Broadside.Tests/Filters/FilterAllocationTests.cs`. Fuzz: `tests/Broadside.Fuzz/FuzzTargets.cs`, target `filter-<name>`.

## Contract

The contract landed with #38 in `src/Broadside/Filters/` (public surface in `src/Broadside/PublicAPI.Unshipped.txt`):

```csharp
namespace Broadside.Filters;

public interface IStreamFilter
{
    CosName Name { get; }                       // full /Filter name (Table 6), the registry key; never an abbreviation
    void Decode(ReadOnlyMemory<byte> encoded,   // never empty: the pipeline short-circuits empty data without a diagnostic
                IBufferWriter<byte> output,
                FilterContext context);
}

public sealed class FilterContext
{
    public FilterContext();                              // stand-alone: default filters, no document, own diagnostics
    public CosDictionary? Parameters { get; init; }      // this filter's DecodeParms (values may be references: Resolve them)
    public CosDictionary StreamDictionary { get; init; } // the stream's dictionary: Width, Height, BitsPerComponent, ColorSpace, Decode...
    public PdfReadingMode ReadingMode { get; init; }     // Strict => Report throws
    public long MaxDecodedLength { get; init; }          // check header-declared sizes against this before allocating
    public IReadOnlyList<Diagnostic> Diagnostics { get; }// what was reported through this context
    public void Report(string code, DiagnosticSeverity severity, string message); // lenient: record; strict: throw DiagnosticException
    public CosObject Resolve(CosObject? value);          // indirect references of the document (CosNull outside one)
    public ReadOnlyMemory<byte> DecodeStream(CosStream stream); // another stream through the same pipeline (JBIG2Globals)
}

// Registration, per engine (no static registry):
new PdfOptions().UseFilter(IStreamFilter filter);     // replaces the default of filter.Name or adds a name; later wins
new PdfOptions().WithMaxDecodedStreamLength(long);    // decompression-bomb limit, default PdfOptions.DefaultMaxDecodedStreamLength (1 GiB)

// Consumption:
ReadOnlyMemory<byte> PdfDocument.DecodeStream(CosStream stream);
void PdfDocument.DecodeStream(CosStream stream, IBufferWriter<byte> output);
```

Managed defaults (`sealed`, stateless): `AsciiHexDecodeFilter`, `Ascii85DecodeFilter`, `LzwDecodeFilter`, `FlateDecodeFilter`, `RunLengthDecodeFilter`, listed in internal `FilterRegistry.Defaults`. A Phase 2B codec adds its instance there (one line; registration lines conflict trivially).

What the pipeline (internal `StreamDecoder`, one per document) does so a filter does not:

- reads `Filter`/`DecodeParms` (resolving references), repairs Table 5 deviations with `DecodeParmsInvalid`/`FilterInvalid`, expands the §8.9.7 Table 92 abbreviations (with `FilterAbbreviationNotAllowed` outside inline images), and runs the chain in order;
- applies the §7.4.4.4 predictors (internal `Predictor`) after any filter named `LZWDecode` or `FlateDecode`, including a replacement: a replacement must not apply them itself;
- routes `Crypt` (§7.4.10) to the internal `ICryptFilterHandler` slot (`StreamDecoder.CryptFilter`, set by #42); `Crypt` and abbreviations cannot be registered;
- buffers between stages in pooled memory, truncates at `MaxDecodedLength` with `StreamDecodedLengthExceeded`, stops the chain at an unregistered name with `FilterUnsupported` and returns the data decoded so far;
- converts any exception a filter throws (other than a strict-mode `DiagnosticException`) into `FilterFailed`, keeping what it wrote.

Rules for an implementation: keep no state between calls (one instance serves every thread of every document of the engine); write through `IBufferWriter<byte>` in chunks (internal `FilterOutput` ref struct does this for the defaults); report each kind of deviation once per call with the codes in `Parsing/DiagnosticCodes.cs` ("Stream filters" group: `FilterDataInvalid`, `FilterDataTruncated`, ...); in lenient mode write what can be decoded and return.

Image codecs (Phase 2B): read the image entries from `StreamDictionary`, resolve auxiliary streams with `Resolve` and decode them with `DecodeStream`, size-check against `MaxDecodedLength` before allocating, and write the §8.9.3 sample layout so a codec behaves like any filter in a chain. Codec metadata (DCT transform, JPX components and colour, alpha) is #60's optional image facet: a second interface the four codecs implement in addition to `IStreamFilter`, used when the codec is last in the chain. Encoding (Flate, DCT baseline, PNG predictors; Phase 4A) will be a separate interface, so decode-only filters need not change.

## Managed default

Every filter the PDF file format depends on ships in the core, written in C#. Allowed dependencies are `System.*` only: Flate uses `System.IO.Compression` (`ZLibStream` or `DeflateStream`); everything else (LZW, ASCII, RunLength, predictors, and in Phase 2B the DCT, CCITT, JBIG2 and JPX decoders) is implemented here. Do not add a NuGet package to save time; stub behind the extension point and open an issue (`CLAUDE.md`, "Do not").

## Deliverables of a filter PR

1. Implementation in `Broadside.Filters`, `sealed`, XML docs on every public member with `<remarks>ISO 32000-2 §7.4.x</remarks>` (plus the codec's own reference: ITU-T T.81 for DCT, T.4/T.6 for CCITT, T.88 for JBIG2, ISO/IEC 15444-1 for JPX, cited from `Specs/References/`).
2. `PublicAPI.Unshipped.txt` entries for the new public surface, explained in the PR body.
3. Conformance rows: split `7.4` in `docs/conformance/iso-32000-2.md` to third level (`7.4.2` ASCIIHexDecode ... `7.4.10` Crypt) and mark the ones you implemented (skill `updating-the-conformance-map`).
4. Corpus file(s): one `<filter>-stream.pdf` per filter and one per distinctive parameter set (`png-predictor.pdf` covers Predictor 12), generated by `generate.py` (skill `writing-a-corpus-file`). Truncated and EOD-less variants belong in the "Deliberately broken files" table.
5. Unit tests: known-answer vectors from the spec (the LZW example in §7.4.4.2, the ASCII85 `~>` and `z` cases), round trip against the generator's encoder, the broken variants in lenient and strict mode, and the chain.
6. Fuzz target `filter-<name>` over raw stream bodies (skill `adding-a-fuzz-target`).
7. Benchmark `<Filter>Benchmarks` with `[MemoryDiagnoser]`, `Allocated` at `-` for the decode loop (skill `adding-a-benchmark`).
8. Fuzz smoke and `dotnet test Broadside.Core.slnf` green.

## Worked example: LZWDecode

`tests/Corpus/lzw-stream.pdf` holds a content stream encoded by `lzw_encode` in `generate.py` (9–12-bit codes, clear code 256 first, EOD 257 last, `EarlyChange` 1). The decoder lives in `Broadside.Filters`, cites `<remarks>ISO 32000-2 §7.4.4.2.</remarks>`, handles `/EarlyChange 0` from `/DecodeParms` (Table 8), and then the TIFF and PNG predictors from §7.4.4.4 run as a second stage shared with Flate. Tests: the Table 7 sequence `45 45 45 45 45 65 45 45 45 66` decodes from `80 0B 60 50 22 0C 0C 85 01` (the known answer `self_test()` in `generate.py` already checks for the encoder); `lzw-stream.pdf` decodes to the bytes `pdftotext` shows (`LZWDecode`); a truncated body yields a diagnostic in lenient mode and throws in strict mode. Fuzz target `filter-lzw`, benchmark `LzwBenchmarks.Decode`, row `7.4.4` done with those tests named.

## Checklist

- [ ] Implementation in `Broadside.Filters`, managed only, `sealed`, clause-cited XML docs
- [ ] Registered through `PdfOptions`, not a static registry
- [ ] Lenient repairs recorded as `Diagnostic`; strict mode throws
- [ ] `PublicAPI.Unshipped.txt`, conformance rows, corpus file(s), unit tests, fuzz target, benchmark
- [ ] No per-byte or per-row allocation (`Allocated` reads `-`)
