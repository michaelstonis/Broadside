# Broadside Uno Platform viewer sample

An Uno Platform app around `Broadside.Viewer.Uno`'s `PdfView` (#225): a toolbar (documents, previous and next page, zoom, fit width,
dark mode, find) over the viewer. One Uno.Sdk project with two heads:

- **Desktop** (`net10.0-desktop`, Uno's Skia renderer in a native window on macOS, Windows and Linux X11). Tiles, text and search run
  on worker threads.
- **WebAssembly** (`net10.0-browserwasm`, Uno's Skia renderer on a canvas). No .NET WebAssembly threads: tiles, text and search run in
  cooperative slices on the page's thread, between frames.

```sh
# Desktop
dotnet build samples/Broadside.Viewer.Uno.Sample -f net10.0-desktop -t:Run

# Browser (needs the wasm-tools workload); serve artifacts/uno-sample-wasm/wwwroot with any static file server
dotnet publish samples/Broadside.Viewer.Uno.Sample -f net10.0-browserwasm -c Release -o artifacts/uno-sample-wasm
```

The documents are embedded: the textbook, paper and application form from `samples/documents/pdf` and `tests/Corpus/tagged-structure.pdf`.

Input: drag or wheel to scroll, Ctrl (Cmd) + wheel or pinch to zoom, double-tap to zoom, mouse drag over text to select (double
click: word, triple: line), long press on touch, Ctrl+A / Ctrl+C, Tab between links and fields, Ctrl+F to find, Enter or F3 for the next
match, the + and - keys to zoom, H/T/L/K for the next heading, table, list or link (as screen readers do).

## UI tests

`tests/Broadside.Viewer.Uno.UITests` drives both heads; see its project file. The sample's test hooks are in `Automation.cs`: with
`?automation=1` the browser head publishes its state to `globalThis.broadsideState` for Playwright, and with
`BROADSIDE_UNO_SCRIPT=1` the desktop head runs its script (open, scroll, zoom, select, search, dark mode, automation peers) and exits.
