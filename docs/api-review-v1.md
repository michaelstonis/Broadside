# v1 API review

The package-by-package review of the public API before the v1 freeze (issue #262), against the [API review checklist](../site/articles/api-review.md). Every shipping package was run through the automated guards in `tests/Broadside.ApiGuard.Tests` and reviewed by hand for the rules the guards cannot check. After the review, every package's `PublicAPI.Unshipped.txt` entries moved into `PublicAPI.Shipped.txt`. From here on a removed or changed member fails the build (RS0017), and the guard `No_shipped_member_is_removed_or_changed` fails CI when someone silences that error with a `*REMOVED*` line. `./check-api-freeze.sh` proves it: it renames a shipped method of `Broadside.Fonts.Cmaps`, builds, and expects RS0017 and RS0016.

Surface size is the number of `PublicAPI` entries (types, members, enum values and constants) and of public types, after the fixes below.

Decisions that apply to every package:

- All 17 packages ship as 1.0.0, the viewer packages included. The viewer namespaces (`Broadside.Viewer` and one per shell) are allowed next to the spec's areas.
- Every breach of a naming or shape rule was fixed, except the accepted exceptions listed per package (also in `KnownViolations` and in the checklist's "Accepted exceptions").
- Nothing new is marked `[Experimental]`. The document builder in `Broadside.Layout` was already marked `[Experimental("BROADSIDE0001")]` before this review and still is; see that package and the open question at the end.
- Overloads replace defaulted parameters: wherever a method had a defaulted parameter beside another overload of the same name, it is now two overloads without defaults. The only defaulted parameter left on an overloaded method is a trailing `CancellationToken cancellationToken = default`.

## Broadside

- **Surface:** 5,858 entries, 589 public types.
- **Findings:**
  - `PdfOptions.WithLoggerFactory` took an implementation, so it is a `Use*` method.
  - `GenerateAppearances` on `PdfChoiceField`, `PdfTextField` and `PdfToggleButtonField` used the verb the glossary avoids for Write.
  - `DisplayList.Replay` and both `DisplayList.BeginReplay` overloads took the cancellation token before the progress callback.
  - Defaulted parameters beside an overload: `PdfDocument.AddEmbeddedFile`, `PdfDocument.EmbedFont`, `PdfDocument.EmbedTrueTypeFont`, `PdfDocument.GetColorConverter`, `PdfPage.CreateHitTestIndex`, and the `PdfEngine(IOptions<PdfOptions>, ILoggerFactory?)` constructor.
  - Abstract classes a consumer derives from: `ContentProcessor`, `ContentEditor`, `FontProgram`, `FontCharacterMap`, `PdfCredentials`.
  - `IRenderingBackend` has a managed default (the software rasterizer) but is not registered through an options type.
  - `PdfPublicKeyEncryptionOptions.AddOwner` and `AddRecipient` are neither `Use*` nor `With*`.
- **Fixed:**
  - `PdfOptions.UseLoggerFactory` (was `WithLoggerFactory`).
  - `WriteAppearances()` on the three field types (was `GenerateAppearances()`).
  - `DisplayList.Replay(IRenderingBackend, IProgress<double>?, CancellationToken)` and `DisplayList.BeginReplay(IRenderingBackend, [PdfRectangle,] IProgress<double>?, CancellationToken)`: the token is last.
  - `AddEmbeddedFile(string, ReadOnlySpan<byte>)` beside `(…, PdfEmbeddedFileOptions?)`; `EmbedFont(ReadOnlyMemory<byte>)` and `EmbedTrueTypeFont(ReadOnlyMemory<byte>)` beside `(…, int faceIndex)`; `GetColorConverter(PdfColorSpace, ColorConversion)` beside `(…, PdfDefaultColorSpaces?)`; `CreateHitTestIndex(DisplayList)` beside `(…, PdfOptionalContentState?)`; `PdfEngine(IOptions<PdfOptions>)` beside `(…, ILoggerFactory?)`. Existing calls compile unchanged.
- **Accepted exceptions:**
  - `ContentProcessor`, `ContentEditor`, `FontProgram`, `FontCharacterMap`, `PdfCredentials` stay abstract base classes, not interfaces: an abstract base can gain members in 1.x without breaking the classes derived from it.
  - `IRenderingBackend` is passed to `DisplayList.Replay` instead of being registered on options: a display list is replayed into whichever backend draws it.
  - `PdfPublicKeyEncryptionOptions.AddOwner` and `AddRecipient` are accumulators (one call per certificate adds an entry), which neither `Use*` (select) nor `With*` (set) describes.
- **Owner sign-off:** pending

## Broadside.Rendering

- **Surface:** 181 entries, 14 public types.
- **Findings:** `SoftwareRasterizer.BeginRender(DisplayList, Bitmap, in Matrix, CancellationToken, IProgress<double>?)` took the token before the progress callback. `PdfImageRenderingExtensions.ToBitmap(PdfImage, PdfColor, PdfDefaultColorSpaces? = null)` had a defaulted parameter beside other `ToBitmap` overloads.
- **Fixed:** `BeginRender(DisplayList, Bitmap, in Matrix, IProgress<double>?, CancellationToken)`. `ToBitmap(PdfImage, PdfColor)` beside `ToBitmap(PdfImage, PdfColor, PdfDefaultColorSpaces?)`.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Rendering.Skia

- **Surface:** 25 entries, 3 public types (`SkiaBackend`, `SkiaImageDecoder`, `SkiaImageDecoderExtensions`).
- **Findings:** none. `SkiaBackend` already follows the glossary.
- **Fixed:** nothing.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Rendering.WebGL

- **Surface:** 27 entries, 2 public types (`WebGLBackend`, `WebGLRender`).
- **Findings:** the backend was named `WebGLRenderer` ("Renderer" is the word the glossary avoids for Backend), and `InitializeAsync()` took no cancellation token.
- **Fixed:** `WebGLBackend` (was `WebGLRenderer`); `InitializeAsync(CancellationToken cancellationToken = default)`, where the token stops the caller's wait and the shared initialization still completes for later calls.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Rendering.Android

- **Surface:** 25 entries, 3 public types (`AndroidCanvasBackend`, `AndroidImageDecoder`, `AndroidImageDecoderExtensions`). Checked from its `PublicAPI` files and by hand (it builds only with the Android workload).
- **Findings:** none. Every class is sealed or static; `UseAndroidImageDecoder` is a `Use*` that registers an implementation.
- **Fixed:** nothing.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Rendering.CoreGraphics

- **Surface:** 20 entries, 2 public types (`CoreGraphicsBackend`, `ImageIODecoder`). Checked from its `PublicAPI` files and by hand; built on macOS.
- **Findings:** the backend was named `CoreGraphicsRenderer`.
- **Fixed:** `CoreGraphicsBackend` (was `CoreGraphicsRenderer`). The internal display-list consumer that held that name is now `CoreGraphicsReplayer`, as Skia's is `Replayer`.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Rendering.Direct2D

- **Surface:** 31 entries, 4 public types (`Direct2DBackend`, `Direct2DDeviceType`, `WicImageDecoder`, `WicImageDecoderExtensions`). Checked from its `PublicAPI` files and by hand; it builds only on Windows, so the rename was checked by a search of every use and of its `PublicAPI` files.
- **Findings:** the backend was named `Direct2DRenderer`, and `Direct2DDriver`, the enum that picks the Direct3D device (graphics hardware or WARP), used "Driver", which the glossary avoids for Backend.
- **Fixed:** `Direct2DBackend` (was `Direct2DRenderer`; the internal consumer is now `Direct2DReplayer`). `Direct2DDeviceType` (was `Direct2DDriver`), with the backend's `DeviceType` property (was `Driver`) and constructor parameter `deviceType` (was `driver`).
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Fonts.Cmaps

- **Surface:** 5 entries, 2 public types.
- **Findings:** none.
- **Fixed:** nothing.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Fonts.Standard14

- **Surface:** 6 entries, 2 public types.
- **Findings:** none.
- **Fixed:** nothing.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Layout

- **Surface:** 928 entries, 82 public types.
- **Findings:** `TableElement.Border(Length, PdfColor? = null)` had a defaulted parameter beside `Border(TableBorder)`. The document builder (`Broadside.Layout.Flow` builder types and `Broadside.Layout.Flow.Units`) is marked `[Experimental("BROADSIDE0001")]` from its own design (#397).
- **Fixed:** `Border(Length)` beside `Border(Length, PdfColor?)`.
- **Accepted exceptions:** none by the guards.
- **Experimental surface:** the document builder, diagnostic ID `BROADSIDE0001` (URL https://github.com/michaelstonis/Broadside/issues/397). Left as it was; see the open question.
- **Owner sign-off:** pending

## Broadside.Viewer

- **Surface:** 898 entries, 73 public types.
- **Findings:**
  - Async methods without a cancellation token: `DocumentSession.OpenAsync(Stream)` and `OpenAsync(HttpClient, Uri)`.
  - Method names starting with the verb the glossary avoids for Read: `TextSelection.LoadPageAsync`, `AnnotationLayer.LoadPageAsync`, `AccessibilityBridge.LoadPage` and `LoadPageAsync`, `DocumentOutline.LoadAsync`, `PageLabelTable.LoadAsync`.
  - Type names with a noun the glossary avoids: `IAccessibleElementProvider`, `IReadingOrderProvider`, `IRemoteTileRenderer`.
- **Fixed:**
  - `OpenAsync(Stream, CancellationToken cancellationToken = default)` and `OpenAsync(HttpClient, Uri, CancellationToken cancellationToken = default)`, as the core's open entry points are.
  - `ReadPageAsync` on `TextSelection`, `AnnotationLayer` and `AccessibilityBridge`, `AccessibilityBridge.ReadPage`, `DocumentOutline.ReadAsync`, `PageLabelTable.ReadAsync`.
  - `IAccessibleElementSource` (was `IAccessibleElementProvider`): what the annotation, link and form layers implement to supply a page's accessible elements.
- **Accepted exceptions:** `IReadingOrderProvider` and `IRemoteTileRenderer` keep their names: they are the glossary's own terms ("Reading-order provider", "Remote tile renderer"), so they follow the glossary rather than break it.
- **Owner sign-off:** pending

## Broadside.Viewer.Avalonia

- **Surface:** 71 entries, 5 public types.
- **Findings:** `PdfView.CopySelectionAsync()` took no cancellation token.
- **Fixed:** `CopySelectionAsync(CancellationToken cancellationToken = default)`: the token stops the wait for the selection's pages; nothing is copied then.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Viewer.Blazor

- **Surface:** 101 entries, 7 public types.
- **Findings:** `WebGLTileBackend.InitializeAsync()`, `BrowserImageDecoder.InitializeAsync()` and `RenderWorker.StartAsync(TimeSpan?)` took no cancellation token.
- **Fixed:** each gained a trailing `CancellationToken cancellationToken = default` that stops the caller's wait; the shared initialization or worker start still completes for later calls.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Viewer.Maui

- **Surface:** 134 entries, 14 public types. Checked from its `PublicAPI` files and by hand.
- **Findings:** `PdfView.CopySelectionAsync()` and `RefreshAsync()` on `PdfAnnotationsPanel`, `PdfFormPanel` and `PdfSignaturesPanel` took no cancellation token.
- **Fixed:** each gained a trailing `CancellationToken cancellationToken = default` (the form and signature panels pass it to the read they wait for).
- **Accepted exceptions:** none. By hand: every class is sealed or static.
- **Owner sign-off:** pending

## Broadside.Viewer.Uno

- **Surface:** 74 entries, 7 public types. Checked from its `PublicAPI` files and by hand.
- **Findings:** `PdfView.CopySelectionAsync()` took no cancellation token. `GlobalStaticResources` is public because Uno.Sdk generates it for every Uno library; it is not ours to rename or hide.
- **Fixed:** `CopySelectionAsync(CancellationToken cancellationToken = default)`.
- **Accepted exceptions:** none by the guards.
- **Owner sign-off:** pending

## Broadside.Viewer.WinUI

- **Surface:** 57 entries, 4 public types. Checked from its `PublicAPI` files and by hand; it builds only on Windows.
- **Findings:** `PdfView.CopySelectionAsync()` took no cancellation token.
- **Fixed:** `CopySelectionAsync(CancellationToken cancellationToken = default)`.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Broadside.Viewer.Wpf

- **Surface:** 60 entries, 4 public types. Checked from its `PublicAPI` files and by hand; it builds only on Windows.
- **Findings:** `PdfView.CopySelectionAsync()` took no cancellation token.
- **Fixed:** `CopySelectionAsync(CancellationToken cancellationToken = default)`.
- **Accepted exceptions:** none.
- **Owner sign-off:** pending

## Open question for the owner

The owner decided that nothing is marked `[Experimental]` in v1, but the document builder in `Broadside.Layout` already carries `[Experimental("BROADSIDE0001")]` from its own design (#397), and the README and the building-documents guide tell users to suppress that warning. This review left it as it was. Either remove the attribute (the builder ships as stable 1.0 API under the freeze, and the README, guide and `NoWarn` entries are cleaned up), or keep it as the one experimental surface, listed here with its ID.
