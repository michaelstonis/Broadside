---
title: "Getting started"
---

# Getting started

Broadside is in preview. The first preview, `0.1.0-preview.1`, is on NuGet.org, and the public API may still change before v1. The packages target `net10.0` only.

To see the library before installing anything, open the [live demo](/demo/): the Blazor WebAssembly viewer and the rest of the library running in your browser.

## Install

Every package is a prerelease, so pass `--prerelease`:

```sh
dotnet add package Broadside --prerelease                    # read, write, edit; text extraction
dotnet add package Broadside.Rendering --prerelease          # software rasterizer and PNG output
dotnet add package Broadside.Fonts.Standard14 --prerelease   # glyphs for pages that use non-embedded Standard 14 fonts
dotnet add package Broadside.Layout --prerelease             # flow layout (FlowDocument, Paragraph)
```

The full list of 13 packages, with what each one contains and its target frameworks, is in the [README](https://github.com/michaelstonis/Broadside#packages).

## A first example

This compiles and runs against the `0.1.0-preview.1` packages `Broadside`, `Broadside.Rendering`, `Broadside.Fonts.Standard14` and `Broadside.Layout`, in a `dotnet new console` project:

```csharp
using Broadside;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
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

// Flow layout: paragraphs broken into lines and flowed across pages.
using (PdfDocument report = PdfDocument.Create())
{
    report.Pages.RemoveAt(0); // the flow appends its own pages
    PdfCanvasFont body = report.GetStandard14Font(Standard14Font.TimesRoman);
    var flow = new FlowDocument(PageSetup.Letter);
    flow.Add(new Paragraph("Broadside lays this paragraph out in lines and pages.", new ParagraphStyle(body, 11)));
    FlowLayoutResult layout = flow.Render(report);
    Console.WriteLine($"{layout.Pages.Count} page(s) laid out");
    report.Save("report.pdf");
}
```

Reading is lenient by default: a damaged file is repaired and every repair is recorded as a diagnostic on the document. `new PdfOptions().UseStrict()` makes the first deviation an error instead.

## Viewers

- **Blazor WebAssembly** (`Broadside.Viewer.Blazor`): the `PdfViewer` component. The software rasterizer draws the tiles by default. WebGL 2 is opt-in, and so is a render worker that draws content tiles in a JS Web Worker. The app has to start the worker explicitly; it uses no .NET WebAssembly threads.
- **.NET MAUI** (`Broadside.Viewer.Maui`): the `PdfView` control, drawn with SkiaSharp, for iOS, Android, Mac Catalyst and Windows. Tiles render off the UI thread and are composed on the GPU (Metal on iOS and Mac Catalyst). Register it with `builder.UseBroadsideViewer()`. The package's Windows target ships from the next release.

## Samples

Two sample apps are in [`samples/`](https://github.com/michaelstonis/Broadside/tree/main/samples):

- [`Broadside.Viewer.Blazor.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Blazor.Sample/README.md), which this site hosts as the [live demo](/demo/): `dotnet run --project samples/Broadside.Viewer.Blazor.Sample`. To publish a trimmed, AOT-compiled static site, run `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh out/demo`.
- [`Broadside.Viewer.Maui.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Maui.Sample/README.md): `dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-maccatalyst -t:Run` (or `net10.0-android`, `net10.0-ios`). It needs the `maui` workload.

## Building from source

The repository needs the .NET SDK pinned in [`global.json`](https://github.com/michaelstonis/Broadside/blob/main/global.json).

```sh
git clone https://github.com/michaelstonis/Broadside.git
cd Broadside
dotnet build Broadside.Core.slnf
dotnet test Broadside.Core.slnf
```

## Building this site

The site is built with [Lunet](https://lunet.io), installed as a local .NET tool:

```sh
dotnet tool restore
./check-docs.sh        # builds the site into site/.lunet/build/www and checks its routes and links
```

The live demo is published separately and copied into the site's `demo/` folder; to check the site with it, as the docs job does:

```sh
samples/Broadside.Viewer.Blazor.Sample/publish-static.sh --no-aot out/demo   # the deployed site is compiled ahead of time (no --no-aot)
./check-docs.sh --demo out/demo
```

To see what is done, what is not released yet and what is known to be broken, read the [roadmap](roadmap.md).
