# Broadside

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Broadside is a fully managed .NET implementation of the PDF specification (ISO 32000-2 and its extensions). It reads, writes, edits and renders PDF files, with a cross-platform rendering layer that targets a software rasterizer, SkiaSharp, CoreGraphics, Direct2D and Android Canvas through one backend contract, and viewer controls for MAUI, Uno, Avalonia, WPF, WinUI and Blazor to follow. Everything that interprets the PDF file itself (parsing, writing, fonts, image codecs, cryptography, content interpretation) is written in C# with no native dependency. MIT licensed. The goal is to be the only PDF dependency a .NET application needs.

## Status

**Planning is complete; no library code exists yet.** The repository currently holds the vocabulary, the architecture decisions, the phased build plan and the build scaffolding. Work is organized as GitHub issues in milestones, one per phase, and tracked on the [project board](https://github.com/users/michaelstonis/projects/1). Start with [docs/plan/README.md](docs/plan/README.md) to see what is planned and in what order.

## Orientation

- [CONTEXT.md](CONTEXT.md): the vocabulary.
- [docs/adr](docs/adr): the decisions and why.
- [docs/plan](docs/plan): phases, tracks, and the issue lists.
- [docs/conformance](docs/conformance): what is implemented, clause by clause.
- [docs/research](docs/research): background reports.
- [CLAUDE.md](CLAUDE.md): rules for agents working in this repository.

## Packages

None of these packages is published yet.

| Package | Contents | Native deps | Status |
|---|---|---|---|
| `Broadside` | COS layer, document model, filters, fonts, images, content interpreter, display list + backend contract, text extraction, layout tree, structure, forms, annotations, security, signatures, canvas writing, diagnostics, DI registration | none | not yet published |
| `Broadside.Rendering` | Software rasterizer, ICC color engine, PNG encoder | none | not yet published |
| `Broadside.Rendering.Skia` | SkiaSharp backend (CPU and GPU surfaces) | SkiaSharp | not yet published |
| `Broadside.Rendering.CoreGraphics` | macOS, iOS, Mac Catalyst backend | Apple bindings | not yet published |
| `Broadside.Rendering.Direct2D` | Windows backend via CsWin32 | Windows | not yet published |
| `Broadside.Rendering.Android` | Android Canvas backend | Android bindings | not yet published |
| `Broadside.Fonts.Standard14` | Liberation + Foxit Symbol/Dingbats glyph data | none | not yet published |
| `Broadside.Fonts.Cmaps` | Adobe predefined CMaps and CID-to-Unicode tables | none | not yet published |
| `Broadside.Layout` | Flow-layout document engine over the canvas API | none | not yet published |
| `Broadside.Viewer.*` | Shared viewer core + MAUI, Uno, Avalonia, WPF, WinUI, Blazor shells (post-Phase 3) | per framework | not yet published |

Target framework: `net10.0` only ([ADR 0002](docs/adr/0002-target-net10-only.md)).

## Building

Requires the .NET SDK pinned in `global.json`.

```sh
dotnet build Broadside.slnx        # everything the host can build
dotnet build Broadside.Core.slnf   # the net10.0 packages only; no workloads needed
dotnet pack Broadside.Core.slnf -c Release   # packages land in artifacts/package/release/
```

`Broadside.Rendering.CoreGraphics` (macOS, iOS, Mac Catalyst) and `Broadside.Rendering.Android` need their .NET workloads. Each decides at evaluation time whether the host can build it (by default: yes on macOS, no elsewhere) and otherwise drops out of the solution build as a no-op, so `Broadside.slnx` builds on a Linux or Windows machine without workloads. Override the decision with `-p:BroadsideBuildPlatformBackends=true|false`. `Broadside.Core.slnf` is a solution filter that leaves those two projects out entirely, for machines where the default is wrong or an IDE wants only loadable projects. `Broadside.Rendering.Direct2D` targets Windows but builds everywhere, so it is part of the core set.

## Specifications

`Specs/` is not committed. Place ISO 32000-2:2020, the ISO/TS 32001 through 32005 documents, and the ISO 14289 (PDF/UA) documents there (free sponsored copies are available from the PDF Association), and download the free font, image and color references listed in [docs/specs-sources.md](docs/specs-sources.md) into `Specs/References/`.

## Contributing

Contributions from people and from AI agents follow the same flow: pick an issue, branch, open a PR with a DCO sign-off. See [CONTRIBUTING.md](CONTRIBUTING.md) for the issue, branch and review process, [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for conduct, and [SECURITY.md](SECURITY.md) for how to report a vulnerability.

## Sponsoring

Broadside is free to use under the MIT license and will stay that way. Sponsorship funds the work and buys priority on bugs and features, never access to features. See [SPONSORS.md](SPONSORS.md) for the tiers and [ADR 0008](docs/adr/0008-mit-license-sponsorship-not-restriction.md) for the reasoning.

## License

[MIT](LICENSE).
