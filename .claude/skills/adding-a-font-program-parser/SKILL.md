---
name: adding-a-font-program-parser
description: "Use when implementing a parser for an embedded or substituted font program format (TrueType, OpenType, CFF/Type 2, Type 1, CID-keyed CFF, Type 3) behind the font program parser extension point in Broadside.Fonts."
---

# Adding a font program parser

> The contract below is the one #50 (Embedded TrueType glyph outlines) landed; the TrueType parser in `src/Broadside/Fonts/TrueType/` is the reference implementation.

A font program is "the embedded or substituted glyph data in one of the formats PDF allows: TrueType, OpenType, CFF, Type 1, or Type 3 content streams" (`CONTEXT.md`). Font program parsers are extension points with a managed default (ADR 0001); the font resolver (which finds a program for a non-embedded font) is a separate extension point and not covered here.

## Where it lives

- Contract and parsers: namespace `Broadside.Fonts` in `src/Broadside/`, one sub-namespace or folder per format (`Fonts/TrueType`, `Fonts/Cff`, `Fonts/Type1`). Standard 14 metrics (AFM) are in the core; their glyphs ship in `src/Broadside.Fonts.Standard14` (ADR 0007). Predefined CMaps ship in `src/Broadside.Fonts.Cmaps`.
- Tests: `tests/Broadside.Tests/Fonts/` (`<Format>CorpusTests`, `<Format>ProgramTests`), namespace `Broadside.Tests.Fonts`.
- Corpus: `tests/Corpus/text-<format>-embedded.pdf`. `text-truetype-embedded.pdf` exists, built by `minimal_truetype()` in `generate.py`; the font is synthesized (`.notdef`, `H`, `I` as rectangles), carries no licence, and every other format follows that pattern. Never embed a real typeface.
- Fuzz: target `font-<format>` over the raw program bytes. Benchmarks: `bench/Broadside.Benchmarks/<Format>Benchmarks.cs`.

## Contract

Defined by #50 in `src/Broadside/Fonts/` (namespace `Broadside.Fonts`); the public signatures are in `src/Broadside/PublicAPI.Unshipped.txt`.

```csharp
public interface IFontProgramParser
{
    IReadOnlyList<FontProgramFormat> Formats { get; }                          // formats it reads (fallback selection)
    bool CanParse(ReadOnlySpan<byte> data);                                   // cheap signature sniff over the decoded program
    FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context); // null = nothing usable (after a diagnostic)
}

public enum FontProgramFormat { TrueType, OpenType, Cff, Type1 }
public enum FontProgramSource { Unspecified, FontFile, FontFile2, FontFile3 }

public sealed class FontProgramContext                 // public ctor = stand-alone (tests, fuzzing); documents build their own
{
    FontProgramSource Source { get; init; }            // descriptor key
    CosName? Subtype { get; init; }                     // FontFile3 /Subtype: Type1C, CIDFontType0C, OpenType
    long? Length1 / Length2 / Length3 { get; init; }   // Table 125, as written (may lie)
    bool IsCidFont { get; init; }                       // a CIDFont's TrueType program needs no cmap (§9.9)
    int FaceIndex { get; init; }  string? FaceName { get; init; }   // font collections
    PdfReadingMode ReadingMode { get; init; }
    int MaxCompositeDepth { get; init; }   // 16;  int MaxGlyphPoints { get; init; }  // 65,536
    int MaxCharStringOperators { get; init; }   // 100,000 per glyph, subrs and seac components included (#51)
    IReadOnlyList<Diagnostic> Diagnostics { get; }     // first of each code
    void Report(string code, DiagnosticSeverity severity, string message);   // throws in strict mode (not for Information)
}

public abstract class FontProgram                      // immutable, shared across threads; lookups never throw
{
    protected FontProgram();
    abstract FontProgramFormat Format { get; }
    abstract int GlyphCount { get; }                    // glyph 0 is .notdef
    abstract Matrix FontMatrix { get; }                 // glyph space -> text space (Broadside.Graphics.Matrix)
    abstract GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline);   // clears the outline first
    virtual string? PostScriptName { get; }             // null
    virtual PdfRectangle FontBBox { get; }              // default
    virtual double Ascender / Descender / LineGap { get; }                      // 0
    virtual IReadOnlyList<FontCharacterMap> CharacterMaps { get; }              // [] ("cmap" subtables)
    virtual GlyphMetrics GetMetrics(int glyphId);       // default (advance, left side bearing)
    virtual bool TryGetGlyphId(string glyphName, out int glyphId);              // false (post / charset / CharStrings)
    virtual string? GetGlyphName(int glyphId);          // null
    virtual IReadOnlyList<string>? BuiltInEncoding { get; }                     // null (256 names, .notdef unmapped: Type 1 /Encoding #52, CFF Encoding #51)
}

public abstract class FontCharacterMap { protected FontCharacterMap(); abstract int PlatformId, EncodingId, Format { get; } abstract int GetGlyphId(int code); }
public enum GlyphOutlineStatus { Complete, Empty, Invalid }
public readonly record struct GlyphMetrics(double AdvanceWidth, double LeftSideBearing);

public sealed class GlyphOutline                       // reusable buffer over Broadside.Graphics path types
{
    PathView Path { get; }  bool IsEmpty { get; }  void Clear();
    void MoveTo(double x, double y); void LineTo(double x, double y);
    void QuadTo(double cx, double cy, double x, double y);
    void CubicTo(double c1x, double c1y, double c2x, double c2y, double x, double y);
    void Close();
}
```

Registration and use:

- `PdfOptions.UseFontProgramParser(IFontProgramParser)`; each engine snapshots the list into `EngineConfiguration.FontProgramParsers` (`FontProgramParserRegistry`, internal). Selection: the first parser whose `CanParse` accepts the bytes, registrations newest first, then the managed defaults (`FontProgramParserRegistry.Defaults`: add your parser there, one line); else the first whose `Formats` contain the format the stream declares (`FontProgramParserRegistry.DeclaredFormat`: `FontFile` Type1, `FontFile2` TrueType, `FontFile3` `Type1C`/`CIDFontType0C` Cff, `OpenType` OpenType); else `FontProgramUnsupported` (Information) and no program. A parser accepting bytes of another format than declared gets `FontProgramFormatMismatch` (Warning; an OpenType stream read by a TrueType parser is not a mismatch). An exception other than `DiagnosticException` from `Parse` becomes `FontProgramInvalid` (Error).
- `PdfFont.Program` (public) parses the descriptor's first font file stream through `PdfDocument.GetFontProgram` (internal): decoded with the document's filters, parsed once per stream (an `OnceCache` keyed by the stream and its `Version`, so a changed stream is parsed again), diagnostics recorded once per code against the font file stream's reference.
- Glyph selection belongs to the PDF font: `PdfTrueTypeFont.GetGlyphId(byte)` implements §9.6.5.4 (`TrueTypeGlyphSelector`). #51/#52 supply a simple font's built-in encoding through `PdfSimpleFont.GetProgramEncoding()` (internal virtual, #49) and add the lookups their format has as new virtual members of `FontProgram` (built-in encoding, CID to GID, FDSelect): adding a virtual member with a "not available" default does not break other parsers.
- The sfnt container (`Fonts/TrueType/SfntFile`: table directory, TTC face selection, "name") is internal and meant to be shared with the OpenType-CFF parser (#51); "cmap" (`CmapSubtable`) and "post" (`PostTable`) readers likewise.
- Simple Type 1 fonts select glyphs through `PdfType1Font.GetGlyphId(byte)` (§9.6.5.2): the encoding's glyph name (whose base is `FontProgram.BuiltInEncoding` for an embedded font, through #49's `GetProgramEncoding`) looked up with `FontProgram.TryGetGlyphId`. A Type 1 or CFF program implements `BuiltInEncoding`, `TryGetGlyphId` and `GetGlyphName`. The Type 1 interpreter (`Fonts/Type1/Type1CharStringInterpreter`, #52) and the Type 2 one (`Fonts/Cff/Type2CharStringInterpreter`, #51) keep their own decoders but share `src/Broadside/Fonts/CharStrings/`: `CharStringLimits` (48 operands, 10 subroutine levels), the per-glyph budget `FontProgramContext.MaxCharStringOperators`, and `CharStringReporter` (shared FontCharstring* codes with the glyph id, once per kind per program in lenient mode; Error when the glyph is dropped, Warning for a repair). Type 2 also uses `CharStringPath`, `CharStringStack` and `StandardEncodingNames`. A later interpreter reuses these instead of its own limits and codes.
- Diagnostic codes (`Parsing/DiagnosticCodes.Fonts.cs`): `FontProgramUnsupported`, `FontProgramFormatMismatch`, `FontProgramInvalid`, `FontProgramTruncated`, `FontTableInvalid`, `FontGlyphInvalid`, `FontCmapInvalid`, `FontGlyphMappingFallback`. Glyph-level problems are reported when the glyph is asked for, so strict mode throws from `GetOutline`.
- Hot path: `GetOutline` into a reused `GlyphOutline` allocates nothing once warm (pooled point buffers, stack-allocated ancestor stack): prove it with an allocation test and `<Format>Benchmarks.Outlines` (`Allocated` = `-`).

### Responsibilities

A parser for one format must:

- accept the bytes of a `/FontFile` (Type 1), `/FontFile2` (TrueType) or `/FontFile3` (`/Subtype /Type1C`, `/CIDFontType0C`, `/OpenType`) stream (§9.9 Table 125), using `/Length1 /Length2 /Length3` when present and tolerating their absence or wrong values in lenient mode (ADR 0005), with a `Diagnostic` for every repair;
- expose, for a glyph selected by glyph index, CID or character name as the format allows, its outline as path segments in glyph space with the format's units-per-em or `FontMatrix`, and its advance width; `Pdf`-layer code maps character codes to glyphs through encodings (§9.6.6), CMaps (§9.7.5) and `/CIDToGIDMap` (§9.7.4.2), not the parser;
- expose the mapping tables the font dictionary layer needs: TrueType `cmap` subtables (3,1), (3,0), (1,0) for the §9.6.6.4 lookup rules, `post` glyph names; CFF charset and encoding; Type 1 `/Encoding` and charstring names; CID-keyed CFF `FDSelect`;
- expose the metrics needed when the font dictionary omits them: bounding box, ascent, descent, `hmtx`/`hhea` advances, CFF `nominalWidthX`/`defaultWidthX`;
- parse lazily and allocate nothing per glyph after the tables are located: charstring interpretation (Type 1, Type 2 with hints, `hintmask`, subroutines, `seac`/`endchar` accents, flex) and `glyf` composite resolution run on a hot path (`CLAUDE.md`, "Code conventions");
- be safe on hostile input: every offset and count is bounds-checked, recursion (composite glyphs, subroutine depth) is capped, and the parser never throws in lenient mode on a malformed program; a program that cannot be parsed at all yields a diagnostic and the glyphs fall back to `.notdef`;
- state which format(s) it handles so the engine can pick a parser from the `/Subtype` of the font file stream and, when that lies, from the program's magic bytes (`OTTO`, `true`, `0x00010000`, `%!PS-AdobeFont`, `0x80` PFB segment header, CFF header `01 00`).

Registration goes through the options object (`PdfOptions.Use*`, plan row 1.12), per engine instance. No static registry: a consumer substitutes a parser for one engine without affecting another, and tests inject fakes.

## Managed default

The core ships parsers for every format a PDF can embed: TrueType (`glyf`, `loca`, `cmap`, `post`, `hmtx`, `head`, `hhea`, `maxp`, composite glyphs), OpenType wrapper (`CFF ` table), CFF with Type 2 charstrings and CID-keyed CFF, Type 1 (PFA/PFB segments, eexec, Type 1 charstrings), Type 3 (content streams, handled by the interpreter rather than a parser). Written in C# with `System.*` only (ADR 0001). The references in `Specs/References/` (OpenType specification, Adobe Type 1 Font Format, CFF and Type 2 Charstring Format) are the sources to cite.

## Deliverables of a parser PR

1. Implementation in `Broadside.Fonts.<Format>`, `sealed` types, XML docs on every public member citing `<remarks>ISO 32000-2 §9.9</remarks>` and the format reference (e.g. "OpenType `glyf` table", "Type 1 Font Format §6.4").
2. `PublicAPI.Unshipped.txt` entries, explained in the PR body.
3. Conformance rows: `9.9` (Embedded font programs) and the dictionary-level clauses touched (`9.6.3`, `9.7.4`, `9.8`) in `docs/conformance/iso-32000-2.md`, split to third level.
4. Corpus file `text-<format>-embedded.pdf` from a synthesized font (skill `writing-a-corpus-file`), plus a broken variant (truncated program, wrong `/Length1`) in the broken-files table.
5. Unit tests: table parsing against the synthesized font, charstring interpretation of each operator, composite and subroutine depth limits, lenient vs strict on the broken variant, and a corpus test that every font in `corpus/` (fetched by `tools/CorpusFetcher`) parses without an exception.
6. Fuzz target `font-<format>` (skill `adding-a-fuzz-target`), seeded from the program extracted out of the corpus file (`qpdf --show-object=7 --raw-stream-data tests/Corpus/text-truetype-embedded.pdf`).
7. Benchmark `<Format>Benchmarks` with `[MemoryDiagnoser]`: outline extraction for every glyph, `Allocated` at `-` (skill `adding-a-benchmark`).

## Worked example: the TrueType parser

What #50 shipped, as the pattern to follow:

- Corpus: `text-truetype-embedded.pdf` (`.notdef`, `H`, `I`), plus `text-truetype-composite.pdf` (off-curve contours, every composite argument and transform form, USE_MY_METRICS, a monospace "hmtx" tail, lsb != xMin), `text-truetype-symbolic.pdf` ((3,0) before (1,0)), `text-truetype-macroman.pdf` ((1,0) format 6, Table 113, "post" 2.0) and `text-truetype-loca-long.pdf` (long "loca", odd glyph, (3,10) format 12), all from `ttf_font()` and `TtfGlyph` in `generate.py`, verified against poppler/FreeType renders.
- Tests in `tests/Broadside.Tests/Fonts/`: `TrueTypeCorpusTests` (document API over the corpus: exact outline text through `OutlineText`), `TrueTypeProgramTests` (the contract over in-memory programs from `tests/Broadside.TestSupport/TrueTypeBuilder.cs`: composite math vectors, every malformed-table case, strict mode, allocation-free outlines), `TrueTypeGlyphSelectionTests` (§9.6.5.4 corner cases, diagnostics on the font file stream, reparse after a change), `FontProgramParserRegistrationTests` (counting decorator parsed once, replacement parser, second engine unaffected), `RealWorldTrueTypeTests` (every embedded TrueType program of the fetched corpora outlines every glyph).
- Fuzz target `font-truetype` (an input starting with `%PDF-` is read from its first `00 01 00 00`, so the corpus files seed it) and `document` now outlines the glyph of every code of each TrueType font. Benchmark `TrueTypeBenchmarks` (`Outlines`, `GlyphIds`, `Parse`; composite and a real-world font).
- Conformance rows `9.6.3`, `9.6.5.4` (done) and `9.9.1` (partial: the other formats are yours to add to its types and tests), `9.9.2` (not started).

## Checklist

- [ ] Parser in `Broadside.Fonts`, managed only, `sealed`, clause-cited XML docs with the format reference
- [ ] Registered through `PdfOptions`, not a static registry
- [ ] Bounds-checked, recursion-capped, lenient on malformed programs with a `Diagnostic`
- [ ] `PublicAPI.Unshipped.txt`, conformance rows, corpus file from a synthesized font, unit tests, fuzz target, benchmark
- [ ] No per-glyph allocation after table lookup (`Allocated` reads `-`)
