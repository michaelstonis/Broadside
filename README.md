# Broadside

A PDF library for .NET, written entirely in C#. Read, create, edit, sign and render PDF files, and show them in your app with a ready-made viewer.

[![NuGet](https://img.shields.io/nuget/vpre/Broadside.svg?label=NuGet)](https://www.nuget.org/packages/Broadside)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**[Try the live demo](https://michaelstonis.github.io/Broadside/demo/)**. It runs in your browser, on a phone too. Open a PDF, search it, fill in its form, add notes, or create a new document.

> [!NOTE]
> Broadside is at its 1.0 release candidate; 1.0.0 follows the soak period. The API is frozen for 1.0: only fixes land between now and 1.0.0. Every package, the viewers included, ships as 1.0.0.

## Why Broadside

- **One dependency.** Reading, writing, editing, rendering, text extraction, forms and signatures are all in one place.
- **No native code.** Everything is C#, so it runs anywhere .NET runs, including the browser, and trims well for small apps.
- **Opens broken files.** Damaged PDFs are repaired when they're opened, and you get a list of what was fixed.
- **Follows the standard.** Built on PDF 2.0 (ISO 32000-2), and it reads older PDF versions too.
- **Free.** MIT licensed, for commercial use as well.

## What it can do

| | |
|---|---|
| **Read** | Open files from disk, memory or a URL (only the parts needed are downloaded). Encrypted files, password-protected files, and files that are damaged or incomplete. |
| **Create** | Draw text, shapes and images on a page, or build whole documents (headings, paragraphs, tables, images, headers and footers, a table of contents) from a few lines of C#. Fonts are embedded and subset automatically. |
| **Edit** | Add, remove and reorder pages, merge and split files, fill in and flatten forms, add annotations, and encrypt on save. |
| **Render** | Turn pages into images, with a built-in renderer or through SkiaSharp, Direct2D, CoreGraphics, Android or WebGL. |
| **Extract** | Get the text of a page in reading order, with words, lines and their positions. |
| **Sign** | Sign documents and check existing signatures. |
| **Accessibility and archiving** | Create accessible (PDF/UA) and archival (PDF/A) documents, and add tags to untagged files. |
| **Images and fonts** | JPEG, JPEG 2000, JBIG2, CCITT fax and the other image formats PDF uses. TrueType, OpenType, Type 1 and Type 3 fonts. |

Broadside reads JavaScript, 3D content, video and XFA forms and keeps them when it saves the file, but doesn't run or display them.

## Install

Broadside needs .NET 10. Until 1.0.0 is out the packages are release candidates, so add `--prerelease`:

```sh
dotnet add package Broadside --prerelease                    # read, write and edit
dotnet add package Broadside.Rendering --prerelease          # render pages to images
dotnet add package Broadside.Fonts.Standard14 --prerelease   # the 14 standard PDF fonts (Helvetica, Times, Courier...)
dotnet add package Broadside.Layout --prerelease             # build documents from paragraphs and tables
```

## Quick start

Create a PDF, read it back, and save page 1 as an image:

```csharp
using Broadside;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Rendering;

// Write: a new document with one page of text.
using (PdfDocument created = PdfDocument.Create())
{
    PdfPage page = created.Pages[0]; // Create() starts with one empty US Letter page
    PdfCanvasFont helvetica = created.GetStandard14Font(Standard14Font.Helvetica);
    using (PdfCanvas canvas = page.CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(helvetica, 24);
        canvas.MoveText(72, 700);
        canvas.ShowText("Hello from Broadside");
        canvas.EndText();
    }

    created.Save("hello.pdf");
}

// Read: open a file (lenient by default), list what was repaired, extract text.
PdfOptions options = new PdfOptions().UseStandard14Fonts();
using PdfDocument document = PdfDocument.Open("hello.pdf", options);
foreach (Broadside.Diagnostics.Diagnostic diagnostic in document.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

Console.WriteLine($"{document.Pages.Count} page(s)");
Console.WriteLine(document.Pages[0].ExtractText().Text);

// Render: page 1 to a PNG with the software rasterizer.
using var rasterizer = new SoftwareRasterizer();
using (Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(150)))
using (FileStream png = File.Create("page-1.png"))
{
    bitmap.SavePng(png);
}

```

### Build a document

The document builder lays out content for you and produces an accessible, tagged PDF:

```csharp
using Broadside;
using Broadside.Layout.Flow;

using PdfDocument pdf = PdfDocument.Create(doc => doc
    .WithTitle("Hello")
    .WithLanguage("en-US")
    .Content(c =>
    {
        c.Heading(1, "Hello, Broadside");
        c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
    }));

pdf.Save("hello.pdf");
```

Tables, images, columns, links, form fields and page numbers work the same way. The builder is still experimental, so turn off its warning with `<NoWarn>$(NoWarn);BROADSIDE0001</NoWarn>`. The [building documents guide](https://michaelstonis.github.io/Broadside/articles/building-documents/) walks through more examples, and [`samples/Broadside.BuilderSamples`](samples/Broadside.BuilderSamples) has an invoice, a report and a form you can run.

## Viewer controls

Drop a PDF viewer into your app. Every viewer scrolls and zooms smoothly, and supports text selection and copy, search, links, form filling, dark mode and screen readers.

| Framework | Package | Runs on |
|---|---|---|
| Blazor WebAssembly | `Broadside.Viewer.Blazor` | Any modern browser, phones included |
| .NET MAUI | `Broadside.Viewer.Maui` | Android, iOS, Mac Catalyst, Windows |
| WPF | `Broadside.Viewer.Wpf` | Windows |
| WinUI 3 | `Broadside.Viewer.WinUI` | Windows |
| Avalonia | `Broadside.Viewer.Avalonia` | Windows, macOS, Linux |
| Uno Platform | `Broadside.Viewer.Uno` | Windows, macOS, Linux, browser |

Blazor:

```razor
@using Broadside.Viewer.Blazor

<PdfViewer Source="@(new Uri("sample.pdf", UriKind.Relative))" Label="Sample document" />
```

Avalonia (WPF, WinUI and Uno work the same way):

```xml
<bs:PdfView xmlns:bs="using:Broadside.Viewer.Avalonia" Source="sample.pdf" />
```

For MAUI, call `builder.UseBroadsideViewer()` in `MauiProgram`, then add a `PdfView` and set its `Source` to a file path or URL.

Each viewer has a sample app in [`samples/`](samples).

## Platforms

| Platform | Read, write, edit, render | Native renderer | Viewer |
|---|---|---|---|
| Windows | Yes | Direct2D, SkiaSharp | WPF, WinUI, MAUI, Avalonia, Uno |
| macOS | Yes | CoreGraphics, SkiaSharp | Avalonia, Uno |
| Linux | Yes | SkiaSharp | Avalonia, Uno |
| iOS and Mac Catalyst | Yes | CoreGraphics, SkiaSharp | MAUI |
| Android | Yes | Android Canvas, SkiaSharp | MAUI |
| Browser (WebAssembly) | Yes | WebGL (optional) | Blazor, Uno |

The built-in renderer draws the same pixels on every platform, so a page looks identical everywhere. Windows, macOS, Linux, Android and the browser are tested automatically; iOS and Mac Catalyst are build-checked.

## Packages

| Package | What it's for |
|---|---|
| `Broadside` | Reading, writing, editing, text, forms, signatures |
| `Broadside.Rendering` | The built-in renderer and PNG output |
| `Broadside.Rendering.Skia` | Render with SkiaSharp |
| `Broadside.Rendering.Direct2D` | Render with Direct2D on Windows |
| `Broadside.Rendering.CoreGraphics` | Render with CoreGraphics on macOS, iOS and Mac Catalyst |
| `Broadside.Rendering.Android` | Render with Android Canvas |
| `Broadside.Rendering.WebGL` | Render with WebGL in the browser |
| `Broadside.Fonts.Standard14` | The 14 standard PDF fonts, for files that use them without embedding them |
| `Broadside.Fonts.Cmaps` | Character maps for Chinese, Japanese and Korean text |
| `Broadside.Layout` | The document builder |
| `Broadside.Viewer` | The shared engine behind every viewer control |
| `Broadside.Viewer.*` | The viewer controls listed above |

## Roadmap

**Done.** Reading, writing, editing, rendering, text extraction, forms, signatures, PDF/A and PDF/UA output, the document builder, and all six viewer controls.

**1.0 release candidate:** the API is frozen, every part of the library has a guide, the security review is done, and the [performance comparison](https://michaelstonis.github.io/Broadside/articles/performance/) with PDFium and pdf.js is published. 1.0.0 follows the soak period ([general-availability criteria](docs/release/ga-criteria.md)).

**After 1.0:** migration guides from PdfSharp, iText and PdfPig, text shaping for more scripts (Indic, Thai and others), more form features, more signature types, and the less common parts of the PDF standard.

## Documentation

- [Documentation site and API reference](https://michaelstonis.github.io/Broadside/)
- [Live demo](https://michaelstonis.github.io/Broadside/demo/)
- [Known limitations](docs/known-limitations.md)
- [Release notes](https://github.com/michaelstonis/Broadside/releases)

## Building from source

You need the .NET SDK version listed in `global.json`.

```sh
dotnet build Broadside.Core.slnf   # the main libraries; no extra workloads needed
dotnet test Broadside.Core.slnf
dotnet build Broadside.slnx        # everything, including the platform-specific projects your machine can build
```

The Apple and Android renderers need their .NET workloads (`dotnet workload install maui`). On a machine without them, those projects are skipped.

## Contributing

Bug reports, ideas and pull requests are welcome. Start with [`CONTRIBUTING.md`](CONTRIBUTING.md). To report a security problem, see [`SECURITY.md`](SECURITY.md).

## Sponsoring

Broadside is free and will stay free. Sponsors help fund the work and get priority on bugs and feature requests. See [`SPONSORS.md`](SPONSORS.md).

## License

[MIT](LICENSE)
