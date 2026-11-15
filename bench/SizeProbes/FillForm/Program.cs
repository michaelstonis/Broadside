using Broadside;
using Broadside.Forms;

// Open a form, set a text field's value (which regenerates its appearance) and save the result.
using PdfDocument document = PdfDocument.Open(args[0]);
if (document.AcroForm?.FindField(args[1]) is not PdfTextField field)
{
    Console.WriteLine($"no text field '{args[1]}'");
    return 1;
}

field.SetValue("Size probe");
using var output = new MemoryStream();
document.Save(output);
Console.WriteLine($"saved: {output.Length} bytes");
return output.Length > 0 ? 0 : 1;
