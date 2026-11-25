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
| 6. Viewer | Viewer core, then one shell per UI framework. | done. The viewer core and the Blazor, MAUI, Avalonia, Uno, WinUI and WPF shells, with search, outline and thumbnails, form filling and smart dark mode. The viewer packages ship as 1.0.0 with the rest. |
| 7. Hardening and v1 release | Coverage gaps, deferred hardening, release engineering. | 1.0 release candidate; 1.0.0 follows the soak period. Done: the API review and the v1 API freeze, the published diagnostic-code catalogue, versioning and release notes, package validation, the guides, the threat model and the security review, and the performance comparison. Open: the release-candidate gate (#276) and the [general-availability criteria](https://github.com/michaelstonis/Broadside/blob/main/docs/release/ga-criteria.md). |
| Post-v1 | Deferred and earmarked work (#232, the Post-v1 milestone). | Migration guides from PdfSharp, iText and PdfPig (#271), JBIG2 extended templates, shaping for complex scripts, and the gaps in the known limitations. |

The dependency graph between tracks, and the exit criteria of each phase, are in the plan.

## Releases

Releases are published from `main`, every package with the same version. Previews (`0.1.0-preview.N`) came first; Broadside is now at its 1.0 release candidate (`1.0.0-rc.N`), and 1.0.0 follows the soak period. Work merged into the integration branch `spec/33-broadside-v1` reaches the next release when it merges into `main`. What each release changed is on the [releases page](https://github.com/michaelstonis/Broadside/releases) and in the [changelog](https://github.com/michaelstonis/Broadside/blob/main/CHANGELOG.md).

## Known issues

The [known limitations of v1](https://github.com/michaelstonis/Broadside/blob/main/docs/known-limitations.md) are one list, each item with the open issue that tracks it. The [open issues](https://github.com/michaelstonis/Broadside/issues) are the complete list.
