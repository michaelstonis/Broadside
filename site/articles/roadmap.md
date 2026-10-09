---
title: "Roadmap"
---

# Roadmap

The build plan is [`docs/plan/README.md`](https://github.com/michaelstonis/Broadside/blob/main/docs/plan/README.md) and the full v1 specification is issue [#33](https://github.com/michaelstonis/Broadside/issues/33). Each phase is a GitHub milestone; each task is an issue, tracked on the [project board](https://github.com/users/michaelstonis/projects/1). Tracks inside a phase share nothing but the phases below them, so they run in parallel.

Capability order: Read, then Render, then Write and Edit, then the cross-cutting capabilities. Rendering comes before writing because it is the differentiator and the hardest to get right.

| Phase | Scope | Tracks |
|---|---|---|
| 0. Scaffold | Solution and build conventions, empty package projects, test, benchmark and fuzz harnesses, CI, corpus fetcher, conformance map, this site. | single track |
| 1. COS layer | Lexer, COS objects, object and cross-reference parsing with repair, standard filters, standard security handler, serializer, engine and options, document and page tree skeleton. | single track |
| 2. Model and codecs | Fonts (§9), images and their codecs (§8.9), the document model (§7.7, §12, §14), the content interpreter (§8, §9.4). | 2A, 2B, 2C, 2D |
| 3. Rendering | Display list first, then the software rasterizer, ICC color engine, Skia, CoreGraphics, Direct2D and Android backends, golden-image suite, benchmarks. | 3A, then 3B to 3I |
| 4. Write and Edit | Canvas API with font subsetting, incremental update and edit, forms, flow layout (`Broadside.Layout`), encryption on write. | 4A to 4E |
| 5. Cross-cutting | Text extraction, layout tree and auto-tagging, signatures, PDF/A and PDF/UA output, streaming sources. | 5A to 5E |
| 6. Viewer | Shared viewer core, then one shell per UI framework. Runs after Phase 3, in parallel with 4 and 5. | per framework |

The dependency graph between tracks, and the exit criteria of each phase, are in the plan.
