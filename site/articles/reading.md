---
title: "Reading"
---

# Reading

This guide covers opening PDF files and reading them safely: where a document can come from, what happens when a file is damaged, encrypted files, the objects under the document model, reading from several threads, large files, and hosting the library with dependency injection and logging.

The code comes from the console apps in [`samples/guides`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides) (`ReadingSources`, `ReadingDiagnostics`, `ReadingEncrypted`, `ReadingCos`, `ReadingConcurrency` and `ReadingHosted`). CI builds and runs them on every change, and they read files from the repository's test corpus, so every snippet here compiles and does what the text says. Everything on this page is in the `Broadside` package.

## Opening a file

A document opens from a path, a stream or bytes in memory. Each form has an overload that also takes `PdfOptions`.

From a path, the file is memory-mapped and read in place:

<!-- snippet: samples/guides/ReadingSources/Program.cs#open-path -->
```csharp
using PdfDocument document = PdfDocument.Open("page-tree-inherited.pdf");
Console.WriteLine($"{document.Pages.Count} pages");
```
<!-- endsnippet -->

From a stream, the document reads from it as objects are used, so the stream must stay open and unchanged until the document is disposed. A seekable stream is read in place. A stream that cannot seek (a network response, for instance) is copied once when it is opened, because a PDF file is read from its end:

<!-- snippet: samples/guides/ReadingSources/Program.cs#open-stream -->
```csharp
using FileStream stream = File.OpenRead("page-tree-inherited.pdf");
using PdfDocument document = PdfDocument.Open(stream); // keep the stream open until the document is disposed
Console.WriteLine(document.Pages[1].ExtractText().Text);
```
<!-- endsnippet -->

From bytes, the document reads the memory you give it without copying it, so leave it unchanged while the document is open:

<!-- snippet: samples/guides/ReadingSources/Program.cs#open-bytes -->
```csharp
byte[] bytes = File.ReadAllBytes("page-tree-inherited.pdf");
using PdfDocument document = PdfDocument.Open(bytes); // the bytes are not copied: leave them unchanged
Console.WriteLine(document.Pages[0].ExtractText().Text);
```
<!-- endsnippet -->

Every open has an asynchronous counterpart with the same parameters and a cancellation token. Only the open is asynchronous: for a stream that cannot seek, it copies the stream without blocking a thread. Reading the document afterwards is synchronous.

<!-- snippet: samples/guides/ReadingSources/Program.cs#open-async -->
```csharp
await using FileStream stream = File.OpenRead("page-tree-inherited.pdf");
using PdfDocument document = await PdfDocument.OpenAsync(stream);
```
<!-- endsnippet -->

`PdfDocument.OpenAsync(HttpClient, Uri)` opens a file on a web server through HTTP range requests, fetching only the parts of the file it reads.

Opening reads the file's header, cross-reference information, trailer and catalog. Pages, fonts, images and every other object load when they are first used, are parsed once, and are kept for the life of the document.

### One engine for many files

`PdfDocument.Open` uses default options unless you pass some. When an application opens many files with the same options, create one `PdfEngine` and open them all through it. An engine copies its options when it is created, holds no other state, and is safe to share between threads.

<!-- snippet: samples/guides/ReadingSources/Program.cs#engine -->
```csharp
var engine = new PdfEngine(new PdfOptions().UseStrict());
foreach (string path in new[] { "page-tree-inherited.pdf", "linearized.pdf" })
{
    using PdfDocument document = engine.Open(path);
    Console.WriteLine($"{path}: {document.Pages.Count} pages, linearized: {document.IsLinearized}");
}
```
<!-- endsnippet -->

## Diagnostics and strict mode

Real PDF files are often damaged: offsets that point to the wrong place, missing keywords, wrong stream lengths. Broadside reads leniently by default. It repairs what it can, the way other readers do, and records each repair as a `Diagnostic` on the document. Nothing is repaired silently.

<!-- snippet: samples/guides/ReadingDiagnostics/Program.cs#lenient -->
```csharp
using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf");
foreach (Diagnostic diagnostic in document.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code} at offset {diagnostic.Offset}: {diagnostic.Message}");
}
```
<!-- endsnippet -->

A diagnostic has a severity (`Information`, `Warning` or `Error`), a code, a message, and where it is known, the byte offset in the file and the object it concerns. `Diagnostic.Kind` is the typed form of the code: a `DiagnosticCode` with its area of the library and the clause of ISO 32000-2 it relates to. The codes the library records are static properties of catalogue classes such as `FileStructureDiagnostics`, so you can test for one without comparing strings:

<!-- snippet: samples/guides/ReadingDiagnostics/Program.cs#find -->
```csharp
using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf");
bool repairedStartxref = document.Diagnostics.Any(d => d.Kind.Equals(FileStructureDiagnostics.StartxrefInvalid));
bool anyErrors = document.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
DiagnosticCode kind = document.Diagnostics[0].Kind;
Console.WriteLine($"{kind.Name}: area {kind.Area}, {kind.Clause}");
```
<!-- endsnippet -->

Because objects load on first use, so do their repairs. A damage that only affects a page is recorded when that page is read, and `Diagnostics` grows as more of the document is used:

<!-- snippet: samples/guides/ReadingDiagnostics/Program.cs#lazy -->
```csharp
using PdfDocument document = PdfDocument.Open("broken-xref-offsets.pdf");
Console.WriteLine($"After opening: {document.Diagnostics.Count} diagnostic(s)");
_ = document.Pages[0].ExtractText(); // loads the page tree and the page
Console.WriteLine($"After reading page 1: {document.Diagnostics.Count} diagnostic(s)");
```
<!-- endsnippet -->

Strict mode turns the first deviation from the specification into a `DiagnosticException` instead. Use it to validate files you produce, or to reject input that is not well-formed:

<!-- snippet: samples/guides/ReadingDiagnostics/Program.cs#strict -->
```csharp
PdfOptions strict = new PdfOptions().UseStrict();
try
{
    using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf", strict);
}
catch (DiagnosticException ex)
{
    Console.WriteLine($"Rejected: {ex.Diagnostic.Code} at offset {ex.Diagnostic.Offset}");
}
```
<!-- endsnippet -->

Strict mode checks what it reads. A deviation in an object that loads after the open (a page, a font) throws from the call that loads it, so opening a file in strict mode does not validate the whole file.

## Passwords and certificates

An encrypted file whose user password is empty opens like any other: most encrypted files only restrict what a reader may do. `IsEncrypted`, `Security` and `Permissions` say how the file is protected and what the opener may do with it.

<!-- snippet: samples/guides/ReadingEncrypted/Program.cs#empty-user-password -->
```csharp
using PdfDocument document = PdfDocument.Open("encrypted-aes-128.pdf"); // the user password is empty
Console.WriteLine($"Encrypted: {document.IsEncrypted}, access: {document.Security?.Access}, may print: {document.Permissions.HasFlag(PdfPermissions.Print)}");
```
<!-- endsnippet -->

A file that needs a password opens through an engine with the password as credentials. The user password opens it with the permissions the file grants; the owner password opens it with full access.

<!-- snippet: samples/guides/ReadingEncrypted/Program.cs#password -->
```csharp
var engine = new PdfEngine();
using PdfDocument document = engine.Open("encrypted-user-password.pdf", new PdfPassword("pässwort"));
Console.WriteLine($"{document.Properties.Title}: {document.Permissions}");
```
<!-- endsnippet -->

To offer the same password to every file opened with a set of options, set it on the options. It is never exposed as a property, so configuration binding and logging cannot see it.

<!-- snippet: samples/guides/ReadingEncrypted/Program.cs#password-options -->
```csharp
PdfOptions options = new PdfOptions().WithPassword("pässwort");
using PdfDocument document = PdfDocument.Open("encrypted-user-password.pdf", options);
```
<!-- endsnippet -->

A missing password and a wrong one throw the same exception type, told apart by `Failure`:

<!-- snippet: samples/guides/ReadingEncrypted/Program.cs#password-failure -->
```csharp
try
{
    using PdfDocument document = PdfDocument.Open("encrypted-user-password.pdf");
}
catch (PdfPasswordException ex) when (ex.Failure == PdfPasswordFailure.Required)
{
    Console.WriteLine("This file needs a password.");
}

try
{
    using PdfDocument document = new PdfEngine().Open("encrypted-user-password.pdf", new PdfPassword("wrong"));
}
catch (PdfPasswordException ex) when (ex.Failure == PdfPasswordFailure.Incorrect)
{
    Console.WriteLine("That password does not open this file.");
}
```
<!-- endsnippet -->

A file encrypted for certificate recipients (the public-key security handler) opens with `PdfCertificateCredentials`, which take one or more certificates with their private keys. When none of them is a recipient, `PdfCertificateException` lists the recipients the file names:

<!-- snippet: samples/guides/ReadingEncrypted/Program.cs#certificate -->
```csharp
var engine = new PdfEngine();
try
{
    using PdfDocument document = engine.Open("for-recipient.pdf", new PdfCertificateCredentials(certificate));
    Console.WriteLine($"Opened for {certificate.Subject}: {document.Permissions}");
}
catch (PdfCertificateException ex)
{
    Console.WriteLine($"Not for this certificate ({ex.Failure}); recipients: {ex.Recipients.Count}");
}
```
<!-- endsnippet -->

A file encrypted in a way the library cannot decrypt throws `PdfEncryptionNotSupportedException`, whose `Reason` says why.

## The objects under the model

Every type that starts with `Pdf` is a view over the file's objects, which are the `Cos` types in `Broadside.Objects`: dictionaries, arrays, names, numbers, strings, streams and references. When the document model does not cover something, read the objects directly. `Catalog` and `Trailer` are the roots, every page has its `Dictionary`, and `Resolve` follows a reference to the object it points to:

<!-- snippet: samples/guides/ReadingCos/Program.cs#catalog -->
```csharp
CosDictionary catalog = document.Catalog;
foreach ((CosName key, CosObject value) in catalog)
{
    Console.WriteLine($"/{key.Value} {value}");
}

Console.WriteLine($"The trailer has {document.Trailer.Count} entries");
```
<!-- endsnippet -->

`DecodeStream` returns a stream's data with its filters applied, here a page's content stream:

<!-- snippet: samples/guides/ReadingCos/Program.cs#content-stream -->
```csharp
PdfPage page = document.Pages[0];
CosObject contents = document.Resolve(page.Dictionary[new CosName("Contents")]);
if (contents is CosStream stream)
{
    ReadOnlyMemory<byte> decoded = document.DecodeStream(stream);
    Console.WriteLine(Encoding.Latin1.GetString(decoded.Span));
}
```
<!-- endsnippet -->

The model never copies the objects, so a change made to them shows through the model at once, and is written by the next save:

<!-- snippet: samples/guides/ReadingCos/Program.cs#live-view -->
```csharp
page.Dictionary[new CosName("Rotate")] = new CosInteger(90);
Console.WriteLine($"Rotation through the model: {page.Rotation}");
```
<!-- endsnippet -->

## Reading from several threads

While nobody changes a document, any number of threads may read it at the same time: walk pages, resolve objects, decode streams, extract text and render. Each object is still parsed once, and every thread sees the same instance.

<!-- snippet: samples/guides/ReadingConcurrency/Program.cs#pages-in-parallel -->
```csharp
using PdfDocument document = PdfDocument.Open("page-tree-inherited.pdf");
string[] texts = new string[document.Pages.Count];
Parallel.For(0, texts.Length, i => texts[i] = document.Pages[i].ExtractText().Text);
Console.WriteLine(string.Join(" | ", texts));
```
<!-- endsnippet -->

An engine is safe to share too, so one engine can open documents on many threads:

<!-- snippet: samples/guides/ReadingConcurrency/Program.cs#documents-in-parallel -->
```csharp
var engine = new PdfEngine();
string[] paths = ["page-tree-inherited.pdf", "linearized.pdf", "text-standard14.pdf"];
int[] pageCounts = new int[paths.Length];
Parallel.For(0, paths.Length, i =>
{
    using PdfDocument opened = engine.Open(paths[i]);
    pageCounts[i] = opened.Pages.Count;
});
```
<!-- endsnippet -->

Changes are single-threaded, and synchronizing them is up to you: no read may run while a change runs. Disposing a document while another thread reads it is an error too, and reads after `Dispose` throw `ObjectDisposedException`.

<!-- snippet: samples/guides/ReadingConcurrency/Program.cs#mutation -->
```csharp
// Reads may run in parallel; a change may not run alongside them. Finish the reads, then change the document on one thread.
document.Properties.Title = "Read in parallel";
document.Save("page-tree-inherited-titled.pdf");
```
<!-- endsnippet -->

## Large files

Memory grows with the objects a program uses, not with the size of the file. A file opened from a path is memory-mapped, a seekable stream is read in place, and stream data stays in the file and is read each time it is asked for. A multi-gigabyte file opens and reads its last page in a few megabytes of managed memory, and saving it copies large objects through a fixed buffer.

The options bound the rest:

<!-- snippet: samples/guides/ReadingSources/Program.cs#large-files -->
```csharp
PdfOptions options = new PdfOptions()
    .WithStreamBufferLimit(16L << 20)    // a non-seekable stream over 16 MiB is copied to a temporary file
    .WithMaxDecodedLength(256L << 20)    // no single stream decodes to more than 256 MiB
    .WithImageCacheBudget(16L << 20);    // decoded images kept for rendering stay under 16 MiB

using PdfDocument document = PdfDocument.Open("linearized.pdf", options);
foreach (PdfPage page in document.Pages)
{
    Console.WriteLine(page.ExtractText().Text);
    document.TrimCaches(4L << 20); // keep at most 4 MiB of render caches between pages
}

Console.WriteLine($"The render caches hold {document.CachedBytes} bytes");
```
<!-- endsnippet -->

- `WithStreamBufferLimit` (64 MiB by default): a stream that cannot seek is copied into memory up to this size, and into a temporary file beyond it. The temporary file is deleted when the document is disposed.
- `WithMaxDecodedLength` (1 GiB by default): the most bytes one stream may decode to. Longer output is cut off with a diagnostic, which guards against decompression bombs.
- `WithMaxImagePixels` and `WithMaxMeshVertices` bound a single image and a single shading mesh.
- `WithContentCacheBudget`, `WithImageCacheBudget`, `WithGlyphCacheBudget` and `WithDisplayListCacheBudget` bound what rendering keeps between uses. A budget of zero turns that cache off without changing any output.

`CacheStatistics` reports what each cache holds, and `TrimCaches` drops the least recently used entries down to a size you choose. It is safe to call while other threads render.

## Dependency injection and logging

`AddBroadside` registers one `PdfEngine` as a singleton, built from `PdfOptions` through the options pattern, so a hosted application opens documents exactly as the static entry points do. Documents are not registered: open and dispose them where you use them.

<!-- snippet: samples/guides/ReadingHosted/Program.cs#register -->
```csharp
var services = new ServiceCollection();
services.AddLogging(logging => logging.AddConsole());
services.AddBroadside(options => options.WithMaxDecodedLength(256L << 20));
```
<!-- endsnippet -->

When the container has an `ILoggerFactory`, the engine logs every diagnostic through it as well as recording it on the document. Diagnostics are logged under the category `Broadside.PdfDocument` with event id 1, at `Warning` or `Error` after their severity, with the structured values `Code`, `Severity`, `Offset`, `ObjectReference` and `Message`. In strict mode the deviation is logged before it is thrown.

<!-- snippet: samples/guides/ReadingHosted/Program.cs#resolve -->
```csharp
PdfEngine engine = provider.GetRequiredService<PdfEngine>();
using PdfDocument document = engine.Open("startxref-wrong.pdf"); // the repair is logged as a warning, and recorded
```
<!-- endsnippet -->

The options bind from configuration with the host's own binding. Every value option is a settable property, so a section such as `"Broadside": { "ReadingMode": "Strict", "MaxDecodedLength": 268435456 }` configures the engine. Implementations (filters, font resolvers, security handlers) and the password are set in code.

<!-- snippet: samples/guides/ReadingHosted/Program.cs#configuration -->
```csharp
IConfiguration configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Broadside:ReadingMode"] = "Strict" })
    .Build();

var services = new ServiceCollection();
services.AddSingleton(configuration);
services.AddBroadside();
services.AddOptions<PdfOptions>().BindConfiguration("Broadside");
```
<!-- endsnippet -->

Without a container, give the options a logger factory:

<!-- snippet: samples/guides/ReadingHosted/Program.cs#logger-factory -->
```csharp
using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
PdfOptions options = new PdfOptions().WithLoggerFactory(loggerFactory);
using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf", options);
```
<!-- endsnippet -->

The hosted sample uses `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Options.ConfigurationExtensions`. The `Broadside` package itself depends only on the dependency-injection and logging abstractions and `Microsoft.Extensions.Options`.
