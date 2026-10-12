using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>Top-level runs that content rewriting needs beside a page's: a form XObject's own content.</summary>
internal sealed partial class ContentInterpreter
{
    /// <summary>
    /// Runs a form XObject's content as a top-level run (§8.10.1) of kind <see cref="ContentRunKind.Form"/>, as <c>Do</c> would from
    /// the initial graphics state: the form matrix as the CTM, the clip intersected with the bounding box, names resolving in the form's
    /// resources. No page is involved, so a form without Resources (allowed before PDF 1.2) resolves no names.
    /// </summary>
    internal static void RunForm(PdfFormXObject form, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        ContentInterpreter interpreter = Rent();
        try
        {
            interpreter.RunFormCore(form, document, processor, options);
        }
        finally
        {
            interpreter.Release();
        }
    }

    private void RunFormCore(PdfFormXObject form, PdfDocument document, ContentProcessor processor, ContentOptions options)
    {
        Prepare(document, page: null, processor, options, form.Reference);
        Matrix matrix = form.Matrix;
        _context.RunKind = ContentRunKind.Form;
        _context.Resources = form.Resources;
        _context.StreamBaseMatrix = matrix;
        _context.ContentStream = form.Stream;
        _context.StructParents = form.StructParents;
        _states[0].Ctm = matrix;
        _parts.Add(new ContentPart(0, form.Reference));
        Start();
        if (form.BoundingBox is { } box)
        {
            IntersectBox(box);
        }

        _frames[0] = new RunFrame(form.Stream, matrix, State);
        Execute(_context.GetContent(form.Stream).Span);
        End();
    }
}
