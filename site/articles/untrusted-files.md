---
title: "Processing untrusted files"
---

# Processing untrusted files

A server that converts uploads, a mail gateway that indexes attachments, a viewer that opens whatever the user downloads: all of
them hand Broadside files that may have been crafted to break it. Broadside is built for that. Every parser and codec is fuzzed,
reading repairs damaged files instead of throwing, and every place where a file could make it recurse, allocate or decode without
bound has a limit. This page explains what Broadside does and does not protect you from, and what to configure.

The full threat model, with every limit, its default, the option that changes it and the test that proves it, is in
[docs/threat-model.md](https://github.com/michaelstonis/Broadside/blob/main/docs/threat-model.md). To report a vulnerability, see
[SECURITY.md](https://github.com/michaelstonis/Broadside/blob/main/SECURITY.md).

## What a document cannot do

- **Run code.** JavaScript, XFA forms, 3D, rich media and multimedia are read as data and kept when the file is saved. Nothing in
  them is ever executed or played.
- **Read files.** Stream data stored in an external file (`F`, `FFilter`) is not read; launch, remote go-to and import actions are
  exposed as data and never followed. Broadside itself reads only the file you open, the operating system's font directories
  (see Fonts below) and nothing else.
- **Reach the network.** Broadside makes no network request unless you give it an `HttpClient`: `PdfDocument.OpenAsync(HttpClient, Uri)`,
  `PdfSignatureValidationOptions.UseOnlineRevocation(HttpClient)` and `PdfTimestampClient`. See Network access below.
- **Make a signature look valid when it is not.** Nothing is trusted by default; see Signatures below.

## What the limits cover

Reaching a limit is a diagnostic, never an exception (in the default lenient mode): the stream is cut, the image or mesh is not
drawn, the nested object reads as null, and the rest of the document reads as usual. `document.Diagnostics` says what was cut.

| Limit | Default | Set with |
|---|---|---|
| Bytes one stream may decode to (decompression bombs) | 1 GiB | `PdfOptions.WithMaxDecodedLength` |
| Pixels in one image | 2^28 (16384 x 16384) | `PdfOptions.WithMaxImagePixels` |
| Vertices in one mesh shading | 2^22 | `PdfOptions.WithMaxMeshVertices` |
| Operands before one content operator | 65,536 | `ContentOptions.WithMaxOperands` |
| Graphics state (`q`) depth | 4,096 | `ContentOptions.WithMaxSaveDepth` |
| Nested forms, patterns, Type 3 glyphs, soft masks and appearances | 50 | `ContentOptions.WithMaxNestingDepth` |
| Bytes of a non-seekable stream kept in memory (the rest goes to a temporary file) | 64 MiB | `PdfOptions.WithStreamBufferLimit` |
| Caches (content, images, glyphs, display lists) | 32, 64, 8 and 32 MiB | `PdfOptions.With*CacheBudget` |

Everything else, such as the depth of object nesting, page trees, name and number trees, outlines, colour spaces, functions,
CMaps and font subroutines, is limited by fixed values chosen well above anything real files use; the threat model lists them.

The limits apply to **one stream, one image or one mesh at a time**. They stop a single object from exhausting memory; they do
not cap the total a document can make you do. A file with thousands of streams that each decode to just under the limit still
takes time to read. For that, use deadlines and, for hard guarantees, process isolation.

## Recommended configuration

The defaults suit a desktop viewer opening the user's own files. For a service that processes files from strangers, tighten the
per-object limits to what your documents need and turn off what depends on the machine:

```csharp
var options = new PdfOptions()
    .WithMaxDecodedLength(64L << 20)       // 64 MiB per stream
    .WithMaxImagePixels(1L << 26)          // 8192 x 8192
    .WithMaxMeshVertices(1 << 20)
    .UseManagedImageDecoding()             // never hand image bytes to the operating system's codecs
    .UseSystemFontResolver(null)           // no machine fonts: output does not depend on the host
    .UseStandard14Fonts();                 // the bundled fonts (Broadside.Fonts.Standard14)

var engine = new PdfEngine(options);
```

Strict mode (`UseStrict()`) rejects any file that deviates from the specification by throwing `DiagnosticException`. Use it to
refuse damaged files, not as a security measure: lenient mode is held to the same limits and is fuzzed just as hard.

## Deadlines

Every long operation can be canceled, and the checks are frequent: the content interpreter checks the token every 1,024
operators and at every nested form, and image codecs check it while they decode.

```csharp
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
DisplayList list = page.CreateDisplayList(new ContentOptions().WithCancellation(deadline.Token));
using Bitmap bitmap = rasterizer.Render(page, new RenderOptions().WithCancellation(deadline.Token));
```

A canceled operation throws `OperationCanceledException` and leaves nothing behind that changes a later run. Interactive code can
use the sliced forms instead (`DisplayListBuild.Continue(budget)`, `ProgressiveRender.Continue(budget)`), which run for a time
budget and resume where they stopped.

## Page size

A page's size comes from the file, and a `MediaBox` of 200,000 points is legal. Check the size before you render, and pick a
scale or a region that fits your budget:

```csharp
var render = new RenderOptions().WithDpi(150);
PixelSize size = render.GetPixelSize(page);
if ((long)size.Width * size.Height > 50_000_000)
{
    // Render a thumbnail, a region, or refuse the page.
}
```

A page that would need a bitmap larger than .NET can allocate is refused with `ArgumentException`, by `GetPixelSize` and by the
render, before anything is allocated or drawn.

## Fonts

By default, fonts a document uses but does not embed are looked up in the operating system's font directories, which Broadside
only reads. `UseSystemFontResolver(null)` turns that off. `WithSystemFontIndexPath` writes an index of those fonts to the path you
give; without it, nothing is written.

## Network access

Each `HttpClient` you pass is used only for its one purpose, but the addresses can come from the document:

- **Remote files** (`OpenAsync(HttpClient, Uri)`) fetch byte ranges of the URI you give, and nothing else.
- **Online revocation checking** (`UseOnlineRevocation`) sends OCSP and CRL requests to the addresses named in the certificates
  inside the document, at most 32 per validation and only over `http` and `https`. An attacker who controls the document chooses
  those hosts. On a server, either leave online revocation off and pass revocation data yourself
  (`PdfSignatureValidationOptions.OcspResponses` and `CertificateRevocationLists`), or give it an `HttpClient` whose handler only
  allows the hosts you trust, so that it cannot reach internal services.
- **Time-stamping** (`PdfTimestampClient`) contacts only the authority you configure, and only when you sign.

## Signatures

`PdfDocument.ValidateSignatures` trusts nothing by default: no certificate store is consulted and no certificate is downloaded. Pass
the certificates you trust in `PdfSignatureValidationOptions.TrustAnchors`. Then accept a signature only when its `Status` is
`Valid`, and check what changed after it was applied: `CoversWholeFile` is true only when the signature covers the whole file,
and `Modifications` lists every change made in later revisions. A document can show anything on its pages, including a picture
of a signature; only the validation result says who signed it.

## Encrypted files

Passwords and certificates are given with `PdfOptions.WithPassword` or `WithCredentials`. A wrong password throws
`PdfPasswordException`, which is not a sign of an attack. Permissions (`document.Security.Permissions`) are a request from the author, not
an enforcement: anyone who can open a file can read all of it. Respect them in your application if you must, but do not rely on
them to protect content.

## Process isolation

The limits make every single operation bounded, but no in-process limit can bound everything a process does. When you process
files from untrusted sources at scale, run Broadside in a worker process or container with its own memory limit and a wall-clock
timeout, and recycle it after a failure. This also contains the platform code Broadside calls (graphics backends and, if you turn
them on, platform image decoders), which is outside Broadside's own guarantees.
