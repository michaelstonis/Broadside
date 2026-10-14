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
    void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color);   // §8.5.3.2, after the fill of B/b (#99)
    void IntersectClip(PathView path, FillRule rule);   // §8.5.4 W/W* (after the paint of its path), §8.10.1 form BBox; restored by RestoreState
    void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion);   // §8.9.4, #102; masks #106
    void FillImageMask(DisplayImageMask mask, in DisplayColor color);   // §8.9.6.2 stencil mask in the fill colour, #106
    void BeginAnnotation(in DisplayAnnotation annotation);             // §12.5.5 an annotation's appearance begins (a save), #98
    void EndAnnotation();                                              // composite it, restore
    void DrawGlyphs(in GlyphRun run);   // §9.4.3, §9.3.6, #100: Program, FontMatrix, RenderingMode, Glyphs (GlyphId, TextMatrix, HorizontalScale), GetOutline(i), GetGlyphTransform(i), Fills/FillColor, Strokes/StrokeStyle/StrokeColor
    void DrawShading(in DisplayShading shading);   // §8.7.4.2 sh: fills the current clip, no Background (#109)
    void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading);   // §8.7.4.1 shading pattern fill, with Background (#109)
    void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading);   // §8.7.4.1 shading pattern stroke (#109)
    void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern);   // §8.7.3 tiling pattern fill: Pattern, Matrix (pattern space), Cell (a DisplayList in pattern space) (#112)
    void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern);   // §8.7.3 tiling pattern stroke (#112)
    void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode);   // §11.6.4.4 CA (strokes) / ca (all else), §11.6.3 BM, §11.3 formulas; saved/restored with the state, starts 1/1/Normal (#113)
    void DrawGroup(in DisplayGroup group);   // §11.4/§11.6.6/§11.7.4.4 transparency group: replay group.Replay(this) into a layer (isolated/knockout/ColorSpace), composite with Opacity+BlendMode through the clip; the list skips the content itself (#114)
}

public sealed class DisplayImage                      // §8.9.5, §8.9.7: decoded once per list, one instance per image XObject
{
    public int Width { get; }  public int Height { get; }  public int Components { get; }
    public int BitsPerComponent { get; }                // logical n: raw values 0 .. 2^n - 1
    public int StorageBits { get; }  public int Stride { get; }
    public ReadOnlySpan<byte> Samples { get; }          // §8.9.3 layout (rows top first, MSB first, 16-bit big-endian)
    public ReadOnlySpan<byte> GetRow(int y);
    public PdfColorSpace ColorSpace { get; }            // resolved, before defaults; never Pattern; Indexed has 1 component
    public ImageDecodeMap DecodeMap { get; }            // §8.9.5.2; MapToIndices for Indexed
    public bool Interpolate { get; }                    // §8.9.5.3
    public DisplayImageMask? Mask { get; }              // §8.9.6.3 explicit mask, at its own size (#106)
    public DisplayImage? SoftMask { get; }              // §11.6.5.2 SMask or the JPX opacity plane (SMaskInData), own size, 1 component
    public ReadOnlySpan<double> ColorKey { get; }       // §8.9.6.4 min/max per component on raw samples; empty when none
    public ReadOnlySpan<float> Matte { get; }           // §11.6.5.2 Table 144, base-space components; zeros for SMaskInData 2
    public ReadOnlySpan<byte> ColorTable { get; }       // §8.6.6.3 Indexed table as stored; empty otherwise
}

public sealed class DisplayImageMask                  // §8.9.6.2/§8.9.6.3: 1 bit per sample, own size, interned per mask stream
{
    public int Width { get; }  public int Height { get; }  public int Stride { get; }
    public ReadOnlySpan<byte> Samples { get; }  public ReadOnlySpan<byte> GetRow(int y);
    public bool IsInverted { get; }                     // a sample 1 paints (Decode [1 0], codec polarity folded in); else 0 paints
    public bool Interpolate { get; }
}

public readonly ref struct DisplayColor               // §8.6.1: colour specification, never pre-converted
{
    public PdfColorSpace ColorSpace { get; }            // as selected (DeviceCMYK, Separation, Lab, ICCBased, ...), before defaults
    public ReadOnlySpan<float> Components { get; }      // as the operator gave them, unclipped
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }   // §8.6.5.6, current at the paint
    public ColorConversion Conversion { get; }          // intent, BPC, BG, UCR from the state; Target = Rgb
}

public readonly ref struct StrokeStyle                // §8.4.3, §10.7.5: line parameters at the stroking operator, user space
{
    public double LineWidth { get; }                    // >= 0; 0 = thinnest line the device can render (one device pixel)
    public LineCap LineCap { get; }                     // Table 53
    public LineJoin LineJoin { get; }                   // Table 54
    public double MiterLimit { get; }                   // >= 1; ratio 1 / sin(phi/2), phi measured in user space
    public ReadOnlySpan<double> DashArray { get; }      // empty = solid; non-negative, not all zero
    public double DashPhase { get; }                    // >= 0 (already normalized), 0 for a solid line
    public bool StrokeAdjustment { get; }               // SA
}
```

Rules every backend follows:

- A replay starts with the identity transform: user space is the page's default user space. Mapping it to the device (scale or DPI, rotation, CropBox, UserUnit, the clip rectangle of a tile) is the backend's or its renderer's, applied under the list's transforms. Use the mapping the software rasterizer uses (#95, package `Broadside.Rendering`): `RenderOptions.GetDeviceTransform(page)` (default user space to pixels, origin top left, y down, crop box, `Rotate` plus the options' rotation, `UserUnit` times the scale) and `RenderOptions.GetPixelSize(page)` (rounded up to whole pixels), so every backend's image lines up with the rasterizer's snapshots.
- `SetTransform` comes before the paints it applies to and only when the CTM changes; `RestoreState` restores the transform, the clip (#105) and, as later tickets add them, everything else the backend keeps per state. Save/restore pairs with nothing painted between them (only transforms and clips) are dropped at build time, with what is between them.
- `PathView` is the path exactly as constructed, in user space: close open subpaths for filling, ignore a trailing single-point subpath, treat degenerate subpaths per §8.5.3.2/§8.5.3.3.1. Spans and views are valid only during the call: copy what you keep.
- Colour: convert with `list.Document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = <your model> }, color.DefaultColorSpaces)`. That expands Indexed, Separation, DeviceN, applies the defaults and goes through the engine's `IColorManagement` (the colour-management extension point). Check `PdfColorConverter.PaintsNothing` (Separation `None`). Cache converters per colour space if you convert per paint; the document caches them too.
- Strokes (#99): stroke in the stroke's user space (the transform set before it) so a non-uniform CTM gives an elliptical pen; each subpath on its own, dash pattern restarted per subpath, closed subpaths joined where they close, the last and first dash of a closed subpath joined when both are on, a degenerate subpath a dot only with round caps, a lone moveto nothing, zero-length dashes painted with their caps along the path, width 0 one device pixel. The software rasterizer's `Stroker` (outline polygons, non-zero fill, round joins between the flattened pieces of a curve, at most 2^20 dashes per stroke) and `paths-strokes.pdf` are the reference; a stroke in a Pattern colour is not in the list yet.
- Clips (#105): `IntersectClip` intersects the current clip with the inside of the path (the area `FillPath` would fill with that rule) under the current transform; it arrives after the paint of its path object, and a form's bounding box arrives the same way inside the state saved around the form. `RestoreState` brings back the clip of the matching `SaveState`. The replay starts with the initial clip, the page area your renderer maps (the crop box). Anti-alias clip edges like fill edges. The software rasterizer's reference: `RasterClip` (a device rectangle for axis-aligned rectangles, the no-mask fast path, times a pooled 8-bit coverage mask for other paths), with every paint's coverage going through `ScanlineRasterizer.Paint`/`Fill` so strokes, images and shadings are clipped too. Text clips (§9.3.6) are not in the list yet (#107).
- Shadings (`DrawShading`, `FillPathWithShading`, `StrokePathWithShading`, #109): `DisplayShading.Shading` is the resolved model (`PdfAxialShading`, `PdfRadialShading`, ... with geometry, Domain, Extend, Background, BBox, AntiAlias and `EvaluateFunction`); `DisplayShading.Matrix` maps its target space to default user space (the CTM at `sh`, pattern space for a pattern; independent of `SetTransform`, which still places the path). `sh` fills the whole current clip and ignores the Background; pattern paints fill the Background outside the shading's bounds (`PaintsBackground`). Convert the function's colours with `GetColorConverter(shading.Shading.InterpolationColorSpace, shading.Conversion with { Target = ... }, shading.DefaultColorSpaces)` and never interpolate after conversion (§8.7.4.4). Native gradients fit Types 2 and 3 (Extend = clamp vs. decal; radial must take the greatest s, §8.7.4.5.4). The software rasterizer's reference: `ShadingSource` (a 1024-entry table of converted colours per paint), `ShadingGeometry` (s per point) and `ShadingPainter` (a coverage sink of `ScanlineRasterizer.Paint`, so the clip applies). The list carries Types 1 and 4 to 7 too; the rasterizer skips them until #110/#111.
- Images (`DrawImage`, #102): image space maps to the unit square through `[1/Width 0 0 -1/Height 0 1]` (first row at the top), then the current transform. Map samples through `DecodeMap` (Indexed: `MapToIndices`), convert with `GetColorConverter(image.ColorSpace, conversion with { Target = ... }, defaultColorSpaces)` (the conversion's intent is already the image's Intent entry when it has one), resample as your platform does; honour `Interpolate` when enlarging. Key per-surface caches on the `DisplayImage` instance.
- Masks (#106): a mask and its image both map onto the unit square whatever their sizes, so resample each from its own grid (never decode a mask at the image's size). `Mask` (1 bit, `ImageRows.Stencil` gives 255 where it paints) and `ColorKey` (on raw samples, `ImageRows.ColorKey`) punch holes; `SoftMask` values through its own `DecodeMap` are opacities; remove `Matte` from the component values before conversion (`ImageRows.Unpremultiply`; Indexed: the table's base-space colours from `ColorTable`), then composite premultiplied. `FillImageMask` paints the colour where the stencil paints, resampled like an image. The software rasterizer folds a mask of the image's size into the converted pixels' alpha (`ImageSource`) and samples one of another size on its own grid (`MaskSource`, `ImagePainter`), every image and stencil paint going through `ScanlineRasterizer.Paint` so the clip applies. Stencil masks in a Pattern colour are not in the list yet.
- Annotations (#98): `PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, ContentOptions)` builds the content, annotation and form-field layers (`RenderOptions.Layers`, All by default); between `BeginAnnotation` and `EndAnnotation` come the appearance's paints, already placed by AA. `BeginAnnotation` saves the state as `SaveState` does. For `NoZoom`/`NoRotate` (§12.5.3, Figure 78) map default user space to the device about `Rectangle`'s upper-left corner: keep the image of that corner, drop the device transform's scale (to one pixel per unit) and/or its rotation (to upright, y flipped), until `EndAnnotation`. Composite the appearance as a group with `Opacity` (the annotation's ca) and `BlendMode`; the software rasterizer clips to the whole pixels around the rectangle and paints a translucent annotation (or one in a blend mode other than Normal) into one of its transparency-group layers, isolated and knockout as `Isolated`/`Knockout` say, composited at `EndAnnotation` (`SoftwareRasterizer.Annotations.cs`, `SoftwareRasterizer.Groups.cs`).
- Transparency groups (#114): `DrawGroup(in DisplayGroup)` hands over a group XObject (`Transparency`), the page group (`Page`, always isolated, composite onto white in its `ColorSpace`) or the implicit knockout group of a translucent fill and stroke (`FillAndStroke`). The list does not replay the content: call `group.Replay(this)` (once or more, e.g. per colour plane) between your own layer begin and end. A backend without layers may replay directly: exact for a non-isolated, non-knockout, Normal, opacity-1 group, an approximation otherwise (report a diagnostic). The software rasterizer's design (layers the size of the group's bounds with a clip origin, knockout and alpha/shape tracking through `ScanlineRasterizer.Hook`, gray/CMYK pixel models, a probe that paints a group directly when that is the same) is in `GroupLayer` and `SoftwareRasterizer.Groups.cs`.
- Text (`DrawGlyphs`, #100): each glyph maps glyph space -> user space through `run.GetGlyphTransform(i)` (program FontMatrix x [HorizontalScale 0 0 1 0 0] x TextMatrix), then the current transform. Outline backends fill `run.GetOutline(i)` non-zero with `FillColor` when `Fills`, then stroke the outline mapped to user space with `StrokeStyle`/`StrokeColor` when `Strokes`, glyph by glyph (mode 2 fills then strokes each glyph). A run with neither (mode 3, invisible) paints nothing; it is in the list for hit-testing. A native backend may draw `run.Program` glyph ids with its font engine instead, but must use the outlines when `RenderOptions.ForceOutlines` is set (golden-image comparison). `HorizontalScale` is 1 except for glyphs drawn with a substitute (PDF width / substitute width). Outlines are interned per (program, glyph id) in the list: key glyph caches on the outline or the (program, glyph id) pair. Type 3 glyphs (#108) and text clips (#107) are not in the list yet.
- Tiling patterns (`FillPathWithTilingPattern`, `StrokePathWithTilingPattern`, #112): `DisplayTilingPattern.Cell` is the cell already built into a display list whose default user space is pattern space (an uncoloured pattern's colour is baked in, its transparency state starts at the defaults, §11.6.7), clipped to the BBox; `Matrix` maps pattern space to default user space (independent of `SetTransform`, which places the path); `Pattern` gives BBox, XStep, YStep (either may be negative) and TilingType. Replicate the cell at (i × XStep, j × YStep) in pattern space over the painted area, overlapping where the steps are smaller than the BBox, and composite the tiled result as one object with the paint's alpha and blend mode. Every paint with the same pattern on a page hands over the same `Cell` instance: key per-surface caches (tile bitmaps, CGPattern, picture shaders, image brushes) on it. The software rasterizer's reference: `SoftwareRasterizer.Tiling.cs` (the cell rendered once per paint through a child backend into a scratch bitmap; TilingType 1/3 round the steps to whole pixels, 2 rounds each tile's position; the BBox corner snapped to the pixel grid; tiles laid into a layer, then `TilingPainter` composites it through `ScanlineRasterizer.Paint`). Glyph runs and stencil masks in a tiling pattern are not in the list yet.
- Not in the list yet (later Phase 3 tickets): text clips, Type 3 glyphs, transparency groups and soft masks.

A backend must also, as those operations arrive:

- draw clipping (path and text, intersect), decoded images with transforms and resampling, shadings 1–7, tiling patterns, glyph runs (font reference + glyph ids + positions, native glyph rendering where the platform has it with an outline fallback when `ForceOutlines` is set), and the transparency model (§11: isolated and non-isolated groups, knockout, soft masks, the 16 blend modes, constant alpha);
- honour the render options of Track 3A: scale or DPI, rotation, clip rectangle (tiled region rendering), layer selection (annotations and forms are separate replayable lists), rendering intent, anti-aliasing, cancellation and progress;
- keep per-surface resource caches (glyphs, images, shaders) within the deterministic memory budgets the viewer relies on;
- fall back to the software rasterizer for any group or blend it cannot express (plan, Track 3D) rather than approximate silently, and record a `Diagnostic` when it does;
- allocate nothing per operation, glyph or scanline during replay; buffers are pooled per surface (`CLAUDE.md`, "Code conventions");
- render each corpus page at `GoldenCorpus.Dpi` (72) through `GoldenCorpus.CreateRenderOptions()` so the golden-image harness (#120) can compare it with the rasterizer's golden image; any 8-bit four-channel layout works (`GoldenPixelFormat`: BGRA or RGBA, premultiplied or straight, transparent or white page).

Testing against the contract: `Broadside.TestSupport.RecordingBackend` records every call as a line (`save`, `restore`, `transform [a b c d e f]`, `fill nonzero|evenodd M x y L ... Z | Family c0 c1 ...`, `stroke width Cap Join miterLimit [dash ...] phase [adjust] M ... | Family c0 c1 ...`, `clip nonzero|evenodd M x y L ... Z`, `image WxH Family bpc N[ interpolate][ mask WxH[ inverted]][ smask WxH Family bpc N][ matte m ...][ colorkey min max ...]`, `imagemask WxH[ inverted][ interpolate] | Family c0 c1 ...`) and keeps each fill's and stroke's colour space, components, defaults and conversion (`Fills`, `Strokes`), each image paint (`Images`) and each stencil mask paint (`ImageMasks`); `Broadside.Tests.Rendering.DisplayListTests` shows the pattern. `DisplayListBenchmarks` (Build, Replay) is the replay allocation baseline.

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
