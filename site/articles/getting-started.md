---
title: "Getting started"
---

# Getting started

Broadside's packages are on [NuGet.org](https://www.nuget.org/packages/Broadside) as the 1.0 release candidate (`1.0.0-rc.N`); 1.0.0 follows the soak period. The public API is frozen for 1.0: only fixes land between candidates. Every package, the viewers included, ships as 1.0.0, and the packages target `net10.0` only.

To see the library before installing anything, open the [live demo](/demo/): the Blazor WebAssembly viewer and the rest of the library running in your browser.

Every piece of code on this page comes from [`samples/guides/GettingStarted`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/GettingStarted), a console app that CI builds and runs on every change, so it compiles against the current API.

## Install

Until 1.0.0 is out the packages are release candidates, which NuGet treats as prereleases, so pass `--prerelease`:

```sh
dotnet new console -o HelloBroadside && cd HelloBroadside
dotnet add package Broadside --prerelease                    # read, write, edit; text extraction
dotnet add package Broadside.Rendering --prerelease          # software rasterizer and PNG output
dotnet add package Broadside.Fonts.Standard14 --prerelease   # glyphs for pages that use non-embedded Standard 14 fonts
```

Add the others when you need them: `Broadside.Layout` for the [document builder](building-documents.md), `Broadside.Fonts.Cmaps` for Chinese, Japanese and Korean text in files that do not embed their character maps, and one rendering backend per platform you draw with (SkiaSharp, CoreGraphics, Direct2D, Android or WebGL). The full list, with what each package contains, is in the [README](https://github.com/michaelstonis/Broadside#packages) and the [API coverage](api-coverage.md) page.

## Open a file

`PdfDocument.Open` takes a path, a stream or the file's bytes. The document reads lazily: opening reads the file's structure and catalog, and every page and object loads when it is first used. Dispose the document when you are done with it.

<!-- snippet: samples/guides/GettingStarted/Program.cs#open -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
```
<!-- endsnippet -->

The [reading guide](reading.md) covers the other sources, encrypted files, strict mode and large files.

## Inspect it

<!-- snippet: samples/guides/GettingStarted/Program.cs#inspect -->
```csharp
Console.WriteLine($"PDF {document.Version}, {document.Pages.Count} page(s)");
Console.WriteLine($"Title: {document.Properties.Title ?? "(none)"}");

PdfPage first = document.Pages[0];
Console.WriteLine($"Page 1 is {first.MediaBox.Width} x {first.MediaBox.Height} points, rotated {first.Rotation} degrees");
Console.WriteLine($"Page 1 says: {first.ExtractText().Text}");

foreach (Diagnostic diagnostic in document.Diagnostics)
{
    Console.WriteLine($"Repaired: {diagnostic}");
}
```
<!-- endsnippet -->

`Properties` reads the title, author and dates from the document's metadata. Page sizes are in points (1/72 inch). Reading is lenient: a damaged file is repaired as it is read, and each repair is recorded in `Diagnostics`, so a file that opens cleanly has none.

## Change it and save it

The document is a live view over the file's objects, so a change is made in place and written by `Save`.

<!-- snippet: samples/guides/GettingStarted/Program.cs#save -->
```csharp
document.Properties.Title = "Hello from Broadside";
document.Save("text-standard14-titled.pdf");
```
<!-- endsnippet -->

`Save` takes a path or a stream and writes a complete new file. `new PdfSaveOptions().WithIncrementalUpdate(true)` appends the changes to the original bytes instead, leaving every byte an existing signature covers unchanged.

## Create a document

`PdfDocument.Create()` starts a document with one empty US Letter page. A page's canvas draws text, paths and images in PDF's own terms:

<!-- snippet: samples/guides/GettingStarted/Program.cs#create -->
```csharp
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
```
<!-- endsnippet -->

To write reports, invoices and forms rather than single blocks, use the document builder in `Broadside.Layout`: `PdfDocument.Create(doc => ...)` describes the document as nested content and lays it out tagged and accessible. [Building documents](building-documents.md) walks through it.

## Render a page

The software rasterizer in `Broadside.Rendering` draws a page into a bitmap, which saves as PNG:

<!-- snippet: samples/guides/GettingStarted/Program.cs#render -->
```csharp
PdfOptions options = new PdfOptions().UseStandard14Fonts(); // glyphs for fonts a file uses without embedding them
using (PdfDocument hello = PdfDocument.Open("hello.pdf", options))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(hello.Pages[0], new RenderOptions().WithDpi(150)))
{
    bitmap.SavePng("hello.png");
}
```
<!-- endsnippet -->

`UseStandard14Fonts()` comes from `Broadside.Fonts.Standard14`. Without it, text in a font the file does not embed (such as Helvetica here) is not drawn, and the document records a `FontProgramNotFound` diagnostic that says which option to add.

The software rasterizer gives the same bytes for the same package version, page and `RenderOptions` on Linux, Windows and macOS, on x64 and Arm64: it uses no platform maths library, fused multiply-add or vector path that could round differently, and CI compares render hashes across those runners. WebAssembly is not covered yet, and native backends (SkiaSharp, CoreGraphics, Direct2D, Android) match it within the golden-image threshold rather than bit for bit.

## Where to go next

The [guides](guides.md) cover each part of the library in turn: reading, rendering, writing and editing, extracting text, signatures, and accessibility and conformance. The [API reference](../api/readme.md) documents every public type.

## Trimmed and Native AOT apps

Broadside is trimmable and Native AOT compatible: publishing with `PublishAot` or `PublishTrimmed` gives no warnings, and an app
keeps only the parts it calls. It does not use `System.Xml` (XMP metadata is read and written by its own small XML reader and
writer), so an app that saves, fills forms or signs carries neither the XML stack nor the networking code an XML resolver would
keep. The sizes of each scenario and the app settings worth knowing are in
[`docs/benchmarks/core-size.md`](https://github.com/michaelstonis/Broadside/blob/main/docs/benchmarks/core-size.md).

CI checks this on every change to the library. It publishes Native AOT apps for Linux x64, Windows x64 and macOS Arm64, and the
Blazor WebAssembly demo trimmed, with and without WebAssembly AOT. Each app opens a file, renders page 1, extracts its text and
saves it. Any trim or AOT warning fails the build, and so does an app that grows past its size budget. The WebAssembly builds draw
the same pixels as the desktop.

## Fonts in the browser

A browser app has no operating-system fonts to fall back on. Fonts embedded in the PDF work as everywhere else. For a font that is
not embedded, Broadside needs a font program from somewhere:

- **The Standard 14 fonts package.** `PdfOptions.UseStandard14Fonts()` adds metric-compatible stand-ins for Helvetica, Times,
  Courier, Symbol and ZapfDingbats (about 1.5 MB). Other non-embedded fonts get the closest of these by family and style. This
  is what the Blazor viewer uses.
- **Your own fonts.** `PdfOptions.UseFontResolver(...)` serves font programs your app ships or downloads.

With neither configured, a non-embedded font's glyphs are not drawn. The document records a `FontProgramNotFound` diagnostic that
names the font and says which option to add. When a different font stands in, it records `FontSubstituted`. Text extraction does
not need a font program and works either way.

## Viewers

Viewer controls for Blazor WebAssembly, .NET MAUI, Avalonia, Uno Platform, WinUI 3 and WPF show documents in your app. [Viewers](viewers.md) shows each one with code from its sample app.

## Samples

[`samples/`](https://github.com/michaelstonis/Broadside/tree/main/samples) holds:

- [`samples/guides`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides): one small console app per guide topic. The guides include their code from these apps, and CI runs them all. `samples/guides/run-samples.sh` builds and runs them locally.
- [`Broadside.BuilderSamples`](https://github.com/michaelstonis/Broadside/tree/main/samples/Broadside.BuilderSamples): every example of the document builder guide.
- [`Broadside.Viewer.Blazor.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Blazor.Sample/README.md), which this site hosts as the [live demo](/demo/): `dotnet run --project samples/Broadside.Viewer.Blazor.Sample`. To publish a trimmed, AOT-compiled static site, run `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh out/demo`.
- [`Broadside.Viewer.Maui.Sample`](https://github.com/michaelstonis/Broadside/blob/main/samples/Broadside.Viewer.Maui.Sample/README.md): `dotnet build samples/Broadside.Viewer.Maui.Sample -f net10.0-maccatalyst -t:Run` (or `net10.0-android`, `net10.0-ios`). It needs the `maui` workload.
- A sample app for each of the other viewer shells (Avalonia, Uno Platform, WPF and WinUI 3).

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
./check-docs.sh        # builds the site into site/.lunet/build/www and checks its routes, links and included samples
```

A guide includes sample code with a pair of markers that name a region of a file in `samples/guides/`:

```md
<!-- snippet: samples/guides/GettingStarted/Program.cs#open -->
<!-- endsnippet -->
```

`dotnet run --project tools/DocSnippets -- --write` fills in the code between the markers from the `#region open` of that file. `./check-docs.sh` fails when a page's code differs from its sample or a sample does not build.

The live demo is published separately and copied into the site's `demo/` folder; to check the site with it, as the docs job does:

```sh
samples/Broadside.Viewer.Blazor.Sample/publish-static.sh --no-aot out/demo   # the deployed site is compiled ahead of time (no --no-aot)
./check-docs.sh --demo out/demo
```

To see what is done, how releases are made and what is known to be broken, read the [roadmap](roadmap.md).
