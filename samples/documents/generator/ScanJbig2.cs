using System.Globalization;
using Broadside.Objects;

namespace Broadside.SampleDocuments;

/// <summary>
/// The JBIG2 copy of the scanned memo: tesseract's OCR'd file (CCITT G4 page images under an invisible text layer) with each page
/// image replaced by jbig2enc's symbol-mode encoding of the same page, which shares one symbol dictionary (<c>JBIG2Globals</c>)
/// across all of them, as archival scanners write it. build.sh leaves jbig2enc's output in the output folder's .jbig2/ (and
/// deletes it afterwards): output.sym, the globals, and output.0000 onward, one page each.
/// </summary>
internal static class ScanJbig2
{
    public static void Write(GeneratorContext context)
    {
        string folder = context.OutputPath(".jbig2");
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("scan-memo-g4.pdf")));
        CosReference globals = document.AddObject(new CosStream(new CosDictionary(), File.ReadAllBytes(Path.Combine(folder, "output.sym"))));
        for (int i = 0; i < document.Pages.Count; i++)
        {
            PdfPage page = document.Pages[i];
            var resources = (CosDictionary)document.Resolve(page.Dictionary[new CosName("Resources")]);
            var xobjects = (CosDictionary)document.Resolve(resources[new CosName("XObject")]);
            CosName name = xobjects.First().Key;
            var old = (CosStream)document.Resolve(xobjects[name]);
            var dictionary = new CosDictionary
            {
                [new CosName("Type")] = new CosName("XObject"),
                [new CosName("Subtype")] = new CosName("Image"),
                [new CosName("Width")] = old.Dictionary[new CosName("Width")],
                [new CosName("Height")] = old.Dictionary[new CosName("Height")],
                [new CosName("ColorSpace")] = new CosName("DeviceGray"),
                [new CosName("BitsPerComponent")] = new CosInteger(1),
                [new CosName("Filter")] = new CosName("JBIG2Decode"),
                [new CosName("DecodeParms")] = new CosDictionary { [new CosName("JBIG2Globals")] = globals },
            };
            byte[] data = File.ReadAllBytes(Path.Combine(folder, "output." + i.ToString("0000", CultureInfo.InvariantCulture)));
            xobjects[name] = document.AddObject(new CosStream(dictionary, data));
        }

        document.Properties.Title = "Memorandum on the Phillips report (JBIG2)";
        document.Save(context.OutputPath("scan-memo-jbig2.pdf"), new PdfSaveOptions().WithUpdateModificationDate(false).WithRemoveUnreachableObjects(true));
    }
}
