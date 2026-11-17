# Broadside WinUI 3 viewer sample

A WinUI 3 desktop app around `Broadside.Viewer.WinUI`'s `PdfView`: a toolbar (pages, zoom, copy, dark mode, find) over the view,
with a status line. Tiles are drawn by the managed software rasterizer on worker threads and shown as image elements the XAML
compositor draws. The app is unpackaged and carries the Windows App SDK runtime next to itself, so it runs from its output folder.

Windows only. From the repository root:

```sh
dotnet build samples/Broadside.Viewer.WinUI.Sample -r win-x64
artifacts/bin/Broadside.Viewer.WinUI.Sample/debug_win-x64/Broadside.Viewer.WinUI.Sample.exe [path-to.pdf]
```

Without a path it opens the sample textbook chapter (`documents/textbook.pdf` next to the exe).

Startup steps and any unhandled exception go to stderr and to `Broadside.Viewer.WinUI.Sample.log` next to the exe: if the app exits
without a window, that file says how far it got. There is no App.xaml, so the app loads WinUI's control styles in code and serves
WinUI's XAML type information itself (`App` is its own `IXamlMetadataProvider`); its resource index
(`Broadside.Viewer.WinUI.Sample.pri`, generated on Windows) must sit next to the exe.

Try: drag, the wheel or a touchpad to scroll, Ctrl+wheel or pinch to zoom, double-tap to toggle 2x fit-width, drag over text with a
mouse (double click: word) or long-press on touch and drag the handles to select, Ctrl+C or Copy to copy, Tab between form fields,
arrows, Page Up/Down, Home/End and +/-, H/T/L/K to move the reading focus between headings, tables, lists and links. Narrator and
other screen readers read the page text in reading order through the control's automation peers.

The UI tests in `tests/Broadside.Viewer.WinUI.UITests` drive this app through UI Automation (CI's `winui` job); every control has an
automation id, and the status line (`Page 3 of 8 | Zoom 125% | Ready`), the match line (`3 matches | complete`) and the selection line
(`Selected 1:4-1:38`) are what they read. With `BROADSIDE_WINUI_TRACE=1` (the UI tests set it) the app also logs every pointer press,
move with a button down, release and lost capture the view gets, with the selection after each, to the log file above.
