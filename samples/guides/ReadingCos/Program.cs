using System.Text;
using Broadside;
using Broadside.Objects;
using Broadside.Samples;

Sample.Start("Reading: COS objects");

using PdfDocument document = PdfDocument.Open("text-standard14.pdf");

#region catalog
CosDictionary catalog = document.Catalog;
foreach ((CosName key, CosObject value) in catalog)
{
    Console.WriteLine($"/{key.Value} {value}");
}

Console.WriteLine($"The trailer has {document.Trailer.Count} entries");
#endregion

#region content-stream
PdfPage page = document.Pages[0];
CosObject contents = document.Resolve(page.Dictionary[new CosName("Contents")]);
if (contents is CosStream stream)
{
    ReadOnlyMemory<byte> decoded = document.DecodeStream(stream);
    Console.WriteLine(Encoding.Latin1.GetString(decoded.Span));
}
#endregion
Sample.Expect(contents is CosStream, "the page's content is one stream");

#region live-view
page.Dictionary[new CosName("Rotate")] = new CosInteger(90);
Console.WriteLine($"Rotation through the model: {page.Rotation}");
#endregion
Sample.Expect(page.Rotation == 90, "the model sees the COS change");

Console.WriteLine("OK");
