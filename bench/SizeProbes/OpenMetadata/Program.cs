using Broadside;

// Open a file and read its metadata: the page count and the document information dictionary.
using PdfDocument document = PdfDocument.Open(args[0]);
Console.WriteLine($"pages: {document.Pages.Count}, title: {document.Information?.Title}");
return document.Pages.Count > 0 ? 0 : 1;
