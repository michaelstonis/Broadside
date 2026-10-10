# Broadside build plan

Broadside is a fully managed .NET implementation of ISO 32000-2 with a cross-platform, cross-backend rendering layer and, later, viewer controls. This plan is the top of the work tree. Every phase lists its tracks; tracks inside a phase share nothing but the phases below them, so they run in parallel across agents. Decisions behind the plan are in `docs/adr/`; vocabulary is in `CONTEXT.md`; what is implemented is in `docs/conformance/`.

## Packages

| Package | Contents | Native deps |
|---|---|---|
| `Broadside` | COS layer, document model, filters, fonts, images, content interpreter, display list + backend contract, text extraction, layout tree, structure, forms, annotations, security, signatures, canvas writing, diagnostics, DI registration | none |
| `Broadside.Rendering` | Software rasterizer, ICC color engine, PNG encoder | none |
| `Broadside.Rendering.Skia` | SkiaSharp backend (CPU and GPU surfaces) | SkiaSharp |
| `Broadside.Rendering.CoreGraphics` | macOS, iOS, Mac Catalyst backend | Apple bindings |
| `Broadside.Rendering.Direct2D` | Windows backend via CsWin32 | Windows |
| `Broadside.Rendering.Android` | Android Canvas backend | Android bindings |
| `Broadside.Fonts.Standard14` | Liberation + Foxit Symbol/Dingbats glyph data | none |
| `Broadside.Fonts.Cmaps` | Adobe predefined CMaps and CID-to-Unicode tables | none |
| `Broadside.Layout` | Flow-layout document engine over the canvas API | none |
| `Broadside.Viewer.*` | Shared viewer core + MAUI, Uno, Avalonia, WPF, WinUI, Blazor shells (post-Phase 3) | per framework |

Target framework: `net10.0` only (ADR 0002). License: MIT (ADR 0008).

## Capability order

Read → Render → Write → Edit, then the cross-cutting capabilities (extraction, layout, signatures, subset standards). Rendering comes before writing because it is the differentiator and the hardest to get right.

## Phases

### Phase 0: Scaffold

Single track. Everything later depends on it.

- Solution, `Directory.Build.props` (nullable, warnings as errors, analyzers, CPM, deterministic, SourceLink), `global.json`, MinVer.
- Empty projects for every package above except Viewer and Layout, with `PublicAPI.Shipped.txt`/`Unshipped.txt`.
- Test projects (xUnit v3), `bench/` (BenchmarkDotNet), `tools/` skeletons, SharpFuzz harness project.
- CI: build + test on ubuntu, windows, macos; API-diff check; conformance-map checker; fuzz smoke (60 s per target).
- `tools/CorpusFetcher`: downloads pdf.js, PDFBox, veraPDF, GhentPDF, PDF Association 2.0 examples and SafeDocs corpora into gitignored `corpus/`.
- `tests/Corpus/`: hand-written minimal PDFs, one feature per file, with a README describing each.
- `docs/conformance/` skeleton per spec (clause tables), CI test that `done` rows have tests.
- Docs site with Lunet (.NET static site generator, API reference straight from XML docs, no Node; see `docs/research/doc-site-tooling.md`) publishing to GitHub Pages.
- GitHub: issue template, labels (`phase:N`, `track:*`, `area:*`, `in-progress`), milestones per phase, project board.
- `CLAUDE.md`, `CONTRIBUTING.md` (DCO), `SPONSORS.md`, `README.md`.

### Phase 1: COS layer (Read foundation)

Single track with internal parallelism once the lexer exists.

- Lexer over `ReadOnlySpan<byte>` (§7.2 character set, tokens, comments). No allocation per token.
- COS object types (§7.3) with dirty tracking (ADR 0004).
- Object parser, indirect objects, object streams (§7.5.7), cross-reference tables and streams (§7.5.4, §7.5.8), hybrid files, incremental-update chains (§7.5.6), trailer, linearization hints read (Annex F).
- Cross-reference reconstruction by scanning when the table is wrong; stream `/Length` recovery; `startxref` recovery (ADR 0005). Diagnostics model.
- File source abstraction: in-memory, memory-mapped, seekable `Stream`; object cache with the concurrency contract of Q19 (read-safe, mutate-single-threaded).
- Standard filters (§7.4): Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors (TIFF and PNG). Crypt filter. DCT/CCITT/JBIG2/JPX are Phase 2 tracks behind the same `Filter` extension point.
- Standard security handler (§7.6): RC4 40/128, AES-128, AES-256 (R5 and R6), AES-GCM (ISO/TS 32003), public-key security handler read (§7.6.5), integrity protection (ISO/TS 32004). Decrypt on read.
- COS serializer: write any object graph back out, with cross-reference streams and object streams; byte-exact round trip of unchanged objects.
- Engine and options: `PdfEngine`, `PdfOptions` fluent surface, `services.AddBroadside()`, `ILogger` wiring.
- Document skeleton: `PdfDocument.Open/Create/Save`, catalog, page tree (§7.7), page inheritance, `PdfPage` with media/crop/bleed/trim/art boxes and rotation.

Exit criteria: every file in the pdf.js and PDFBox corpora opens without an exception in lenient mode; strict mode agrees with veraPDF on the well-formed subset; fuzzing finds no crash in 24 h; round-trip serialization is byte-identical for untouched objects.

Evidence (issue #48): real fuzzing of every Phase 1 target runs weekly in [`.github/workflows/fuzz.yml`](../../.github/workflows/fuzz.yml) (more than 24 h of fuzzing per run, findings fail the run); the performance baseline the Phase 3I regression gate compares against, with allocation figures, is [`bench/baselines/phase1/`](../../bench/baselines/phase1/README.md).

### Phase 2: Model and codecs (four tracks)

**Track 2A Fonts** (§9): font dictionaries and encodings (§9.6), Type 1 (eexec, charstrings Type 1), CFF/Type 2 charstrings and CID-keyed CFF, TrueType (glyf, loca, cmap, post, hmtx; composite glyphs), OpenType wrapper, Type 3, Type 0 composite fonts with CIDFontType0/2, CMaps embedded and predefined (package), AFM metrics for the Standard 14, font-matching for substitution, ToUnicode. Glyph outline output as paths in glyph space. Font program parsers are extension points.

**Track 2B Images** (§8.9): image dictionaries, decode arrays, color key masking, soft masks, stencil masks, inline images; DCTDecode (baseline, extended, progressive, arithmetic optional; CMYK and YCCK with Adobe marker), CCITTFaxDecode (G3 1D/2D, G4), JBIG2Decode (generic, refinement, symbol dictionary, text region, pattern, halftone, MMR; embedded stream organization), JPXDecode (Part 1: tiles, precincts, code-blocks, EBCOT, 5/3 and 9/7 wavelets, RCT/ICT, ROI; colorspace and SMask-in-data handling). Decoded output is a planar or interleaved sample buffer with a declared color space.

**Track 2C Document model** (§7.7, §12, §14): name trees and number trees, page labels, outlines, destinations, actions (parse-and-preserve for JavaScript and multimedia), viewer preferences, annotations (all §12.5.6 subtypes as typed classes, appearance streams), AcroForm read (§12.7) including field hierarchy and inheritance, structure tree read (§14.7, §14.8, ISO/TS 32005 namespaces), marked content, metadata streams and XMP read, associated files (AN002), object metadata locations (AN003), optional content (§8.11), embedded files, collections, PDF Declarations.

**Track 2D Content interpreter** (§8, §9.4): content stream lexer and operator stream, graphics state machine (§8.4) including ExtGState, color spaces (§8.6: Device*, CalGray/RGB, Lab, ICCBased via alternate until Phase 3 ICC, Indexed, Separation, DeviceN, Pattern) with the color-space extension point, shadings 1–7 (§8.7.4) as a resolved model, tiling patterns, text state and text object positioning (§9.4), Type 3 glyph procedures, form XObjects, inline images, marked content events, PostScript calculator functions (§7.10.5) and sampled/exponential/stitching functions. One interpreter, processors consume (ADR: Q40).

Exit criteria per track: conformance rows for the listed clauses `done`; corpus-driven tests that every font and image in the corpora decodes; interpreter replays every content stream in the corpora without diagnostics on well-formed files.

Track 2D exit gate (#80): `tests/Broadside.Tests/Content/CorpusGate/` interprets every page and annotation appearance of every fetched corpus file, with no exception and every diagnostic on a well-formed file triaged in `content-diagnostics.allowlist.txt`; `.github/workflows/corpus.yml` runs it weekly. The interpretation baseline over a pinned real-world subset is [`docs/benchmarks/phase-2-content-baseline.md`](../benchmarks/phase-2-content-baseline.md).

### Phase 3: Rendering (parallel tracks after 3A)

**Track 3A Display list** (ADR 0006): struct-based op buffer, interned resources (decoded images, glyph outlines, shadings), text runs with font reference + glyph ids + positions, clip stack, transparency group markers, annotation appearance ops as a separate layer, hit-test index for text. Render options per Q39 (scale/DPI, rotation, clip rectangle, layers, intent, anti-aliasing, `ForceOutlines`, cancellation, progress). The display list is replayable on any thread.

Then in parallel:

- **3B Software rasterizer**: scanline path filler with analytic anti-aliasing, non-zero and even-odd, strokes (joins, caps, dashes, minimum width), clipping (path and text, intersect), image drawing with transform and resampling (bilinear, and box-filter downscale), shading rasterization for types 1–7, tiling patterns, full transparency model (§11: groups isolated/non-isolated, knockout, soft masks alpha/luminosity, 16 blend modes, constant alpha, SMask in ExtGState), stroke adjustment, text render modes incl. clipping. Output premultiplied BGRA8. This backend is the oracle.
- **3C ICC engine** (`Broadside.Rendering`): matrix/TRC and LUT (mft1, mft2, mAB, mBA) profiles, rendering intents, Black Point Compensation (AN001), sRGB and CMYK default profiles, cache of transforms. Color-management extension point was defined in 2D.
- **3D Skia backend**: path/text/image/shader mapping, GPU surface support, native glyph rendering with `ForceOutlines` fallback, group fallback to 3B where Skia cannot express a blend/knockout.
- **3E CoreGraphics backend**, **3F Direct2D backend** (CsWin32), **3G Android backend**: same contract, same fallback rules.
- **3H Golden-image suite**: every corpus page rendered by 3B at fixed DPI becomes a snapshot; backends must match within a perceptual threshold; `tools/RenderDiff` compares against PDFium and pdf.js renders for correctness triage.
- **3I Benchmarks**: parse, render, extract baselines vs PDFium and pdf.js; CI regression gate.

Viewer-driven requirements folded in here (from `docs/research/pdf-viewer-landscape.md`): tiled region rendering from one cached display list; cancellation and progress on every render call; thread-safe replay for render-ahead; annotation and form layers as separate replayable lists; hit-test index for selection; deterministic memory budgets for the image and glyph caches; linearization-aware loading from range-capable sources for Phase 5.

### Phase 4: Write and Edit (parallel tracks)

- **4A Canvas API**: `PdfCanvas` over a page or form XObject: paths, transforms, clipping, text (simple and composite fonts), images, shadings, patterns, transparency, marked content and tags, optional content. Font embedding with subsetting (TrueType glyf subsetting, CFF subsetting, Type 1 to CFF conversion), encoders (Flate, DCT baseline, PNG predictors), image placement from PNG/JPEG/raw buffers. Version raising per ADR 0003.
- **4B Incremental update and Edit**: dirty-tracked save as incremental update, full rewrite with object renumbering and garbage collection, page insertion/removal/reorder, document merge and split, object copying across documents with reference remapping, metadata and XMP write, attachment write.
- **4C Forms**: field creation, fill, appearance stream regeneration (needs 4A), flatten, XFA detection with diagnostic.
- **4D Flow layout** (`Broadside.Layout`): text blocks with line breaking (Unicode UAX #14), bidi (UAX #9), OpenType shaping (GSUB/GPOS basic features, kerning, ligatures, mark positioning), paragraphs, lists, tables with spans and page breaking, images, headers/footers, page templates. Emits tags via 4A so output is tagged by construction.
- **4E Encryption write**: all handlers from Phase 1 in write mode; public-key encryption with `System.Security.Cryptography.Pkcs`.

### Phase 5: Cross-cutting (parallel tracks)

- **5A Text extraction** (`Text`): glyph positions from the 2D interpreter, Unicode via ToUnicode/encodings/CID collections, word and line assembly, reading order from the structure tree when tagged.
- **5B Layout tree and auto-tagging** (`Layout` namespace): geometric detection of columns, paragraphs, headings, lists, tables with cell spans, figures and captions, reading order, producing the same tree as the structure tree; writing a detected tree back as a structure tree; headless and Type 3 edge cases.
- **5C Signatures**: CMS/PAdES signing (RSA, ECDSA per ISO/TS 32002, SHA-2 and SHA-3 per ISO/TS 32001), RFC 3161 timestamps, DSS and LTV, visible signature appearances, validation (byte-range integrity, certificate chain, OCSP/CRL, timestamp verification, modification detection via incremental-update analysis), document MDP and field locks.
- **5D Subset standards**: PDF/A-2b, 2u, 3b, 4 output conformance (color profile embedding, font requirements, metadata, prohibited features), PDF/UA-1 and 2 output over the tagging pipeline, Well-Tagged PDF rules; validation of our own output against veraPDF in CI.
- **5E Streaming sources**: range-request `HttpClient` source with linearization hints, first-page-first loading.

### Phase 6: Viewer (after Phase 3; parallel with 4 and 5)

Shared viewer core: document session, page layout modes (single, continuous, facing, with and without snapping), tile scheduler with render-ahead and viewport priority, zoom with low-res-first progressive paint, selection over the hit-test index, search with background indexing, outline and thumbnails, annotation and form layers, smart dark mode, memory budget. Then one shell per framework. Capability targets are the ranked list in `docs/research/pdf-viewer-landscape.md` §5.

## Dependency graph

```
Phase 0 → Phase 1 → {2A, 2B, 2C, 2D} → 3A → {3B, 3C, 3D, 3E, 3F, 3G, 3H, 3I}
                                      ↘ 4A (needs 2A, 2B, 2D) → {4C, 4D}
                                        4B (needs Phase 1 + 2C)
                                        4E (needs Phase 1)
                                        5A (needs 2A, 2D) → 5B (needs 5A, 2C) → 5D (needs 5B, 4A)
                                        5C (needs Phase 1, 4B, 4A for appearances)
                                        5E (needs Phase 1)
                     Phase 6 (needs 3A–3D, 5A; 5B improves it)
```

## Ticket lists

- [Phase 0](phase-0-issues.md) (scaffold, horizontal by nature)
- [Phase 1](phase-1-issues.md) and [Phase 2](phase-2-issues.md): vertical slices of the [spec](spec-broadside-v1.md), published as sub-issues of #33
- Phases 3 to 6: cut when Phase 2 is underway

## Working agreement

- Every task is a GitHub issue created from the template, in a milestone, with phase/track/area labels. Agents self-assign, add `in-progress`, work in a worktree, open a PR that closes the issue, and update the conformance map in the same PR.
- CI must be green. Agents self-merge during Phase 0 and 1; from Phase 2 the owner reviews and merges.
- Public API changes show up in `PublicAPI.Unshipped.txt`; a PR that changes it says why.
- Every spec-implementing type and member cites its clause in XML docs.
- Hot paths (lexer, interpreter, rasterizer, codecs) have benchmarks and allocate nothing per token, operator, glyph or scanline.
