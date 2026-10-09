---
name: adding-a-font-program-parser
description: "Use when implementing a parser for an embedded or substituted font program format (TrueType, OpenType, CFF/Type 2, Type 1, CID-keyed CFF, Type 3) behind the font program parser extension point in Broadside.Fonts."
---

# Adding a font program parser

> **The font program parser contract is defined by the Phase 2A Fonts track (`docs/plan/README.md`, "Track 2A Fonts"); its issues are not filed yet. When the contract issue lands, replace the "Contract" section of this skill with the real signatures from `src/Broadside/Fonts/` and `src/Broadside/PublicAPI.Unshipped.txt`.** Until then this is a design brief: responsibilities and deliverables, not method names.

A font program is "the embedded or substituted glyph data in one of the formats PDF allows: TrueType, OpenType, CFF, Type 1, or Type 3 content streams" (`CONTEXT.md`). Font program parsers are extension points with a managed default (ADR 0001); the font resolver (which finds a program for a non-embedded font) is a separate extension point and not covered here.

## Where it lives

- Contract and parsers: namespace `Broadside.Fonts` in `src/Broadside/`, one sub-namespace or folder per format (`Fonts/TrueType`, `Fonts/Cff`, `Fonts/Type1`). Standard 14 metrics (AFM) are in the core; their glyphs ship in `src/Broadside.Fonts.Standard14` (ADR 0007). Predefined CMaps ship in `src/Broadside.Fonts.Cmaps`.
- Tests: `tests/Broadside.Tests/Fonts/<Format>/`, namespace `Broadside.Tests.Fonts`.
- Corpus: `tests/Corpus/text-<format>-embedded.pdf`. `text-truetype-embedded.pdf` exists, built by `minimal_truetype()` in `generate.py`; the font is synthesized (`.notdef`, `H`, `I` as rectangles), carries no licence, and every other format follows that pattern. Never embed a real typeface.
- Fuzz: target `font-<format>` over the raw program bytes. Benchmarks: `bench/Broadside.Benchmarks/<Format>Benchmarks.cs`.

## Contract (responsibilities, not signatures)

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

`tests/Corpus/text-truetype-embedded.pdf` embeds a font with tables `head hhea maxp OS/2 hmtx cmap(3,1 format 4) loca glyf name post` and the glyph set `.notdef`, `H` (code 72), `I` (code 73), 1000 units per em. The parser reads the table directory, locates the tables by tag, resolves `loca` by `indexToLocFormat`, and returns two rectangles for `H` and `I` with the widths `hmtx` stores; `cmap` (3,1) maps 0x48 and 0x49 to glyphs 1 and 2. Tests assert exactly that, since the generator defines every value. A `font-truetype` fuzz target parses the whole program and walks every glyph; `TrueTypeBenchmarks.Outlines` extracts every glyph's outline and returns the segment count. Rows `9.9` and `9.6.3` become `partial` or `done` with those tests named.

## Checklist

- [ ] Contract section of this skill replaced with the real signatures once the 2A contract issue has landed
- [ ] Parser in `Broadside.Fonts`, managed only, `sealed`, clause-cited XML docs with the format reference
- [ ] Registered through `PdfOptions`, not a static registry
- [ ] Bounds-checked, recursion-capped, lenient on malformed programs with a `Diagnostic`
- [ ] `PublicAPI.Unshipped.txt`, conformance rows, corpus file from a synthesized font, unit tests, fuzz target, benchmark
- [ ] No per-glyph allocation after table lookup (`Allocated` reads `-`)
