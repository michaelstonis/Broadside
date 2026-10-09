---
title: "Home"
---

# Broadside

Broadside is a fully managed .NET implementation of the PDF specification (ISO 32000-2 and its extensions). It will read, write, edit and render PDF files, with a cross-platform rendering layer that targets a software rasterizer, SkiaSharp, CoreGraphics, Direct2D and Android Canvas through one backend contract, and viewer controls to follow. Everything that interprets the PDF file itself (parsing, writing, fonts, image codecs, cryptography, content interpretation) is written in C# with no native dependency. MIT licensed.

## Status

**Pre-alpha. No library code exists yet.** Planning is complete and the build scaffolding is in place; the first code lands with Phase 1, the COS layer. No package is published, and the [API reference](api/readme.md) lists only what has been built so far, which today is nothing public.

## Where to go next

- [Getting started](articles/getting-started.md): what you can do with Broadside today.
- [Architecture](articles/architecture.md): the vocabulary and the decisions behind the design.
- [Roadmap](articles/roadmap.md): the phases and tracks, and where to follow progress.
- [Conformance](articles/conformance.md): how implementation is tracked clause by clause.
- [Contributing](articles/contributing.md): how to pick up work.
- [Source on GitHub](https://github.com/michaelstonis/Broadside).
