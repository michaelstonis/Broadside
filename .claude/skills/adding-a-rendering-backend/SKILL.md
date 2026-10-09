---
name: adding-a-rendering-backend
description: "Use when implementing a rendering backend (software rasterizer, SkiaSharp, CoreGraphics, Direct2D, Android Canvas, or a new one) that consumes display lists through the backend contract in Broadside.Rendering."
---

# Adding a rendering backend

> **The backend contract is defined by Track 3A Display list (`docs/plan/README.md`, Phase 3; ADR 0006); its issues are not filed yet. When the contract issue lands, replace the "Contract" section of this skill with the real signatures from `src/Broadside/Rendering/` and `src/Broadside/PublicAPI.Unshipped.txt`.** Until then this is a design brief: responsibilities and deliverables, not method names.

A backend is "a concrete implementation of the rendering surface contract that knows how to draw on one technology" (`CONTEXT.md`). Backends consume display lists; nothing renders from content-stream operators directly (ADR 0006, `CLAUDE.md` "Hard rules"). The software rasterizer is the reference every other backend is measured against.

## Where it lives

- Contract: namespace `Broadside.Rendering` in the core package `src/Broadside/` (the plan lists "display list + backend contract" under `Broadside`). Types in `Rendering` carry no `Pdf`/`Cos` prefix (`CLAUDE.md`, "Code conventions").
- Software rasterizer, ICC engine and PNG encoder: `src/Broadside.Rendering/` (no native dependencies; Track 3B, 3C).
- Each other backend is its own package: `src/Broadside.Rendering.Skia/` (references `Broadside.Rendering` and `SkiaSharp`), `src/Broadside.Rendering.CoreGraphics/`, `src/Broadside.Rendering.Direct2D/` (CsWin32), `src/Broadside.Rendering.Android/`. A backend may depend on its platform library and nothing else (ADR 0001). The CoreGraphics and Android projects only build when their workloads exist (`-p:BroadsideBuildPlatformBackends=true|false`).
- Tests: `tests/Broadside.Rendering.Tests/` (rasterizer, snapshots) and `tests/Broadside.Rendering.Skia.Tests/`; the platform backends get a test project and a CI leg when they exist (`tests/README.md`).

## Contract (responsibilities, not signatures)

A backend must:

- replay a display list (ADR 0006: a struct-based operation buffer with interned resources) onto its surface: paths with fill rules, strokes (joins, caps, dashes, minimum width), clipping (path and text, intersect), decoded images with transforms and resampling, shadings 1–7, tiling patterns, glyph runs (font reference + glyph ids + positions, native glyph rendering where the platform has it with an outline fallback when `ForceOutlines` is set), and the transparency model (§11: isolated and non-isolated groups, knockout, soft masks, the 16 blend modes, constant alpha);
- honour the render options of Track 3A: scale or DPI, rotation, clip rectangle (tiled region rendering), layer selection (annotations and forms are separate replayable lists), rendering intent, anti-aliasing, cancellation and progress;
- be replayable on any thread while nobody mutates the document (the thread-safety contract in `CLAUDE.md`), and keep per-surface resource caches (glyphs, images, shaders) within the deterministic memory budgets the viewer relies on;
- fall back to the software rasterizer for any group or blend it cannot express (plan, Track 3D: "group fallback to 3B where Skia cannot express a blend/knockout") rather than approximate silently, and record a `Diagnostic` when it does;
- allocate nothing per operation, glyph or scanline during replay; buffers are pooled per surface (`CLAUDE.md`, "Code conventions");
- produce, for the golden-image suite, pixels in the reference format (premultiplied BGRA8 at a fixed DPI) so the perceptual comparison against the rasterizer's snapshots applies (Track 3H).

Registration goes through the options object (`PdfOptions.Use*`, plan row 1.12) or the package's one fluent configuration call, the same shape as `Broadside.Fonts.Standard14` (ADR 0007). No static registry: two engines in one process may use different backends, and the viewer selects one per surface.

## Managed default

The software rasterizer in `src/Broadside.Rendering/` is the default and the oracle: fully managed, `System.*` only, on every platform. Native backends are exempt from the managed rule (ADR 0001) but are never the default and never a dependency of the core.

## Deliverables of a backend PR

1. Package `src/Broadside.Rendering.<Name>/` with `PublicAPI.Shipped.txt`/`Unshipped.txt`, a `Description` in the csproj, and the platform conditioning the existing backend projects use; added to `Broadside.slnx` under `/src/` (and to `Broadside.Core.slnf` only if it builds everywhere).
2. XML docs citing the clauses each operation implements (`<remarks>ISO 32000-2 §8.5.3</remarks>` for path painting, `§11.3` for compositing, `§9.3` for text state); the backend contract itself cites ADR 0006.
3. Conformance rows: the backend-affected clauses in §8, §9.3–§9.4, §10 and §11 of `docs/conformance/iso-32000-2.md` with the snapshot tests named; the rows stay `partial` until the golden-image suite passes within threshold.
4. Test project `tests/Broadside.Rendering.<Name>.Tests/` (xUnit v3, `InternalsVisibleTo` granted by `src/Directory.Build.props`) that renders every corpus page (`tests/Corpus/`) and compares with the rasterizer's Verify snapshots within the perceptual threshold; snapshots live in `Snapshots/` next to the test (`tests/README.md`).
5. A CI leg on the backend's platform (ubuntu, windows or macos matrix entry) that builds and runs that test project.
6. Fuzz: none for a backend (it consumes a trusted display list), but any new decoder the backend introduces gets a target.
7. Benchmark `<Name>BackendBenchmarks` with `[MemoryDiagnoser]`: replay of the corpus display lists at a fixed DPI, `Allocated` at `-` (skill `adding-a-benchmark`); these feed the Track 3I regression gate alongside the rasterizer's numbers.

## Worked example: the Skia backend

`src/Broadside.Rendering.Skia/Broadside.Rendering.Skia.csproj` already exists with a `ProjectReference` to `Broadside.Rendering` and a `PackageReference` to `SkiaSharp`, and `tests/Broadside.Rendering.Skia.Tests/PackageSmokeTests.cs` proves the wiring. The backend PR replaces `AssemblyMarker` with the surface type that maps display-list paths to `SKPath`, glyph runs to `SKFont`/`SKTextBlob` (outlines when `ForceOutlines`), images to `SKImage` with the sampling the options ask for, shadings to `SKShader`, and groups to `SKCanvas.SaveLayer`; knockout groups and the blend modes Skia lacks fall back to the rasterizer. The test project renders `tests/Corpus/inline-image.pdf` (a 2x2 checkerboard scaled to 100x100 at (72, 600)) and `text-truetype-embedded.pdf` (two block glyphs) and compares them with `tests/Broadside.Rendering.Tests` snapshots. The CI leg runs on all three operating systems because SkiaSharp ships native assets for each.

## Checklist

- [ ] Contract section of this skill replaced with the real signatures once the 3A contract issue has landed
- [ ] Backend consumes display lists only; no operator interpretation in the backend
- [ ] Own package with platform conditioning; depends on its platform library and `Broadside.Rendering` only
- [ ] Registered through `PdfOptions` or the package's fluent call, not a static registry
- [ ] Falls back to the software rasterizer for unsupported groups and blends, with a `Diagnostic`
- [ ] `PublicAPI.*.txt`, clause-cited XML docs, conformance rows, snapshot test project, CI leg, benchmark
- [ ] No per-operation allocation during replay (`Allocated` reads `-`)
