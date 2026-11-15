using Broadside;

// Open a file and extract the text of every page.
using PdfDocument document = PdfDocument.Open(args[0]);
int characters = 0;
foreach (PdfPage page in document.Pages)
{
    characters += page.ExtractText().Text.Length;
}

Console.WriteLine($"characters: {characters}");
return characters > 0 ? 0 : 1;
