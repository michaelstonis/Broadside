# Broadside Blazor WebAssembly demo

A static web app that shows Broadside end to end in the browser: every byte of PDF work (parsing, rendering, text extraction, editing,
layout, PDF/A and PDF/UA output) runs in WebAssembly, in managed code, with no server. It is built on the `PdfViewer` component of
`Broadside.Viewer.Blazor` (#221) and made for phones first: at 390 px the tools open as bottom sheets, and above 900 px as a drawer
beside the page.

## Run it

```sh
dotnet run --project samples/Broadside.Viewer.Blazor.Sample          # debug, interpreted, http://localhost:5xxx
samples/Broadside.Viewer.Blazor.Sample/publish-static.sh out/demo    # trimmed + AOT static site in out/demo
```

Serve the folder with any static file server (`python3 -m http.server -d out/demo`) and open it. The site uses only relative
addresses, so it works from any sub-path (`https://example.com/some/folder/`). AOT needs the wasm-tools workload
(`dotnet workload install wasm-tools`).

## Walkthrough

The gallery (☰) opens pdf.js's TraceMonkey paper, a real-world 14-page file, followed by hand-written files from the test corpus, one
feature each. You can also open a PDF from your device (it stays in the browser) or from a URL, which is read through HTTP range
requests, so page 1 of a linearized file shows before the rest arrives (the server must allow cross-origin range requests).

| | | |
|---|---|---|
| ![Search](docs/demo-search.png) | ![Outline](docs/demo-outline.png) | ![Tags](docs/demo-tags.png) |
| **Search.** Text in reading order from the viewer's selection model; each match is selected and scrolled into view. Copy it, or drag with a mouse to extend it. | **Outline.** Bookmarks resolved to their pages (destinations and GoTo actions, named destinations included). **Pages** shows thumbnails rendered by the same rasterizer. | **Info.** Version, metadata, PDF/A and PDF/UA identification, the reader's diagnostics (what it repaired), and the tag tree as assistive technology reads it. |
| ![Annotate](docs/demo-annotate.png) | ![Form](docs/demo-form.png) | ![Create](docs/demo-create.png) |
| **Annotate.** Add a note, box, text, stamp, or a highlight over the selected text; list and remove annotations. Only the tiles an edit touches redraw. | **Form.** Fill text fields, check boxes, radio buttons and choices; tapping a field in the page opens it here. ⤓ downloads the document with your edits. | **Create.** A report laid out with the flow layout package (headings, list, table, figure, link, header and footer), tagged by construction and saved as PDF/A-2u, -3u, -4 and PDF/UA-1 or -2. |

**View** switches the page layout (continuous, single page, two-up, horizontal), rotates the view and zooms; pinch, double-tap,
Ctrl/⌘ + wheel and the keyboard work in the page as well. Its **Renderer** switch draws the tiles with WebGL 2 on the GPU instead of
the managed software rasterizer (#353; `?renderer=webgl` starts that way) and shows the GPU, the tile count, the mean tile time and
how many tiles fell back to the rasterizer; see `docs/benchmarks/viewer-gpu-vs-software.md` for what it changes. **Signatures** validates signed fields where the runtime can: .NET on
WebAssembly has no X.509 support, so in the browser the panel lists what each signature dictionary says, unchecked, and says why
(the same code validates on a server or desktop).

![Signatures](docs/demo-signatures.png)

The screenshots come from the browser tests (`BROADSIDE_BROWSER_SCREENSHOTS=<folder>`, Chromium at 390 × 844).

## Publishing for static hosts

`publish-static.sh [--no-aot] [--runtime-folder <name>] <folder>` writes the site and prints its file count, total size and largest file.

| | Files | Size | Largest file |
|---|---|---|---|
| default (trimmed, AOT) | 74 | 37.6 MB | `dotnet.native.wasm`, 24.5 MB |
| `--no-aot` | 74 | 14.3 MB | `Broadside.Fonts.Standard14.wasm`, 4.2 MB |

Sizes include the 1 MB of gallery documents. A host that compresses responses sends about a quarter of that.

- **Per-file size limits.** `--no-aot` skips ahead-of-time compilation: the IL runs in the .NET interpreter (slower pages, a much
  smaller runtime), so no file comes near a 16 MB per-file limit. It builds in its own `obj/noaot` and `bin/noaot` folders, because a
  non-AOT build over AOT intermediates produces a runtime that fails at startup. The MSBuild switch is `-p:BroadsideAot=false`.
- **Hosts that reserve paths starting with `_`.** `--runtime-folder framework` (MSBuild: `-p:BroadsideRuntimeFolder=framework`) moves
  `_framework` to `framework/` and `_content` to `framework-content/` after publishing, and points `index.html` at them. Blazor has no
  setting for the folder name, so the sample relies on two standard browser mechanisms: `wwwroot/js/start.js` starts Blazor with a
  `loadBootResource` callback that maps every runtime address under `_framework/` to the folder `index.html` loaded
  `blazor.webassembly.js` from (the runtime resolves everything else relative to `dotnet.js`), and an import map
  (`{ "imports": { "./_content/": "./framework-content/" } }`, written into `index.html`) sends the viewer's JavaScript module import
  to the renamed folder.
- **No precompressed files.** The site has no `.br`/`.gz` copies; any host that serves `.wasm` as `application/wasm` works.
- **GitHub Pages.** `.github/workflows/demo-pages.yml` builds the site on demand and uploads it as an artifact; deploying is opt-in
  because this repository's Pages site is the documentation (see the workflow's header).

## Documents and licences

- `tracemonkey.pdf`: "Trace-based Just-in-Time Type Specialization for Dynamic Languages" (Gal et al., PLDI 2009), the default document
  of pdf.js, taken from [mozilla/pdf.js](https://github.com/mozilla/pdf.js/tree/master/test/pdfs) `test/pdfs` at commit
  `c53f395e`, distributed in that repository under its Apache-2.0 licence.
- `signed.pdf`: written by Broadside and signed with `PdfCertificateSigner` (PAdES, `ETSI.CAdES.detached`, SHA-256) by a self-signed
  demo certificate, so a validator finds it intact but its signer untrusted.
- Every other document is linked from `tests/Corpus` (Broadside's own hand-written files, MIT).
