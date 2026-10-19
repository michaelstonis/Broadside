# Broadside .NET MAUI viewer sample

A MAUI app around `Broadside.Viewer.Maui`'s `PdfView`: the same gallery as the Blazor demo, TraceMonkey first, drawn with SkiaSharp
(tiles rendered off the UI thread, composed on the GPU: Metal on Mac Catalyst, OpenGL ES on Android and iOS).

Needs the `maui` workload. Run it on Mac Catalyst from the repository root:

```sh
dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-maccatalyst -t:Run
```

Android (a running emulator or device) and iOS simulator: `-f net10.0-android -t:Run`, `-f net10.0-ios -t:Run`.

Try: drag or swipe to scroll (touch flings), pinch or Ctrl/Cmd+wheel to zoom, double-tap to toggle 2x fit-width, drag over text with
a mouse (double click: word) or long-press on touch and drag the handles to select, Cmd/Ctrl+C or the Copy button to copy, tap a
form field to edit it, Tab between fields, arrows, Page Up/Down, Home/End and +/- on a keyboard. A web link asks before it opens;
links inside the document navigate.

Smoke run: `BROADSIDE_SMOKE=1` (Android: `adb shell am start -n dev.broadside.viewer.sample/.MainActivity --ez smoke true`) opens
TraceMonkey, waits for page 1, captures the frame from the GPU surface, logs `BROADSIDE_SMOKE_OK` with what it measured and quits.

Frame bench (#357): `BROADSIDE_FRAMES=1` (iOS simulator: `SIMCTL_CHILD_BROADSIDE_FRAMES=1 xcrun simctl launch --console-pty ...`;
Android: `--ez frames true`) scrolls TraceMonkey, then pdf.js issue5567 zoomed in (bundled only when `BROADSIDE_CORPUS_DIR` has it at
build time), for four seconds each while a probe thread measures how long the UI thread takes to pick up work, and logs one
`BROADSIDE_FRAMES` line per document (`max` is the longest UI-thread stall).

## Documents

- `tracemonkey.pdf`: "Trace-based Just-in-Time Type Specialization for Dynamic Languages" (Gal et al., PLDI 2009), the default document
  of pdf.js, taken from [mozilla/pdf.js](https://github.com/mozilla/pdf.js/tree/master/test/pdfs) `test/pdfs` at commit
  `c53f395e`, distributed in that repository under its Apache-2.0 licence (linked from the Blazor sample).
- `signed.pdf`: the Blazor sample's self-signed demo document.
- Every other document is linked from `tests/Corpus` (Broadside's own hand-written files, MIT).
