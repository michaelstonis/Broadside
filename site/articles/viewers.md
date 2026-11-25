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

Each control's full API (state and error events, layout options, selection, search and the rest) is in the [API reference](../api/readme.md).
