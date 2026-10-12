---
title: "Architecture"
---

# Architecture

Broadside's vocabulary lives in [`CONTEXT.md`](https://github.com/michaelstonis/Broadside/blob/main/CONTEXT.md) and its decisions in [`docs/adr/`](https://github.com/michaelstonis/Broadside/tree/main/docs/adr). This page summarizes both; the linked files are authoritative.

## Vocabulary

**Capabilities.** *Read* parses an existing PDF file into the object model, including damaged files. *Write* produces a new file. *Edit* modifies an existing file and saves it, preserving everything not touched. *Render* turns a page into drawing operations on a surface.

**Object model.** A *COS object* is one of the primitive values a PDF file is built from, exactly as stored (boolean, number, string, name, array, dictionary, stream, null, indirect reference). The *document model* is the typed view over COS objects (document, page, font, annotation, form field), and a document-model object is a view over its COS object, never a copy.

**Parsing.** A *diagnostic* records one deviation from the specification and what was done about it. *Lenient mode*, the default, repairs and records; *strict mode* makes the first deviation an error. A *filter* is a stream encoding such as FlateDecode or JBIG2Decode and the codec for it; image codecs are filters.

**Fonts.** A *font program* is embedded or substituted glyph data (TrueType, OpenType, CFF, Type 1, Type 3). The *Standard 14 fonts* may be used without embedding. The *font resolver* finds a font program for a font that is not embedded.

**Rendering.** The *display list* is the device-independent, fully resolved sequence of drawing operations every *backend* consumes. The *software rasterizer* is the managed reference backend; a *native backend* draws through a platform API (CoreGraphics, Direct2D, Android Canvas).

**Text and layout.** The *structure tree* is the tagged-PDF hierarchy stored in the file (ISO 32000-2 §14.7). The *layout tree* is the logical content recovered from a page, from the structure tree when the file is tagged and from geometry when it is not.

**Principles.** *Fully managed* means no dependency outside packages Microsoft ships without a native component. An *extension point* is a contract with a managed default that a consumer can replace. The *canonical model* is PDF 2.0. *Parse-and-preserve* is the posture toward out-of-scope features: kept and written back, never executed or rendered. The *conformance map* is the per-clause implementation record ([Conformance](conformance.md)).

## Decisions

| ADR | Decision |
|---|---|
| [0001](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0001-fully-managed-core-with-extension-points.md) | Fully managed core, with extension points for every component a consumer may want to substitute. |
| [0002](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0002-target-net10-only.md) | Target `net10.0` only. |
| [0003](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0003-pdf-2-0-as-canonical-model.md) | PDF 2.0 is the canonical object model; the written version is raised to what the used features need. |
| [0004](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0004-two-layer-object-model-with-public-cos.md) | Two public layers: COS objects and a document model that is a live view over them. |
| [0005](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0005-lenient-reading-with-diagnostics.md) | Lenient reading by default, every repair recorded as a diagnostic. |
| [0006](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0006-display-list-rendering-architecture.md) | Rendering goes through a display list; no backend sees content-stream operators. |
| [0007](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0007-standard-14-glyphs-in-separate-package.md) | Standard 14 glyph data ships in a separate package; the core carries only metrics. |
| [0008](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0008-mit-license-sponsorship-not-restriction.md) | MIT license; sponsorship is asked for, not enforced. |
| [0009](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0009-font-resolver-contract.md) | One font resolver interface, asked in order (configured resolvers, then system fonts), serves font programs and named CMap resources. |
