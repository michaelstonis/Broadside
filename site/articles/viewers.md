---
title: "Viewers"
---

# Viewers

Broadside ships a PDF viewer control for six UI frameworks. They share one engine, `Broadside.Viewer`, which renders pages as tiles off the UI thread. Every viewer scrolls and zooms smoothly, and supports text selection and copy, search, links, form filling, dark mode and screen readers. Each shell is a package of its own, published with the library under the same version.

| Framework | Package | Control | Platforms |
|---|---|---|---|
| Blazor WebAssembly | `Broadside.Viewer.Blazor` | `PdfViewer` | any modern browser, phones included |
| .NET MAUI | `Broadside.Viewer.Maui` | `PdfView` | Android, iOS, Mac Catalyst, Windows |
| Avalonia | `Broadside.Viewer.Avalonia` | `PdfView` | Windows, macOS, Linux |
| Uno Platform | `Broadside.Viewer.Uno` | `PdfView` | Windows, macOS, Linux, browser |
| WinUI 3 | `Broadside.Viewer.WinUI` | `PdfView` | Windows |
| WPF | `Broadside.Viewer.Wpf` | `PdfView` | Windows |

A viewer shows a document from a path (or an http(s) address) in `Source`, or a `DocumentSession` your app opens and owns in `Session`. A session opened with `DocumentSession.OpenAsync(HttpClient, Uri)` reads the file through HTTP range requests, through the client you give it. `ViewerOptions` carries the `PdfOptions` documents open with: the snippets below add the bundled Standard 14 fonts, so files that use fonts without embedding them render the same on every platform.

The snippets are taken from the sample app for each shell under [`samples/`](https://github.com/michaelstonis/Broadside/tree/main/samples). The apps need their platform's workloads, so unlike the other guides' samples they are not run as console programs; CI builds each one in its own job.

## Blazor WebAssembly

The `PdfViewer` component shows a session. The [live demo](/demo/) is the [Blazor sample](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Blazor.Sample/README.md):

```csharp
public static readonly ViewerOptions Options = new ViewerOptions()
    .UseEngine(new PdfEngine(new PdfOptions().UseStandard14Fonts().UseBrowserImageDecoder()));

DocumentSession session = await DocumentSession.OpenAsync(http, uri, Options, token);
```

```razor
<PdfViewer Session="Demo.Session" Label="@Demo.Name" OnStateChanged="OnStateChanged" OnError="OnError" />
```

The software rasterizer draws the tiles by default. WebGL 2 is opt-in, and so is a render worker that draws content tiles in a JavaScript Web Worker; the app starts the worker explicitly, and nothing uses .NET WebAssembly threads. `UseBrowserImageDecoder()` decodes JPEG images with the browser's decoder.

## .NET MAUI

Register the viewer when the app is built, then add a `PdfView`. It draws with SkiaSharp and composes tiles on the GPU (Metal on iOS and Mac Catalyst). From the [MAUI sample](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Maui.Sample/README.md):

```csharp
MauiAppBuilder builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>().UseBroadsideViewer();
```

```csharp
private readonly PdfView _view = new() { ShowAnnotations = true, AutomationId = "viewer" };

_view.Session = _document.Session;
```

JPEG images decode with SkiaSharp's decoder (libjpeg-turbo, bit-identical to the managed decoder); `PdfOptions.WithPlatformImageDecoder("skia", false)` in the view's `Options.DocumentOptions` turns it off, and `PdfOptions.UseSkiaImageDecoder()` turns it on for a session the app opens itself.

## Avalonia

From the [Avalonia sample](https://github.com/michaelstonis/Broadside/tree/main/samples/Broadside.Viewer.Avalonia.Sample):

```csharp
private readonly PdfView _viewer = new();

_viewer.Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
_viewer.Source = path;
```

`UsePredefinedCMaps()` comes from the `Broadside.Fonts.Cmaps` package, for Chinese, Japanese and Korean text in files that name a predefined CMap.

## Uno Platform

From the [Uno sample](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Uno.Sample/README.md), which opens its own session:

```csharp
Viewer = new PdfView();

_session = DocumentSession.Open(copy.ToArray(), Options());
Viewer.Session = _session;

public static ViewerOptions Options() => new ViewerOptions()
    .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
```

The app owns a session it opens: dispose the previous one when you replace it.

## WinUI 3

From the [WinUI sample](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.WinUI.Sample/README.md):

```csharp
private readonly PdfView _view = new()
{
    Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts()),
};

_view.Source = path;
```

## WPF

From the [WPF sample](https://github.com/michaelstonis/Broadside/tree/main/samples/Broadside.Viewer.Wpf.Sample):

```csharp
private readonly PdfView _view = new();

_view.Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
_view.Source = file;
```

## Toolbar

A view shows pages and nothing else. Every shell also ships a `PdfToolbar` control (a `PdfToolbar` component in Blazor) that you bind to the view: previous and next page, a page box that takes a number or a page label, zoom out and in, a zoom picker (fit width, fit page, actual size and presets), the page layout, rotation, find and dark mode. A view without a toolbar pays nothing for it.

The commands live once in `Broadside.Viewer`, in the `ViewerToolbar` model, and drive the view through `IDocumentViewer`, which every view implements. Each shell only lays the commands out with its own controls: a WPF `ToolBar`, a WinUI or Uno `CommandBar`, Avalonia and MAUI buttons and pickers, HTML in Blazor. A document whose viewer preferences ask to hide the tool bars (`HideToolbar`) starts with the toolbar hidden, unless the options say otherwise.

```xml
<!-- WPF; the other XAML shells look the same -->
<viewer:PdfView x:Name="View" />
<viewer:PdfToolbar Viewer="{Binding ElementName=View}" />
```

```razor
<PdfViewer @ref="_viewer" Session="Demo.Session" />
<PdfToolbar Viewer="_viewer" Options="_toolbarOptions" />
```

`ViewerToolbarOptions` picks the built-in commands and their order (`ViewerCommandIds`), the zoom presets, and a compact layout for phones that keeps the page commands and find in the bar and moves the rest to an overflow menu. The model is the control's `Toolbar` property, where an app adds its own commands, removes or hides built-in ones, or moves them to the overflow menu:

```csharp
toolbar.Options = new ViewerToolbarOptions()
    .WithItems(ViewerCommandIds.PagePrevious, ViewerCommandIds.PageNumber, ViewerCommandIds.PageNext, ViewerCommandIds.ZoomMode, ViewerCommandIds.Search)
    .WithZoomPresets(0.5, 1, 2)
    .WithCompact(isPhone);
toolbar.Viewer = view;

ViewerToolbar model = toolbar.Toolbar!;
model.Insert(model.Items.Count, new ViewerButtonCommand("app.share", "Share", Share, () => view.CurrentSession is not null) { Glyph = "⇪" });
model.Find(ViewerCommandIds.Search)!.Placement = ViewerCommandPlacement.Overflow;
```

To restyle items without forking the control, give it a template: `ItemTemplate` in Avalonia, MAUI and Blazor, `ItemTemplateSelector` in WPF, WinUI and Uno. The template's data is the command; commands raise `PropertyChanged`, and a button command is an `ICommand`, so XAML bindings work. In Blazor, a template falls back to the default markup with `<PdfToolbarItem Command="context" />`. Typed input that names no page leaves the view where it is and sets the page command's `Error`; nothing throws.

## Side panels

Every shell also ships the side panels a reader expects, bound to the view the same way as the toolbar: `PdfThumbnailsPanel` (page thumbnails with their page labels), `PdfOutlinePanel` (the bookmarks as a tree that expands and collapses), `PdfSearchPanel` (a query box and the matches grouped by page, with the text around each), `PdfAttachmentsPanel` (embedded files and file attachment annotations, with size, type and checksum status), `PdfLayersPanel` (the optional content groups as the file's own tree, with a check box each) and `PdfAnnotationsPanel` (every comment, page by page, with its author and reply count). `PdfSidebar` puts all six behind one row of tabs. A click in a panel moves the view: a thumbnail shows its page, an outline entry its destination, a result its match, a comment its annotation.

```xml
<viewer:PdfSidebar Viewer="{Binding ElementName=View}" AttachmentOpening="OnAttachment" />
```

```razor
<PdfViewer @ref="_viewer" Source="report.pdf" />
<PdfSidebar Viewer="_viewer" AttachmentOpening="OnAttachmentAsync" />
```

The logic lives once in `Broadside.Viewer`, framework-free: `ViewerSidebar` follows an `IDocumentViewer` and reads the panel models of the open document, `DocumentOutline`, `SearchResults`, `DocumentAttachments`, `DocumentLayers` and `AnnotationList`, again for each document; each shell only lays them out. A panel never throws: a model that cannot be read shows why, and a broken file specification, a missing embedded stream or a bad checksum still lists the file with its status.

The viewer never opens or launches an attachment. Open and Save raise `AttachmentOpening` (`Opening` on the attachments panel) with the decoded bytes, and the app decides what to do with them, such as a save dialog or a download.

Showing or hiding a layer sets the session's `OptionalContentState`: the display lists are built again under the new state, every tile is drawn again, and annotations whose layer is hidden stop taking part. Locked layers cannot be switched, a radio-button set keeps one layer on, and Reset returns to the file's own states. The same property is there for an app that switches layers itself:

```csharp
PdfOptionalContentProperties layers = session.Document.OptionalContent!;
session.OptionalContentState = layers.GetDefaultStates().With(layers.Groups[0], false);
```

Each control's full API (state and error events, layout options, selection, search and the rest) is in the [API reference](../api/readme.md).
