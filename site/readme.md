---
title: "Home"
---

# Broadside

Broadside is a fully managed .NET implementation of the PDF specification (ISO 32000-2 and its extensions). It reads, writes, edits and renders PDF files. Everything that interprets the PDF file itself (parsing, writing, fonts, image codecs, cryptography, content interpretation) is written in C# with no native dependency. Rendering goes through a display list that several backends consume: the managed software rasterizer, SkiaSharp, CoreGraphics, Android Canvas, Direct2D and WebGL 2. On top of that sit a framework-neutral viewer core and viewer controls for Blazor WebAssembly and .NET MAUI. MIT licensed.

## Status

**Preview.** The first preview, `0.1.0-preview.1`, is on NuGet.org as 13 packages. The public API is not frozen yet, so expect breaking changes between previews. Phases 0 to 5 (the COS layer, the model and codecs, rendering, writing and editing, and the cross-cutting capabilities) are complete. The viewer (Phase 6) and hardening for v1 (Phase 7) are in progress. The [API reference](api/readme.md) is generated from the XML documentation of every package; [API coverage](articles/api-coverage.md) says which target framework each one is built from.

## Try it in the browser

The [live demo](/demo/) is the Blazor WebAssembly sample, running entirely in your browser: open a PDF from the gallery, your device or a URL, then search it, read its outline and tags, fill its form, annotate it, check its signatures, or create a tagged PDF/A document with the layout package. Every byte of PDF work happens in WebAssembly, in managed code, with nothing sent to a server. It works on phones too.

## Where to go next

- [Live demo](/demo/): the Blazor WebAssembly viewer and the library, in your browser.
- [Getting started](articles/getting-started.md): install the packages and run a first example.
- [Architecture](articles/architecture.md): the vocabulary and the decisions behind the design.
- [Roadmap](articles/roadmap.md): the phases and their status, and the known issues.
- [Conformance](articles/conformance.md): what is implemented, clause by clause.
- [Contributing](articles/contributing.md): how to pick up work.
- [Source on GitHub](https://github.com/michaelstonis/Broadside).
