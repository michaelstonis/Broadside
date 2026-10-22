---
title: "Roadmap"
---

# Roadmap

The build plan is [`docs/plan/README.md`](https://github.com/michaelstonis/Broadside/blob/main/docs/plan/README.md) and the full v1 specification is issue [#33](https://github.com/michaelstonis/Broadside/issues/33). Each phase is a GitHub milestone and each task is an issue, tracked on the [project board](https://github.com/users/michaelstonis/projects/1). Tracks inside a phase share nothing but the phases below them, so they run in parallel. Ticket branches merge into the integration branch `spec/33-broadside-v1`, which reaches `main` in batches.

Capability order: Read, then Render, then Write and Edit, then the cross-cutting capabilities. Rendering comes before writing because it is the differentiator and the hardest to get right.

## Phases

"Done" means every task issue in the milestone is closed; the phase's parent issue stays open until spec #33 reaches `main`.

| Phase | Scope | Status |
|---|---|---|
| 0. Scaffold | Solution and build conventions, package projects, test, benchmark and fuzz harnesses, CI, corpus fetcher, conformance map, this site. | done |
| 1. COS layer | Lexer, COS objects, object and cross-reference parsing with repair, standard filters, standard security handler, serializer, engine and options, document and page tree skeleton. | done |
| 2. Model and codecs | Fonts (§9), images and their codecs (§8.9), the document model (§7.7, §12, §14), the content interpreter (§8, §9.4). | done |
| 3. Rendering | Display list, software rasterizer, ICC colour engine, Skia, CoreGraphics, Direct2D and Android backends, golden-image suite, benchmarks. | done |
| 4. Write and Edit | Canvas API with font subsetting, incremental update and edit, forms, flow layout (`Broadside.Layout`), encryption on write. | done |
| 5. Cross-cutting | Text extraction, layout tree and auto-tagging, signatures, PDF/A and PDF/UA output, streaming sources. | done |
| 6. Viewer | Viewer core, then one shell per UI framework. | In progress. The viewer core and the Blazor and MAUI shells are done, with a WebGL 2 backend for the browser and rendering off the UI thread. Still open: search with background indexing (#215), outline and thumbnails (#216), form filling in the viewer (#218), smart dark mode (#219), and the Uno, Avalonia, WPF and WinUI shells (#222 to #225). |
| 7. Hardening and v1 release | Coverage gaps, deferred hardening, release engineering. | In progress (#231). The first preview, `0.1.0-preview.1`, is published. Still open: the coverage gaps, typed diagnostic codes, API tidy and the v1 API freeze, the guides, trimming and AOT verification, the threat model, the security review, and the comparison with PDFium and pdf.js. |
| Post-v1 | Earmarked work (#232). | JBIG2 extended templates, shaping for complex scripts. |

The dependency graph between tracks, and the exit criteria of each phase, are in the plan.

## Not released yet

These are on the integration branch since `0.1.0-preview.1`:

- Saves stay bounded on damaged files that declare huge object numbers (#371).
- The MAUI package includes the Windows target (#381).
- The WebGL backend reads thumbnails and other pixel surfaces back from the GPU (#380).

## Known issues

- `AddObject`, and the edits built on it, throws on a file that already holds object number 2147483647 (#387). Saving a damaged RC4-encrypted input can write a file that cannot be reopened (#390).
- Large images are decoded at full resolution and slowly, above all in the browser: #362, #365, #352 and the platform image decoders #372 to #377.
- Rendering is slower than PDFium on the pinned real-world pages (#336). Stroking got slower after the overlap fix (#335). The software rasterizer has no glyph cache yet (#370).
- Unencrypted wrapper documents (#234, #235), order 3 sampled functions (#236) and bounded memory on multi-gigabyte files (#237) are not implemented yet.

The [open issues](https://github.com/michaelstonis/Broadside/issues) are the complete list.
