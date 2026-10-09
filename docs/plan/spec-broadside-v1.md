# Spec: Broadside v1

Umbrella specification for the library as planned on 2026-10-09. Vocabulary is from `CONTEXT.md`; decisions it restates are recorded in `docs/adr/`; the build order is in `docs/plan/README.md`. Phase issues cite this spec; this spec does not replace them.

## Problem Statement

A .NET developer who needs to open, create, change, or display PDF files today has to choose between commercial SDKs with opaque per-developer or per-deployment licensing, open-source libraries that cover only a slice of the format (no rendering, no fonts, no signatures), and wrappers over native engines (PDFium, MuPDF) that break on whichever platform the native build does not cover and behave differently on each one. None of them is fully managed, none renders the same on every platform, none produces an accessible, tagged document from an untagged one, and none can be dropped into a Blazor WebAssembly app. The developer ends up with two or three PDF dependencies, each with its own bugs, licenses and platform gaps.

## Solution

Broadside is one MIT-licensed, fully managed .NET 10 library that reads any PDF (including damaged ones), writes new documents, edits existing ones without disturbing what it did not touch, and renders pages identically through a software rasterizer, SkiaSharp, CoreGraphics, Direct2D and Android Canvas. It extracts text and recovers a layout tree from any page, tags untagged documents, signs and validates signatures, encrypts, and produces PDF/A and PDF/UA output. Everything that interprets the file is in C#; backends are separate packages. Consumers configure it through a fluent options object or dependency injection, and viewer controls for every major .NET UI framework follow on the same rendering core.

## User Stories

### Reading
1. As an application developer, I want to open any PDF file from a path, stream or byte array, so that my application never rejects a document a user can open in Acrobat.
2. As an application developer, I want damaged files (bad cross-reference offsets, missing keywords, wrong stream lengths, missing trailers) to open with a list of diagnostics, so that I can decide whether to trust the repair.
3. As an application developer, I want a strict mode that throws on the first deviation, so that I can validate files I produce myself.
4. As an application developer, I want to open password-protected files (RC4, AES-128, AES-256, AES-GCM, certificate-based), so that encrypted documents are not a special case.
5. As an application developer, I want the raw COS objects exposed, so that I can read structures the typed model does not know about.
6. As an application developer, I want the typed document model (pages, fonts, images, annotations, form fields, outlines, structure tree) as a live view over COS, so that edits in either layer are visible in the other.
7. As a server developer, I want to open a multi-gigabyte file from a seekable stream without loading it all, so that memory stays bounded.
8. As a server developer, I want one loaded document to be safe for concurrent reads and renders from many threads, so that I can render pages in parallel.
9. As a library consumer on a legacy stack, I want earlier PDF versions read into the same PDF 2.0 model, so that I never branch on version.
10. As an application developer, I want JavaScript, XFA, 3D and multimedia objects preserved on round trip even though they are never executed, so that saving a file does not lose anything.

### Rendering
11. As an application developer, I want to render a page to a bitmap at a given scale with zero native dependencies, so that it works in a bare Linux container and in Blazor WebAssembly.
12. As a desktop or mobile developer, I want the same page rendered through SkiaSharp, CoreGraphics, Direct2D or Android Canvas, so that I can use the GPU and the platform's text rendering.
13. As a developer, I want output across backends to match within a perceptual threshold, with a force-outlines option for bit-identical results, so that my app looks the same everywhere.
14. As a viewer developer, I want to render only a clip rectangle of a page from a cached display list, so that tiled and zoomed rendering is cheap.
15. As a viewer developer, I want every render call to accept a cancellation token and report progress, so that scrolling never freezes the UI.
16. As a viewer developer, I want page content, annotations and form fields rendered as separately toggleable layers, so that overlays can be redrawn without re-rasterizing the page.
17. As a viewer developer, I want a hit-test index from the display list, so that text selection and link clicks map screen points to content.
18. As a developer, I want the full transparency model (groups, soft masks, all blend modes, knockout, isolation) honored, so that print-oriented files look right.
19. As a developer, I want ICC-based color managed with Black Point Compensation, so that PDF/A files render with correct color.
20. As a developer, I want shadings of all seven types, tiling patterns, Type 3 fonts and inline images rendered, so that no page is partially blank.
21. As a developer, I want non-embedded Standard 14 fonts rendered from a separately packaged font set or the operating system, so that text never silently disappears.
22. As a developer, I want CJK documents with predefined CMaps to render from a separately packaged CMap set, so that the core stays lightweight.

### Writing
23. As an application developer, I want to create a document and draw paths, text, images, shadings and transparency on a canvas, so that I can generate reports and receipts.
24. As an application developer, I want fonts embedded and subsetted automatically, so that output files are small and portable.
25. As an application developer, I want the file's PDF version raised automatically to the minimum my features need, or an error in strict mode, so that I never emit an invalid version header.
26. As an application developer, I want a flow-layout engine (paragraphs, lists, tables with page breaks, headers and footers, bidi and shaped text), so that I do not position every line by hand.
27. As an application developer, I want documents produced through the flow engine to be tagged by construction, so that they are accessible without extra work.
28. As an application developer, I want to write encrypted files with any supported handler, so that I can protect what I produce.

### Editing
29. As an application developer, I want to change a document and save it as an incremental update, so that existing signatures and untouched bytes survive.
30. As an application developer, I want to insert, remove, reorder, merge and split pages across documents, so that assembly workflows need no other tool.
31. As an application developer, I want to fill form fields, regenerate their appearances and flatten them, so that completed forms print correctly everywhere.
32. As an application developer, I want XFA forms detected with a clear diagnostic, so that I can tell users why a form cannot be filled.
33. As an application developer, I want to add, edit and remove annotations, outlines, metadata, attachments and page labels, so that common document operations are all in one library.

### Extraction and layout
34. As a developer, I want text with glyph positions and correct Unicode, so that search and indexing work on any file.
35. As a developer, I want reading order from the structure tree when the file is tagged, so that extracted text reads like the page.
36. As a developer, I want a layout tree (columns, paragraphs, headings, lists, tables with cell spans, figures, captions) recovered from untagged pages, so that I can extract tables and reflow content.
37. As a developer, I want the layout tree to have the same shape for tagged and untagged files, so that my code has one path.
38. As a developer, I want an untagged document auto-tagged from its detected layout tree, so that I can make existing documents accessible.

### Signatures and conformance
39. As a developer, I want to sign documents (CMS/PAdES, RSA and ECDSA, SHA-2 and SHA-3) with timestamps and long-term validation data, so that signed output is legally usable.
40. As a developer, I want to validate signatures including chains, revocation, timestamps and modification detection, so that I can trust a signed document.
41. As a developer, I want PDF/A-2, PDF/A-3, PDF/A-4 and PDF/UA conformant output, so that archival and accessibility requirements are met.
42. As a developer, I want the library's own conformant output validated against veraPDF in CI, so that conformance claims are tested, not asserted.

### Developer experience
43. As a developer, I want a fluent entry point (`Open`, `Create`, options via `Use*`/`With*`) and a DI registration that share one engine, so that simple and hosted applications behave the same.
44. As a developer, I want every extension point (filters, font program parsers, font resolver, color management, backends, security handlers) replaceable through the options object, so that I can plug in a native codec or a company font store.
45. As a developer, I want optional logging through the standard abstractions, so that diagnostics flow into my existing pipeline.
46. As a developer, I want trimming and AOT compatibility, so that mobile and WebAssembly builds stay small.
47. As a developer, I want packages whose public API changes are explicit in each release, so that upgrades are predictable.
48. As a developer, I want every public type that implements a spec concept to cite its clause, so that I can check behavior against ISO 32000-2.

### Project and community
49. As a contributor or agent, I want a per-clause conformance map with linked tests, so that I can see what is done and pick the next gap.
50. As a contributor, I want a corpus of minimal one-feature PDFs and a fetcher for real-world corpora, so that tests are reproducible.
51. As a maintainer, I want fuzzing on every parser and codec, so that malformed input never crashes a host process.
52. As a maintainer, I want benchmarks with a regression gate, so that performance does not erode.
53. As a sponsor, I want sponsorship to buy priority and support but never access, so that the library stays fully open.
54. As a viewer-control consumer (later phase), I want a shared viewer core with thin shells for MAUI, Uno, Avalonia, WPF, WinUI and Blazor, so that behavior is identical across frameworks.

## Implementation Decisions

- **Fully managed core.** Everything that interprets the file (parsing, writing, fonts, image codecs, cryptography, content interpretation) is C# with no dependency outside Microsoft-shipped packages that carry no native component. Backends other than the software rasterizer are separate packages and may depend on their platform library. (ADR 0001)
- **net10.0 only**, no multi-targeting. (ADR 0002)
- **PDF 2.0 is the canonical model.** Earlier versions are read into it; on write, features declare a minimum version and the header is raised, or strict mode throws. (ADR 0003)
- **Two public layers.** COS objects (`Cos*` types) and the document model (`Pdf*` types) as live views, never copies. Dirty tracking on COS objects from the start to support incremental update. (ADR 0004)
- **Lenient by default with diagnostics**, strict mode opt-in. Cross-reference reconstruction by scanning, stream-length recovery, `startxref` recovery. (ADR 0005)
- **Display list.** Interpretation produces a struct-based, device-independent operation buffer with interned resources; every backend consumes it; immediate rendering is interpret-then-replay. Glyph runs carry font program, glyph ids and positions; backends may render glyphs natively, with a force-outlines render option. Transparency groups a backend cannot express fall back to the software rasterizer per group. (ADR 0006)
- **One content interpreter**, many processors: the display-list builder, the text extractor, the layout detector and the editor consume one interpreted stream from one graphics-state machine; nothing re-parses content.
- **Memory and I/O.** Random access over a source abstraction (in-memory, memory-mapped, seekable stream); lazy object loading through the cross-reference table with an object cache; synchronous core with `Async` only at open and save boundaries.
- **Thread safety.** A document is safe for concurrent reads and renders when unmutated; mutation is single-threaded and the caller's responsibility.
- **Configuration.** A fluent options object passed at open or create; `Use*` selects an implementation, `With*` sets a value; a DI registration binds the same options and exposes one engine. No static or global registries.
- **Extension points** with managed defaults: stream filters, font program parsers, font resolver, color management, rendering backends, security handlers. Each is an interface registered through the options object.
- **Fonts.** Standard 14 metrics ship in the core; glyphs ship in a separate package; the default resolver falls back to operating-system fonts with a diagnostic. Predefined CMaps and CID-to-Unicode tables ship in a separate package; Identity CMaps and embedded CMaps are in the core. (ADR 0007)
- **Codecs.** Flate via the BCL; LZW, ASCII, RunLength, predictors, DCT (baseline and progressive), CCITT G3/G4, JBIG2 (all region types) and JPEG 2000 Part 1 implemented in managed code, in that order, each behind the filter extension point. Encoders: Flate always, DCT baseline for photos.
- **Color.** ICC-based spaces resolve through a managed ICC engine (matrix/TRC and LUT profiles, rendering intents, Black Point Compensation) behind the color-management extension point; until it lands, the alternate space is used.
- **Render options.** Scale or DPI, rotation, page-space clip rectangle, layer toggles (content, annotations, form fields), rendering intent (view or print), per-category anti-aliasing, force outlines, cancellation token, progress callback. Software output is premultiplied BGRA 8-bit into a caller-provided or pooled buffer.
- **Writing.** Canvas-style API over a page or form XObject with font embedding and subsetting; a separate flow-layout package built on it that emits structure tags by construction.
- **Editing.** Incremental-update save from dirty tracking, full rewrite with renumbering and garbage collection, cross-document object copying with reference remapping.
- **Text and layout.** Extraction yields positioned glyphs with Unicode and reading order; the layout tree is one type produced from the structure tree or from geometric detection; auto-tagging writes a detected tree back as a structure tree.
- **Signatures.** CMS/PAdES creation and validation with RFC 3161 timestamps and DSS/LTV, using Microsoft's PKCS package.
- **Subset standards.** PDF/A-2b, 2u, 3b, 4 and PDF/UA-1, 2 output conformance; validation against veraPDF in CI; full input validation is out of v1 scope.
- **Packages.** Core, Rendering (software rasterizer and ICC), one package per backend, Fonts.Standard14, Fonts.Cmaps, Layout, Viewer.* later.
- **Namespaces.** One per area under the root: Objects, Filters, Fonts, Images, Graphics, Content, Rendering, Text, Layout, Structure, Forms, Annotations, Security, Signatures, Writing, Diagnostics; the document model lives in the root.
- **Conventions.** Analyzers at the strictest level with warnings as errors, public API tracked per package, trimming and AOT flags on, clause citations in XML docs, Conventional Commits, DCO.
- **Process.** GitHub issues from a template with phase, track and area labels and per-phase milestones; a project board; the conformance map updated in the same PR as code; self-merge in Phases 0 and 1, owner review from Phase 2.

## Testing Decisions

- A good test drives a public seam with a real PDF or a spec-derived vector and asserts observable output: parsed values, diagnostics, extracted text, a rendered image, a written file that round-trips. Tests never reach into parser internals or assert on intermediate state.
- **Primary seam: the public document API driven by corpus files.** Every capability (open, read model, render, extract, write, edit, sign) is tested through `PdfDocument` with inputs from `tests/Corpus/` (hand-written, one feature per file, byte-deterministic from a generator script) and from fetched real-world corpora (pdf.js, PDF 2.0 examples, veraPDF, PDFBox, pdfium, qpdf, GWG). Malformed corpus files assert the exact diagnostics a lenient open records and that strict mode throws.
- **Second seam: the display list.** Rendering correctness is asserted on the display list and on the software rasterizer's output. Backends are tested by comparing their output to the software rasterizer within a perceptual threshold (golden images), never by their own private assertions. A development-only diff tool compares against PDFium and pdf.js renders for triage.
- **Third seam: the filter and font-program contracts.** Codecs and font parsers are tested as bytes in, samples or glyph outlines out, with vectors from the specifications (ITU, Adobe technical notes) and from the corpora. No seam below this.
- **Property and fuzz testing.** Every parser and codec gets a fuzz target in the same PR that adds it; a corpus-seeded smoke run executes in CI on every change and real fuzzing runs on a schedule.
- **Round-trip tests.** Serializing an unchanged document reproduces untouched objects byte for byte; incremental saves append only.
- **Snapshot tests** (Verify) for extracted text, layout trees and conformance-map summaries; **golden-image tests** for rendering with per-page thresholds.
- **Benchmarks** for lexer, interpreter, rasterizer and codecs with allocation assertions; a regression gate once baselines exist.
- **Conformance map** rows marked `done` must link a test; a checker enforces it on every test run.
- Prior art in the repository: the corpus smoke test and Verify snapshot in `Broadside.Tests`, the corpus-seeded fuzz smoke runner, the dry benchmark run, and the conformance checker's fixture tests.

## Out of Scope

- Executing JavaScript, rendering XFA, 3D or rich media, playing multimedia, printing subsystems, OCR. All are parse-and-preserve.
- Full PDF/A, PDF/UA or PDF/X *input* validation (a veraPDF-sized project); only conformant output and CI validation of our own output.
- Viewer controls are specified separately after Phase 3; this spec covers the rendering requirements they impose, not the controls.
- Targets older than net10.0.
- Any third-party dependency in the core, including managed image or crypto libraries.
- A hosted or paid service; sponsorship buys priority only.

## Further Notes

- Prior art to study, not copy without attribution: pdf.js and PDFBox (Apache-2.0) for leniency heuristics; PDFium for rendering behavior; Sołtes's ProPDF as a recent managed engine; veraPDF for conformance rules.
- Research reports backing these decisions: `docs/research/pdf-viewer-landscape.md` (what viewers need), `docs/research/doc-site-tooling.md` (Lunet), `docs/research/missing-iso-specs.md` (which ISO documents are free or paid).
- Open scope questions deliberately deferred to their phases: exact public API signatures (phase issues), viewer feature set (Phase 6 spec), performance targets (set after Phase 3 baselines).
