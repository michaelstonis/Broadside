using Broadside;

// Open a file, change its metadata and save it.
using PdfDocument document = PdfDocument.Open(args[0]);
document.Properties.Title = "Size probe";
using var output = new MemoryStream();
document.Save(output);
Console.WriteLine($"saved: {output.Length} bytes");
return output.Length > 0 ? 0 : 1;
