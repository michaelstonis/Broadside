# Unsafe code and pooled buffer review

The security review pass of issue #274, for v1.0. It covers every construct that bypasses .NET bounds or type safety and every
pooled buffer in the shipping packages: `Broadside`, `Broadside.Rendering`, `Broadside.Layout`, the rendering backends (Skia,
CoreGraphics, Direct2D, Android Canvas, WebGL) and the viewers (`Broadside.Viewer` and its Avalonia, Blazor, MAUI, Uno, WinUI and
WPF packages, with the Blazor viewer's JavaScript that receives buffers from .NET). `Broadside.Fonts.Cmaps` and
`Broadside.Fonts.Standard14` have neither.

It sits next to the [threat model](threat-model.md) ("Managed parsing") and the [fuzzing coverage report](fuzz-coverage.md): fuzzing
finds what a hostile input does; this review argues, line by line, what it cannot do.

## Method

- **Unsafe constructs.** `unsafe` blocks and methods, pointers and `fixed`; `Unsafe.*` (`Add`, `As`, `ReadUnaligned`,
  `WriteUnaligned`); `MemoryMarshal.GetReference`, `GetArrayDataReference`, `CreateSpan` and `CreateReadOnlySpan`;
  `Vector128.LoadUnsafe` and `StoreUnsafe`; `CollectionsMarshal`; `[SkipLocalsInit]` with `stackalloc`; native calls that take a
  pointer and a length. Each row states why the construct is there and the concrete bound that keeps every access inside its
  buffer. Bounds-checked helpers (`MemoryMarshal.Cast`, `AsBytes`, `Read`, `BinaryPrimitives`) are noted once per group.
- **Pooled buffers.** Every `ArrayPool` rental and every wrapper that rents (`PooledBufferWriter`, `DecodedImageBuilder`,
  `RegionBuffer`, `JpxPlane`, `GlyphLoader`, `Pooled`, the thread-static object pools). Each row says where the buffer is returned,
  on which paths, and what bounds the requested length. The review looked for leaks, double returns, use after return, reads of
  stale pool contents beyond what was written, secrets returned unclear, and rentals sized by an unchecked file value.
- A **finding** is anything a hostile document reaches (or, where marked, a host configuration) that breaks memory safety, crashes
  the process, leaks one document's data into another, or allocates without a limit.

## Findings

| # | Where | What | Reached by | Status |
|---|---|---|---|---|
| U1 | `PostScriptRegisterProgram.Evaluate`, `PostScriptLowering` | `stackalloc double[RegisterCount]`, where `RegisterCount` counted the program's distinct literals with no limit: a Type 4 function of 10^5 literals that never deepens the stack overflowed the thread stack and killed the process | hostile file, default options | **Fixed**: constants count against `PostScriptLowering.MaxRegisters` (1,024); a larger program runs in the interpreter, whose stack is fixed at 100 entries. Test `PostScriptLoweringTests.A_program_of_more_distinct_literals_than_registers_runs_in_the_interpreter` |
| U2 | `JpxDecoder` tile buffers, `JpxCodeBlockDecoder.WriteOutput` | The tile buffer length was cast from `long` to `int` unchecked and the code-block output guard was computed in `int`: with `MaxImagePixels` raised above 2^31 a tile of 2^32 samples rented a 1-element array and the guard wrapped, so `Unsafe.Add` wrote past it (heap corruption) | hostile file, only when the host raises `MaxImagePixels` above `int.MaxValue` | **Fixed**: a tile whose component area exceeds `Array.MaxLength` is refused with `JpxLimitExceeded`, the rental length is `checked`, and the guard is computed in `long` |
| U3 | `StandardSecurityHandler.TryUser` (RC4 revisions 3 and 4) | `stackalloc byte[32 + documentId.Length]` with the trailer `ID` string unbounded: a multi-megabyte `ID` overflowed the stack when a password was tested | hostile encrypted file | **Fixed**: only an `ID` of up to 224 bytes goes on the stack; a longer one uses the heap |
| P1 | `ContentInterpreter` graphics state stack | Growing the stack (`q`, or a nested run's implicit save) returned the old array to the pool at once, cleared, while `ContentContext.State` references into it were still held: the display list builder holds one across soft mask groups and pattern cells, then reads the CTM, alphas and blend mode from a cleared array, or from an array another render had rented again | any page whose soft mask group or tiling pattern cell nests about 15 `q` | **Fixed**: an outgrown stack stays out of the pool until the top-level run ends (`ContentInterpreter.Release`) |
| P2 | `ClipArena` (`ContentContext.GetClip`, public) | Growing the clip arena returned the old verb and point arrays at once, while a `ClipView`, documented as valid until the run ends, could still span them across a nested run | third-party `ContentProcessor` holding a clip view across `RunForm`, `RunPatternCell` or `RunSoftMaskGroup` | **Fixed**: outgrown arrays are returned in `ClipArena.Reset` at the end of the top-level run |
| P3 | `Predictor.Run.Begin` | With empty predicted data the row buffers were sized from `Columns` alone: two rentals of up to the decoded-length limit (1 GiB by default) from a 20-byte stream | hostile file | **Fixed**: empty data has no rows and rents nothing |
| P4 | `StandardSecurityHandler.HashRevision6` | The revision 6 hash buffers (64 copies of the password, and AES output derived from it) went back to the shared pool unclear; they were also rented before a call that can throw | any AES-256 file opened or saved with a password | **Fixed**: both are returned with `clearArray: true`, the stack key is zeroed, and the rentals follow the cipher's creation |
| P5 | `ObjectEncryptor.EncryptCbc` | The plaintext of a stream being encrypted went back to the pool unclear | saving an encrypted document | **Fixed**: cleared before it is returned |
| P6 | `CosParser.ParseLiteralString`, `ContentInterpreter.SetDash`, `Jbig2SegmentList.Parse`, `WicCodec` (Direct2D), `Paragraph` (Layout) | A rented array is not returned when the code between rent and return throws (strict mode, or out of memory) | strict mode with a malformed string, dash array or JBIG2 header | **Listed**: the array falls to the garbage collector; a performance cost only, no safety issue |
| R1 | Direct2D and CoreGraphics `ImageReducer.Upright` | The horizontal pass rents `outWidth × (channels + 1) × source height` doubles with no limit (about 10 GB for a 4096 × 65536 RGB image drawn into a 4096 × 1 strip); a larger product overflows `int` and throws. Skia and Android already band this pass (issue #432) | hostile file rendered on those backends | **Listed** (advisory, follow-up): port the banded pass; managed and bounds-checked, so denial of service only |
| R2 | Direct2D, CoreGraphics, Android mesh and radial shading textures | When one side of the texture is under a pixel, the other side is not limited: a shading `BBox [0 0 5e8 1e-6]` asks for a texture of about 2 GB | hostile file, after the raster cache declines the shading | **Listed** (advisory, follow-up): clamp each side to the device's largest bitmap and the area to `MeshRasterizer.MaxPixels`; denial of service only |
| R3 | `ColorantPlanes` (software rasterizer, overprint preview) | Each distinct Separation or DeviceN colorant adds one page-sized plane for the rest of the render, with no limit | hostile file, only with `RenderOptions.OverprintPreview` | **Listed** (advisory, follow-up): cap the spot planes and paint further spots through their process equivalent with a diagnostic |
| V1 | WPF viewer `FrameRenderer.CreateImage` | `BitmapSource.Create` was told the buffer holds `Stride × Height` bytes; a tile over caller memory (a third-party `IRemoteTileRenderer`) with a padded stride only holds `(Height − 1) × Stride + Width × 4`, so WIC could read past the pinned buffer | an application's remote renderer, not a document | **Fixed**: such a buffer is packed to `Width × 4` rows first |
| R4 | `ImageSource`, `MaskSource` (software rasterizer) | A conversion that throws partway leaves the previous cache key valid over a partly overwritten buffer | a converter or ICC evaluation that throws | **Listed** (low): invalidate the key before converting |

Hardening notes without a current bug: in the Blazor viewer, a throw between a browser decode and its release leaves the decoded
samples in the JavaScript decode map until the page goes, and a short decode result would leave stale pool bytes at the end of the
image's samples (the sizes are checked first, so neither happens today); the MAUI frame presenter does not dispose its paint objects
when disposed during a paint (a native leak); `HeadlessSurface.PresentedTile.Pixels` is a pooled bitmap the surface recycles, so a
span held across `Evict` or the next present reads recycled memory (managed, documented as surface-owned); the Direct2D, CoreGraphics and Android `PixelImage` constructors trust that the pixel buffer
holds width × height × channels bytes (every caller passes exactly that); `IccClut` relies on its parser for the table size its
unchecked reads assume; the Skia and Android `PackRgb` kernels rely on their one caller's span lengths; CoreGraphics' ImageIO decoder
hands ImageIO a no-copy `CFData` that must not outlive the pinned buffer (it does not today); `PooledBufferWriter.GetSpan` hands out
uncleared pooled memory, which a third-party `IStreamFilter` that advances past what it wrote would emit.

## Unsafe constructs: core (`Broadside`)

`AllowUnsafeBlocks` is on; no module has `SkipLocalsInit`. Bounds-checked helpers, not reviewed one by one: `MemoryMarshal.Cast` (14
uses), `MemoryMarshal.AsBytes` (4), `BinaryPrimitives` (380). `Unsafe.SizeOf<T>` (`RenderCacheEntries`, `DisplayList.Memory`) is
arithmetic only.

| Site | Construct | Why | Bounds | Verdict |
|---|---|---|---|---|
| `PdfObjectCopier.cs` | `MemoryMarshal.TryGetArray` | keep array-backed data, copy mapped data | read-only test; mapped data is copied | ok |
| `Filters/Codecs/MqDecoder.cs` | `GetReference(Words)` + `Unsafe.Add(context)` | branch-free MQ decoding | `context` is a `byte`; the table has 256 entries | ok |
| `Filters/Dct/JpegEncoder.cs` `Encode` | `[SkipLocalsInit]`, stackalloc components, predictors, block | encoder scratch | every component field set before use; predictors cleared; the block filled by the forward DCT | ok |
| `Filters/Dct/InverseDct.cs` (scalar, 12-bit, 1/2 and 1/4 scale) | `[SkipLocalsInit]` workspaces of 64, 32, 16 | IDCT scratch | pass 1 writes every entry pass 2 reads (skipped columns are never read) | ok |
| `Filters/Dct/InverseDct.Vector128.cs` | `GetReference`, `LoadUnsafe`, `StoreUnsafe`, `WriteUnaligned` | vector IDCT | coefficients and quantization sliced to 64, output to `7 × stride + 8` on entry; loads end at 64 | ok |
| `Filters/Dct/JpegColor.Vector128.cs` | `LoadUnsafe`, `StoreUnsafe` | YCbCr and YCCK conversion | inputs sliced to the width; loop to `width − 16`; stores end below `3` or `4 × width` | ok |
| `Filters/Jpx/JpxBlockJob.cs` | `unsafe` class, `fixed`, `byte*` shared with helper threads | share tile bytes with the thread pool | span of the original length; helpers enter under a gate only while open, and the run waits for all to leave before `fixed` ends | ok |
| `Filters/Jpx/JpxLifting.Vector128.cs`, `JpxLevelShift.Vector128.cs` | `GetReference`, `LoadUnsafe`, `StoreUnsafe` | wavelet lifting, interleave, component transforms, level shift | every span sliced to `n` on entry; loops to `n − 4` or `n − 8`; interleave target at least `2n` | ok |
| `Filters/Jpx/JpxCodeBlockDecoder.cs` `WriteOutput` | `GetArrayDataReference`, `Unsafe.Add` by row pitch | write coefficients | guard `output.Length ≥ (height − 1) × pitch + width`, now in `long` (U2) | ok after fix |
| `JpxCodeBlockDecoder.cs` significance, refinement, cleanup passes | `Unsafe.Add` over the state words and magnitudes, `IsAddressLessThan` walk | tier-1 hot loops | state is `(width + 2) × (stripes + 2)` words and every walk stays inside rows 1 to stripes, columns 1 to width, so ±1 and ±stride land in the border; magnitudes `stripes × 4 × width`; context indexes below 19; block size checked (≤ 1,024 a side, ≤ 4,096 samples) | ok |
| `JpxCodeBlockDecoder.cs` pooled magnitudes | read through `Unsafe.Add`, not cleared | allocation-free | a magnitude is read only where its significance bit is set, and setting the bit writes it | ok |
| `Images/PixelPacking.cs`, `Graphics/Colors/GrayExpansion.cs` | `LoadUnsafe`, `StoreUnsafe` | pack and expand pixels | spans sliced to the pixel count; loops to `count − 16` | ok |
| `Graphics/Colors/CmykCharacterization.Vector128.cs` | `GetReference`, `GetArrayDataReference(Weights)`, `Unsafe.Add` | CMYK polynomial | `cmyk` sliced to `4 × count`; loop to `count − 4`; weights 102 entries, index ≤ 101 | ok |
| `Numerics/DeterministicMath.cs` | `Unsafe.Add` into `PowLogTable`, `ExpTable` | table lookups | indexes masked to 127 and 255; tables of 384 and 256 entries | ok |
| `IO/MappedFileSource.cs` | `unsafe` class over a mapped file, `MemoryManager` segments | zero-copy files | reads check `offset < length` and `count ≤ length − offset` under `DangerousAddRef`; segments clipped to the file | ok (windows must not outlive the document, a documented contract; a file truncated by another process can fault) |
| `Content/ContentInterpreter.NestedRunCore.cs`, `.Editing.cs`, `.Forms.cs` | `CollectionsMarshal.AsSpan`, `SetCount` on the frame list | in-place frame updates | no mutation while a span lives; new slots cleared (reference type); indexes checked | ok |
| `Layout/GeometryLayout*.cs`, `Graphics/Colors/ColorSpaceCache.cs` | `CollectionsMarshal.AsSpan`, `GetValueRefOrAddDefault` | in-place updates | lists not mutated while spans or refs live; slices bounds-checked | ok |
| `Rendering/DisplayListBuilder.Images.cs` | `MemoryMarshal.TryGetArray` | adopt a lookup array | read-only test | ok |
| `Writing/FileWriter.cs`, `FileWriter.Linearized.cs` | `MemoryMarshal.AsMemory` | patch the `AuthCode` placeholder in the writer's own buffer | bounds-checked span, before the chunk is yielded | ok |
| `Graphics/Functions/PostScriptEvaluator.cs` | `[SkipLocalsInit]`, stackalloc 100 values and kinds | Type 4 interpreter | entries `[0, sp)` are always written; every read checks against `sp` | ok |
| `Graphics/Functions/PostScriptRegisterProgram.cs` | `[SkipLocalsInit]`, `stackalloc double[RegisterCount]` | lowered Type 4 registers | every register is written before it is read (constants and inputs copied in, every instruction writes its destination, joins get moves on every edge); size now at most 1,024 plus the outputs (U1) | ok after fix |
| `Security/StandardSecurityHandler.cs` `TryUser` | `stackalloc` sized by the trailer `ID` | MD5 input | now at most 256 bytes on the stack (U3) | ok after fix |

## Unsafe constructs: rendering, layout and backends

| Site | Construct | Why | Bounds | Verdict |
|---|---|---|---|---|
| `Broadside.Rendering/BoxFilter.cs` (scalar, vector, run averages) | `GetReference` + `Unsafe.Add` on columns and weights | per-sample bounds checks removed | weights sliced to `columns.Length` (or `Slice(start, length)`) on entry; loops to the slice length; pixel reads checked | ok |
| `CompositingRows.cs` (six vector kernels) | `LoadUnsafe`, `StoreUnsafe`, `ReadUnaligned<uint>` | SIMD compositing | other spans sliced to `count` first; loops to `count − 4`; a row is at most `Array.MaxLength` bytes | ok |
| `Icc/IccClut.cs` | `GetArrayDataReference` + `Unsafe.Add` | CLUT interpolation | the parser guarantees `values = Π grid × outputs`, every grid ≥ 2, channels ≤ 15 (`IccLut`); `Cell` keeps the index below the last point; NaN goes to 0 | ok |
| `ImagePainter.cs` | `[SkipLocalsInit]` scratch | image painting | each scratch entry written before it is read | ok |
| `Broadside.Layout/Paragraph.cs` | `CollectionsMarshal.AsSpan(runs)` | reorder in place | the list is not mutated while the span lives | ok |
| Skia `SkiaImageDecoder.cs` | `fixed` + `SKData.Create(ptr, length)`; `GetScanlines` into the builder and a rented band | native JPEG decoding | exact lengths; codec size must equal the request; `rows × rowBytes` within the band | ok |
| Skia `SkiaImageDecoder.PackRgb`, Android `AndroidImageDecoder.PackRgb` | `LoadUnsafe`, `StoreUnsafe` | RGBA to RGB | one caller, whose spans are exactly `4 × width` and `3 × width` | ok |
| Skia `SKImage.FromPixelCopy` uploads (image converter, reducer, backend, transparency, shading) | managed span to native copy | upload | every span exactly `w × h × channels` with `rowBytes = w × channels`, or a `Bitmap` validated at construction | ok |
| Document font bytes to native parsers (Skia, CoreGraphics, Android, DirectWrite) | native glyph rendering | glyph outlines | every backend copies the data first; glyph ids checked | ok (the platform font parsers remain attack surface, as the threat model says) |
| CoreGraphics `ImageIODecoder.cs` | `Memory.Pin` + `CFDataCreateWithBytesNoCopy`; span over `CFDataGetBytePtr` | ImageIO decoding | exact length; every CF object released before the pin; the output span length checked against `bytesPerRow × (h − 1) + w × pixelBytes` | ok |
| CoreGraphics `ShadingBuilder.cs` | `nfloat*` callback | gradient evaluation | 1 input and 4 outputs declared to CoreGraphics; table reads checked | ok |
| CoreGraphics `PixelImage.cs`, `CoreGraphicsBackend.Patterns.cs` | `CGDataProvider` + `CGImage`, `CGBitmapContext` over an array | upload, tile cells | buffers exactly `w × h × channels`; tiles within `MaxTilePixels` | ok |
| Android `AndroidImageDecoder.cs` | `fixed` + `SetByteArrayRegion`; `AndroidBitmap_lockPixels` span per row | decoding through Java | exact lengths; bitmap width, height and format checked against the request; the NDK guarantees `stride ≥ 4 × width` | ok |
| Android `AndroidCanvasBackend.Text.cs`, `PixelImage.cs` | `fixed` + `Set*ArrayRegion`; `NewDirectByteBuffer` | glyph batches, bitmap upload | counts at most the arrays' length; buffer exactly `rowBytes × height` | ok |
| Direct2D `ComInterfaces.cs` (218 vtable calls) | `delegate* unmanaged` through vtable slots | COM without a runtime | arguments are pointers to locals or fixed-size stack buffers; slots are constants; every HRESULT checked | ok |
| Direct2D `Direct2DSurface.cs`, `PixelImage.cs` | `CopyFromMemory`, mapped read-back, `CreateBitmap(data, pitch)` | uploads and read-back | every buffer exactly `w × h × channels`; the read-back bitmap is exactly the target size | ok |
| Direct2D shadings, strokes, text | `stackalloc` 256 gradient stops; `fixed` figure + `AddLines(count − 1)`; one-glyph runs | native drawing | constants; the figure grows to `count − 1` first | ok |
| Direct2D `WicCodec.cs` | pinned data + `InitializeFromMemory`; planar and packed `CopyPixels` | WIC decoding | released before unpinning; plane buffers sized from WIC's own descriptions; WIC validates sizes | ok |
| WebGL `WebGLInterop` | `Span<byte>` marshaled as a memory view | JavaScript hand-off | spans sliced to the exact length; JavaScript copies synchronously | ok |

Bounds-checked helpers in the backends (`MemoryMarshal.Cast` and `AsBytes` for masks and rows, Skia's path cache, the WebGL
command list, the Unicode line breaker) and every variable `stackalloc` of color components (at most 32, enforced by
`PdfColor.MaxComponents`) are fine.

## Pooled buffers: core (`Broadside`)

| Site | Rented | Returned | Size bound | Verdict |
|---|---|---|---|---|
| `Filters/PooledBufferWriter.cs` | byte buffer, grown on demand | `Dispose` (idempotent); the old buffer on growth | the decoded-length limit; the `DL` hint capped at 16 MiB | ok |
| `PooledBufferWriter` users (stream decoder and its sliced chain, stream encoder, CCITT, `PdfImage`, file writers) | wrapper | `using` or `try`/`finally`; the sliced chain's `Dispose` is idempotent and runs from every exit | as above | ok (the written memory never outlives the writer) |
| `Filters/FlateDecodeFilter.cs`, `PredictorEncoder.cs`, `LzwDecodeFilter.cs` | chunk, rows, tables | `finally` | constants, or the data | ok |
| `Filters/Predictor.cs` | two rows | `Run.Release` (idempotent) from every exit | the decoded-length limit; the data (P3) | ok after fix |
| `Filters/CcittFaxDecodeFilter.cs`, `Ccitt/MmrDecoder.cs` | changing-element arrays, row bytes | `finally` | `Columns` within the decoded-length limit; the caller's bitmap | ok |
| `Filters/DctDecodeFilter.cs`, `Dct/JpegComponent.cs`, `Dct/JpegEncoder.cs` | pooled decoder, coefficient planes, strips | `finally` through `JpegDecoder.Return` and `JpegFrame.Reset`; `finally` | coefficients within the decoded-length limit; JPEG dimensions | ok |
| `Filters/Jbig2/*` (statistics, refinement, region buffers, halftone planes, segment list) | contexts, bitmaps, segments | `Dispose`, `using`, `finally` | region and symbol limits, `Array.MaxLength` | ok (the segment list leaks to the GC if strict-mode parsing throws, P6) |
| `Filters/Jpx/*` (block job, joined tile parts, tile buffers, planes, wavelet lines, code-block state) | bytes, ints, floats, segments | `EndTile`, `Dispose` (idempotent), `finally`; old arrays on growth | tile samples within `MaxPixels` and `Array.MaxLength` (U2); code blocks within `MaxBlockSamples` | ok after fix |
| `Forms/PdfDefaultAppearance.cs` | two buffers of the DA length | `finally` | the DA string | ok |
| `Security/StandardSecurityHandler.cs` `HashRevision6` | two buffers of `(password + 64 + U) × 64` | `finally`, now cleared (P4) | password ≤ 127 bytes | ok after fix |
| `Security/ObjectEncryptor.cs` | padded plaintext | `finally`, now cleared (P5) | the stream | ok after fix |
| `Images/DecodedImageBuilder.cs` → `DecodedImage` | samples | ownership passed to `DecodedImage`, returned once in its `Dispose` (interlocked) | image limits, `checked` | ok (cleared; samples sliced to the written length) |
| `Images/PlatformImageDecoding.cs` | encoded copy | `finally`, after the platform decoder's task completes | the encoded length | ok |
| `Images/Png/PngReader.cs`, `PngImage.cs` | IDAT, profile chunk, rows, planes | `finally`; `Dispose` | IDAT and rows within `Array.MaxLength`; pixels within the limit; profile within `MaxProfileLength` | ok |
| `Images/ImageReducer.cs` | rows | `finally` | source geometry | ok |
| `Signatures/Validation/SignatureValidator.cs` | gap and digest windows | `finally` | `Contents` length + 4,098; 64 KiB | ok |
| `Objects/CosParser.cs` | literal string output | after use, not in `finally` | the token | ok (P6) |
| `VisibilityProgram.cs` | evaluation stack | `finally` | program size | ok |
| `Content/PathBuilder.cs`, `OperandArena.cs` | verbs, points, operands | `Trim`, growth | content length; operand limit | ok |
| `Content/ClipArena.cs` | nodes, verbs, points | outgrown arrays at `Reset` (P2) | content length | ok after fix |
| `Content/ContentInterpreter` graphics state stack | states | outgrown stacks at `Release` (P1), cleared | `MaxSaveDepth` (4,096) | ok after fix |
| `Content/ContentInterpreter.State.cs` `SetDash` | dash values over 32 | after use, not in `finally` | operand limit | ok (P6) |
| `Parsing/FileHeader.cs`, `FileScan.cs` | 64 KiB windows | `finally` | constants; reads sliced to what was read | ok |
| `Fonts/OpenType/OpenTypeShaper.cs`, `GlyphBuffer.cs` | joining forms, glyph buffer | `finally`; old buffer on growth | glyph count | ok |
| `Fonts/TrueType/TrueTypeFontProgram.Glyphs.cs` | glyph loader arrays | `Dispose` in `finally` | `MaxComponentsPerGlyph`; 65,535 points | ok |
| Object pools (text, image and layout extractors, display list builder, content interpreter) | objects | `finally` or `Abandon`, guarded against double ends | n/a | ok |

## Pooled buffers: rendering, layout and backends

| Site | Rented | Returned | Size bound | Verdict |
|---|---|---|---|---|
| `Broadside.Rendering/Pooled.cs` and its owners (color filter, image footprints, groups, patches, meshes, soft masks, tiling, overprint) | grow-only field buffers | the owner's `Dispose`; the field reset to empty, so never returned twice | render and area sizes; `MaxPatchTriangles` | ok (each buffer filled or cleared before it is read) |
| `ColorantPlanes.cs` | one plane per colorant | `Dispose` | the render size per plane; the plane count is not limited (R3) | listed |
| `MeshLayer.cs`, `GroupLayer.cs` | pixels, twin, alpha, shape | `Dispose`; ownership moves by swapping fields | `checked(w × h)` | ok |
| `ImageSource.cs`, `MaskSource.cs` | samples, masks, counts | `Dispose`; `finally` | image limits | ok (R4) |
| `Bitmap.cs` | pixels | `Dispose` | `w × h × 4 ≤ Array.MaxLength` | ok (`Pixels` exposes only the requested length) |
| `BitmapJpegEncoder.cs`, `PngEncoder.cs`, `PdfImageRenderingExtensions.cs` | rows and planes | `finally` | JPEG and image limits | ok |
| `GlyphCache.cs`, `RasterClip.cs`, `ScanlineRasterizer.cs` | glyph scratch and arena, clip masks, cells | `Dispose`, free list, growth | glyph masks ≤ 512; the clip box; cell counts | ok (clip masks fully written) |
| Backend `ImageConverter.cs` (Direct2D, CoreGraphics, Android, Skia, WebGL) | pixels, rows, floats | `finally` (WebGL: the texture cache's `finally`) | `w × h × 4 ≤ Array.MaxLength` | ok (every row written; native code receives exact spans) |
| Backend `ImageReducer.cs` output | reduced pixels | `finally` | `MaxPixels` (2^22) × channels | ok |
| Direct2D and CoreGraphics `ImageReducer.cs` horizontal pass | `outWidth × (channels + 1) × source height` doubles | `finally` | not limited (R1) | listed |
| Skia and Android `ImageReducer.cs` horizontal pass | banded | `finally` | `stride × span` | ok |
| Skia `SkiaImageDecoder.cs` band, `SkiaBackend.Transparency.cs` values | band, mask values | `finally` | `4 × 65,535 × 16`; `MaxRasterPixels` | ok |
| Direct2D `WicCodec.cs` | planes, pixels | `finally` | JPEG ≤ 65,535 a side; `checked` | ok (P6 on an overflow) |
| WebGL `Recorder.cs`, `WebGLBackend.cs` | read-back pixels | `finally` | the viewport and target | ok |
| Layout `Paragraph.cs`, `UnicodeBidi.cs`, `UnicodeLineBreaker.cs`, `Seams.cs` | levels, classes, breaks | `finally` | application text | ok (P6 in `Paragraph`) |

## Viewers

No viewer package parses untrusted bytes itself: document bytes, files and addresses go to the core, and JPEG data the Blazor
viewer decodes goes to the browser's decoder (`BrowserImageDecoder`, the only viewer type named like a decoder; the core decides
which images it may take, fuzz target `platform-image-eligibility`). Form edits from the page are parsed with `int.TryParse` and
clamped by the core's form filling. So the viewers add no fuzz target. Every tile bitmap is a pooled `Bitmap` whose `Pixels` is
exactly `Stride × Height` and whose size the tile scheduler's memory budget (`TileScheduler.Reserve`) has accepted.

| Site | Construct | Why | Bounds | Verdict |
|---|---|---|---|---|
| Avalonia `BitmapTileSurface.cs` | `unsafe`, `ILockedFramebuffer.Address`, a span per row | tile to a `WriteableBitmap` | the bitmap has the tile's size; each row writes `Width × 4` bytes at `y × RowBytes` (computed in `long`); source rows range-checked | ok |
| MAUI `TileImageSurface.cs`, `SkiaTileBackend.cs` | `fixed` + `SKImage.FromPixelCopy`, `SKSurface.Create` over the tile | tile upload; Skia renders into the tile | Skia touches at most `(Height − 1) × Stride + Width × 4` bytes; everything inside `fixed`; the render runs under the scheduler's gate, so the bitmap cannot be released meanwhile | ok |
| MAUI `PdfView.cs` | `SKBitmap.GetPixels` + `ReadPixels` | frame capture | the same image info and row bytes as the allocation; size from the canvas, not the document | ok |
| WPF `FrameRenderer.cs` | `fixed` + `BitmapSource.Create` | tile to a frozen bitmap | `Stride × Height` bytes, packed first when the buffer is shorter (V1) | ok after fix |
| Blazor `RenderWorker.cs`, `BrowserImageDecoder.cs`, `ViewerInterop.cs` and the JavaScript | `MemoryMarshal.CreateSpan` (read-only to writable for a `MemoryView`), `MemoryView` copies both ways | documents to the worker, JPEG data to the browser decoder and samples back, tiles and frame commands to the canvas | lengths are exact; JavaScript copies synchronously (`slice`, `copyTo`); a `TypedArray.set` past the destination throws `RangeError`; decoded sizes are checked against the request | ok |
| WinUI `WriteableTileImage.cs`, Uno `FrameView.cs` | `PixelBuffer.AsStream().Write` | tile upload | writes exactly `Width × 4 × Height`, the buffer's capacity | ok |
| Blazor `WebGLTileBackend.cs` | GPU read-back into a held tile | WebGL presentation | the hold is dropped before the tile is released; a released tile throws | ok |
| Uno `TileSurface.cs`, WinUI `TileStore.cs` | `ArrayPool` copies of tiles | handing tiles to the UI thread | returned once (`Interlocked.Exchange`, or swapped out before a `finally`); reads sliced to the copied length | ok |
| `TileScheduler.cs`, `DocumentSession.cs`, Blazor `RenderWorker.cs`, `HeadlessSurface.cs` | pooled `Bitmap` tiles | rendering | released on every path (presentation, failure, cancellation) under the scheduler's gate, or by `using` | ok |

## Totals

Unsafe constructs: 44 rows in the core (about 450 call sites, identical patterns grouped) and 37 in rendering, layout and the
backends (the 218 Direct2D vtable calls as one row), and 15 sites in the viewers. Pooled buffers: 54 rows in the core (about 115
rentals and 8 wrapper types), 33 in rendering, layout and the backends, and 8 in the viewers. Findings: 3 unsafe (U1 to U3, fixed),
6 pooled (P1 to P5 fixed, P6 listed), 1 in the viewers (V1, fixed) and 4 in the renderers (R1 to R4, listed as advisories for after
v1.0: all denial of service or robustness, none memory corruption).
