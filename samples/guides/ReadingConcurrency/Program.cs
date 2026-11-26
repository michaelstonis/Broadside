using Broadside;
using Broadside.Samples;

Sample.Start("Reading: concurrency");

#region pages-in-parallel
using PdfDocument document = PdfDocument.Open("page-tree-inherited.pdf");
string[] texts = new string[document.Pages.Count];
Parallel.For(0, texts.Length, i => texts[i] = document.Pages[i].ExtractText().Text);
Console.WriteLine(string.Join(" | ", texts));
#endregion
Sample.Expect(texts[0].Contains("Page 1", StringComparison.Ordinal) && texts[1].Contains("Page 2", StringComparison.Ordinal), "each page's text");

#region documents-in-parallel
var engine = new PdfEngine();
string[] paths = ["page-tree-inherited.pdf", "linearized.pdf", "text-standard14.pdf"];
int[] pageCounts = new int[paths.Length];
Parallel.For(0, paths.Length, i =>
{
    using PdfDocument opened = engine.Open(paths[i]);
    pageCounts[i] = opened.Pages.Count;
});
#endregion
Sample.Expect(pageCounts.SequenceEqual([2, 2, 1]), "each document's page count");

#region mutation
// Reads may run in parallel; a change may not run alongside them. Finish the reads, then change the document on one thread.
document.Properties.Title = "Read in parallel";
document.Save("page-tree-inherited-titled.pdf");
#endregion

Sample.Finish();
