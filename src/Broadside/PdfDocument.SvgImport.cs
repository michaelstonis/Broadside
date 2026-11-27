using Broadside.Graphics;
using Broadside.Graphics.Svg;

namespace Broadside;

/// <summary>Importing SVG (issue #643): a static SVG image drawn into a form XObject with the canvas operators, so it stays vector.</summary>
public sealed partial class PdfDocument
{
    /// <summary>Imports an SVG image as a form XObject that can be drawn on any page or form of this document.</summary>
    /// <param name="svg">The SVG document: UTF-8 or UTF-16 XML, or gzip-compressed (SVGZ).</param>
    /// <param name="options">The size, default font and resource resolver; <see langword="null"/> for the defaults.</param>
    /// <returns>
    /// The form: <c>BBox</c> <c>[0 0 width height]</c> in points (from the SVG at 0.75 point per pixel, or <see cref="SvgImportOptions.Width"/>
    /// and <see cref="SvgImportOptions.Height"/>), its content the SVG's shapes, paint, text and images. Draw it with
    /// <see cref="PdfCanvas.DrawForm"/> after scaling user space to the size wanted.
    /// </returns>
    /// <exception cref="ArgumentException">The input is not well-formed XML, has a document type declaration, has no <c>svg</c> root element, or is SVGZ that cannot be decompressed.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The document is in strict mode and the SVG has something the importer repairs or skips.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.10 (form XObjects), §8.5 (paths), §8.7.4.5.3 and §8.7.4.5.4 (axial and radial shadings), §8.7.3 (tiling patterns),
    /// §11.3.5 (blend modes), §11.6.5 (soft masks), §11.6.6 (transparency groups) and §9.4 (text). The supported subset of SVG 1.1:
    /// <c>svg</c>, <c>g</c>, <c>path</c>, the basic shapes, <c>text</c> and <c>tspan</c>, <c>image</c> (PNG, JPEG, SVG), <c>use</c>,
    /// <c>symbol</c>, <c>defs</c>, gradients, <c>clipPath</c>, <c>mask</c>, <c>pattern</c> and <c>style</c>, with presentation attributes,
    /// the <c>style</c> attribute and style sheets of type, class, id, attribute, descendant and child selectors. Filters, foreignObject,
    /// animation, scripts and markers are skipped with a diagnostic.
    /// </para>
    /// <para>
    /// Security: the XML reader refuses a document type declaration, so no entity is expanded and nothing outside the bytes is read.
    /// An <c>href</c> that is not a <c>data:</c> URI is resolved only by <see cref="SvgImportOptions.ResourceResolver"/>.
    /// </para>
    /// <para>
    /// Leniency (ADR 0005): a value that cannot be read skips its element or property and records an <see cref="Diagnostics.SvgDiagnostics"/>
    /// code on this document. Transparency raises the document to PDF 1.4, gradients to 1.3 and patterns to 1.2. A mutation.
    /// </para>
    /// </remarks>
    public PdfFormXObject CreateFormXObjectFromSvg(ReadOnlySpan<byte> svg, SvgImportOptions? options) =>
        SvgImporter.Import(this, svg, options ?? new SvgImportOptions(), depth: 0);

    /// <summary>Imports an SVG image as a form XObject with the default options.</summary>
    /// <param name="svg">The SVG document: UTF-8 or UTF-16 XML, or gzip-compressed (SVGZ).</param>
    /// <returns>The form, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</returns>
    /// <exception cref="ArgumentException">The input is not an SVG document, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The document is in strict mode and the SVG has something the importer repairs or skips.</exception>
    /// <remarks>ISO 32000-2 §8.10. As the overload with options, sized from the SVG.</remarks>
    public PdfFormXObject CreateFormXObjectFromSvg(ReadOnlySpan<byte> svg) => CreateFormXObjectFromSvg(svg, null);

    /// <summary>Imports an SVG image from <paramref name="svg"/>, read to its end from its current position, as a form XObject.</summary>
    /// <param name="svg">The stream. It is read, not disposed.</param>
    /// <returns>The form, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="svg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The input is not an SVG document, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The document is in strict mode and the SVG has something the importer repairs or skips.</exception>
    /// <remarks>ISO 32000-2 §8.10. As the span overload, with the default options.</remarks>
    public PdfFormXObject CreateFormXObjectFromSvg(Stream svg) => CreateFormXObjectFromSvg(svg, null);

    /// <summary>Imports an SVG image from <paramref name="svg"/>, read to its end from its current position, as a form XObject.</summary>
    /// <param name="svg">The stream. It is read, not disposed.</param>
    /// <param name="options">The size, default font and resource resolver; <see langword="null"/> for the defaults.</param>
    /// <returns>The form, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="svg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The input is not an SVG document, as for <see cref="CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The document is in strict mode and the SVG has something the importer repairs or skips.</exception>
    /// <remarks>ISO 32000-2 §8.10. As the span overload.</remarks>
    public PdfFormXObject CreateFormXObjectFromSvg(Stream svg, SvgImportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(svg);
        using var buffer = new MemoryStream();
        svg.CopyTo(buffer);
        return CreateFormXObjectFromSvg(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options);
    }
}
