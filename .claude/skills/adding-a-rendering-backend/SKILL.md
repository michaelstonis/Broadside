---
name: adding-a-rendering-backend
description: "Use when implementing a rendering backend (software rasterizer, SkiaSharp, CoreGraphics, Direct2D, Android Canvas, or a new one) that consumes display lists through the backend contract in Broadside.Rendering."
---

# Adding a rendering backend

> The contract below is the real one, landed by issue #94 (`src/Broadside/Rendering/`, `src/Broadside/PublicAPI.Unshipped.txt`). Later Phase 3 tickets add members to `IRenderingBackend` as their operations enter the display list (strokes, clips, glyph runs, images, shadings, patterns, groups); update the signature list here in the same PR.

A backend is "a concrete implementation of the rendering surface contract that knows how to draw on one technology" (`CONTEXT.md`). Backends consume display lists; nothing renders from content-stream operators directly (ADR 0006, `CLAUDE.md` "Hard rules"). The software rasterizer is the reference every other backend is measured against.

## Where it lives

- Contract: namespace `Broadside.Rendering` in the core package, `src/Broadside/Rendering/` (`DisplayList`, `IRenderingBackend`, `DisplayColor`; the internal `DisplayListBuilder` split into one file per operation category). Types in `Rendering` carry no `Pdf`/`Cos` prefix (`CLAUDE.md`, "Code conventions").
- Software rasterizer, ICC engine and PNG encoder: `src/Broadside.Rendering/` (no native dependencies; Track 3B, 3C).
- Each other backend is its own package: `src/Broadside.Rendering.Skia/` (references `Broadside.Rendering` and `SkiaSharp`), `src/Broadside.Rendering.CoreGraphics/`, `src/Broadside.Rendering.Direct2D/` (CsWin32), `src/Broadside.Rendering.Android/`. A backend may depend on its platform library and nothing else (ADR 0001). The CoreGraphics and Android projects only build when their workloads exist (`-p:BroadsideBuildPlatformBackends=true|false`).
- Tests: `tests/Broadside.Rendering.Tests/` (rasterizer, snapshots) and `tests/Broadside.Rendering.Skia.Tests/`; the platform backends get a test project and a CI leg when they exist (`tests/README.md`).

## Contract

Namespace `Broadside.Rendering`, core package (ADR 0006: backends consume display lists, never content-stream operators):

```csharp
// Built once per page; immutable; replayable on any number of threads at once, each with its own backend.
public sealed class DisplayList
{
    public PdfDocument Document { get; }               // whose GetColorConverter a backend converts colours through
    public void Replay(IRenderingBackend backend);     // reads only the list, allocates nothing
}

public sealed class PdfPage
{
    public DisplayList CreateDisplayList();
    public DisplayList CreateDisplayList(ContentOptions options);   // limits, optional-content state, cancellation
}

public interface IRenderingBackend
{
    void SaveState();                                   // §8.4.2 q (and the state saved around a form XObject)
    void RestoreState();                                // §8.4.2 Q; saves and restores always balance
    void SetTransform(in Matrix transform);             // replaces (does not concatenate) the user space -> default user space transform
    void FillPath(PathView path, FillRule rule, in DisplayColor color);   // §8.5.3.3
}

public readonly ref struct DisplayColor               // §8.6.1: colour specification, never pre-converted
{
    public PdfColorSpace ColorSpace { get; }            // as selected (DeviceCMYK, Separation, Lab, ICCBased, ...), before defaults
    public ReadOnlySpan<float> Components { get; }      // as the operator gave them, unclipped
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }   // §8.6.5.6, current at the paint
    public ColorConversion Conversion { get; }          // intent, BPC, BG, UCR from the state; Target = Rgb
}
```

Rules every backend follows:

- A replay starts with the identity transform: user space is the page's default user space. Mapping it to the device (scale or DPI, rotation, CropBox, UserUnit, the clip rectangle of a tile) is the backend's or its renderer's, applied under the list's transforms. Use the mapping the software rasterizer uses (#95, package `Broadside.Rendering`): `RenderOptions.GetDeviceTransform(page)` (default user space to pixels, origin top left, y down, crop box, `Rotate` plus the options' rotation, `UserUnit` times the scale) and `RenderOptions.GetPixelSize(page)` (rounded up to whole pixels), so every backend's image lines up with the rasterizer's snapshots.
- `SetTransform` comes before the paints it applies to and only when the CTM changes; `RestoreState` restores the transform (and, as later tickets add them, the clip and everything else the backend keeps per state). Save/restore pairs with nothing painted between them are dropped at build time.
- `PathView` is the path exactly as constructed, in user space: close open subpaths for filling, ignore a trailing single-point subpath, treat degenerate subpaths per §8.5.3.2/§8.5.3.3.1. Spans and views are valid only during the call: copy what you keep.
- Colour: convert with `list.Document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = <your model> }, color.DefaultColorSpaces)`. That expands Indexed, Separation, DeviceN, applies the defaults and goes through the engine's `IColorManagement` (the colour-management extension point). Check `PdfColorConverter.PaintsNothing` (Separation `None`). Cache converters per colour space if you convert per paint; the document caches them too.
- Not in the list yet (later Phase 3 tickets): strokes, clips, glyph runs, images, shadings, pattern fills, transparency groups and soft masks.

A backend must also, as those operations arrive:

- draw strokes (joins, caps, dashes, minimum width), clipping (path and text, intersect), decoded images with transforms and resampling, shadings 1–7, tiling patterns, glyph runs (font reference + glyph ids + positions, native glyph rendering where the platform has it with an outline fallback when `ForceOutlines` is set), and the transparency model (§11: isolated and non-isolated groups, knockout, soft masks, the 16 blend modes, constant alpha);
- honour the render options of Track 3A: scale or DPI, rotation, clip rectangle (tiled region rendering), layer selection (annotations and forms are separate replayable lists), rendering intent, anti-aliasing, cancellation and progress;
- keep per-surface resource caches (glyphs, images, shaders) within the deterministic memory budgets the viewer relies on;
- fall back to the software rasterizer for any group or blend it cannot express (plan, Track 3D) rather than approximate silently, and record a `Diagnostic` when it does;
- allocate nothing per operation, glyph or scanline during replay; buffers are pooled per surface (`CLAUDE.md`, "Code conventions");
- render each corpus page at `GoldenCorpus.Dpi` (72) through `GoldenCorpus.CreateRenderOptions()` so the golden-image harness (#120) can compare it with the rasterizer's golden image; any 8-bit four-channel layout works (`GoldenPixelFormat`: BGRA or RGBA, premultiplied or straight, transparent or white page).

Testing against the contract: `Broadside.TestSupport.RecordingBackend` records every call as a line (`save`, `restore`, `transform [a b c d e f]`, `fill nonzero|evenodd M x y L ... Z | Family c0 c1 ...`) and keeps each fill's colour space, components, defaults and conversion; `Broadside.Tests.Rendering.DisplayListTests` shows the pattern. `DisplayListBenchmarks` (Build, Replay) is the replay allocation baseline.

Registration goes through the options object (`PdfOptions.Use*`, plan row 1.12) or the package's one fluent configuration call, the same shape as `Broadside.Fonts.Standard14` (ADR 0007). No static registry: two engines in one process may use different backends, and the viewer selects one per surface.

## Managed default

The software rasterizer in `src/Broadside.Rendering/` is the default and the oracle: fully managed, `System.*` only, on every platform. Since #95: `SoftwareRasterizer.Render(page[, options])` returns a pooled `Bitmap` (premultiplied BGRA8, white page), `Render(page, bitmap, options)` draws into caller memory, `Render(list, bitmap, deviceTransform)` replays a cached list without a background; `Bitmap.SavePng` writes PNG. Its private backend (`SoftwareRasterizer.Backend`) is where a new `IRenderingBackend` member gets its reference implementation; scan conversion is `ScanlineRasterizer` (exact-area cells, both fill rules, zero allocation once warm: keep it that way, `RasterizerAllocationTests` and `RasterizerBenchmarks` guard it). Its renders of every corpus page are the golden images in `tests/Golden/` (#120: `GoldenImageTests`, compared perceptually through `tests/Broadside.GoldenImages`); its older per-file snapshots (`tests/Broadside.Rendering.Tests/Snapshots/*.verified.png`) compare by decoded pixels (`PngSnapshots`), and `tests/Broadside.Rendering.AotSmoke` re-checks them under Native AOT. Native backends are exempt from the managed rule (ADR 0001) but are never the default and never a dependency of the core.

## Deliverables of a backend PR

1. Package `src/Broadside.Rendering.<Name>/` with `PublicAPI.Shipped.txt`/`Unshipped.txt`, a `Description` in the csproj, and the platform conditioning the existing backend projects use; added to `Broadside.slnx` under `/src/` (and to `Broadside.Core.slnf` only if it builds everywhere).
2. XML docs citing the clauses each operation implements (`<remarks>ISO 32000-2 §8.5.3</remarks>` for path painting, `§11.3` for compositing, `§9.3` for text state); the backend contract itself cites ADR 0006.
3. Conformance rows: the backend-affected clauses in §8, §9.3–§9.4, §10 and §11 of `docs/conformance/iso-32000-2.md` with the snapshot tests named; the rows stay `partial` until the golden-image suite passes within threshold.
4. Test project `tests/Broadside.Rendering.<Name>.Tests/` (xUnit v3, `InternalsVisibleTo` granted on its own package only by `src/Directory.Build.props`) referencing `tests/Broadside.GoldenImages`. It renders corpus pages and compares them with the rasterizer's golden images through the harness, never with its own snapshots:

   ```csharp
   [Theory]
   [MemberData(nameof(GoldenCorpus.Pages), MemberType = typeof(GoldenCorpus))]   // or GoldenCorpus.PagesOf("paths-fill-rules.pdf", ...)
   public void Page_matches_the_golden_image(string fileName, int page)
   {
       using PdfDocument document = GoldenCorpus.Open(fileName);                   // password files open with their user password
       RenderOptions options = GoldenCorpus.CreateRenderOptions();                  // 72 DPI; GetPixelSize / GetDeviceTransform
       // ... render document.Pages[page] onto a surface of options.GetPixelSize(page) ...
       GoldenCorpus.AssertMatchesReference("<name>", fileName, page,
           GoldenImage.FromPixels(pixels, width, height, rowBytes, GoldenPixelFormat.Bgra8Premultiplied));
   }
   ```

   A failure writes `tests/Golden/<file>.page<N>.<name>.received.png` and `.diff.png` (red: beyond tolerance, amber: tolerated). A page that legitimately differs more than `GoldenThreshold.Default` gets a row, with its reason, in `GoldenThresholds.Rules` (`tests/Broadside.GoldenImages/GoldenThresholds.cs`), the one reviewed table.
5. A CI leg on the backend's platform (ubuntu, windows or macos matrix entry) that builds and runs that test project.
6. Fuzz: none for a backend (it consumes a trusted display list), but any new decoder the backend introduces gets a target.
7. Benchmark `<Name>BackendBenchmarks` with `[MemoryDiagnoser]`: replay of the corpus display lists at a fixed DPI, `Allocated` at `-` (skill `adding-a-benchmark`); these feed the Track 3I regression gate alongside the rasterizer's numbers.

## Worked example: the Skia backend

`src/Broadside.Rendering.Skia/Broadside.Rendering.Skia.csproj` already exists with a `ProjectReference` to `Broadside.Rendering` and a `PackageReference` to `SkiaSharp`, and `tests/Broadside.Rendering.Skia.Tests/PackageSmokeTests.cs` proves the wiring. The backend PR replaces `AssemblyMarker` with the surface type that maps display-list paths to `SKPath`, glyph runs to `SKFont`/`SKTextBlob` (outlines when `ForceOutlines`), images to `SKImage` with the sampling the options ask for, shadings to `SKShader`, and groups to `SKCanvas.SaveLayer`; knockout groups and the blend modes Skia lacks fall back to the rasterizer. The test project renders `tests/Corpus/inline-image.pdf` (a 2x2 checkerboard scaled to 100x100 at (72, 600)) and `text-truetype-embedded.pdf` (two block glyphs) and compares them with `tests/Broadside.Rendering.Tests` snapshots. The CI leg runs on all three operating systems because SkiaSharp ships native assets for each.

## Checklist

- [ ] Contract section of this skill updated with any `IRenderingBackend` members your ticket adds
- [ ] Backend consumes display lists only; no operator interpretation in the backend
- [ ] Own package with platform conditioning; depends on its platform library and `Broadside.Rendering` only
- [ ] Registered through `PdfOptions` or the package's fluent call, not a static registry
- [ ] Falls back to the software rasterizer for unsupported groups and blends, with a `Diagnostic`
- [ ] `PublicAPI.*.txt`, clause-cited XML docs, conformance rows, snapshot test project, CI leg, benchmark
- [ ] No per-operation allocation during replay (`Allocated` reads `-`)
