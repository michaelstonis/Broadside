using Broadside;

// open-metadata without the image codecs (issue #544): options built with CreateWithoutImageCodecs, so the JPX, JBIG2, DCT and
// CCITT decoders are not part of the app.
using PdfDocument document = PdfDocument.Open(args[0], PdfOptions.CreateWithoutImageCodecs());
Console.WriteLine($"pages: {document.Pages.Count}, title: {document.Information?.Title}");
return document.Pages.Count > 0 ? 0 : 1;
