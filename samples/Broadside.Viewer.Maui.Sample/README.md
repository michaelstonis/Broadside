# Broadside .NET MAUI viewer sample

A MAUI app around `Broadside.Viewer.Maui`'s `PdfView` and panels: the same gallery as the Blazor demo, the sample documents first, drawn with SkiaSharp
(tiles rendered off the UI thread, composed on the GPU: Metal on iOS and Mac Catalyst, OpenGL ES on Android).

Needs the `maui` workload. Run it on Mac Catalyst from the repository root:

```sh
dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-maccatalyst -t:Run
```

Android (a running emulator or device) and iOS simulator: `-f net10.0-android -t:Run`, `-f net10.0-ios -t:Run`.

It is the Blazor demo's feature set in native MAUI patterns (#358). On a phone: a compact top bar (documents, title, pager, save or
share), the tools in a bottom bar, and each tool's panel in a bottom sheet (drag its handle down or tap × to close). On a tablet or
desktop (720 units and wider): zoom in the top bar, a rail of tools on the left and the panel in a sidebar beside the pages.

- Documents (☰): the gallery, a PDF from this device (the platform's file picker), or an http(s) address read through range requests.
- Search, Outline, Pages (thumbnails), Annotate, Info (facts, diagnostics, tag tree), Signed (signature status) and View (layout,
  rotation, zoom, dark mode, GPU or CPU composition) are `Broadside.Viewer.Maui`'s reusable panels (`PdfSearchPanel`,
  `PdfOutlinePanel`, `PdfThumbnailsPanel`, `PdfAnnotationsPanel`, `PdfDocumentInfoPanel`, `PdfSignaturesPanel`,
  `PdfViewSettingsPanel`), each bound to the `PdfView` through its `Viewer` property. View adds the sample's tile renderer switch
  (SkiaSharp or Broadside's managed rasterizer).
- Create: the Blazor demo's `DocumentCreator` (linked): the flow layout package, saved as PDF/A and PDF/UA, opened at once.
- Save or share (⤴): the document with every edit, through the platform's share sheet (which can also save it to Files).

Gestures: drag or swipe to scroll (touch flings), pinch or Ctrl/Cmd+wheel to zoom, double-tap to toggle 2x fit-width, drag over text
with a mouse (double click: word) or long-press on touch and drag the handles to select, Cmd/Ctrl+C to copy, tap a form field to edit
it, Tab between fields, arrows, Page Up/Down, Home/End and +/- on a keyboard. A web link asks before it opens; links inside the
document navigate.

UI tests (#358): `BROADSIDE_UITEST=1` (Android: `--ez uitest true`; iOS simulator: `SIMCTL_CHILD_BROADSIDE_UITEST=1`) walks every
panel through its real controls (`UiTests.cs`), logs one `BROADSIDE_UITEST_PASS` or `BROADSIDE_UITEST_FAIL` line per step and a
final `BROADSIDE_UITEST_OK` or `BROADSIDE_UITEST_FAILED`, then quits. `uitest.sh android|catalyst|ios <apk or app>` runs it and waits
for the verdict (it never boots a simulator or emulator); `.github/workflows/maui-ui.yml` runs it on Mac Catalyst and the iOS
simulator, `maui-android.yml` on an Android emulator after the smoke run.

Smoke run: `BROADSIDE_SMOKE=1` (Android: `adb shell am start -n dev.broadside.viewer.sample/.MainActivity --ez smoke true`) opens
the sample textbook chapter, waits for page 1, captures the frame from the GPU surface, logs `BROADSIDE_SMOKE_OK` with what it measured and quits.
`smoke-android.sh <apk>` installs an APK on the connected emulator or device, runs that intent and fails on `BROADSIDE_SMOKE_FAILED`,
a native abort or no verdict in time; `.github/workflows/maui-android.yml` runs it on the Release build (#368).

Frame bench (#357): `BROADSIDE_FRAMES=1` (iOS simulator: `SIMCTL_CHILD_BROADSIDE_FRAMES=1 xcrun simctl launch --console-pty ...`;
Android: `--ez frames true`) scrolls the sample catalogue, then pdf.js issue5567 zoomed in (bundled only when `BROADSIDE_CORPUS_DIR` has it at
build time), for four seconds each while a probe thread measures how long the UI thread takes to pick up work, and logs one
`BROADSIDE_FRAMES` line per document (`max` is the longest UI-thread stall).

## Documents

- TraceMonkey: "Trace-based Just-in-Time Type Specialization for Dynamic Languages" (Gal et al., PLDI 2009), the default document
  of pdf.js. Its first page carries the ACM's copyright notice, which does not allow redistribution, so the app does not bundle it:
  it reads pdf.js's copy (<https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf>) through range requests when the gallery opens it,
  which needs a network connection (the smoke run and the frame bench included).
- The sample documents: linked from `samples/documents/pdf` (sources and licences in `samples/documents/MANIFEST.md`).
- `signed.pdf`: the Blazor sample's self-signed demo document.
- Every other document is linked from `tests/Corpus` (Broadside's own hand-written files, MIT).
