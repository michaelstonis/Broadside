# Changelog

Every release of the Broadside packages, newest first. Each section is written by `tools/ReleaseNotes` from the Conventional Commit titles merged since the previous tag (see the [versioning policy](https://michaelstonis.github.io/Broadside/articles/versioning/)). The GitHub release of each tag carries the same notes and the packages.

## 0.1.0-preview.10 (2026-11-19)

Changes since [`v0.1.0-preview.9`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.9). Full diff: [`v0.1.0-preview.9...v0.1.0-preview.10`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.9...v0.1.0-preview.10).

### Documentation

- **readme:** bring the README up to date with preview.9 ([#592](https://github.com/michaelstonis/Broadside/issues/592))
- **readme:** rewrite the README in plain language ([#593](https://github.com/michaelstonis/Broadside/issues/593))

### Build and CI

- build and test only what a pull request affects ([#587](https://github.com/michaelstonis/Broadside/issues/587)) ([#588](https://github.com/michaelstonis/Broadside/issues/588))

### Public API

No change to any package's public API.

## 0.1.0-preview.9 (2026-11-19)

Changes since [`v0.1.0-preview.8`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.8). Full diff: [`v0.1.0-preview.8...v0.1.0-preview.9`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.8...v0.1.0-preview.9).

### Features

- **document:** read and write XMP without System.Xml ([#545](https://github.com/michaelstonis/Broadside/issues/545)) ([#562](https://github.com/michaelstonis/Broadside/issues/562))
- **filters:** choose the image codecs through the options ([#544](https://github.com/michaelstonis/Broadside/issues/544)) ([#561](https://github.com/michaelstonis/Broadside/issues/561))
- **rendering:** make quantized glyph positioning the software rasterizer's default ([#550](https://github.com/michaelstonis/Broadside/issues/550))
- **viewer:** WPF, WinUI 3 and Avalonia edit form fields through FormFilling ([#543](https://github.com/michaelstonis/Broadside/issues/543)) ([#556](https://github.com/michaelstonis/Broadside/issues/556))

### Fixes

- **bench:** randomize benchmark placement and rerun in several processes ([#554](https://github.com/michaelstonis/Broadside/issues/554)) ([#575](https://github.com/michaelstonis/Broadside/issues/575))
- **content:** keep a sliced run's interpreter out of every thread's cache while it runs ([#585](https://github.com/michaelstonis/Broadside/issues/585))
- **cos:** bound linearization hint object counts by their byte ranges ([#568](https://github.com/michaelstonis/Broadside/issues/568)) ([#573](https://github.com/michaelstonis/Broadside/issues/573))
- **fonts:** draw each CID glyph through its own Font DICT matrix when the first is singular ([#582](https://github.com/michaelstonis/Broadside/issues/582)) ([#583](https://github.com/michaelstonis/Broadside/issues/583))
- **rendering:** cull glyph fills and strokes separately so a cache hit keeps the stroke ([#576](https://github.com/michaelstonis/Broadside/issues/576))
- **viewer:** extend the text selection to the release point in the Avalonia, WPF and MAUI viewers ([#542](https://github.com/michaelstonis/Broadside/issues/542)) ([#555](https://github.com/michaelstonis/Broadside/issues/555))
- **viewer:** toggle multi-select list box options with the mouse ([#560](https://github.com/michaelstonis/Broadside/issues/560)) ([#567](https://github.com/michaelstonis/Broadside/issues/567))
- **viewer-blazor:** extend the text selection to the release point ([#558](https://github.com/michaelstonis/Broadside/issues/558)) ([#559](https://github.com/michaelstonis/Broadside/issues/559))

### Performance

- **caching:** count changes per document so other documents' caches stay current ([#578](https://github.com/michaelstonis/Broadside/issues/578)) ([#580](https://github.com/michaelstonis/Broadside/issues/580))
- **layout:** lay out builder tables in time linear in their rows ([#541](https://github.com/michaelstonis/Broadside/issues/541)) ([#557](https://github.com/michaelstonis/Broadside/issues/557))
- **rendering:** decode build-time Flate images in slices ([#566](https://github.com/michaelstonis/Broadside/issues/566)) ([#579](https://github.com/michaelstonis/Broadside/issues/579))
- **viewer:** slice text selection and accessibility page loads ([#546](https://github.com/michaelstonis/Broadside/issues/546)) ([#564](https://github.com/michaelstonis/Broadside/issues/564))
- **viewer-blazor:** fit the --hot-aot demo build under 16 MB with a recorded AOT profile ([#547](https://github.com/michaelstonis/Broadside/issues/547)) ([#569](https://github.com/michaelstonis/Broadside/issues/569))

### Tests

- **viewer:** drive tile scheduler frames with a work-counting clock ([#553](https://github.com/michaelstonis/Broadside/issues/553))
- **viewer:** run the dark mode cache tests outside the parallel tests ([#570](https://github.com/michaelstonis/Broadside/issues/570)) ([#577](https://github.com/michaelstonis/Broadside/issues/577))
- **viewer-winui:** wait for the Home scroll to end before reading the line to drag across ([#563](https://github.com/michaelstonis/Broadside/issues/563)) ([#565](https://github.com/michaelstonis/Broadside/issues/565))

### Build and CI

- **bench:** record the runner's CPU and time without AVX-512 ([#539](https://github.com/michaelstonis/Broadside/issues/539)) ([#552](https://github.com/michaelstonis/Broadside/issues/552))

### Chores

- fix format errors in the Blazor sample and browser tests ([#571](https://github.com/michaelstonis/Broadside/issues/571))

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.8...v0.1.0-preview.9)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 15 | 0 |
| `Broadside.Viewer.Avalonia` | 1 | 0 |
| `Broadside.Viewer.WinUI` | 1 | 0 |
| `Broadside.Viewer.Wpf` | 1 | 0 |

<details><summary><code>Broadside</code>: 15 added, 0 removed</summary>

```diff
+ Broadside.Layout.LayoutExtraction
+ Broadside.Layout.LayoutExtraction.Continue(System.TimeSpan budget) -> bool
+ Broadside.Layout.LayoutExtraction.Dispose() -> void
+ Broadside.Layout.LayoutExtraction.IsCompleted.get -> bool
+ Broadside.Layout.LayoutExtraction.Result.get -> Broadside.Layout.LayoutTree!
+ Broadside.PdfOptions.UseImageCodecs() -> Broadside.PdfOptions!
+ Broadside.PdfPage.BeginLayoutExtraction(Broadside.Layout.LayoutOptions! options) -> Broadside.Layout.LayoutExtraction!
+ Broadside.PdfPage.BeginTextExtraction(Broadside.Text.TextExtractionOptions! options) -> Broadside.Text.TextExtraction!
+ Broadside.Text.TextExtraction
+ Broadside.Text.TextExtraction.Continue(System.TimeSpan budget) -> bool
+ Broadside.Text.TextExtraction.Dispose() -> void
+ Broadside.Text.TextExtraction.IsCompleted.get -> bool
+ Broadside.Text.TextExtraction.Result.get -> Broadside.Text.TextPage!
+ static Broadside.Diagnostics.FilterDiagnostics.FilterCodecNotRegistered.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.PdfOptions.CreateWithoutImageCodecs() -> Broadside.PdfOptions!
```

</details>

<details><summary><code>Broadside.Viewer.Avalonia</code>: 1 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Avalonia.PdfView.Forms.get -> Broadside.Viewer.FormFilling?
```

</details>

<details><summary><code>Broadside.Viewer.WinUI</code>: 1 added, 0 removed</summary>

```diff
+ Broadside.Viewer.WinUI.PdfView.Forms.get -> Broadside.Viewer.FormFilling?
```

</details>

<details><summary><code>Broadside.Viewer.Wpf</code>: 1 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Wpf.PdfView.Forms.get -> Broadside.Viewer.FormFilling?
```

</details>

## 0.1.0-preview.8 (2026-11-17)

Changes since [`v0.1.0-preview.7`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.7). Full diff: [`v0.1.0-preview.7...v0.1.0-preview.8`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.7...v0.1.0-preview.8).

### Features

- **core:** PdfColor.FromHex and layout diagnostics on PdfDocument.Diagnostics ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **jpx:** reassemble in-file fragment tables and refuse external fragments ([#259](https://github.com/michaelstonis/Broadside/issues/259))
- **layout:** page breaks, spacers, rules, groups, drawings, columns and page decorations ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** document builder values, entry points, content and tables ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** wire the document builder to the engine prerequisites ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** document builder drawings, columns and page decorations ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** document builder profiles, attached files and table width ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** anchors, field blocks and forward references in flow layout ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** document builder links, outline, table of contents, page references and form fields ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **rendering:** smart dark mode color filter ([#219](https://github.com/michaelstonis/Broadside/issues/219))
- **rendering:** offer JPXDecode images to platform decoders, measure ImageIO on them and keep it off ([#482](https://github.com/michaelstonis/Broadside/issues/482))
- **samples:** document builder samples and the Blazor demo on the builder ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **viewer:** search with background indexing ([#215](https://github.com/michaelstonis/Broadside/issues/215))
- **viewer:** dark mode in the Blazor viewer and the demo ([#219](https://github.com/michaelstonis/Broadside/issues/219))
- **viewer:** outline model with resolved destinations, page labels and thumbnails ([#216](https://github.com/michaelstonis/Broadside/issues/216))
- **viewer:** WPF viewer control and sample ([#222](https://github.com/michaelstonis/Broadside/issues/222))
- **viewer:** WinUI 3 viewer control, automation peers and sample ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer:** form filling in the viewer ([#218](https://github.com/michaelstonis/Broadside/issues/218))
- **viewer:** Avalonia viewer shell with headless UI tests ([#224](https://github.com/michaelstonis/Broadside/issues/224))
- **viewer:** Uno Platform viewer with desktop and WebAssembly heads ([#225](https://github.com/michaelstonis/Broadside/issues/225))
- **viewer:** slice long decode, conversion, paint and build steps; hot-path AOT demo build ([#365](https://github.com/michaelstonis/Broadside/issues/365), [#352](https://github.com/michaelstonis/Broadside/issues/352))
- **viewer-maui:** search highlights, reveal, dark mode and panel logic in the MAUI view ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-maui:** reusable viewer panels, adaptive sample shell and UI tests ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-maui:** form filling panel over the annotation layer ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-maui:** form filling on [#218](https://github.com/michaelstonis/Broadside/issues/218) in the view and the form panel ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-maui:** in-place field editing through the core form filling ([#358](https://github.com/michaelstonis/Broadside/issues/358))

### Fixes

- **bench:** let cases with legitimately varying allocations opt out of the exact allocation gate ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **bench:** name the allocation-gate opt-out category in kebab case, so it does not read as a diagnostic code ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **cos:** colour-space views and decode caches see removals behind indirect references ([#305](https://github.com/michaelstonis/Broadside/issues/305))
- **document:** recover the pages of a catalog without a usable page tree ([#258](https://github.com/michaelstonis/Broadside/issues/258))
- **document:** keep the catalog and every page when a save writes the tree for recovered pages ([#258](https://github.com/michaelstonis/Broadside/issues/258))
- **fonts:** drop a glyph whose coordinates overflow to infinity ([#536](https://github.com/michaelstonis/Broadside/issues/536))
- **forms:** creating a check box or radio buttons reports no missing appearance ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** embed field appearance fonts, give Form elements a PrintField role and write structure destinations ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **rendering:** check the type ImageIO parsed, for systems that ignore the allowable types ([#482](https://github.com/michaelstonis/Broadside/issues/482))
- **viewer:** state the off-thread rule without ADR references and serialize the dark-mode viewer tests ([#219](https://github.com/michaelstonis/Broadside/issues/219), [#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer:** build the info panel's list tabs once so a document change never gives a list view a second parent ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer:** give the code-only WinUI sample a XAML metadata provider and make its startup failures diagnosable ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer:** keep a late page load of the previous document away from the next one's layers ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer:** a copy the document's closing overtakes returns null instead of throwing into the host's click handler
- **viewer-maui:** keep the viewer in place when a panel opens; sample builds on every head ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-winui:** read the held modifier keys for a wheel turn too, so Ctrl+wheel zooms under injected input ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** count Win32's key state and the modifier keys the window saw for a wheel turn, and trace wheel input for the UI tests ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** end a mouse selection at the release point, and drag with real pointer moves in the UI test ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **writing:** write the integrity MAC in linearized saves ([#311](https://github.com/michaelstonis/Broadside/issues/311))
- **writing:** save recovered pages under a page tree so a file whose catalog lost Pages keeps its pages ([#258](https://github.com/michaelstonis/Broadside/issues/258))

### Performance

- **bench:** size the builder's table benchmark to what the engine can lay out ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **caching:** create a cache's table on first use and intern stroke records ([#337](https://github.com/michaelstonis/Broadside/issues/337))
- **content:** measure graphics-state copies and copy the state three times per nested run ([#257](https://github.com/michaelstonis/Broadside/issues/257))
- **core:** shrink the core in trimmed and Native AOT apps ([#361](https://github.com/michaelstonis/Broadside/issues/361))
- **direct2d:** shrink the backend assembly from 1.6 MB to 0.2 MB with hand-written COM vtable structs ([#360](https://github.com/michaelstonis/Broadside/issues/360))
- **fonts:** parse predefined CMaps and UCS2 tables once per engine ([#256](https://github.com/michaelstonis/Broadside/issues/256))
- **fonts:** keep a font's last program, so a shown code does not recompute the decode stamp ([#305](https://github.com/michaelstonis/Broadside/issues/305))
- **rendering:** vectorize the mix, knockout, backdrop removal and mask scaling rows ([#473](https://github.com/michaelstonis/Broadside/issues/473))
- **rendering:** resolve stroke unions faster with bit-identical coverage ([#335](https://github.com/michaelstonis/Broadside/issues/335))
- **rendering:** cache glyph coverage in the software rasterizer ([#370](https://github.com/michaelstonis/Broadside/issues/370))
- **rendering:** keep the resolve's sample-line state on the stack, so it does not spill without AVX-512 ([#335](https://github.com/michaelstonis/Broadside/issues/335))

### Documentation

- **layout:** document builder guide, README quick start and benchmarks ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **layout:** cite the clauses the document builder and sliced display-list build implement ([#397](https://github.com/michaelstonis/Broadside/issues/397), [#365](https://github.com/michaelstonis/Broadside/issues/365))
- **site:** map external UI framework xrefs, cover the Avalonia and Uno viewers, refresh conformance counts
- **site:** refresh the conformance counts after the PDF/UA fixes ([#397](https://github.com/michaelstonis/Broadside/issues/397))

### Tests

- **fonts:** name the thread count in camel case ([#256](https://github.com/michaelstonis/Broadside/issues/256))
- **forms:** run the transient-appearance tests with the other cache-epoch tests, not in parallel ([#218](https://github.com/michaelstonis/Broadside/issues/218))
- **golden:** review the golden image of the fragmented JPEG 2000 corpus file ([#259](https://github.com/michaelstonis/Broadside/issues/259))
- **layout:** compare builder snapshots by structure and text, not by compressed bytes that differ per runtime ([#397](https://github.com/michaelstonis/Broadside/issues/397))
- **rendering:** accept the pages the page-tree rebuild recovers in the render corpus gates ([#258](https://github.com/michaelstonis/Broadside/issues/258))
- **rendering:** time the deferred decode alone so the cancel points fall inside it ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **viewer:** compare form-filling saves under a fixed clock so they cannot straddle a second ([#218](https://github.com/michaelstonis/Broadside/issues/218), [#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer:** retry the WinUI drag at a few heights of the line box, and tell a moment of adb offline from an app that exited on Android ([#223](https://github.com/michaelstonis/Broadside/issues/223), [#358](https://github.com/michaelstonis/Broadside/issues/358))
- **viewer-uno:** clear the drag's selection before the double click, which may select the same word ([#225](https://github.com/michaelstonis/Broadside/issues/225))
- **viewer-winui:** keep the UI test fixture's cleanup from hiding the test's own failure ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** build the timeout diagnostics only when a wait times out ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** hold Ctrl until the wheel zoom shows, and drop the input workarounds the trace proved unneeded ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** drag the selection in paced steps, as injected input sent at once can reach WinUI out of order ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** toggle the dark-mode toggle button, which has no Invoke pattern
- **viewer-winui:** search for a whole word of the page, not one with its inner hyphen dropped ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** return to the top of page 1 after zooming, before looking for its first line ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** list page 1's nodes and bounds when no text node shows ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** drag across the longest line on screen, which in a short window can be a heading ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-winui:** drag the first long line on screen, and a shorter one only when none shows ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer-wpf:** compute the expected page text over an annotation layer like the control does ([#222](https://github.com/michaelstonis/Broadside/issues/222))

### Benchmarks

- **gate:** re-record the allocation baseline for a font's kept program ([#305](https://github.com/michaelstonis/Broadside/issues/305))

### Build and CI

- **bench:** run the timing rounds in process, a class per process, over eight shards ([#337](https://github.com/michaelstonis/Broadside/issues/337))
- **bench:** gate the core's Native AOT size per scenario ([#361](https://github.com/michaelstonis/Broadside/issues/361))
- **maui:** skip the Apple UI tests with a warning while the runner image has no Xcode 27 ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **maui:** keep the Android smoke and UI scripts polling when adb fails for a moment; name the test's counts in camel case ([#358](https://github.com/michaelstonis/Broadside/issues/358))
- **samples:** link the builder's experimental API into the MAUI sample and silence CA5392 from the Windows App SDK auto-initializer ([#358](https://github.com/michaelstonis/Broadside/issues/358), [#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer:** stop the WinUI library advertising a project .pri ([#223](https://github.com/michaelstonis/Broadside/issues/223))
- **viewer:** do not build the Uno viewer's project references when packing without a build ([#225](https://github.com/michaelstonis/Broadside/issues/225))

### Chores

- **bench:** re-record the size probes after the glyph cache, slicing and builder ([#361](https://github.com/michaelstonis/Broadside/issues/361))
- **bench:** re-record the allocation baseline from CI after sweep 7B ([#337](https://github.com/michaelstonis/Broadside/issues/337), [#370](https://github.com/michaelstonis/Broadside/issues/370), [#365](https://github.com/michaelstonis/Broadside/issues/365), [#397](https://github.com/michaelstonis/Broadside/issues/397))

### Other changes

- wip: debug

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.7...v0.1.0-preview.8)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 39 | 0 |
| `Broadside.Layout` | 615 | 0 |
| `Broadside.Rendering` | 20 | 0 |
| `Broadside.Viewer` | 165 | 0 |
| `Broadside.Viewer.Avalonia` | 70 | 0 |
| `Broadside.Viewer.Blazor` | 7 | 0 |
| `Broadside.Viewer.Maui` | 68 | 0 |
| `Broadside.Viewer.Uno` | 74 | 0 |
| `Broadside.Viewer.WinUI` | 56 | 0 |
| `Broadside.Viewer.Wpf` | 59 | 0 |

<details><summary><code>Broadside</code>: 39 added, 0 removed</summary>

```diff
+ Broadside.Content.ContentOptions.TransientFieldAppearances.get -> bool
+ Broadside.Content.ContentOptions.TransientFieldAppearances.set -> void
+ Broadside.Content.ContentOptions.WithTransientFieldAppearances(bool transient) -> Broadside.Content.ContentOptions!
+ Broadside.Diagnostics.DiagnosticArea.Layout = 31 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.LayoutDiagnostics
+ Broadside.Forms.PdfAcroForm.SelectFields(System.Collections.Generic.IReadOnlyList<Broadside.PdfActionTarget!>? targets, bool exclude) -> System.Collections.Generic.IReadOnlyList<Broadside.Forms.PdfTerminalField!>!
+ Broadside.Forms.PdfFieldOptions.Font.get -> Broadside.Fonts.PdfCanvasFont?
+ Broadside.Forms.PdfFieldOptions.Font.set -> void
+ Broadside.Forms.PdfFieldOptions.WithFont(Broadside.Fonts.PdfCanvasFont? font) -> Broadside.Forms.PdfFieldOptions!
+ Broadside.PdfDocument.DefaultSaveOptions.get -> Broadside.PdfSaveOptions?
+ Broadside.PdfDocument.DefaultSaveOptions.set -> void
+ Broadside.PdfDocument.ReportDiagnostic(Broadside.Diagnostics.Diagnostic! diagnostic) -> void
+ Broadside.PdfPage.BeginDisplayList(Broadside.Rendering.DisplayLayers layers, Broadside.Rendering.DisplayIntent intent, Broadside.Content.ContentOptions! options) -> Broadside.Rendering.DisplayListBuild!
+ Broadside.PdfResetFormAction.SelectFields() -> System.Collections.Generic.IReadOnlyList<Broadside.Forms.PdfTerminalField!>!
+ Broadside.PdfSubmitFormAction.SelectFields() -> System.Collections.Generic.IReadOnlyList<Broadside.Forms.PdfTerminalField!>!
+ Broadside.Rendering.DisplayImage.ContinueDecode(int reduction, System.TimeSpan budget, System.Threading.CancellationToken cancellationToken) -> bool
+ Broadside.Rendering.DisplayListBuild
+ Broadside.Rendering.DisplayListBuild.Continue(System.TimeSpan budget) -> bool
+ Broadside.Rendering.DisplayListBuild.Dispose() -> void
+ Broadside.Rendering.DisplayListBuild.IsCompleted.get -> bool
+ Broadside.Rendering.DisplayListBuild.Result.get -> Broadside.Rendering.DisplayList!
+ static Broadside.Diagnostics.CharStringDiagnostics.FontCharstringCoordinateOverflow.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.JpxDiagnostics.JpxFragmentExternal.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.AlternateTextLooksLikeFileName.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.AnchorDuplicate.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.AnchorNotFound.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.AppendedToUntaggedDocument.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.CharacterNotEncodable.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.DocumentLanguageMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.DocumentMetadataKept.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.DocumentTitleMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.DrawingStateUnbalanced.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.FontFaceMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.FontNotFound.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.ForwardReferencesNotConverged.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.HeadingLevelSkipped.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.LayoutDiagnostics.KeepTogetherTooTall.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.SavingDiagnostics.ConformanceOverridden.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Graphics.PdfColor.FromHex(string! hex) -> Broadside.Graphics.PdfColor
```

</details>

<details><summary><code>Broadside.Layout</code>: 615 added, 0 removed</summary>

```diff
+ Broadside.Layout.Flow.BlockGroup
+ Broadside.Layout.Flow.BlockGroup.BlockGroup(params System.Collections.Generic.IEnumerable<Broadside.Layout.Flow.FlowBlock!>! blocks) -> void
+ Broadside.Layout.Flow.BlockGroup.Blocks.get -> System.Collections.Generic.IReadOnlyList<Broadside.Layout.Flow.FlowBlock!>!
+ Broadside.Layout.Flow.BlockGroup.IsTagged.get -> bool
+ Broadside.Layout.Flow.BlockGroup.IsTagged.init -> void
+ Broadside.Layout.Flow.BlockGroup.KeepTogether.get -> bool
+ Broadside.Layout.Flow.BlockGroup.KeepTogether.init -> void
+ Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.AlignCenter() -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.AlignLeft() -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.AlignRight() -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Background(Broadside.Graphics.PdfColor color) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Bold() -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.CellElement() -> void
+ Broadside.Layout.Flow.CellElement.Color(Broadside.Graphics.PdfColor color) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.ColumnSpan(int columns) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Font(Broadside.Layout.Flow.FontFamily! family) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.FontSize(double size) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Italic() -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Language(string! language) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Padding(Broadside.Layout.Flow.Length padding) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.RowSpan(int rows) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.CellElement.Style(Broadside.Layout.Flow.Style! style) -> Broadside.Layout.Flow.CellElement
+ Broadside.Layout.Flow.ColumnsBlock
+ Broadside.Layout.Flow.ColumnsBlock.Columns.get -> System.Collections.Generic.IReadOnlyList<Broadside.Layout.Flow.FlowColumn!>!
+ Broadside.Layout.Flow.ColumnsBlock.ColumnsBlock(params System.Collections.Generic.IEnumerable<Broadside.Layout.Flow.FlowColumn!>! columns) -> void
+ Broadside.Layout.Flow.ColumnsBlock.Gap.get -> double
+ Broadside.Layout.Flow.ColumnsBlock.Gap.init -> void
+ Broadside.Layout.Flow.ColumnsBlock.SpaceAfter.get -> double
+ Broadside.Layout.Flow.ColumnsBlock.SpaceAfter.init -> void
+ Broadside.Layout.Flow.ColumnsBlock.SpaceBefore.get -> double
+ Broadside.Layout.Flow.ColumnsBlock.SpaceBefore.init -> void
+ Broadside.Layout.Flow.ColumnsBuilder
+ Broadside.Layout.Flow.ColumnsBuilder.Column(Broadside.Layout.Flow.ColumnWidth width, System.Action<Broadside.Layout.Flow.ContainerBuilder>! build) -> void
+ Broadside.Layout.Flow.ColumnsBuilder.Column(System.Action<Broadside.Layout.Flow.ContainerBuilder>! build) -> void
+ Broadside.Layout.Flow.ColumnsBuilder.ColumnsBuilder() -> void
+ Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.ColumnsElement() -> void
+ Broadside.Layout.Flow.ColumnsElement.Gap(Broadside.Layout.Flow.Length gap) -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.KeepTogether() -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.KeepWithNext() -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.SpaceAfter(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.SpaceBefore(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ColumnsElement.Style(Broadside.Layout.Flow.Style! style) -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ContainerBuilder
+ Broadside.Layout.Flow.ContainerBuilder.Add(Broadside.Layout.Flow.FlowBlock! block) -> void
+ Broadside.Layout.Flow.ContainerBuilder.CheckBox(string! name, string! label) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.Columns(System.Action<Broadside.Layout.Flow.ColumnsBuilder>! build) -> Broadside.Layout.Flow.ColumnsElement
+ Broadside.Layout.Flow.ContainerBuilder.ComboBox(string! name, string! label, System.Collections.Generic.IReadOnlyList<string!>! choices) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.ContainerBuilder() -> void
+ Broadside.Layout.Flow.ContainerBuilder.DecorativeDrawing(Broadside.Layout.Flow.Length width, Broadside.Layout.Flow.Length height, System.Action<Broadside.Graphics.PdfCanvas!, Broadside.PdfRectangle>! draw) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.ContainerBuilder.DecorativeImage(Broadside.Layout.Flow.ImageSource! source) -> Broadside.Layout.Flow.ImageElement
+ Broadside.Layout.Flow.ContainerBuilder.Document.get -> Broadside.PdfDocument!
+ Broadside.Layout.Flow.ContainerBuilder.Drawing(Broadside.Layout.Flow.Length width, Broadside.Layout.Flow.Length height, string! alternateText, System.Action<Broadside.Graphics.PdfCanvas!, Broadside.PdfRectangle>! draw) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.ContainerBuilder.Group(System.Action<Broadside.Layout.Flow.ContainerBuilder>! build) -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.ContainerBuilder.Heading(int level, string! text) -> Broadside.Layout.Flow.ParagraphElement
+ Broadside.Layout.Flow.ContainerBuilder.Image(Broadside.Layout.Flow.ImageSource! source, string! alternateText) -> Broadside.Layout.Flow.ImageElement
+ Broadside.Layout.Flow.ContainerBuilder.List(System.Action<Broadside.Layout.Flow.ListBuilder>! build) -> Broadside.Layout.Flow.ListElement
+ Broadside.Layout.Flow.ContainerBuilder.ListBox(string! name, string! label, System.Collections.Generic.IReadOnlyList<string!>! choices) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.PageBreak() -> void
+ Broadside.Layout.Flow.ContainerBuilder.Paragraph(System.Action<Broadside.Layout.Flow.TextBuilder>! build) -> Broadside.Layout.Flow.ParagraphElement
+ Broadside.Layout.Flow.ContainerBuilder.Paragraph(string! text) -> Broadside.Layout.Flow.ParagraphElement
+ Broadside.Layout.Flow.ContainerBuilder.RadioGroup(string! name, string! label, System.Collections.Generic.IReadOnlyList<string!>! choices) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.Rule() -> Broadside.Layout.Flow.RuleElement
+ Broadside.Layout.Flow.ContainerBuilder.SignatureField(string! name, string! label) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.Spacer(Broadside.Layout.Flow.Length height) -> void
+ Broadside.Layout.Flow.ContainerBuilder.Table(System.Action<Broadside.Layout.Flow.TableBuilder>! build) -> Broadside.Layout.Flow.TableElement
+ Broadside.Layout.Flow.ContainerBuilder.TableOfContents() -> Broadside.Layout.Flow.TableOfContentsElement
+ Broadside.Layout.Flow.ContainerBuilder.TextField(string! name, string! label) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.ContainerBuilder.Theme.get -> Broadside.Layout.Flow.Theme!
+ Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.AttachFile(string! fileName, System.ReadOnlySpan<byte> contents, Broadside.PdfFileRelationship relationship) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.AttachFile(string! fileName, System.ReadOnlySpan<byte> contents, Broadside.PdfFileRelationship relationship, string! mediaType) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.AttachFile(string! fileName, string! text, Broadside.PdfFileRelationship relationship) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.Content(System.Action<Broadside.Layout.Flow.ContainerBuilder>! build) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.Document.get -> Broadside.PdfDocument!
+ Broadside.Layout.Flow.DocumentBuilder.DocumentBuilder() -> void
+ Broadside.Layout.Flow.DocumentBuilder.Section(System.Action<Broadside.Layout.Flow.SectionBuilder>! build) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.Theme.get -> Broadside.Layout.Flow.Theme!
+ Broadside.Layout.Flow.DocumentBuilder.WithAuthor(string! author) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithKeywords(string! keywords) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithLanguage(string! language) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithOutlineDepth(int depth) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithPdfAConformance(Broadside.PdfAConformance conformance) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithPdfUAConformance(Broadside.PdfUAConformance conformance) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithSubject(string! subject) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithTagging(bool tagged) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithTheme(Broadside.Layout.Flow.Theme! theme) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DocumentBuilder.WithTitle(string! title) -> Broadside.Layout.Flow.DocumentBuilder
+ Broadside.Layout.Flow.DrawingBlock
+ Broadside.Layout.Flow.DrawingBlock.Alignment.get -> Broadside.Layout.Flow.TextAlignment
+ Broadside.Layout.Flow.DrawingBlock.Alignment.init -> void
+ Broadside.Layout.Flow.DrawingBlock.AlternateText.get -> string?
+ Broadside.Layout.Flow.DrawingBlock.AlternateText.init -> void
+ Broadside.Layout.Flow.DrawingBlock.Draw.get -> System.Action<Broadside.Graphics.PdfCanvas!, Broadside.PdfRectangle>!
+ Broadside.Layout.Flow.DrawingBlock.DrawingBlock(double width, double height, System.Action<Broadside.Graphics.PdfCanvas!, Broadside.PdfRectangle>! draw) -> void
+ Broadside.Layout.Flow.DrawingBlock.Height.get -> double
+ Broadside.Layout.Flow.DrawingBlock.IsDecorative.get -> bool
+ Broadside.Layout.Flow.DrawingBlock.IsDecorative.init -> void
+ Broadside.Layout.Flow.DrawingBlock.SpaceAfter.get -> double
+ Broadside.Layout.Flow.DrawingBlock.SpaceAfter.init -> void
+ Broadside.Layout.Flow.DrawingBlock.SpaceBefore.get -> double
+ Broadside.Layout.Flow.DrawingBlock.SpaceBefore.init -> void
+ Broadside.Layout.Flow.DrawingBlock.Width.get -> double
+ Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.AlignCenter() -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.AlignLeft() -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.AlignRight() -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.Caption(string! caption) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.DrawingElement() -> void
+ Broadside.Layout.Flow.DrawingElement.FillWidth() -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.Id(string! id) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.KeepWithNext() -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.SpaceAfter(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.DrawingElement.SpaceBefore(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.DrawingElement
+ Broadside.Layout.Flow.FieldBlock
+ Broadside.Layout.Flow.FieldBlock.Choices.get -> System.Collections.Generic.IReadOnlyList<string!>!
+ Broadside.Layout.Flow.FieldBlock.Choices.init -> void
+ Broadside.Layout.Flow.FieldBlock.FieldBlock(Broadside.Layout.Flow.FormFieldType type, string! name, string! label, Broadside.Layout.Flow.ParagraphStyle! labelStyle) -> void
+ Broadside.Layout.Flow.FieldBlock.Height.get -> double
+ Broadside.Layout.Flow.FieldBlock.Height.init -> void
+ Broadside.Layout.Flow.FieldBlock.IsChecked.get -> bool
+ Broadside.Layout.Flow.FieldBlock.IsChecked.init -> void
+ Broadside.Layout.Flow.FieldBlock.Label.get -> string!
+ Broadside.Layout.Flow.FieldBlock.LabelGap.get -> double
+ Broadside.Layout.Flow.FieldBlock.LabelGap.init -> void
+ Broadside.Layout.Flow.FieldBlock.LabelPosition.get -> Broadside.Layout.Flow.FieldLabelPosition
+ Broadside.Layout.Flow.FieldBlock.LabelPosition.init -> void
+ Broadside.Layout.Flow.FieldBlock.LabelStyle.get -> Broadside.Layout.Flow.ParagraphStyle!
+ Broadside.Layout.Flow.FieldBlock.Name.get -> string!
+ Broadside.Layout.Flow.FieldBlock.Options.get -> Broadside.Forms.PdfFieldOptions?
+ Broadside.Layout.Flow.FieldBlock.Options.init -> void
+ Broadside.Layout.Flow.FieldBlock.SpaceAfter.get -> double
+ Broadside.Layout.Flow.FieldBlock.SpaceAfter.init -> void
+ Broadside.Layout.Flow.FieldBlock.SpaceBefore.get -> double
+ Broadside.Layout.Flow.FieldBlock.SpaceBefore.init -> void
+ Broadside.Layout.Flow.FieldBlock.Type.get -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FieldBlock.Value.get -> string?
+ Broadside.Layout.Flow.FieldBlock.Value.init -> void
+ Broadside.Layout.Flow.FieldBlock.Width.get -> double
+ Broadside.Layout.Flow.FieldBlock.Width.init -> void
+ Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Checked() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.FieldElement() -> void
+ Broadside.Layout.Flow.FieldElement.FillWidth() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Height(Broadside.Layout.Flow.Length height) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.KeepWithNext() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.LabelBeside() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Multiline(int lines = 3) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.ReadOnly() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Required() -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Size(Broadside.Layout.Flow.Length width, Broadside.Layout.Flow.Length height) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.SpaceAfter(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.SpaceBefore(Broadside.Layout.Flow.Length space) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Style(Broadside.Layout.Flow.Style! style) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Value(string! value) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldElement.Width(Broadside.Layout.Flow.Length width) -> Broadside.Layout.Flow.FieldElement
+ Broadside.Layout.Flow.FieldLabelPosition
+ Broadside.Layout.Flow.FieldLabelPosition.Above = 0 -> Broadside.Layout.Flow.FieldLabelPosition
+ Broadside.Layout.Flow.FieldLabelPosition.Before = 1 -> Broadside.Layout.Flow.FieldLabelPosition
+ Broadside.Layout.Flow.FlowAnchor
+ Broadside.Layout.Flow.FlowAnchor.Id.get -> string!
+ Broadside.Layout.Flow.FlowAnchor.Left.get -> double
+ Broadside.Layout.Flow.FlowAnchor.Page.get -> Broadside.PdfPage!
+ Broadside.Layout.Flow.FlowAnchor.PageNumber.get -> int
+ Broadside.Layout.Flow.FlowAnchor.Top.get -> double
+ Broadside.Layout.Flow.FlowBlock.Id.get -> string?
+ Broadside.Layout.Flow.FlowBlock.Id.init -> void
+ Broadside.Layout.Flow.FlowBlock.KeepWithNext.get -> bool
+ Broadside.Layout.Flow.FlowBlock.KeepWithNext.init -> void
+ Broadside.Layout.Flow.FlowColumn
+ Broadside.Layout.Flow.FlowColumn.Blocks.get -> System.Collections.Generic.IReadOnlyList<Broadside.Layout.Flow.FlowBlock!>!
+ Broadside.Layout.Flow.FlowColumn.FlowColumn(Broadside.Layout.Flow.ColumnWidth width, params System.Collections.Generic.IEnumerable<Broadside.Layout.Flow.FlowBlock!>! blocks) -> void
+ Broadside.Layout.Flow.FlowColumn.FlowColumn(params System.Collections.Generic.IEnumerable<Broadside.Layout.Flow.FlowBlock!>! blocks) -> void
+ Broadside.Layout.Flow.FlowColumn.Width.get -> Broadside.Layout.Flow.ColumnWidth
+ Broadside.Layout.Flow.FlowDocument.MaxLayoutPasses.get -> int
+ Broadside.Layout.Flow.FlowDocument.MaxLayoutPasses.set -> void
+ Broadside.Layout.Flow.FlowDocument.OutlineDepth.get -> int
+ Broadside.Layout.Flow.FlowDocument.OutlineDepth.set -> void
+ Broadside.Layout.Flow.FlowLayoutResult.Anchors.get -> System.Collections.Generic.IReadOnlyDictionary<string!, Broadside.Layout.Flow.FlowAnchor!>!
+ Broadside.Layout.Flow.FontFamily
+ Broadside.Layout.Flow.FontFamily.Fallbacks.get -> System.Collections.Generic.IReadOnlyList<Broadside.Layout.Flow.FontFamily!>!
+ Broadside.Layout.Flow.FontFamily.Name.get -> string!
+ Broadside.Layout.Flow.FontFamily.WithFallback(Broadside.Layout.Flow.FontFamily! fallback) -> Broadside.Layout.Flow.FontFamily!
+ Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.CheckBox = 1 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.ComboBox = 3 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.ListBox = 4 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.RadioButtons = 2 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.Signature = 5 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.FormFieldType.Text = 0 -> Broadside.Layout.Flow.FormFieldType
+ Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.AlignCenter() -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.AlignRight() -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.Bold() -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.Color(Broadside.Graphics.PdfColor color) -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.Font(Broadside.Layout.Flow.FontFamily! family) -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.FontSize(double size) -> Broadside.Layout.Flow.GroupElement
+ Broadside.Layout.Flow.GroupElement.GroupElement() -> void
+ Broadside.Layout.Flow.GroupElement.Italic() -> Broadside.Layout.Flow.GroupElement
# 415 more in the full diff
```

</details>

<details><summary><code>Broadside.Rendering</code>: 20 added, 0 removed</summary>

```diff
+ Broadside.Rendering.ColorFilter
+ Broadside.Rendering.ColorFilter.DarkMode = 1 -> Broadside.Rendering.ColorFilter
+ Broadside.Rendering.ColorFilter.None = 0 -> Broadside.Rendering.ColorFilter
+ Broadside.Rendering.GlyphPositioning
+ Broadside.Rendering.GlyphPositioning.Exact = 0 -> Broadside.Rendering.GlyphPositioning
+ Broadside.Rendering.GlyphPositioning.Quantized = 1 -> Broadside.Rendering.GlyphPositioning
+ Broadside.Rendering.RenderOptions.ColorFilter.get -> Broadside.Rendering.ColorFilter
+ Broadside.Rendering.RenderOptions.ColorFilter.set -> void
+ Broadside.Rendering.RenderOptions.GlyphPositioning.get -> Broadside.Rendering.GlyphPositioning
+ Broadside.Rendering.RenderOptions.GlyphPositioning.set -> void
+ Broadside.Rendering.RenderOptions.WithColorFilter(Broadside.Rendering.ColorFilter colorFilter) -> Broadside.Rendering.RenderOptions!
+ Broadside.Rendering.RenderOptions.WithGlyphPositioning(Broadside.Rendering.GlyphPositioning glyphPositioning) -> Broadside.Rendering.RenderOptions!
+ Broadside.Rendering.SoftwareRasterizer.ColorFilter.get -> Broadside.Rendering.ColorFilter
+ Broadside.Rendering.SoftwareRasterizer.ColorFilter.set -> void
+ Broadside.Rendering.SoftwareRasterizer.GlyphCacheBudget.get -> long
+ Broadside.Rendering.SoftwareRasterizer.GlyphCacheBudget.set -> void
+ Broadside.Rendering.SoftwareRasterizer.GlyphCacheStatistics.get -> Broadside.PdfCacheStatistics
+ Broadside.Rendering.SoftwareRasterizer.GlyphPositioning.get -> Broadside.Rendering.GlyphPositioning
+ Broadside.Rendering.SoftwareRasterizer.GlyphPositioning.set -> void
+ const Broadside.Rendering.SoftwareRasterizer.DefaultGlyphCacheBudget = 4194304 -> long
```

</details>

<details><summary><code>Broadside.Viewer</code>: 165 added, 0 removed</summary>

```diff
+ Broadside.Viewer.AnnotationLayer.FindTarget(int pageIndex, Broadside.Objects.CosDictionary! dictionary) -> Broadside.Viewer.AnnotationTarget?
+ Broadside.Viewer.AnnotationLayer.ResetFields(System.Collections.Generic.IEnumerable<Broadside.Forms.PdfTerminalField!>! fields) -> int
+ Broadside.Viewer.AnnotationLayer.SetFieldChoice(Broadside.Viewer.AnnotationTarget target, string? value) -> bool
+ Broadside.Viewer.AnnotationLayer.SetFieldSelection(Broadside.Viewer.AnnotationTarget target, System.Collections.Generic.IEnumerable<int>! indices) -> bool
+ Broadside.Viewer.AnnotationLayer.SetFieldText(Broadside.Viewer.AnnotationTarget target, string? value) -> bool
+ Broadside.Viewer.AnnotationLayer.XfaForm.get -> Broadside.Viewer.XfaFormEventArgs?
+ Broadside.Viewer.AnnotationLayer.XfaFormDetected -> System.EventHandler<Broadside.Viewer.XfaFormEventArgs!>?
+ Broadside.Viewer.DocumentOutline
+ Broadside.Viewer.DocumentOutline.Entries.get -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.OutlineEntry!>!
+ Broadside.Viewer.DocumentOutline.FindEntryFor(int pageIndex) -> Broadside.Viewer.OutlineEntry?
+ Broadside.Viewer.DocumentOutline.GetVisible() -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.OutlineEntry!>!
+ Broadside.Viewer.DocumentOutline.GetVisible(System.Func<Broadside.Viewer.OutlineEntry!, bool>! isOpen) -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.OutlineEntry!>!
+ Broadside.Viewer.DocumentOutline.IsEmpty.get -> bool
+ Broadside.Viewer.DocumentOutline.Roots.get -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.OutlineEntry!>!
+ Broadside.Viewer.DocumentSearch
+ Broadside.Viewer.DocumentSearch.Changed -> System.EventHandler?
+ Broadside.Viewer.DocumentSearch.Clear() -> void
+ Broadside.Viewer.DocumentSearch.Current.get -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.DocumentSearch.CurrentIndex.get -> int
+ Broadside.Viewer.DocumentSearch.Dispose() -> void
+ Broadside.Viewer.DocumentSearch.DocumentSearch(Broadside.Viewer.DocumentSession! session) -> void
+ Broadside.Viewer.DocumentSearch.FailedPageCount.get -> int
+ Broadside.Viewer.DocumentSearch.GetQuads(int pageIndex, System.Span<Broadside.Graphics.Quad> destination) -> int
+ Broadside.Viewer.DocumentSearch.IndexedPageCount.get -> int
+ Broadside.Viewer.DocumentSearch.IsIndexComplete.get -> bool
+ Broadside.Viewer.DocumentSearch.IsIndexing.get -> bool
+ Broadside.Viewer.DocumentSearch.IsSearchComplete.get -> bool
+ Broadside.Viewer.DocumentSearch.MatchCount.get -> int
+ Broadside.Viewer.DocumentSearch.Matches.get -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.SearchMatch!>!
+ Broadside.Viewer.DocumentSearch.MoveTo(int index) -> Broadside.Viewer.SearchMatch!
+ Broadside.Viewer.DocumentSearch.Next() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.DocumentSearch.Options.get -> Broadside.Viewer.TextSearchOptions!
+ Broadside.Viewer.DocumentSearch.Previous() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.DocumentSearch.Query.get -> string!
+ Broadside.Viewer.DocumentSearch.Search(string! query, Broadside.Viewer.TextSearchOptions? options = null, int startPage = 0) -> void
+ Broadside.Viewer.DocumentSearch.SearchedPageCount.get -> int
+ Broadside.Viewer.DocumentSearch.Session.get -> Broadside.Viewer.DocumentSession!
+ Broadside.Viewer.DocumentSearch.StartIndexing() -> void
+ Broadside.Viewer.DocumentSession.ColorFilter.get -> Broadside.Rendering.ColorFilter
+ Broadside.Viewer.DocumentSession.ColorFilter.set -> void
+ Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FieldEditKind.Button = 3 -> Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FieldEditKind.Choice = 2 -> Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FieldEditKind.None = 0 -> Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FieldEditKind.Text = 1 -> Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FormFilling
+ Broadside.Viewer.FormFilling.Cancel() -> bool
+ Broadside.Viewer.FormFilling.Caret.get -> int
+ Broadside.Viewer.FormFilling.Changed -> System.EventHandler?
+ Broadside.Viewer.FormFilling.Commit() -> bool
+ Broadside.Viewer.FormFilling.Field.get -> Broadside.Forms.PdfTerminalField?
+ Broadside.Viewer.FormFilling.FormFilling(Broadside.Viewer.AnnotationLayer! layer) -> void
+ Broadside.Viewer.FormFilling.GetMissingRequiredFields(System.Collections.Generic.IEnumerable<Broadside.Forms.PdfTerminalField!>? fields = null) -> System.Collections.Generic.IReadOnlyList<Broadside.Forms.PdfTerminalField!>!
+ Broadside.Viewer.FormFilling.HighlightedOption.get -> int
+ Broadside.Viewer.FormFilling.IsDirty.get -> bool
+ Broadside.Viewer.FormFilling.IsReadOnly.get -> bool
+ Broadside.Viewer.FormFilling.IsRequired.get -> bool
+ Broadside.Viewer.FormFilling.KeyDown(Broadside.Viewer.InputKey key, Broadside.Viewer.InputModifiers modifiers) -> bool
+ Broadside.Viewer.FormFilling.Kind.get -> Broadside.Viewer.FieldEditKind
+ Broadside.Viewer.FormFilling.Layer.get -> Broadside.Viewer.AnnotationLayer!
+ Broadside.Viewer.FormFilling.MaxLength.get -> int?
+ Broadside.Viewer.FormFilling.SelectAll() -> bool
+ Broadside.Viewer.FormFilling.SelectedOptions.get -> System.Collections.Generic.IReadOnlyList<int>!
+ Broadside.Viewer.FormFilling.SelectionLength.get -> int
+ Broadside.Viewer.FormFilling.SelectionStart.get -> int
+ Broadside.Viewer.FormFilling.SetSelectedOptions(System.Collections.Generic.IEnumerable<int>! indices) -> bool
+ Broadside.Viewer.FormFilling.SetText(string! text, int? caret = null) -> bool
+ Broadside.Viewer.FormFilling.Target.get -> Broadside.Viewer.AnnotationTarget?
+ Broadside.Viewer.FormFilling.Text.get -> string!
+ Broadside.Viewer.FormFilling.TextInput(string! text) -> bool
+ Broadside.Viewer.IRemoteTileRenderer.CanRender(in Broadside.Viewer.Tile tile, Broadside.Rendering.ColorFilter colorFilter) -> bool
+ Broadside.Viewer.IRemoteTileRenderer.RenderAsync(Broadside.Viewer.Tile tile, Broadside.Rendering.ColorFilter colorFilter, Broadside.Viewer.IRenderSurface! surface, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task!
+ Broadside.Viewer.ITileBackend.Begin(Broadside.Rendering.DisplayList! list, Broadside.Rendering.Bitmap! target, in Broadside.Graphics.Matrix deviceTransform, Broadside.Rendering.ColorFilter colorFilter, System.Threading.CancellationToken cancellationToken) -> Broadside.Viewer.ITileRender!
+ Broadside.Viewer.InputKey.Backspace = 16 -> Broadside.Viewer.InputKey
+ Broadside.Viewer.InputKey.Delete = 17 -> Broadside.Viewer.InputKey
+ Broadside.Viewer.InputKey.Enter = 14 -> Broadside.Viewer.InputKey
+ Broadside.Viewer.InputKey.Escape = 15 -> Broadside.Viewer.InputKey
+ Broadside.Viewer.InputKey.Tab = 13 -> Broadside.Viewer.InputKey
+ Broadside.Viewer.OutlineEntry
+ Broadside.Viewer.OutlineEntry.ActionKind.get -> Broadside.PdfActionKind?
+ Broadside.Viewer.OutlineEntry.Children.get -> System.Collections.Generic.IReadOnlyList<Broadside.Viewer.OutlineEntry!>!
+ Broadside.Viewer.OutlineEntry.Color.get -> Broadside.PdfRgbColor
+ Broadside.Viewer.OutlineEntry.Id.get -> int
+ Broadside.Viewer.OutlineEntry.IsBold.get -> bool
+ Broadside.Viewer.OutlineEntry.IsItalic.get -> bool
+ Broadside.Viewer.OutlineEntry.IsNavigable.get -> bool
+ Broadside.Viewer.OutlineEntry.IsOpen.get -> bool
+ Broadside.Viewer.OutlineEntry.Level.get -> int
+ Broadside.Viewer.OutlineEntry.PageLabel.get -> string?
+ Broadside.Viewer.OutlineEntry.Parent.get -> Broadside.Viewer.OutlineEntry?
+ Broadside.Viewer.OutlineEntry.Target.get -> Broadside.Viewer.ViewTarget?
+ Broadside.Viewer.OutlineEntry.Title.get -> string!
+ Broadside.Viewer.OutlineEntry.Uri.get -> System.Uri?
+ Broadside.Viewer.PageLabelTable
+ Broadside.Viewer.PageLabelTable.Count.get -> int
+ Broadside.Viewer.PageLabelTable.GetLabel(int pageIndex) -> string!
+ Broadside.Viewer.PageLabelTable.HasLabels.get -> bool
+ Broadside.Viewer.PageLabelTable.TryFindPage(string! label, out int pageIndex) -> bool
+ Broadside.Viewer.PageThumbnails
+ Broadside.Viewer.PageThumbnails.Failed -> System.EventHandler<Broadside.Viewer.ThumbnailFailedEventArgs!>?
+ Broadside.Viewer.PageThumbnails.GetScaleAsync(int pageIndex, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<double>
+ Broadside.Viewer.PageThumbnails.PageThumbnails(Broadside.Viewer.DocumentSession! session, Broadside.Viewer.IRenderSurface! surface, Broadside.Viewer.ThumbnailOptions? options = null) -> void
+ Broadside.Viewer.PageThumbnails.RenderAsync(System.Collections.Generic.IEnumerable<int>! pages, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<int>!
+ Broadside.Viewer.SearchMatch
+ Broadside.Viewer.SearchMatch.Bounds.get -> Broadside.PdfRectangle
+ Broadside.Viewer.SearchMatch.Context.get -> string!
+ Broadside.Viewer.SearchMatch.PageIndex.get -> int
+ Broadside.Viewer.SearchMatch.Quads.get -> System.Collections.Generic.IReadOnlyList<Broadside.Graphics.Quad>!
+ Broadside.Viewer.SearchMatch.Text.get -> string!
+ Broadside.Viewer.SoftwareTileBackend.Begin(Broadside.Rendering.DisplayList! list, Broadside.Rendering.Bitmap! target, in Broadside.Graphics.Matrix deviceTransform, Broadside.Rendering.ColorFilter colorFilter, System.Threading.CancellationToken cancellationToken) -> Broadside.Viewer.ITileRender!
+ Broadside.Viewer.TextSearchOptions
+ Broadside.Viewer.TextSearchOptions.MatchCase.get -> bool
+ Broadside.Viewer.TextSearchOptions.MatchCase.set -> void
+ Broadside.Viewer.TextSearchOptions.MatchDiacritics.get -> bool
+ Broadside.Viewer.TextSearchOptions.MatchDiacritics.set -> void
+ Broadside.Viewer.TextSearchOptions.TextSearchOptions() -> void
+ Broadside.Viewer.TextSearchOptions.WholeWord.get -> bool
+ Broadside.Viewer.TextSearchOptions.WholeWord.set -> void
+ Broadside.Viewer.TextSearchOptions.WithMatchCase(bool matchCase = true) -> Broadside.Viewer.TextSearchOptions!
+ Broadside.Viewer.TextSearchOptions.WithMatchDiacritics(bool matchDiacritics = true) -> Broadside.Viewer.TextSearchOptions!
+ Broadside.Viewer.TextSearchOptions.WithWholeWord(bool wholeWord = true) -> Broadside.Viewer.TextSearchOptions!
+ Broadside.Viewer.ThumbnailFailedEventArgs
+ Broadside.Viewer.ThumbnailFailedEventArgs.Exception.get -> System.Exception!
+ Broadside.Viewer.ThumbnailFailedEventArgs.PageIndex.get -> int
+ Broadside.Viewer.ThumbnailOptions
+ Broadside.Viewer.ThumbnailOptions.MaxDeferral.get -> System.TimeSpan
+ Broadside.Viewer.ThumbnailOptions.Size.get -> int
+ Broadside.Viewer.ThumbnailOptions.ThumbnailOptions() -> void
+ Broadside.Viewer.ThumbnailOptions.WithMaxDeferral(System.TimeSpan deferral) -> Broadside.Viewer.ThumbnailOptions!
+ Broadside.Viewer.ThumbnailOptions.WithSize(int pixels) -> Broadside.Viewer.ThumbnailOptions!
+ Broadside.Viewer.ViewTarget
+ Broadside.Viewer.ViewTarget.Bottom.get -> double?
+ Broadside.Viewer.ViewTarget.Bottom.init -> void
+ Broadside.Viewer.ViewTarget.GetViewport(Broadside.Viewer.PageLayout! layout, in Broadside.Viewer.Viewport viewport, double actualSize = 1, double minZoom = 0.1, double maxZoom = 64) -> Broadside.Viewer.Viewport
+ Broadside.Viewer.ViewTarget.Left.get -> double?
+ Broadside.Viewer.ViewTarget.Left.init -> void
+ Broadside.Viewer.ViewTarget.PageIndex.get -> int
+ Broadside.Viewer.ViewTarget.PageIndex.init -> void
+ Broadside.Viewer.ViewTarget.Right.get -> double?
+ Broadside.Viewer.ViewTarget.Right.init -> void
+ Broadside.Viewer.ViewTarget.Top.get -> double?
+ Broadside.Viewer.ViewTarget.Top.init -> void
+ Broadside.Viewer.ViewTarget.View.get -> Broadside.PdfDestinationView
+ Broadside.Viewer.ViewTarget.View.init -> void
+ Broadside.Viewer.ViewTarget.ViewTarget() -> void
+ Broadside.Viewer.ViewTarget.ViewTarget(int PageIndex, Broadside.PdfDestinationView View, double? Left, double? Top, double? Right, double? Bottom, double? Zoom) -> void
+ Broadside.Viewer.ViewTarget.Zoom.get -> double?
+ Broadside.Viewer.ViewTarget.Zoom.init -> void
+ Broadside.Viewer.ViewerOptions.ColorFilter.get -> Broadside.Rendering.ColorFilter
+ Broadside.Viewer.ViewerOptions.ColorFilter.set -> void
+ Broadside.Viewer.ViewerOptions.TransientFieldAppearances.get -> bool
+ Broadside.Viewer.ViewerOptions.TransientFieldAppearances.set -> void
+ Broadside.Viewer.ViewerOptions.WithColorFilter(Broadside.Rendering.ColorFilter colorFilter) -> Broadside.Viewer.ViewerOptions!
+ Broadside.Viewer.ViewerOptions.WithTransientFieldAppearances(bool transient) -> Broadside.Viewer.ViewerOptions!
+ Broadside.Viewer.XfaFormEventArgs
+ Broadside.Viewer.XfaFormEventArgs.Explanation.get -> string!
+ Broadside.Viewer.XfaFormEventArgs.IsDynamic.get -> bool
+ Broadside.Viewer.XfaFormEventArgs.PacketCount.get -> int
+ const Broadside.Viewer.ThumbnailOptions.DefaultSize = 160 -> int
+ override Broadside.Viewer.OutlineEntry.ToString() -> string!
+ static Broadside.Viewer.DocumentOutline.LoadAsync(Broadside.Viewer.DocumentSession! session, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<Broadside.Viewer.DocumentOutline!>
+ static Broadside.Viewer.PageLabelTable.LoadAsync(Broadside.Viewer.DocumentSession! session, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<Broadside.Viewer.PageLabelTable!>
+ static Broadside.Viewer.ViewTarget.FindNamedAsync(Broadside.Viewer.DocumentSession! session, string! name, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<Broadside.Viewer.ViewTarget?>
+ static Broadside.Viewer.ViewTarget.Resolve(Broadside.PdfDestination? destination) -> Broadside.Viewer.ViewTarget?
+ static readonly Broadside.Viewer.ThumbnailOptions.DefaultMaxDeferral -> System.TimeSpan
```

</details>

<details><summary><code>Broadside.Viewer.Avalonia</code>: 70 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Avalonia.PdfView
+ Broadside.Viewer.Avalonia.PdfView.Accessibility.get -> Broadside.Viewer.AccessibilityBridge?
+ Broadside.Viewer.Avalonia.PdfView.ActionRequested -> System.EventHandler<Broadside.Viewer.ViewerActionEventArgs!>?
+ Broadside.Viewer.Avalonia.PdfView.AllowAnnotationEditing.get -> bool
+ Broadside.Viewer.Avalonia.PdfView.AllowAnnotationEditing.set -> void
+ Broadside.Viewer.Avalonia.PdfView.Annotations.get -> Broadside.Viewer.AnnotationLayer?
+ Broadside.Viewer.Avalonia.PdfView.ClearSearch() -> void
+ Broadside.Viewer.Avalonia.PdfView.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.Avalonia.PdfView.ColorFilter.set -> void
+ Broadside.Viewer.Avalonia.PdfView.CopySelectionAsync() -> System.Threading.Tasks.Task<string?>!
+ Broadside.Viewer.Avalonia.PdfView.CurrentSession.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Avalonia.PdfView.Dispose() -> void
+ Broadside.Viewer.Avalonia.PdfView.Error -> System.EventHandler<Broadside.Viewer.Avalonia.PdfViewErrorEventArgs!>?
+ Broadside.Viewer.Avalonia.PdfView.Find(string! query, Broadside.Viewer.TextSearchOptions? options = null) -> void
+ Broadside.Viewer.Avalonia.PdfView.FindNext() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.Avalonia.PdfView.FindPrevious() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.Avalonia.PdfView.GoTo(Broadside.Viewer.ViewTarget target) -> void
+ Broadside.Viewer.Avalonia.PdfView.GoToPage(int pageIndex) -> void
+ Broadside.Viewer.Avalonia.PdfView.LayoutOptions.get -> Broadside.Viewer.PageLayoutOptions?
+ Broadside.Viewer.Avalonia.PdfView.LayoutOptions.set -> void
+ Broadside.Viewer.Avalonia.PdfView.NextPage() -> void
+ Broadside.Viewer.Avalonia.PdfView.Options.get -> Broadside.Viewer.ViewerOptions?
+ Broadside.Viewer.Avalonia.PdfView.Options.set -> void
+ Broadside.Viewer.Avalonia.PdfView.PageLabels.get -> Broadside.Viewer.PageLabelTable?
+ Broadside.Viewer.Avalonia.PdfView.PdfView() -> void
+ Broadside.Viewer.Avalonia.PdfView.PreviousPage() -> void
+ Broadside.Viewer.Avalonia.PdfView.Redraw() -> void
+ Broadside.Viewer.Avalonia.PdfView.Reveal(int pageIndex, Broadside.PdfRectangle area) -> void
+ Broadside.Viewer.Avalonia.PdfView.ScrollBy(double deltaX, double deltaY) -> void
+ Broadside.Viewer.Avalonia.PdfView.Search.get -> Broadside.Viewer.DocumentSearch?
+ Broadside.Viewer.Avalonia.PdfView.Selection.get -> Broadside.Viewer.TextSelection?
+ Broadside.Viewer.Avalonia.PdfView.Session.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Avalonia.PdfView.Session.set -> void
+ Broadside.Viewer.Avalonia.PdfView.ShowAnnotations.get -> bool
+ Broadside.Viewer.Avalonia.PdfView.ShowAnnotations.set -> void
+ Broadside.Viewer.Avalonia.PdfView.Source.get -> string?
+ Broadside.Viewer.Avalonia.PdfView.Source.set -> void
+ Broadside.Viewer.Avalonia.PdfView.State.get -> Broadside.Viewer.Avalonia.PdfViewState
+ Broadside.Viewer.Avalonia.PdfView.StateChanged -> System.EventHandler<Broadside.Viewer.Avalonia.PdfViewStateChangedEventArgs!>?
+ Broadside.Viewer.Avalonia.PdfView.ZoomBy(double factor) -> void
+ Broadside.Viewer.Avalonia.PdfView.ZoomTo(Broadside.Viewer.ZoomMode mode) -> void
+ Broadside.Viewer.Avalonia.PdfViewAutomationPeer
+ Broadside.Viewer.Avalonia.PdfViewAutomationPeer.PdfViewAutomationPeer(Broadside.Viewer.Avalonia.PdfView! owner) -> void
+ Broadside.Viewer.Avalonia.PdfViewAutomationPeer.Viewer.get -> Broadside.Viewer.Avalonia.PdfView!
+ Broadside.Viewer.Avalonia.PdfViewErrorEventArgs
+ Broadside.Viewer.Avalonia.PdfViewErrorEventArgs.Exception.get -> System.Exception!
+ Broadside.Viewer.Avalonia.PdfViewErrorEventArgs.PdfViewErrorEventArgs(System.Exception! exception) -> void
+ Broadside.Viewer.Avalonia.PdfViewState
+ Broadside.Viewer.Avalonia.PdfViewState.IsLoading.get -> bool
+ Broadside.Viewer.Avalonia.PdfViewState.IsLoading.init -> void
+ Broadside.Viewer.Avalonia.PdfViewState.IsRendering.get -> bool
+ Broadside.Viewer.Avalonia.PdfViewState.IsRendering.init -> void
+ Broadside.Viewer.Avalonia.PdfViewState.PageCount.get -> int
+ Broadside.Viewer.Avalonia.PdfViewState.PageCount.init -> void
+ Broadside.Viewer.Avalonia.PdfViewState.PageIndex.get -> int
+ Broadside.Viewer.Avalonia.PdfViewState.PageIndex.init -> void
+ Broadside.Viewer.Avalonia.PdfViewState.PdfViewState() -> void
+ Broadside.Viewer.Avalonia.PdfViewState.PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering) -> void
+ Broadside.Viewer.Avalonia.PdfViewState.Zoom.get -> double
+ Broadside.Viewer.Avalonia.PdfViewState.Zoom.init -> void
+ Broadside.Viewer.Avalonia.PdfViewStateChangedEventArgs
+ Broadside.Viewer.Avalonia.PdfViewStateChangedEventArgs.PdfViewStateChangedEventArgs(Broadside.Viewer.Avalonia.PdfViewState state) -> void
+ Broadside.Viewer.Avalonia.PdfViewStateChangedEventArgs.State.get -> Broadside.Viewer.Avalonia.PdfViewState
+ override Broadside.Viewer.Avalonia.PdfView.Render(Avalonia.Media.DrawingContext! context) -> void
+ static readonly Broadside.Viewer.Avalonia.PdfView.AllowAnnotationEditingProperty -> Avalonia.StyledProperty<bool>!
+ static readonly Broadside.Viewer.Avalonia.PdfView.ColorFilterProperty -> Avalonia.StyledProperty<Broadside.Rendering.ColorFilter?>!
+ static readonly Broadside.Viewer.Avalonia.PdfView.LayoutOptionsProperty -> Avalonia.StyledProperty<Broadside.Viewer.PageLayoutOptions?>!
+ static readonly Broadside.Viewer.Avalonia.PdfView.SessionProperty -> Avalonia.StyledProperty<Broadside.Viewer.DocumentSession?>!
+ static readonly Broadside.Viewer.Avalonia.PdfView.ShowAnnotationsProperty -> Avalonia.StyledProperty<bool>!
+ static readonly Broadside.Viewer.Avalonia.PdfView.SourceProperty -> Avalonia.StyledProperty<string?>!
```

</details>

<details><summary><code>Broadside.Viewer.Blazor</code>: 7 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Blazor.PdfViewer.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.Blazor.PdfViewer.ColorFilter.set -> void
+ Broadside.Viewer.Blazor.PdfViewer.Forms.get -> Broadside.Viewer.FormFilling?
+ Broadside.Viewer.Blazor.PdfViewer.GoTo(Broadside.Viewer.ViewTarget target) -> void
+ Broadside.Viewer.Blazor.PdfViewer.OnXfaForm.get -> Microsoft.AspNetCore.Components.EventCallback<Broadside.Viewer.XfaFormEventArgs!>
+ Broadside.Viewer.Blazor.PdfViewer.OnXfaForm.set -> void
+ Broadside.Viewer.Blazor.PdfViewer.Search.get -> Broadside.Viewer.DocumentSearch?
```

</details>

<details><summary><code>Broadside.Viewer.Maui</code>: 68 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Maui.PdfAnnotationsPanel
+ Broadside.Viewer.Maui.PdfAnnotationsPanel.Edited -> System.EventHandler?
+ Broadside.Viewer.Maui.PdfAnnotationsPanel.PdfAnnotationsPanel() -> void
+ Broadside.Viewer.Maui.PdfAnnotationsPanel.RefreshAsync() -> System.Threading.Tasks.Task!
+ Broadside.Viewer.Maui.PdfAnnotationsPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfAnnotationsPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.FileName.get -> string?
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.FileName.set -> void
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.PdfDocumentInfoPanel() -> void
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.Refresh() -> void
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfDocumentInfoPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfFormPanel
+ Broadside.Viewer.Maui.PdfFormPanel.Edited -> System.EventHandler?
+ Broadside.Viewer.Maui.PdfFormPanel.FieldCount.get -> int
+ Broadside.Viewer.Maui.PdfFormPanel.PdfFormPanel() -> void
+ Broadside.Viewer.Maui.PdfFormPanel.RefreshAsync() -> System.Threading.Tasks.Task!
+ Broadside.Viewer.Maui.PdfFormPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfFormPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfFormPanel.XfaExplanation.get -> string?
+ Broadside.Viewer.Maui.PdfOutlinePanel
+ Broadside.Viewer.Maui.PdfOutlinePanel.Activate(int index) -> bool
+ Broadside.Viewer.Maui.PdfOutlinePanel.PdfOutlinePanel() -> void
+ Broadside.Viewer.Maui.PdfOutlinePanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfOutlinePanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfOutlinePanel.VisibleCount.get -> int
+ Broadside.Viewer.Maui.PdfSearchPanel
+ Broadside.Viewer.Maui.PdfSearchPanel.Clear() -> void
+ Broadside.Viewer.Maui.PdfSearchPanel.PdfSearchPanel() -> void
+ Broadside.Viewer.Maui.PdfSearchPanel.Query.get -> string!
+ Broadside.Viewer.Maui.PdfSearchPanel.Query.set -> void
+ Broadside.Viewer.Maui.PdfSearchPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfSearchPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfSignaturesPanel
+ Broadside.Viewer.Maui.PdfSignaturesPanel.PdfSignaturesPanel() -> void
+ Broadside.Viewer.Maui.PdfSignaturesPanel.RefreshAsync() -> System.Threading.Tasks.Task!
+ Broadside.Viewer.Maui.PdfSignaturesPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfSignaturesPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfThumbnailsPanel
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.Activate(int pageIndex) -> void
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.PageCount.get -> int
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.PdfThumbnailsPanel() -> void
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.RenderedCount.get -> int
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfThumbnailsPanel.Viewer.set -> void
+ Broadside.Viewer.Maui.PdfView.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.Maui.PdfView.ColorFilter.set -> void
+ Broadside.Viewer.Maui.PdfView.DocumentChanged -> System.EventHandler?
+ Broadside.Viewer.Maui.PdfView.Forms.get -> Broadside.Viewer.FormFilling?
+ Broadside.Viewer.Maui.PdfView.GoTo(Broadside.Viewer.ViewTarget target) -> void
+ Broadside.Viewer.Maui.PdfView.Redraw() -> void
+ Broadside.Viewer.Maui.PdfView.Reveal(int pageIndex, Broadside.PdfRectangle area) -> void
+ Broadside.Viewer.Maui.PdfView.Search.get -> Broadside.Viewer.DocumentSearch?
+ Broadside.Viewer.Maui.PdfViewSettingsPanel
+ Broadside.Viewer.Maui.PdfViewSettingsPanel.PdfViewSettingsPanel() -> void
+ Broadside.Viewer.Maui.PdfViewSettingsPanel.Viewer.get -> Broadside.Viewer.Maui.PdfView?
+ Broadside.Viewer.Maui.PdfViewSettingsPanel.Viewer.set -> void
+ static readonly Broadside.Viewer.Maui.PdfAnnotationsPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfDocumentInfoPanel.FileNameProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfDocumentInfoPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfFormPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfOutlinePanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfSearchPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfSignaturesPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfThumbnailsPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfView.ColorFilterProperty -> Microsoft.Maui.Controls.BindableProperty!
+ static readonly Broadside.Viewer.Maui.PdfViewSettingsPanel.ViewerProperty -> Microsoft.Maui.Controls.BindableProperty!
```

</details>

<details><summary><code>Broadside.Viewer.Uno</code>: 74 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Uno.AccessibleNodeAutomationPeer
+ Broadside.Viewer.Uno.AccessibleNodeAutomationPeer.Node.get -> Broadside.Viewer.AccessibleNode!
+ Broadside.Viewer.Uno.GlobalStaticResources
+ Broadside.Viewer.Uno.GlobalStaticResources.GlobalStaticResources() -> void
+ Broadside.Viewer.Uno.PdfView
+ Broadside.Viewer.Uno.PdfView.Accessibility.get -> Broadside.Viewer.AccessibilityBridge?
+ Broadside.Viewer.Uno.PdfView.ActionRequested -> System.EventHandler<Broadside.Viewer.ViewerActionEventArgs!>?
+ Broadside.Viewer.Uno.PdfView.AllowAnnotationEditing.get -> bool
+ Broadside.Viewer.Uno.PdfView.AllowAnnotationEditing.set -> void
+ Broadside.Viewer.Uno.PdfView.Annotations.get -> Broadside.Viewer.AnnotationLayer?
+ Broadside.Viewer.Uno.PdfView.ClearFind() -> void
+ Broadside.Viewer.Uno.PdfView.Close() -> void
+ Broadside.Viewer.Uno.PdfView.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.Uno.PdfView.ColorFilter.set -> void
+ Broadside.Viewer.Uno.PdfView.CopySelectionAsync() -> System.Threading.Tasks.Task<string?>!
+ Broadside.Viewer.Uno.PdfView.CurrentSession.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Uno.PdfView.Error -> System.EventHandler<Broadside.Viewer.Uno.PdfViewErrorEventArgs!>?
+ Broadside.Viewer.Uno.PdfView.Find(string! query, Broadside.Viewer.TextSearchOptions? options = null) -> void
+ Broadside.Viewer.Uno.PdfView.FindNext() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.Uno.PdfView.FindPrevious() -> Broadside.Viewer.SearchMatch?
+ Broadside.Viewer.Uno.PdfView.Forms.get -> Broadside.Viewer.FormFilling?
+ Broadside.Viewer.Uno.PdfView.GoToPage(int pageIndex) -> void
+ Broadside.Viewer.Uno.PdfView.LayoutOptions.get -> Broadside.Viewer.PageLayoutOptions?
+ Broadside.Viewer.Uno.PdfView.LayoutOptions.set -> void
+ Broadside.Viewer.Uno.PdfView.NextPage() -> void
+ Broadside.Viewer.Uno.PdfView.Options.get -> Broadside.Viewer.ViewerOptions?
+ Broadside.Viewer.Uno.PdfView.Options.set -> void
+ Broadside.Viewer.Uno.PdfView.PageLabels.get -> Broadside.Viewer.PageLabelTable?
+ Broadside.Viewer.Uno.PdfView.PdfView() -> void
+ Broadside.Viewer.Uno.PdfView.PreviousPage() -> void
+ Broadside.Viewer.Uno.PdfView.Reveal(int pageIndex, Broadside.PdfRectangle area) -> void
+ Broadside.Viewer.Uno.PdfView.ScrollBy(double deltaX, double deltaY) -> void
+ Broadside.Viewer.Uno.PdfView.Search.get -> Broadside.Viewer.DocumentSearch?
+ Broadside.Viewer.Uno.PdfView.Selection.get -> Broadside.Viewer.TextSelection?
+ Broadside.Viewer.Uno.PdfView.Session.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Uno.PdfView.Session.set -> void
+ Broadside.Viewer.Uno.PdfView.ShowAnnotations.get -> bool
+ Broadside.Viewer.Uno.PdfView.ShowAnnotations.set -> void
+ Broadside.Viewer.Uno.PdfView.Source.get -> string?
+ Broadside.Viewer.Uno.PdfView.Source.set -> void
+ Broadside.Viewer.Uno.PdfView.State.get -> Broadside.Viewer.Uno.PdfViewState
+ Broadside.Viewer.Uno.PdfView.StateChanged -> System.EventHandler<Broadside.Viewer.Uno.PdfViewStateChangedEventArgs!>?
+ Broadside.Viewer.Uno.PdfView.ZoomBy(double factor) -> void
+ Broadside.Viewer.Uno.PdfView.ZoomTo(Broadside.Viewer.ZoomMode mode) -> void
+ Broadside.Viewer.Uno.PdfViewAutomationPeer
+ Broadside.Viewer.Uno.PdfViewAutomationPeer.GetPageText(int pageIndex) -> System.Collections.Generic.IReadOnlyList<string!>!
+ Broadside.Viewer.Uno.PdfViewErrorEventArgs
+ Broadside.Viewer.Uno.PdfViewErrorEventArgs.Exception.get -> System.Exception!
+ Broadside.Viewer.Uno.PdfViewErrorEventArgs.PdfViewErrorEventArgs(System.Exception! exception) -> void
+ Broadside.Viewer.Uno.PdfViewState
+ Broadside.Viewer.Uno.PdfViewState.IsLoading.get -> bool
+ Broadside.Viewer.Uno.PdfViewState.IsLoading.init -> void
+ Broadside.Viewer.Uno.PdfViewState.IsRendering.get -> bool
+ Broadside.Viewer.Uno.PdfViewState.IsRendering.init -> void
+ Broadside.Viewer.Uno.PdfViewState.PageCount.get -> int
+ Broadside.Viewer.Uno.PdfViewState.PageCount.init -> void
+ Broadside.Viewer.Uno.PdfViewState.PageIndex.get -> int
+ Broadside.Viewer.Uno.PdfViewState.PageIndex.init -> void
+ Broadside.Viewer.Uno.PdfViewState.PdfViewState() -> void
+ Broadside.Viewer.Uno.PdfViewState.PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering) -> void
+ Broadside.Viewer.Uno.PdfViewState.Zoom.get -> double
+ Broadside.Viewer.Uno.PdfViewState.Zoom.init -> void
+ Broadside.Viewer.Uno.PdfViewStateChangedEventArgs
+ Broadside.Viewer.Uno.PdfViewStateChangedEventArgs.PdfViewStateChangedEventArgs(Broadside.Viewer.Uno.PdfViewState state) -> void
+ Broadside.Viewer.Uno.PdfViewStateChangedEventArgs.State.get -> Broadside.Viewer.Uno.PdfViewState
+ static Broadside.Viewer.Uno.GlobalStaticResources.Initialize() -> void
+ static Broadside.Viewer.Uno.GlobalStaticResources.RegisterDefaultStyles() -> void
+ static Broadside.Viewer.Uno.GlobalStaticResources.RegisterResourceDictionariesBySource() -> void
+ static readonly Broadside.Viewer.Uno.PdfView.AllowAnnotationEditingProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.Uno.PdfView.ColorFilterProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.Uno.PdfView.LayoutOptionsProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.Uno.PdfView.SessionProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.Uno.PdfView.ShowAnnotationsProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.Uno.PdfView.SourceProperty -> Microsoft.UI.Xaml.DependencyProperty!
```

</details>

<details><summary><code>Broadside.Viewer.WinUI</code>: 56 added, 0 removed</summary>

```diff
+ Broadside.Viewer.WinUI.PdfView
+ Broadside.Viewer.WinUI.PdfView.Accessibility.get -> Broadside.Viewer.AccessibilityBridge?
+ Broadside.Viewer.WinUI.PdfView.ActionRequested -> System.EventHandler<Broadside.Viewer.ViewerActionEventArgs!>?
+ Broadside.Viewer.WinUI.PdfView.AllowAnnotationEditing.get -> bool
+ Broadside.Viewer.WinUI.PdfView.AllowAnnotationEditing.set -> void
+ Broadside.Viewer.WinUI.PdfView.Annotations.get -> Broadside.Viewer.AnnotationLayer?
+ Broadside.Viewer.WinUI.PdfView.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.WinUI.PdfView.ColorFilter.set -> void
+ Broadside.Viewer.WinUI.PdfView.CopySelectionAsync() -> System.Threading.Tasks.Task<string?>!
+ Broadside.Viewer.WinUI.PdfView.CurrentSession.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.WinUI.PdfView.Dispose() -> void
+ Broadside.Viewer.WinUI.PdfView.Error -> System.EventHandler<Broadside.Viewer.WinUI.PdfViewErrorEventArgs!>?
+ Broadside.Viewer.WinUI.PdfView.GoToPage(int pageIndex) -> void
+ Broadside.Viewer.WinUI.PdfView.LayoutOptions.get -> Broadside.Viewer.PageLayoutOptions?
+ Broadside.Viewer.WinUI.PdfView.LayoutOptions.set -> void
+ Broadside.Viewer.WinUI.PdfView.NextPage() -> void
+ Broadside.Viewer.WinUI.PdfView.Options.get -> Broadside.Viewer.ViewerOptions?
+ Broadside.Viewer.WinUI.PdfView.Options.set -> void
+ Broadside.Viewer.WinUI.PdfView.PdfView() -> void
+ Broadside.Viewer.WinUI.PdfView.PreviousPage() -> void
+ Broadside.Viewer.WinUI.PdfView.Reveal(int pageIndex, Broadside.PdfRectangle area) -> void
+ Broadside.Viewer.WinUI.PdfView.ScrollBy(double deltaX, double deltaY) -> void
+ Broadside.Viewer.WinUI.PdfView.Search.get -> Broadside.Viewer.DocumentSearch?
+ Broadside.Viewer.WinUI.PdfView.Selection.get -> Broadside.Viewer.TextSelection?
+ Broadside.Viewer.WinUI.PdfView.Session.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.WinUI.PdfView.Session.set -> void
+ Broadside.Viewer.WinUI.PdfView.ShowAnnotations.get -> bool
+ Broadside.Viewer.WinUI.PdfView.ShowAnnotations.set -> void
+ Broadside.Viewer.WinUI.PdfView.Source.get -> string?
+ Broadside.Viewer.WinUI.PdfView.Source.set -> void
+ Broadside.Viewer.WinUI.PdfView.State.get -> Broadside.Viewer.WinUI.PdfViewState
+ Broadside.Viewer.WinUI.PdfView.StateChanged -> System.EventHandler<Broadside.Viewer.WinUI.PdfViewStateChangedEventArgs!>?
+ Broadside.Viewer.WinUI.PdfView.ZoomBy(double factor) -> void
+ Broadside.Viewer.WinUI.PdfView.ZoomTo(Broadside.Viewer.ZoomMode mode) -> void
+ Broadside.Viewer.WinUI.PdfViewErrorEventArgs
+ Broadside.Viewer.WinUI.PdfViewErrorEventArgs.Exception.get -> System.Exception!
+ Broadside.Viewer.WinUI.PdfViewErrorEventArgs.PdfViewErrorEventArgs(System.Exception! exception) -> void
+ Broadside.Viewer.WinUI.PdfViewState
+ Broadside.Viewer.WinUI.PdfViewState.IsLoading.get -> bool
+ Broadside.Viewer.WinUI.PdfViewState.IsLoading.init -> void
+ Broadside.Viewer.WinUI.PdfViewState.IsRendering.get -> bool
+ Broadside.Viewer.WinUI.PdfViewState.IsRendering.init -> void
+ Broadside.Viewer.WinUI.PdfViewState.PageCount.get -> int
+ Broadside.Viewer.WinUI.PdfViewState.PageCount.init -> void
+ Broadside.Viewer.WinUI.PdfViewState.PageIndex.get -> int
+ Broadside.Viewer.WinUI.PdfViewState.PageIndex.init -> void
+ Broadside.Viewer.WinUI.PdfViewState.PdfViewState() -> void
+ Broadside.Viewer.WinUI.PdfViewState.PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering) -> void
+ Broadside.Viewer.WinUI.PdfViewState.Zoom.get -> double
+ Broadside.Viewer.WinUI.PdfViewState.Zoom.init -> void
+ Broadside.Viewer.WinUI.PdfViewStateChangedEventArgs
+ Broadside.Viewer.WinUI.PdfViewStateChangedEventArgs.PdfViewStateChangedEventArgs(Broadside.Viewer.WinUI.PdfViewState state) -> void
+ Broadside.Viewer.WinUI.PdfViewStateChangedEventArgs.State.get -> Broadside.Viewer.WinUI.PdfViewState
+ static readonly Broadside.Viewer.WinUI.PdfView.AllowAnnotationEditingProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.WinUI.PdfView.ShowAnnotationsProperty -> Microsoft.UI.Xaml.DependencyProperty!
+ static readonly Broadside.Viewer.WinUI.PdfView.SourceProperty -> Microsoft.UI.Xaml.DependencyProperty!
```

</details>

<details><summary><code>Broadside.Viewer.Wpf</code>: 59 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Wpf.PdfView
+ Broadside.Viewer.Wpf.PdfView.Accessibility.get -> Broadside.Viewer.AccessibilityBridge?
+ Broadside.Viewer.Wpf.PdfView.ActionRequested -> System.EventHandler<Broadside.Viewer.ViewerActionEventArgs!>?
+ Broadside.Viewer.Wpf.PdfView.AllowAnnotationEditing.get -> bool
+ Broadside.Viewer.Wpf.PdfView.AllowAnnotationEditing.set -> void
+ Broadside.Viewer.Wpf.PdfView.Annotations.get -> Broadside.Viewer.AnnotationLayer?
+ Broadside.Viewer.Wpf.PdfView.ColorFilter.get -> Broadside.Rendering.ColorFilter?
+ Broadside.Viewer.Wpf.PdfView.ColorFilter.set -> void
+ Broadside.Viewer.Wpf.PdfView.CopySelectionAsync() -> System.Threading.Tasks.Task<string?>!
+ Broadside.Viewer.Wpf.PdfView.CurrentSession.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Wpf.PdfView.Dispose() -> void
+ Broadside.Viewer.Wpf.PdfView.Error -> System.EventHandler<Broadside.Viewer.Wpf.PdfViewErrorEventArgs!>?
+ Broadside.Viewer.Wpf.PdfView.GoToPage(int pageIndex) -> void
+ Broadside.Viewer.Wpf.PdfView.LayoutOptions.get -> Broadside.Viewer.PageLayoutOptions?
+ Broadside.Viewer.Wpf.PdfView.LayoutOptions.set -> void
+ Broadside.Viewer.Wpf.PdfView.NextPage() -> void
+ Broadside.Viewer.Wpf.PdfView.Options.get -> Broadside.Viewer.ViewerOptions?
+ Broadside.Viewer.Wpf.PdfView.Options.set -> void
+ Broadside.Viewer.Wpf.PdfView.PdfView() -> void
+ Broadside.Viewer.Wpf.PdfView.PreviousPage() -> void
+ Broadside.Viewer.Wpf.PdfView.Reveal(int pageIndex, Broadside.PdfRectangle area) -> void
+ Broadside.Viewer.Wpf.PdfView.ScrollBy(double deltaX, double deltaY) -> void
+ Broadside.Viewer.Wpf.PdfView.Search.get -> Broadside.Viewer.DocumentSearch?
+ Broadside.Viewer.Wpf.PdfView.Selection.get -> Broadside.Viewer.TextSelection?
+ Broadside.Viewer.Wpf.PdfView.Session.get -> Broadside.Viewer.DocumentSession?
+ Broadside.Viewer.Wpf.PdfView.Session.set -> void
+ Broadside.Viewer.Wpf.PdfView.ShowAnnotations.get -> bool
+ Broadside.Viewer.Wpf.PdfView.ShowAnnotations.set -> void
+ Broadside.Viewer.Wpf.PdfView.Source.get -> string?
+ Broadside.Viewer.Wpf.PdfView.Source.set -> void
+ Broadside.Viewer.Wpf.PdfView.State.get -> Broadside.Viewer.Wpf.PdfViewState
+ Broadside.Viewer.Wpf.PdfView.StateChanged -> System.EventHandler<Broadside.Viewer.Wpf.PdfViewStateChangedEventArgs!>?
+ Broadside.Viewer.Wpf.PdfView.ZoomBy(double factor) -> void
+ Broadside.Viewer.Wpf.PdfView.ZoomTo(Broadside.Viewer.ZoomMode mode) -> void
+ Broadside.Viewer.Wpf.PdfViewErrorEventArgs
+ Broadside.Viewer.Wpf.PdfViewErrorEventArgs.Exception.get -> System.Exception!
+ Broadside.Viewer.Wpf.PdfViewErrorEventArgs.PdfViewErrorEventArgs(System.Exception! exception) -> void
+ Broadside.Viewer.Wpf.PdfViewState
+ Broadside.Viewer.Wpf.PdfViewState.IsLoading.get -> bool
+ Broadside.Viewer.Wpf.PdfViewState.IsLoading.init -> void
+ Broadside.Viewer.Wpf.PdfViewState.IsRendering.get -> bool
+ Broadside.Viewer.Wpf.PdfViewState.IsRendering.init -> void
+ Broadside.Viewer.Wpf.PdfViewState.PageCount.get -> int
+ Broadside.Viewer.Wpf.PdfViewState.PageCount.init -> void
+ Broadside.Viewer.Wpf.PdfViewState.PageIndex.get -> int
+ Broadside.Viewer.Wpf.PdfViewState.PageIndex.init -> void
+ Broadside.Viewer.Wpf.PdfViewState.PdfViewState() -> void
+ Broadside.Viewer.Wpf.PdfViewState.PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering) -> void
+ Broadside.Viewer.Wpf.PdfViewState.Zoom.get -> double
+ Broadside.Viewer.Wpf.PdfViewState.Zoom.init -> void
+ Broadside.Viewer.Wpf.PdfViewStateChangedEventArgs
+ Broadside.Viewer.Wpf.PdfViewStateChangedEventArgs.PdfViewStateChangedEventArgs(Broadside.Viewer.Wpf.PdfViewState state) -> void
+ Broadside.Viewer.Wpf.PdfViewStateChangedEventArgs.State.get -> Broadside.Viewer.Wpf.PdfViewState
+ static readonly Broadside.Viewer.Wpf.PdfView.AllowAnnotationEditingProperty -> System.Windows.DependencyProperty!
+ static readonly Broadside.Viewer.Wpf.PdfView.ColorFilterProperty -> System.Windows.DependencyProperty!
+ static readonly Broadside.Viewer.Wpf.PdfView.LayoutOptionsProperty -> System.Windows.DependencyProperty!
+ static readonly Broadside.Viewer.Wpf.PdfView.SessionProperty -> System.Windows.DependencyProperty!
+ static readonly Broadside.Viewer.Wpf.PdfView.ShowAnnotationsProperty -> System.Windows.DependencyProperty!
+ static readonly Broadside.Viewer.Wpf.PdfView.SourceProperty -> System.Windows.DependencyProperty!
```

</details>

## 0.1.0-preview.7 (2026-11-14)

Changes since [`v0.1.0-preview.6`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.6). Full diff: [`v0.1.0-preview.6...v0.1.0-preview.7`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.6...v0.1.0-preview.7).

### Breaking changes

- Public API removed or changed in `Broadside`: see Public API below.

### Features

- **conformance:** check clause citations against the XML documentation ([#240](https://github.com/michaelstonis/Broadside/issues/240))
- **functions:** interpolate Order 3 sampled functions with a cubic spline ([#236](https://github.com/michaelstonis/Broadside/issues/236))
- **rendering:** render the same bytes on every platform ([#233](https://github.com/michaelstonis/Broadside/issues/233))
- **security:** read unencrypted wrapper documents and open their encrypted payload ([#234](https://github.com/michaelstonis/Broadside/issues/234))
- **security:** write unencrypted wrapper documents around an encrypted payload ([#235](https://github.com/michaelstonis/Broadside/issues/235))

### Fixes

- **ci:** allow 2 ulps for cbrt against the platform library and keep the Android smoke reporting when the app is gone ([#233](https://github.com/michaelstonis/Broadside/issues/233))
- **parsing:** record a diagnostic for the remaining silent file-structure repairs ([#238](https://github.com/michaelstonis/Broadside/issues/238))
- **parsing:** keep table entries beyond Size in lenient mode ([#238](https://github.com/michaelstonis/Broadside/issues/238))

### Performance

- **cos:** keep cross-reference entries compact and copy large streams in windows on save ([#237](https://github.com/michaelstonis/Broadside/issues/237))
- **cos:** record the allocation baseline of compact cross-reference entries ([#237](https://github.com/michaelstonis/Broadside/issues/237))
- **numerics:** table-driven deterministic pow and single-kernel degree sine and cosine ([#233](https://github.com/michaelstonis/Broadside/issues/233))
- **rendering:** compute the arc step once per stroke and skip the angle math for joins too small to need arc points ([#233](https://github.com/michaelstonis/Broadside/issues/233))

### Refactoring

- **annotations:** subtypes read their own entries through shared readers ([#253](https://github.com/michaelstonis/Broadside/issues/253))
- **annotations:** delete the retired subtype readers from PdfAnnotation ([#254](https://github.com/michaelstonis/Broadside/issues/254))
- **api:** async entry points mirror their synchronous overloads ([#250](https://github.com/michaelstonis/Broadside/issues/250))
- **diagnostics:** give diagnostic codes a public, typed catalog ([#242](https://github.com/michaelstonis/Broadside/issues/242))
- **diagnostics:** font, CharString, CFF, Type 1, Type 3, CMap and text codes use the public catalog ([#245](https://github.com/michaelstonis/Broadside/issues/245))
- **diagnostics:** move file structure, object, security and catalog report sites to the public catalog ([#243](https://github.com/michaelstonis/Broadside/issues/243))
- **diagnostics:** keep one set of typed DiagnosticSink overloads after the parallel migrations ([#243](https://github.com/michaelstonis/Broadside/issues/243), [#245](https://github.com/michaelstonis/Broadside/issues/245))
- **diagnostics:** migrate content, color and document-model report sites to the public catalog ([#246](https://github.com/michaelstonis/Broadside/issues/246))
- **diagnostics:** report filter, codec and image codes from the public catalog ([#244](https://github.com/michaelstonis/Broadside/issues/244))
- **diagnostics:** extension-point contexts report typed codes ([#248](https://github.com/michaelstonis/Broadside/issues/248))
- **diagnostics:** retire the internal diagnostic catalog and guard the public one ([#247](https://github.com/michaelstonis/Broadside/issues/247))
- **files:** read associated files through their owners ([#249](https://github.com/michaelstonis/Broadside/issues/249))
- **filters:** split the DCT decoder by responsibility ([#252](https://github.com/michaelstonis/Broadside/issues/252))
- **images:** one geometry value, enums for SMaskInData and the Adobe transform, one name each for image masks and the decoded-size limit ([#251](https://github.com/michaelstonis/Broadside/issues/251))

### Documentation

- **bench:** note the cross-reference and save timings of compact entries ([#237](https://github.com/michaelstonis/Broadside/issues/237))
- **conformance:** close every conformance row for v1 ([#241](https://github.com/michaelstonis/Broadside/issues/241))

### Tests

- **corpus:** re-triage the real-world snapshots and gate allowlists after the beyond-Size change ([#238](https://github.com/michaelstonis/Broadside/issues/238))
- **diagnostics:** the internal catalog may be empty once every area has moved ([#244](https://github.com/michaelstonis/Broadside/issues/244))
- **diagnostics:** let the catalog guard allow owner-prefixed third-party codes ([#247](https://github.com/michaelstonis/Broadside/issues/247))
- **files:** read the wrapper's associated files through the document ([#235](https://github.com/michaelstonis/Broadside/issues/235), [#249](https://github.com/michaelstonis/Broadside/issues/249))
- **files:** leave the unencrypted wrapper out of the encrypted-file theories ([#234](https://github.com/michaelstonis/Broadside/issues/234))
- **hosting:** guard that every option and diagnostic reaches the hosted engine ([#239](https://github.com/michaelstonis/Broadside/issues/239))
- **hosting:** sample a valid cryptographic filter version in the options guard ([#239](https://github.com/michaelstonis/Broadside/issues/239))
- **numerics:** check cbrt exactly instead of against the platform library, which is up to 3 ulps off on glibc ([#233](https://github.com/michaelstonis/Broadside/issues/233))
- **structure:** accept one more real-world structure tree recovered by the xref repairs ([#238](https://github.com/michaelstonis/Broadside/issues/238))

### Chores

- **bench:** re-record the allocation baseline from the CI runner after the 7A sweep
- **bench:** re-record FirstPaint allocation after stroker arc-step fields

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.6...v0.1.0-preview.7)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 774 | 63 |

<details><summary><code>Broadside</code>: 774 added, 63 removed</summary>

```diff
- Broadside.Content.ImageEvent.IsStencil.get -> bool
- Broadside.Filters.FilterContext.Report(string! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
- Broadside.Filters.ImageFilterContext.BitsPerComponent.get -> int
- Broadside.Filters.ImageFilterContext.BitsPerComponent.init -> void
- Broadside.Filters.ImageFilterContext.ColorComponents.get -> int
- Broadside.Filters.ImageFilterContext.ColorComponents.init -> void
- Broadside.Filters.ImageFilterContext.Height.get -> int
- Broadside.Filters.ImageFilterContext.Height.init -> void
- Broadside.Filters.ImageFilterContext.IsStencil.get -> bool
- Broadside.Filters.ImageFilterContext.IsStencil.init -> void
- Broadside.Filters.ImageFilterContext.TryCreateImage(int width, int height, int components, int bitsPerComponent, out Broadside.Images.DecodedImageBuilder? image) -> bool
- Broadside.Filters.ImageFilterContext.WantsAlpha.get -> bool
- Broadside.Filters.ImageFilterContext.WantsAlpha.init -> void
- Broadside.Filters.ImageFilterContext.Width.get -> int
- Broadside.Filters.ImageFilterContext.Width.init -> void
- Broadside.Filters.ImageHeader.BitsPerComponent.get -> int
- Broadside.Filters.ImageHeader.BitsPerComponent.init -> void
- Broadside.Filters.ImageHeader.Components.get -> int
- Broadside.Filters.ImageHeader.Components.init -> void
- Broadside.Filters.ImageHeader.Height.get -> int
- Broadside.Filters.ImageHeader.Height.init -> void
- Broadside.Filters.ImageHeader.ImageHeader(int Width, int Height, int Components, int BitsPerComponent) -> void
- Broadside.Filters.ImageHeader.Width.get -> int
- Broadside.Filters.ImageHeader.Width.init -> void
- Broadside.Fonts.FontProgramContext.Report(string! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
- Broadside.Images.DecodedImage.AdobeTransform.get -> int?
- Broadside.Images.DecodedImage.BitsPerComponent.get -> int
- Broadside.Images.DecodedImage.Components.get -> int
- Broadside.Images.DecodedImage.Height.get -> int
- Broadside.Images.DecodedImage.Width.get -> int
- Broadside.Images.DecodedImageBuilder.AdobeTransform.get -> int?
- Broadside.Images.DecodedImageBuilder.BitsPerComponent.get -> int
- Broadside.Images.DecodedImageBuilder.Components.get -> int
- Broadside.Images.DecodedImageBuilder.Height.get -> int
- Broadside.Images.DecodedImageBuilder.Width.get -> int
- Broadside.Images.PdfImage.IsStencil.get -> bool
- Broadside.Images.PdfImage.SoftMaskInData.get -> int
- Broadside.Images.PdfImageMaskKind.Stencil = 1 -> Broadside.Images.PdfImageMaskKind
- Broadside.PdfDocument.ReadAssociatedFiles(Broadside.Objects.CosDictionary! owner, Broadside.Objects.CosReference? ownerReference = null) -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
- Broadside.PdfDocument.ReadMarkedContentAssociatedFiles(Broadside.Objects.CosObject? properties) -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
- Broadside.PdfDocument.SaveAsync(System.IO.Stream! stream, Broadside.PdfSaveOptions? options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task!
- Broadside.PdfEngine.OpenAsync(System.IO.Stream! stream) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- Broadside.PdfEngine.OpenAsync(System.IO.Stream! stream, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- Broadside.PdfEngine.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- Broadside.PdfEngine.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- Broadside.PdfOptions.MaxDecodedStreamLength.get -> long
- Broadside.PdfOptions.MaxDecodedStreamLength.set -> void
- Broadside.PdfOptions.WithMaxDecodedStreamLength(long maxLength) -> Broadside.PdfOptions!
- Broadside.Rendering.DisplayImage.BitsPerComponent.get -> int
- Broadside.Rendering.DisplayImage.Components.get -> int
- Broadside.Rendering.DisplayImage.Height.get -> int
- Broadside.Rendering.DisplayImage.Width.get -> int
- Broadside.Security.SecurityHandlerContext.Report(string! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
- const Broadside.PdfOptions.DefaultMaxDecodedStreamLength = 1073741824 -> long
- static Broadside.Images.ImageRows.Stencil(System.ReadOnlySpan<byte> row, int width, bool inverted, System.Span<byte> coverage) -> void
- static Broadside.PdfDocument.OpenAsync(System.IO.Stream! stream) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.IO.Stream! stream, Broadside.PdfOptions! options) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.IO.Stream! stream, Broadside.PdfOptions! options, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.IO.Stream! stream, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri, Broadside.PdfOptions! options) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri, Broadside.PdfOptions! options, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
- static Broadside.PdfDocument.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.Content.ImageEvent.IsImageMask.get -> bool
+ Broadside.Content.MarkedContentEvent.AssociatedFiles.get -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
+ Broadside.Diagnostics.ActionDiagnostics
+ Broadside.Diagnostics.AnnotationDiagnostics
+ Broadside.Diagnostics.CMapDiagnostics
+ Broadside.Diagnostics.CcittDiagnostics
+ Broadside.Diagnostics.CffDiagnostics
+ Broadside.Diagnostics.CharStringDiagnostics
+ Broadside.Diagnostics.ColorDiagnostics
+ Broadside.Diagnostics.ContentDiagnostics
+ Broadside.Diagnostics.DctDiagnostics
+ Broadside.Diagnostics.Diagnostic.Kind.get -> Broadside.Diagnostics.DiagnosticCode!
+ Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Action = 27 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Annotation = 28 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.CMap = 21 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Ccitt = 12 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Cff = 18 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.CharString = 17 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Color = 25 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Content = 23 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Dct = 11 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Document = 4 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.FileSpecification = 9 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.FileStructure = 1 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Filter = 10 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Font = 16 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Form = 29 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Function = 24 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Image = 15 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Jbig2 = 13 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Jpx = 14 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Navigation = 7 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.None = 0 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.ObjectSyntax = 2 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.OptionalContent = 8 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Page = 5 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Saving = 6 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Security = 3 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Shading = 26 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Structure = 30 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Text = 22 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Type1 = 19 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticArea.Type3 = 20 -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticCatalog
+ Broadside.Diagnostics.DiagnosticCode
+ Broadside.Diagnostics.DiagnosticCode.Area.get -> Broadside.Diagnostics.DiagnosticArea
+ Broadside.Diagnostics.DiagnosticCode.Clause.get -> string?
+ Broadside.Diagnostics.DiagnosticCode.DefaultSeverity.get -> Broadside.Diagnostics.DiagnosticSeverity
+ Broadside.Diagnostics.DiagnosticCode.Equals(Broadside.Diagnostics.DiagnosticCode? other) -> bool
+ Broadside.Diagnostics.DiagnosticCode.Name.get -> string!
+ Broadside.Diagnostics.DiagnosticCode.Owner.get -> string?
+ Broadside.Diagnostics.DocumentDiagnostics
+ Broadside.Diagnostics.FileSpecificationDiagnostics
+ Broadside.Diagnostics.FileStructureDiagnostics
+ Broadside.Diagnostics.FilterDiagnostics
+ Broadside.Diagnostics.FontDiagnostics
+ Broadside.Diagnostics.FormDiagnostics
+ Broadside.Diagnostics.FunctionDiagnostics
+ Broadside.Diagnostics.ImageDiagnostics
+ Broadside.Diagnostics.Jbig2Diagnostics
+ Broadside.Diagnostics.JpxDiagnostics
+ Broadside.Diagnostics.NavigationDiagnostics
+ Broadside.Diagnostics.ObjectSyntaxDiagnostics
+ Broadside.Diagnostics.OptionalContentDiagnostics
+ Broadside.Diagnostics.PageDiagnostics
+ Broadside.Diagnostics.SavingDiagnostics
+ Broadside.Diagnostics.SecurityDiagnostics
+ Broadside.Diagnostics.ShadingDiagnostics
+ Broadside.Diagnostics.StructureDiagnostics
+ Broadside.Diagnostics.TextDiagnostics
+ Broadside.Diagnostics.Type1Diagnostics
+ Broadside.Diagnostics.Type3Diagnostics
+ Broadside.Filters.FilterContext.Report(Broadside.Diagnostics.DiagnosticCode! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
+ Broadside.Filters.ImageFilterContext.Geometry.get -> Broadside.Images.ImageGeometry
+ Broadside.Filters.ImageFilterContext.Geometry.init -> void
+ Broadside.Filters.ImageFilterContext.IsImageMask.get -> bool
+ Broadside.Filters.ImageFilterContext.IsImageMask.init -> void
+ Broadside.Filters.ImageFilterContext.SoftMaskInData.get -> Broadside.Images.PdfSoftMaskInData
+ Broadside.Filters.ImageFilterContext.SoftMaskInData.init -> void
+ Broadside.Filters.ImageFilterContext.TryCreateImage(Broadside.Images.ImageGeometry geometry, out Broadside.Images.DecodedImageBuilder? image) -> bool
+ Broadside.Filters.ImageHeader.Geometry.get -> Broadside.Images.ImageGeometry
+ Broadside.Filters.ImageHeader.Geometry.init -> void
+ Broadside.Filters.ImageHeader.ImageHeader(Broadside.Images.ImageGeometry Geometry) -> void
+ Broadside.Fonts.FontProgramContext.Report(Broadside.Diagnostics.DiagnosticCode! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
+ Broadside.Graphics.PdfFormXObject.AssociatedFiles.get -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
+ Broadside.Images.AdobeColorTransform
+ Broadside.Images.AdobeColorTransform.Unknown = 0 -> Broadside.Images.AdobeColorTransform
+ Broadside.Images.AdobeColorTransform.YCCK = 2 -> Broadside.Images.AdobeColorTransform
+ Broadside.Images.AdobeColorTransform.YCbCr = 1 -> Broadside.Images.AdobeColorTransform
+ Broadside.Images.DecodedImage.AdobeTransform.get -> Broadside.Images.AdobeColorTransform?
+ Broadside.Images.DecodedImage.Geometry.get -> Broadside.Images.ImageGeometry
+ Broadside.Images.DecodedImageBuilder.AdobeTransform.get -> Broadside.Images.AdobeColorTransform?
+ Broadside.Images.DecodedImageBuilder.Geometry.get -> Broadside.Images.ImageGeometry
+ Broadside.Images.ImageGeometry
+ Broadside.Images.ImageGeometry.AtReduction(int reduction) -> Broadside.Images.ImageGeometry
+ Broadside.Images.ImageGeometry.BitsPerComponent.get -> int
+ Broadside.Images.ImageGeometry.BitsPerComponent.init -> void
+ Broadside.Images.ImageGeometry.Components.get -> int
+ Broadside.Images.ImageGeometry.Components.init -> void
+ Broadside.Images.ImageGeometry.Height.get -> int
+ Broadside.Images.ImageGeometry.Height.init -> void
+ Broadside.Images.ImageGeometry.ImageGeometry() -> void
+ Broadside.Images.ImageGeometry.ImageGeometry(int Width, int Height, int Components, int BitsPerComponent) -> void
+ Broadside.Images.ImageGeometry.Width.get -> int
+ Broadside.Images.ImageGeometry.Width.init -> void
+ Broadside.Images.PdfImage.AssociatedFiles.get -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
+ Broadside.Images.PdfImage.IsImageMask.get -> bool
+ Broadside.Images.PdfImage.SoftMaskInData.get -> Broadside.Images.PdfSoftMaskInData
+ Broadside.Images.PdfImageMaskKind.ImageMask = 1 -> Broadside.Images.PdfImageMaskKind
+ Broadside.Images.PdfSoftMaskInData
+ Broadside.Images.PdfSoftMaskInData.Alpha = 1 -> Broadside.Images.PdfSoftMaskInData
+ Broadside.Images.PdfSoftMaskInData.None = 0 -> Broadside.Images.PdfSoftMaskInData
+ Broadside.Images.PdfSoftMaskInData.PremultipliedAlpha = 2 -> Broadside.Images.PdfSoftMaskInData
+ Broadside.PdfDocument.EncryptedPayload.get -> Broadside.PdfEncryptedPayload?
+ Broadside.PdfDocument.IsUnencryptedWrapper.get -> bool
+ Broadside.PdfDocument.SaveAsync(System.IO.Stream! stream, Broadside.PdfSaveOptions! options, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task!
+ Broadside.PdfDocument.SaveAsync(System.IO.Stream! stream, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task!
+ Broadside.PdfDocument.SaveAsync(string! path, Broadside.PdfSaveOptions! options, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task!
+ Broadside.PdfDocument.SaveAsync(string! path, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task!
+ Broadside.PdfEncryptedPayload
+ Broadside.PdfEncryptedPayload.CryptographicFilter.get -> Broadside.Objects.CosName?
+ Broadside.PdfEncryptedPayload.CryptographicFilterVersion.get -> string?
+ Broadside.PdfEncryptedPayload.Dictionary.get -> Broadside.Objects.CosDictionary?
+ Broadside.PdfEncryptedPayload.File.get -> Broadside.PdfFileSpecification!
+ Broadside.PdfEncryptedPayload.IsCryptographicFilterAvailable.get -> bool
+ Broadside.PdfEncryptedPayload.Name.get -> string!
+ Broadside.PdfEncryptedPayload.Open() -> Broadside.PdfDocument!
+ Broadside.PdfEncryptedPayload.Open(Broadside.Security.PdfCredentials! credentials) -> Broadside.PdfDocument!
+ Broadside.PdfEngine.CreateUnencryptedWrapper(System.ReadOnlySpan<byte> payload) -> Broadside.PdfDocument!
+ Broadside.PdfEngine.CreateUnencryptedWrapper(System.ReadOnlySpan<byte> payload, Broadside.PdfUnencryptedWrapperOptions! options) -> Broadside.PdfDocument!
+ Broadside.PdfEngine.OpenAsync(System.IO.Stream! stream, Broadside.Security.PdfCredentials! credentials, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(System.IO.Stream! stream, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(System.Net.Http.HttpClient! httpClient, System.Uri! uri, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(System.ReadOnlyMemory<byte> bytes, Broadside.Security.PdfCredentials! credentials, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(System.ReadOnlyMemory<byte> bytes, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(string! path, Broadside.Security.PdfCredentials! credentials, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfEngine.OpenAsync(string! path, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!
+ Broadside.PdfFileSpecification.AssociatedFiles.get -> System.Collections.Generic.IReadOnlyList<Broadside.PdfFileSpecification!>!
+ Broadside.PdfOptions.MaxDecodedLength.get -> long
+ Broadside.PdfOptions.MaxDecodedLength.set -> void
+ Broadside.PdfOptions.WithMaxDecodedLength(long maxLength) -> Broadside.PdfOptions!
+ Broadside.PdfUnencryptedWrapperOptions
+ Broadside.PdfUnencryptedWrapperOptions.Cover.get -> Broadside.PdfDocument?
+ Broadside.PdfUnencryptedWrapperOptions.Cover.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.CryptographicFilter.get -> string?
+ Broadside.PdfUnencryptedWrapperOptions.CryptographicFilter.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.CryptographicFilterVersion.get -> string?
+ Broadside.PdfUnencryptedWrapperOptions.CryptographicFilterVersion.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.Description.get -> string?
+ Broadside.PdfUnencryptedWrapperOptions.Description.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.ModificationDate.get -> System.DateTimeOffset?
+ Broadside.PdfUnencryptedWrapperOptions.ModificationDate.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.Name.get -> string?
+ Broadside.PdfUnencryptedWrapperOptions.Name.set -> void
+ Broadside.PdfUnencryptedWrapperOptions.PdfUnencryptedWrapperOptions() -> void
+ Broadside.PdfUnencryptedWrapperOptions.WithCover(Broadside.PdfDocument? cover) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.PdfUnencryptedWrapperOptions.WithCryptographicFilter(string? filter) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.PdfUnencryptedWrapperOptions.WithCryptographicFilterVersion(string? version) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.PdfUnencryptedWrapperOptions.WithDescription(string? description) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.PdfUnencryptedWrapperOptions.WithModificationDate(System.DateTimeOffset? date) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.PdfUnencryptedWrapperOptions.WithName(string? name) -> Broadside.PdfUnencryptedWrapperOptions!
+ Broadside.Rendering.DisplayImage.Geometry.get -> Broadside.Images.ImageGeometry
+ Broadside.Security.SecurityHandlerContext.Report(Broadside.Diagnostics.DiagnosticCode! code, Broadside.Diagnostics.DiagnosticSeverity severity, string! message) -> void
+ const Broadside.PdfOptions.DefaultMaxDecodedLength = 1073741824 -> long
+ override Broadside.Diagnostics.DiagnosticCode.Equals(object? obj) -> bool
+ override Broadside.Diagnostics.DiagnosticCode.GetHashCode() -> int
+ override Broadside.Diagnostics.DiagnosticCode.ToString() -> string!
+ override Broadside.PdfEncryptedPayload.ToString() -> string!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionCycle.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionEntryInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionNextInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionOutOfScope.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionTargetCycle.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.ActionTreeTooDeep.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.AdditionalActionsInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.OpenActionInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.ActionDiagnostics.UriInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationDuplicate.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationEntryInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationFlagsInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationNotIndirect.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationPageMismatch.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationRectInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationRequiredEntryMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationSharedAcrossPages.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationSubtypeMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationSubtypeNotName.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationTypeInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotationValueInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotsInherited.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AnnotsInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceBBoxMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceEntryInvalid.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceNotForm.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceNotRendered.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceResourcesMissing.get -> Broadside.Diagnostics.DiagnosticCode!
+ static Broadside.Diagnostics.AnnotationDiagnostics.AppearanceStateMissing.get -> Broadside.Diagnostics.DiagnosticCode!
# 574 more in the full diff
```

</details>

## 0.1.0-preview.6 (2026-11-13)

Changes since [`v0.1.0-preview.5`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.5). Full diff: [`v0.1.0-preview.5...v0.1.0-preview.6`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.5...v0.1.0-preview.6).

### Features

- **android:** decode JPEG images with BitmapFactory behind the platform image decoder seam ([#376](https://github.com/michaelstonis/Broadside/issues/376)) ([#481](https://github.com/michaelstonis/Broadside/issues/481))
- **coregraphics:** decode JPEG images with ImageIO behind the platform decoder seam ([#375](https://github.com/michaelstonis/Broadside/issues/375)) ([#483](https://github.com/michaelstonis/Broadside/issues/483))
- **direct2d:** decode JPEG images with WIC ([#377](https://github.com/michaelstonis/Broadside/issues/377)) ([#480](https://github.com/michaelstonis/Broadside/issues/480))
- **samples:** representative sample documents, part 1 ([#363](https://github.com/michaelstonis/Broadside/issues/363))
- **samples:** representative sample documents, part 2 ([#363](https://github.com/michaelstonis/Broadside/issues/363))
- **samples:** wire sample documents into demos, benchmarks and browser tests ([#363](https://github.com/michaelstonis/Broadside/issues/363))
- **samples:** coverage matrix, encrypted and signed sample documents ([#451](https://github.com/michaelstonis/Broadside/issues/451)) ([#455](https://github.com/michaelstonis/Broadside/issues/455))
- **samples:** forms, XFA and annotation sample documents ([#451](https://github.com/michaelstonis/Broadside/issues/451)) ([#468](https://github.com/michaelstonis/Broadside/issues/468))
- **samples:** international text sample documents ([#451](https://github.com/michaelstonis/Broadside/issues/451)) ([#463](https://github.com/michaelstonis/Broadside/issues/463))
- **samples:** legacy, scanned and color sample documents ([#451](https://github.com/michaelstonis/Broadside/issues/451)) ([#464](https://github.com/michaelstonis/Broadside/issues/464))
- **samples:** structure, archival, scale and damaged sample documents ([#451](https://github.com/michaelstonis/Broadside/issues/451)) ([#465](https://github.com/michaelstonis/Broadside/issues/465))
- **skia:** decode JPEG images with SkiaSharp's SKCodec, scaled and bit-identical ([#374](https://github.com/michaelstonis/Broadside/issues/374)) ([#485](https://github.com/michaelstonis/Broadside/issues/485))

### Fixes

- **blazor:** give PdfViewer an intrinsic height without host CSS ([#393](https://github.com/michaelstonis/Broadside/issues/393)) ([#453](https://github.com/michaelstonis/Broadside/issues/453))
- **cos:** number new objects of a file that holds object 2147483647 ([#387](https://github.com/michaelstonis/Broadside/issues/387)) ([#457](https://github.com/michaelstonis/Broadside/issues/457))
- **filters:** keep a reduced JPX decode within the header's MaxReduction ([#424](https://github.com/michaelstonis/Broadside/issues/424)) ([#452](https://github.com/michaelstonis/Broadside/issues/452))
- **filters:** share the default filter registry across engines ([#499](https://github.com/michaelstonis/Broadside/issues/499))
- **images:** share one platform decode across concurrent prefetches whatever the decoder does ([#484](https://github.com/michaelstonis/Broadside/issues/484)) ([#489](https://github.com/michaelstonis/Broadside/issues/489))
- **rendering:** paint an image unmasked on every backend when its soft mask fails to decode ([#515](https://github.com/michaelstonis/Broadside/issues/515)) ([#516](https://github.com/michaelstonis/Broadside/issues/516))
- **security:** load objects the encryption dictionary reaches again once decryption is installed ([#398](https://github.com/michaelstonis/Broadside/issues/398)) ([#458](https://github.com/michaelstonis/Broadside/issues/458))
- **security:** read an indirect KDFSalt as stored ([#478](https://github.com/michaelstonis/Broadside/issues/478)) ([#486](https://github.com/michaelstonis/Broadside/issues/486))
- **viewer:** resolve a remote file's named destinations off the UI thread ([#420](https://github.com/michaelstonis/Broadside/issues/420)) ([#450](https://github.com/michaelstonis/Broadside/issues/450))
- **webgl:** fall back to a document canvas when OffscreenCanvas has no WebGL 2 ([#444](https://github.com/michaelstonis/Broadside/issues/444))
- **writing:** never repeat a source trailer's section keys ([#445](https://github.com/michaelstonis/Broadside/issues/445)) ([#454](https://github.com/michaelstonis/Broadside/issues/454))
- **writing:** always write a cross-reference stream as /Type /XRef ([#466](https://github.com/michaelstonis/Broadside/issues/466)) ([#467](https://github.com/michaelstonis/Broadside/issues/467))

### Performance

- **colors:** transfer 8-bit colors run by run while they are in cache ([#434](https://github.com/michaelstonis/Broadside/issues/434)) ([#461](https://github.com/michaelstonis/Broadside/issues/461))
- **colors:** vectorize the CMYK characterization and copy ICCBased samples with a device alternate ([#476](https://github.com/michaelstonis/Broadside/issues/476)) ([#494](https://github.com/michaelstonis/Broadside/issues/494))
- **filters:** vectorize the 8 x 8 islow inverse DCT ([#474](https://github.com/michaelstonis/Broadside/issues/474)) ([#495](https://github.com/michaelstonis/Broadside/issues/495))
- **filters:** vectorize the JPEG 2000 wavelet and component transforms ([#477](https://github.com/michaelstonis/Broadside/issues/477)) ([#501](https://github.com/michaelstonis/Broadside/issues/501))
- **images:** vectorize YCbCr/YCCK to RGB/CMYK rows with Vector128 ([#471](https://github.com/michaelstonis/Broadside/issues/471)) ([#490](https://github.com/michaelstonis/Broadside/issues/490))
- **images:** decode soft masks at the resolution they are drawn ([#513](https://github.com/michaelstonis/Broadside/issues/513)) ([#514](https://github.com/michaelstonis/Broadside/issues/514))
- **jpx:** tier-1 decoding at 41 ns per sample ([#255](https://github.com/michaelstonis/Broadside/issues/255)) ([#502](https://github.com/michaelstonis/Broadside/issues/502))
- **jpx:** fuse and vectorize the 9/7 rounding with the DC level shift ([#503](https://github.com/michaelstonis/Broadside/issues/503)) ([#504](https://github.com/michaelstonis/Broadside/issues/504))
- **jpx:** decode code-blocks in parallel on native builds ([#506](https://github.com/michaelstonis/Broadside/issues/506))
- **jpx:** decode JPX images without a ColorSpace at the resolution they are drawn ([#508](https://github.com/michaelstonis/Broadside/issues/508))
- **rendering:** vectorize the group source-over and partial-coverage fill rows ([#473](https://github.com/michaelstonis/Broadside/issues/473)) ([#492](https://github.com/michaelstonis/Broadside/issues/492))
- **rendering:** vectorize the box filter's channel accumulation ([#475](https://github.com/michaelstonis/Broadside/issues/475)) ([#493](https://github.com/michaelstonis/Broadside/issues/493))
- **rendering:** vectorize BGRA packing, premultiplication and gray expansion ([#472](https://github.com/michaelstonis/Broadside/issues/472)) ([#491](https://github.com/michaelstonis/Broadside/issues/491))
- **rendering:** keep box-filter footprints across rows and compose JPX rows directly ([#509](https://github.com/michaelstonis/Broadside/issues/509)) ([#510](https://github.com/michaelstonis/Broadside/issues/510))
- **rendering:** average a run of box-filter footprints per call ([#511](https://github.com/michaelstonis/Broadside/issues/511)) ([#512](https://github.com/michaelstonis/Broadside/issues/512))
- **rendering:** paint enlarged images a run at a time and composite box-filtered runs together ([#517](https://github.com/michaelstonis/Broadside/issues/517)) ([#518](https://github.com/michaelstonis/Broadside/issues/518))
- **skia:** decode images synchronously now that the core shares a synchronous decode ([#496](https://github.com/michaelstonis/Broadside/issues/496)) ([#497](https://github.com/michaelstonis/Broadside/issues/497))

### Documentation

- **benchmarks:** add the browser AOT column to the sample documents baseline ([#363](https://github.com/michaelstonis/Broadside/issues/363)) ([#487](https://github.com/michaelstonis/Broadside/issues/487))
- **research:** SIMD opportunities and per-target acceleration ([#469](https://github.com/michaelstonis/Broadside/issues/469))

### Tests

- **perf:** add SIMD kernel test and benchmark infrastructure ([#470](https://github.com/michaelstonis/Broadside/issues/470)) ([#488](https://github.com/michaelstonis/Broadside/issues/488))
- **webgl:** switch the view panel test to a document the GPU draws whole ([#456](https://github.com/michaelstonis/Broadside/issues/456))

### Build and CI

- retry the poppler-utils install and skip its checks when Ubuntu's archive is unreachable ([#500](https://github.com/michaelstonis/Broadside/issues/500))
- **blazor-wasm:** pin the browser job to ubuntu-24.04 ([#459](https://github.com/michaelstonis/Broadside/issues/459)) ([#460](https://github.com/michaelstonis/Broadside/issues/460))

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.5...v0.1.0-preview.6)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 10 | 0 |
| `Broadside.Rendering.Android` | 12 | 0 |
| `Broadside.Rendering.CoreGraphics` | 9 | 0 |
| `Broadside.Rendering.Direct2D` | 13 | 0 |
| `Broadside.Rendering.Skia` | 10 | 0 |

<details><summary><code>Broadside</code>: 10 added, 0 removed</summary>

```diff
+ Broadside.Filters.ImageHeader.IccProfile.get -> System.ReadOnlyMemory<byte>
+ Broadside.Filters.ImageHeader.IccProfile.init -> void
+ Broadside.Images.PlatformImageRequest.ChromaHorizontalSubsampling.get -> int
+ Broadside.Images.PlatformImageRequest.ChromaHorizontalSubsampling.init -> void
+ Broadside.Images.PlatformImageRequest.ChromaVerticalSubsampling.get -> int
+ Broadside.Images.PlatformImageRequest.ChromaVerticalSubsampling.init -> void
+ Broadside.Images.YCbCrConversion
+ static Broadside.Images.ImageRows.PackBgra(System.ReadOnlySpan<byte> rgb, System.ReadOnlySpan<byte> opacity, int width, System.Span<byte> pixels) -> void
+ static Broadside.Images.ImageRows.PackRgba(System.ReadOnlySpan<byte> rgb, System.ReadOnlySpan<byte> opacity, int width, System.Span<byte> pixels) -> void
+ static Broadside.Images.YCbCrConversion.ToRgb(System.ReadOnlySpan<byte> y, System.ReadOnlySpan<byte> cb, System.ReadOnlySpan<byte> cr, System.Span<byte> rgb) -> void
```

</details>

<details><summary><code>Broadside.Rendering.Android</code>: 12 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Android.AndroidCanvasBackend.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Rendering.Android.AndroidImageDecoder
+ Broadside.Rendering.Android.AndroidImageDecoder.AndroidImageDecoder() -> void
+ Broadside.Rendering.Android.AndroidImageDecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Rendering.Android.AndroidImageDecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Rendering.Android.AndroidImageDecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Rendering.Android.AndroidImageDecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Rendering.Android.AndroidImageDecoder.Name.get -> string!
+ Broadside.Rendering.Android.AndroidImageDecoderExtensions
+ const Broadside.Rendering.Android.AndroidImageDecoder.DecoderName = "android" -> string!
+ static Broadside.Rendering.Android.AndroidImageDecoder.Shared.get -> Broadside.Rendering.Android.AndroidImageDecoder!
+ static Broadside.Rendering.Android.AndroidImageDecoderExtensions.UseAndroidImageDecoder(this Broadside.PdfOptions! options) -> Broadside.PdfOptions!
```

</details>

<details><summary><code>Broadside.Rendering.CoreGraphics</code>: 9 added, 0 removed</summary>

```diff
+ Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Rendering.CoreGraphics.ImageIODecoder
+ Broadside.Rendering.CoreGraphics.ImageIODecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Rendering.CoreGraphics.ImageIODecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Rendering.CoreGraphics.ImageIODecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Rendering.CoreGraphics.ImageIODecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Rendering.CoreGraphics.ImageIODecoder.Name.get -> string!
+ const Broadside.Rendering.CoreGraphics.ImageIODecoder.DecoderName = "imageio" -> string!
+ static Broadside.Rendering.CoreGraphics.ImageIODecoder.Shared.get -> Broadside.Rendering.CoreGraphics.ImageIODecoder!
```

</details>

<details><summary><code>Broadside.Rendering.Direct2D</code>: 13 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Direct2D.Direct2DRenderer.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Rendering.Direct2D.WicImageDecoder
+ Broadside.Rendering.Direct2D.WicImageDecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Rendering.Direct2D.WicImageDecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Rendering.Direct2D.WicImageDecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Rendering.Direct2D.WicImageDecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Rendering.Direct2D.WicImageDecoder.Name.get -> string!
+ Broadside.Rendering.Direct2D.WicImageDecoder.WicImageDecoder() -> void
+ Broadside.Rendering.Direct2D.WicImageDecoderExtensions
+ const Broadside.Rendering.Direct2D.WicImageDecoder.DecoderName = "wic" -> string!
+ static Broadside.Rendering.Direct2D.WicImageDecoder.IsSupported.get -> bool
+ static Broadside.Rendering.Direct2D.WicImageDecoder.Shared.get -> Broadside.Rendering.Direct2D.WicImageDecoder!
+ static Broadside.Rendering.Direct2D.WicImageDecoderExtensions.UseWicImageDecoder(this Broadside.PdfOptions! options) -> Broadside.PdfOptions!
```

</details>

<details><summary><code>Broadside.Rendering.Skia</code>: 10 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Skia.SkiaImageDecoder
+ Broadside.Rendering.Skia.SkiaImageDecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Rendering.Skia.SkiaImageDecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Rendering.Skia.SkiaImageDecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Rendering.Skia.SkiaImageDecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Rendering.Skia.SkiaImageDecoder.Name.get -> string!
+ Broadside.Rendering.Skia.SkiaImageDecoderExtensions
+ const Broadside.Rendering.Skia.SkiaImageDecoder.DecoderName = "skia" -> string!
+ static Broadside.Rendering.Skia.SkiaImageDecoder.Shared.get -> Broadside.Rendering.Skia.SkiaImageDecoder!
+ static Broadside.Rendering.Skia.SkiaImageDecoderExtensions.UseSkiaImageDecoder(this Broadside.PdfOptions! options) -> Broadside.PdfOptions!
```

</details>

## 0.1.0-preview.5 (2026-10-28)

Changes since [`v0.1.0-preview.4`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.4). Full diff: [`v0.1.0-preview.4...v0.1.0-preview.5`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.4...v0.1.0-preview.5).

### Features

- **android:** decode images at the resolution they are drawn ([#428](https://github.com/michaelstonis/Broadside/issues/428) part 4) ([#440](https://github.com/michaelstonis/Broadside/issues/440))
- **coregraphics:** decode images at the resolution they are drawn ([#428](https://github.com/michaelstonis/Broadside/issues/428)) ([#439](https://github.com/michaelstonis/Broadside/issues/439))
- **direct2d:** decode images at the resolution they are drawn ([#428](https://github.com/michaelstonis/Broadside/issues/428) part 5) ([#438](https://github.com/michaelstonis/Broadside/issues/438))

### Fixes

- **webgl:** fall back to a document canvas when OffscreenCanvas has no WebGL 2

### Performance

- **viewer:** pooled tile backends share converted images; bounded resampling scratch ([#432](https://github.com/michaelstonis/Broadside/issues/432))

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.4...v0.1.0-preview.5)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside.Rendering` | 2 | 0 |
| `Broadside.Rendering.Android` | 4 | 0 |
| `Broadside.Rendering.CoreGraphics` | 4 | 0 |
| `Broadside.Rendering.Direct2D` | 4 | 0 |
| `Broadside.Rendering.Skia` | 2 | 0 |

<details><summary><code>Broadside.Rendering</code>: 2 added, 0 removed</summary>

```diff
+ Broadside.Rendering.SoftwareRasterizer.ShareConvertedImages.get -> bool
+ Broadside.Rendering.SoftwareRasterizer.ShareConvertedImages.set -> void
```

</details>

<details><summary><code>Broadside.Rendering.Android</code>: 4 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Android.AndroidCanvasBackend.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.Android.AndroidCanvasBackend.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.Android.AndroidCanvasBackend.ScaledImageDecode.get -> bool
+ Broadside.Rendering.Android.AndroidCanvasBackend.ScaledImageDecode.set -> void
```

</details>

<details><summary><code>Broadside.Rendering.CoreGraphics</code>: 4 added, 0 removed</summary>

```diff
+ Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer.ScaledImageDecode.get -> bool
+ Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer.ScaledImageDecode.set -> void
```

</details>

<details><summary><code>Broadside.Rendering.Direct2D</code>: 4 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Direct2D.Direct2DRenderer.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.Direct2D.Direct2DRenderer.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.Direct2D.Direct2DRenderer.ScaledImageDecode.get -> bool
+ Broadside.Rendering.Direct2D.Direct2DRenderer.ScaledImageDecode.set -> void
```

</details>

<details><summary><code>Broadside.Rendering.Skia</code>: 2 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Skia.SkiaBackend.ShareConvertedImages.get -> bool
+ Broadside.Rendering.Skia.SkiaBackend.ShareConvertedImages.set -> void
```

</details>

## 0.1.0-preview.4 (2026-10-26)

Changes since [`v0.1.0-preview.3`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.3). Full diff: [`v0.1.0-preview.3...v0.1.0-preview.4`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.3...v0.1.0-preview.4).

### Features

- **images:** platform image decoder seam with an asynchronous prefetch ([#372](https://github.com/michaelstonis/Broadside/issues/372))
- **skia:** decode images at the resolution they are drawn in the Skia backend and the MAUI viewer ([#428](https://github.com/michaelstonis/Broadside/issues/428))
- **viewer-blazor:** decode JPEG images with the browser's own decoder ([#373](https://github.com/michaelstonis/Broadside/issues/373))
- **webgl:** upload images at the resolution they are drawn; the render worker follows the page's decode options ([#428](https://github.com/michaelstonis/Broadside/issues/428))

### Fixes

- **document:** hinted remote files find pages without walking the page tree ([#417](https://github.com/michaelstonis/Broadside/issues/417))
- **skia:** a finer converted image serves a coarser request, so zooming out converts nothing again ([#428](https://github.com/michaelstonis/Broadside/issues/428))
- **viewer:** a resize keeps a running page jump going ([#416](https://github.com/michaelstonis/Broadside/issues/416))
- **viewer:** a tile waiting for its images holds no backend ([#372](https://github.com/michaelstonis/Broadside/issues/372))
- **viewer-blazor:** probe DCT-scaled decodes; find the script next to the worker's ([#373](https://github.com/michaelstonis/Broadside/issues/373))
- **viewer-blazor:** calibrate ImageDecoder's subsampled chroma at start-up ([#373](https://github.com/michaelstonis/Broadside/issues/373))

### Performance

- **viewer:** keep the tile job at its size; a resumed tile takes its list again ([#372](https://github.com/michaelstonis/Broadside/issues/372))

### Documentation

- host the Blazor demo at /demo/ on the docs site ([#426](https://github.com/michaelstonis/Broadside/issues/426)) ([#430](https://github.com/michaelstonis/Broadside/issues/430))
- **benchmarks:** Firefox's measured fidelity for the browser image decoder ([#373](https://github.com/michaelstonis/Broadside/issues/373))
- **benchmarks:** MAUI tile time and memory with scaled image decode on the Skia backend ([#428](https://github.com/michaelstonis/Broadside/issues/428))
- **benchmarks:** link the duplicate-conversion follow-up ([#432](https://github.com/michaelstonis/Broadside/issues/432))
- **ci:** give the measured demo publish times for the docs job ([#426](https://github.com/michaelstonis/Broadside/issues/426))

### Tests

- **viewer:** wait for the resumed tiles until idle, not a fixed number of yields ([#372](https://github.com/michaelstonis/Broadside/issues/372))
- **viewer-maui:** keep the resize test on TraceMonkey from the corpus

### Build and CI

- **blazor:** keep PR browser jobs off WebKit from another job's cache ([#421](https://github.com/michaelstonis/Broadside/issues/421))
- **docs:** host the Blazor demo at /demo/ with the docs ([#426](https://github.com/michaelstonis/Broadside/issues/426))

### Chores

- **sample:** make the photo bench report failures and zoom out where a phone reduces the cover ([#428](https://github.com/michaelstonis/Broadside/issues/428))
- **sample:** one photo-bench measurement per launch, at a chosen view width and k ([#428](https://github.com/michaelstonis/Broadside/issues/428))
- **samples:** stop packaging tracemonkey.pdf; load it from pdf.js's copy

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.3...v0.1.0-preview.4)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 56 | 0 |
| `Broadside.Rendering` | 1 | 0 |
| `Broadside.Rendering.Skia` | 4 | 0 |
| `Broadside.Rendering.WebGL` | 5 | 0 |
| `Broadside.Viewer` | 2 | 0 |
| `Broadside.Viewer.Blazor` | 29 | 0 |
| `Broadside.Viewer.Maui` | 2 | 0 |

<details><summary><code>Broadside</code>: 56 added, 0 removed</summary>

```diff
+ Broadside.Images.IPlatformImageDecoder
+ Broadside.Images.IPlatformImageDecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Images.IPlatformImageDecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Images.IPlatformImageDecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Images.IPlatformImageDecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Images.IPlatformImageDecoder.Name.get -> string!
+ Broadside.Images.PlatformImageFeatures
+ Broadside.Images.PlatformImageFeatures.AppliesExifOrientation = 2 -> Broadside.Images.PlatformImageFeatures
+ Broadside.Images.PlatformImageFeatures.ArithmeticCoding = 1 -> Broadside.Images.PlatformImageFeatures
+ Broadside.Images.PlatformImageFeatures.ConvertsEmbeddedProfiles = 4 -> Broadside.Images.PlatformImageFeatures
+ Broadside.Images.PlatformImageFeatures.None = 0 -> Broadside.Images.PlatformImageFeatures
+ Broadside.Images.PlatformImageFidelity
+ Broadside.Images.PlatformImageFidelity.Exact = 0 -> Broadside.Images.PlatformImageFidelity
+ Broadside.Images.PlatformImageFidelity.Tolerance = 1 -> Broadside.Images.PlatformImageFidelity
+ Broadside.Images.PlatformImageRequest
+ Broadside.Images.PlatformImageRequest.ArithmeticCoding.get -> bool
+ Broadside.Images.PlatformImageRequest.ArithmeticCoding.init -> void
+ Broadside.Images.PlatformImageRequest.BitsPerComponent.get -> int
+ Broadside.Images.PlatformImageRequest.BitsPerComponent.init -> void
+ Broadside.Images.PlatformImageRequest.ChromaSubsampled.get -> bool
+ Broadside.Images.PlatformImageRequest.ChromaSubsampled.init -> void
+ Broadside.Images.PlatformImageRequest.ColorTransform.get -> bool
+ Broadside.Images.PlatformImageRequest.ColorTransform.init -> void
+ Broadside.Images.PlatformImageRequest.Components.get -> int
+ Broadside.Images.PlatformImageRequest.Components.init -> void
+ Broadside.Images.PlatformImageRequest.CreateImage(int reduction) -> Broadside.Images.DecodedImageBuilder!
+ Broadside.Images.PlatformImageRequest.Filter.get -> Broadside.Objects.CosName!
+ Broadside.Images.PlatformImageRequest.Filter.init -> void
+ Broadside.Images.PlatformImageRequest.HasExif.get -> bool
+ Broadside.Images.PlatformImageRequest.HasExif.init -> void
+ Broadside.Images.PlatformImageRequest.HasIccProfile.get -> bool
+ Broadside.Images.PlatformImageRequest.HasIccProfile.init -> void
+ Broadside.Images.PlatformImageRequest.Height.get -> int
+ Broadside.Images.PlatformImageRequest.Height.init -> void
+ Broadside.Images.PlatformImageRequest.PlatformImageRequest() -> void
+ Broadside.Images.PlatformImageRequest.Progressive.get -> bool
+ Broadside.Images.PlatformImageRequest.Progressive.init -> void
+ Broadside.Images.PlatformImageRequest.ReducedHeight.get -> int
+ Broadside.Images.PlatformImageRequest.ReducedWidth.get -> int
+ Broadside.Images.PlatformImageRequest.Reduction.get -> int
+ Broadside.Images.PlatformImageRequest.Reduction.init -> void
+ Broadside.Images.PlatformImageRequest.Width.get -> int
+ Broadside.Images.PlatformImageRequest.Width.init -> void
+ Broadside.PdfDocument.ReadAsync<T>(System.Func<Broadside.PdfDocument!, T>! read, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<T>!
+ Broadside.PdfDocument.UsesPlatformImageDecoder(string! name) -> bool
+ Broadside.PdfOptions.PlatformImageDecoding.get -> bool
+ Broadside.PdfOptions.PlatformImageDecoding.set -> void
+ Broadside.PdfOptions.UseManagedImageDecoding() -> Broadside.PdfOptions!
+ Broadside.PdfOptions.UsePlatformImageDecoder(Broadside.Images.IPlatformImageDecoder! decoder) -> Broadside.PdfOptions!
+ Broadside.PdfOptions.VerifyPlatformImageDecoder.get -> bool
+ Broadside.PdfOptions.VerifyPlatformImageDecoder.set -> void
+ Broadside.PdfOptions.WithPlatformImageDecoder(string! name, bool enabled) -> Broadside.PdfOptions!
+ Broadside.PdfOptions.WithPlatformImageDecoding(bool enabled) -> Broadside.PdfOptions!
+ Broadside.PdfOptions.WithPlatformImageVerification(bool verify) -> Broadside.PdfOptions!
+ Broadside.Rendering.DisplayImage.TryGetPlatformData(Broadside.Images.PlatformImageFeatures features, out Broadside.Images.PlatformImageRequest request, out System.ReadOnlyMemory<byte> data) -> bool
+ Broadside.Rendering.DisplayList.PrefetchImagesAsync(in Broadside.Graphics.Matrix deviceTransform, Broadside.PdfRectangle? region, double samplesPerPixel, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
```

</details>

<details><summary><code>Broadside.Rendering</code>: 1 added, 0 removed</summary>

```diff
+ Broadside.Rendering.SoftwareRasterizer.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
```

</details>

<details><summary><code>Broadside.Rendering.Skia</code>: 4 added, 0 removed</summary>

```diff
+ Broadside.Rendering.Skia.SkiaBackend.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.Skia.SkiaBackend.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.Skia.SkiaBackend.ScaledImageDecode.get -> bool
+ Broadside.Rendering.Skia.SkiaBackend.ScaledImageDecode.set -> void
```

</details>

<details><summary><code>Broadside.Rendering.WebGL</code>: 5 added, 0 removed</summary>

```diff
+ Broadside.Rendering.WebGL.WebGLRenderer.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.WebGL.WebGLRenderer.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.WebGL.WebGLRenderer.ScaledImageDecode.get -> bool
+ Broadside.Rendering.WebGL.WebGLRenderer.ScaledImageDecode.set -> void
+ static Broadside.Rendering.WebGL.WebGLRenderer.PrefetchImageBitmapsAsync(Broadside.Rendering.DisplayList! list, Broadside.Graphics.Matrix deviceTransform, Broadside.PdfRectangle? region, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task!
```

</details>

<details><summary><code>Broadside.Viewer</code>: 2 added, 0 removed</summary>

```diff
+ Broadside.Viewer.ITileBackend.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Viewer.SoftwareTileBackend.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
```

</details>

<details><summary><code>Broadside.Viewer.Blazor</code>: 29 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Blazor.BrowserImageDecoder
+ Broadside.Viewer.Blazor.BrowserImageDecoder.BrowserImageDecoder(Broadside.Viewer.Blazor.BrowserImageDecoderOptions? options = null) -> void
+ Broadside.Viewer.Blazor.BrowserImageDecoder.CanDecode(in Broadside.Images.PlatformImageRequest request) -> bool
+ Broadside.Viewer.Blazor.BrowserImageDecoder.DecodeAsync(System.ReadOnlyMemory<byte> encoded, Broadside.Images.PlatformImageRequest request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Broadside.Images.DecodedImage?>
+ Broadside.Viewer.Blazor.BrowserImageDecoder.Description.get -> string!
+ Broadside.Viewer.Blazor.BrowserImageDecoder.Features.get -> Broadside.Images.PlatformImageFeatures
+ Broadside.Viewer.Blazor.BrowserImageDecoder.Fidelity.get -> Broadside.Images.PlatformImageFidelity
+ Broadside.Viewer.Blazor.BrowserImageDecoder.IsAvailable.get -> bool
+ Broadside.Viewer.Blazor.BrowserImageDecoder.Name.get -> string!
+ Broadside.Viewer.Blazor.BrowserImageDecoderExtensions
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.BrowserImageDecoderOptions() -> void
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseImageBitmapInWorker.get -> bool
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseImageBitmapInWorker.set -> void
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseImageBitmapOnPage.get -> bool
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseImageBitmapOnPage.set -> void
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseWebCodecs.get -> bool
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.UseWebCodecs.set -> void
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.WithImageBitmapInWorker(bool enabled) -> Broadside.Viewer.Blazor.BrowserImageDecoderOptions!
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.WithImageBitmapOnPage(bool enabled) -> Broadside.Viewer.Blazor.BrowserImageDecoderOptions!
+ Broadside.Viewer.Blazor.BrowserImageDecoderOptions.WithWebCodecs(bool enabled) -> Broadside.Viewer.Blazor.BrowserImageDecoderOptions!
+ Broadside.Viewer.Blazor.WebGLTileBackend.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Viewer.Blazor.WebGLTileBackend.WebGLTileBackend(bool scaledImageDecode, double imageSamplesPerPixel) -> void
+ const Broadside.Viewer.Blazor.BrowserImageDecoder.DecoderName = "browser" -> string!
+ static Broadside.Viewer.Blazor.BrowserImageDecoder.InitializeAsync() -> System.Threading.Tasks.Task!
+ static Broadside.Viewer.Blazor.BrowserImageDecoder.Shared.get -> Broadside.Viewer.Blazor.BrowserImageDecoder!
+ static Broadside.Viewer.Blazor.BrowserImageDecoderExtensions.UseBrowserImageDecoder(this Broadside.PdfOptions! options, Broadside.Viewer.Blazor.BrowserImageDecoderOptions? decoderOptions = null) -> Broadside.PdfOptions!
+ static Broadside.Viewer.Blazor.RenderWorker.Open(System.ReadOnlySpan<byte> document, Broadside.Viewer.ViewerOptions! options) -> Broadside.Viewer.IRemoteTileRenderer?
+ static Broadside.Viewer.Blazor.RenderWorker.Open(System.Uri! address, Broadside.Viewer.ViewerOptions! options) -> Broadside.Viewer.IRemoteTileRenderer?
```

</details>

<details><summary><code>Broadside.Viewer.Maui</code>: 2 added, 0 removed</summary>

```diff
+ Broadside.Viewer.Maui.SkiaTileBackend.PrefetchImagesAsync(Broadside.Rendering.DisplayList! list, in Broadside.Graphics.Matrix deviceTransform, int width, int height, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask
+ Broadside.Viewer.Maui.SkiaTileBackend.SkiaTileBackend(bool scaledImageDecode, double imageSamplesPerPixel) -> void
```

</details>

## 0.1.0-preview.3 (2026-10-25)

Changes since [`v0.1.0-preview.2`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.2). Full diff: [`v0.1.0-preview.2...v0.1.0-preview.3`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.2...v0.1.0-preview.3).

### Features

- **filters:** decode DCT images at 1/2, 1/4 and 1/8 ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **filters:** drop JPEG 2000 resolution levels for reduced decodes ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **rendering:** decode images when drawn, at the reduction the device needs ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **rendering:** reduce Flate and raw images by rows when drawn small ([#362](https://github.com/michaelstonis/Broadside/issues/362))

### Fixes

- **ci:** keep the Android emulator job alive through adb offline drops ([#344](https://github.com/michaelstonis/Broadside/issues/344))
- **document:** find a hinted page's index without the page tree ([#410](https://github.com/michaelstonis/Broadside/issues/410))

### Performance

- **filters:** read JPX reduction levels from the main header without allocating ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **images:** keep a sole image filter's data without copying it ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **rendering:** keep the reduction seam free of allocations on unscaled paths ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **rendering:** keep the display image samples and mask source at their size ([#362](https://github.com/michaelstonis/Broadside/issues/362))
- **rendering:** keep image reduction state and the image source at their earlier size ([#362](https://github.com/michaelstonis/Broadside/issues/362))

### Chores

- **bench:** record the gate baseline for [#362](https://github.com/michaelstonis/Broadside/issues/362) part 1
- **bench:** record the gate baseline for [#362](https://github.com/michaelstonis/Broadside/issues/362) part 2
- **bench:** record the gate baseline for [#362](https://github.com/michaelstonis/Broadside/issues/362) part 3
- **bench:** record the gate baseline for [#362](https://github.com/michaelstonis/Broadside/issues/362) part 4

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.2...v0.1.0-preview.3)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside` | 31 | 0 |
| `Broadside.Rendering` | 11 | 0 |
| `Broadside.Viewer` | 7 | 0 |

<details><summary><code>Broadside</code>: 31 added, 0 removed</summary>

```diff
+ Broadside.Filters.ImageFilterContext.Reduction.get -> int
+ Broadside.Filters.ImageFilterContext.Reduction.init -> void
+ Broadside.Filters.ImageHeader.MaxReduction.get -> int
+ Broadside.Filters.ImageHeader.MaxReduction.init -> void
+ Broadside.Images.DecodedImage.Reduction.get -> int
+ Broadside.Images.DecodedImageBuilder.Reduction.get -> int
+ Broadside.Images.DecodedImageBuilder.Reduction.set -> void
+ Broadside.Rendering.DisplayImage.DecodeMapFor(Broadside.Rendering.DisplayImageSamples samples) -> Broadside.Images.ImageDecodeMap!
+ Broadside.Rendering.DisplayImage.GetReduction(in Broadside.Graphics.Matrix transform, double samplesPerPixel, Broadside.Graphics.PdfColorConverter? converter) -> int
+ Broadside.Rendering.DisplayImage.GetSamplesAt(int reduction) -> Broadside.Rendering.DisplayImageSamples?
+ Broadside.Rendering.DisplayImage.GetSamplesAt(int reduction, System.Threading.CancellationToken cancellationToken) -> Broadside.Rendering.DisplayImageSamples?
+ Broadside.Rendering.DisplayImage.MaxReduction.get -> int
+ Broadside.Rendering.DisplayImageMask.GetReduction(in Broadside.Graphics.Matrix transform, double samplesPerPixel) -> int
+ Broadside.Rendering.DisplayImageSamples
+ Broadside.Rendering.DisplayImageSamples.BitsPerComponent.get -> int
+ Broadside.Rendering.DisplayImageSamples.Components.get -> int
+ Broadside.Rendering.DisplayImageSamples.DisplayImageSamples() -> void
+ Broadside.Rendering.DisplayImageSamples.Equals(Broadside.Rendering.DisplayImageSamples other) -> bool
+ Broadside.Rendering.DisplayImageSamples.GetRow(int y) -> System.ReadOnlySpan<byte>
+ Broadside.Rendering.DisplayImageSamples.Height.get -> int
+ Broadside.Rendering.DisplayImageSamples.Reduction.get -> int
+ Broadside.Rendering.DisplayImageSamples.Samples.get -> System.ReadOnlySpan<byte>
+ Broadside.Rendering.DisplayImageSamples.SourceHeight.get -> int
+ Broadside.Rendering.DisplayImageSamples.SourceWidth.get -> int
+ Broadside.Rendering.DisplayImageSamples.StorageBits.get -> int
+ Broadside.Rendering.DisplayImageSamples.Stride.get -> int
+ Broadside.Rendering.DisplayImageSamples.Width.get -> int
+ override Broadside.Rendering.DisplayImageSamples.Equals(object? obj) -> bool
+ override Broadside.Rendering.DisplayImageSamples.GetHashCode() -> int
+ static Broadside.Rendering.DisplayImageSamples.operator !=(Broadside.Rendering.DisplayImageSamples left, Broadside.Rendering.DisplayImageSamples right) -> bool
+ static Broadside.Rendering.DisplayImageSamples.operator ==(Broadside.Rendering.DisplayImageSamples left, Broadside.Rendering.DisplayImageSamples right) -> bool
```

</details>

<details><summary><code>Broadside.Rendering</code>: 11 added, 0 removed</summary>

```diff
+ Broadside.Rendering.RenderOptions.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.RenderOptions.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.RenderOptions.ScaledImageDecode.get -> bool
+ Broadside.Rendering.RenderOptions.ScaledImageDecode.set -> void
+ Broadside.Rendering.RenderOptions.WithImageSamplesPerPixel(double samplesPerPixel) -> Broadside.Rendering.RenderOptions!
+ Broadside.Rendering.RenderOptions.WithScaledImageDecode(bool scaledImageDecode) -> Broadside.Rendering.RenderOptions!
+ Broadside.Rendering.SoftwareRasterizer.ImageSamplesPerPixel.get -> double
+ Broadside.Rendering.SoftwareRasterizer.ImageSamplesPerPixel.set -> void
+ Broadside.Rendering.SoftwareRasterizer.ScaledImageDecode.get -> bool
+ Broadside.Rendering.SoftwareRasterizer.ScaledImageDecode.set -> void
+ const Broadside.Rendering.RenderOptions.DefaultImageSamplesPerPixel = 2 -> double
```

</details>

<details><summary><code>Broadside.Viewer</code>: 7 added, 0 removed</summary>

```diff
+ Broadside.Viewer.SoftwareTileBackend.SoftwareTileBackend(bool scaledImageDecode, double imageSamplesPerPixel) -> void
+ Broadside.Viewer.ViewerOptions.ImageSamplesPerPixel.get -> double
+ Broadside.Viewer.ViewerOptions.ImageSamplesPerPixel.set -> void
+ Broadside.Viewer.ViewerOptions.ScaledImageDecode.get -> bool
+ Broadside.Viewer.ViewerOptions.ScaledImageDecode.set -> void
+ Broadside.Viewer.ViewerOptions.WithImageSamplesPerPixel(double samplesPerPixel) -> Broadside.Viewer.ViewerOptions!
+ Broadside.Viewer.ViewerOptions.WithScaledImageDecode(bool scaledImageDecode) -> Broadside.Viewer.ViewerOptions!
```

</details>

## 0.1.0-preview.2 (2026-10-23)

Changes since [`v0.1.0-preview.1`](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.1). Full diff: [`v0.1.0-preview.1...v0.1.0-preview.2`](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.1...v0.1.0-preview.2).

### Fixes

- **viewer:** read WebGL tiles back for pixel surfaces and drop the hold before the bitmap goes ([#380](https://github.com/michaelstonis/Broadside/issues/380))
- **writing:** bound save memory by the objects written, not the highest object number ([#371](https://github.com/michaelstonis/Broadside/issues/371))
- **writing:** keep dangling references null and undo a refused save's changes ([#371](https://github.com/michaelstonis/Broadside/issues/371))
- **writing:** write a kept encryption dictionary direct ([#390](https://github.com/michaelstonis/Broadside/issues/390))
- **writing:** write a kept encryption dictionary as a new direct object ([#390](https://github.com/michaelstonis/Broadside/issues/390))

### Documentation

- README and docs reflect v0.1.0-preview.1 (docs only) ([#395](https://github.com/michaelstonis/Broadside/issues/395))
- drop ADR references from user-facing text and enforce it ([#396](https://github.com/michaelstonis/Broadside/issues/396))
- fix punctuation left by removing ADR citations ([#400](https://github.com/michaelstonis/Broadside/issues/400))
- bring the README and docs up to the project's current state
- install Broadside.Layout for the example, and size the Blazor viewer example
- **readme:** a plain blockquote for the preview notice, which NuGet.org renders too
- **site:** API reference for every package ([#403](https://github.com/michaelstonis/Broadside/issues/403))

### Benchmarks

- record the save's change log in the allocation baseline ([#371](https://github.com/michaelstonis/Broadside/issues/371))

### Build and CI

- **bench:** check allocations on every PR, compare times on hot paths, perf, main and nightly ([#389](https://github.com/michaelstonis/Broadside/issues/389))
- **bench:** make the gate's plan a tested script and fail the gate when plan fails ([#389](https://github.com/michaelstonis/Broadside/issues/389))
- **release:** pack the MAUI viewer's Windows target ([#381](https://github.com/michaelstonis/Broadside/issues/381))

### Public API

Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` ([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0-preview.1...v0.1.0-preview.2)).

| Package | Added | Removed |
|---|---:|---:|
| `Broadside.Viewer` | 2 | 0 |

<details><summary><code>Broadside.Viewer</code>: 2 added, 0 removed</summary>

```diff
+ Broadside.Viewer.IRenderSurface.AcceptsDevicePixels.get -> bool
+ Broadside.Viewer.ITileRender.ReadBack() -> void
```

</details>

## 0.1.0-preview.1 (2026-10-21)

The first public preview: reading, rendering, writing and editing, and the Blazor viewer path. Its notes are the [GitHub release](https://github.com/michaelstonis/Broadside/releases/tag/v0.1.0-preview.1); the changelog starts with the next preview.
