using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>
/// An attribute object of a structure element: a dictionary (or, in the legacy form, a stream) whose <c>O</c> entry names the owner
/// of the attributes it holds. Owners with typed attributes have a derived view: <see cref="PdfLayoutAttributes"/>,
/// <see cref="PdfListAttributes"/>, <see cref="PdfTableAttributes"/>, <see cref="PdfPrintFieldAttributes"/>,
/// <see cref="PdfArtifactAttributes"/> and <see cref="PdfUserPropertiesAttributes"/>; any other owner (an <c>NSO</c> namespace owner,
/// a format owner such as <c>CSS-3</c>, or a second-class name) is read through this class.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.6.1, Table 360 (<c>O</c>, and <c>NS</c> when <c>O</c> is <c>NSO</c>); §14.8.5.2, Table 376 (standard owners).
/// A view over its dictionary (ADR 0004): every value is read when asked for. <see cref="Revision"/> is the revision number that
/// follows the object in the element's <c>A</c> array or its class in <c>C</c> (§14.7.6.3, deprecated in PDF 2.0; 0 when absent).
/// </para>
/// <para>
/// Values are the object's own, as written. The value that applies to an element, after class attributes, inheritance and
/// defaults, comes from <see cref="PdfStructureElement.GetAttributeValue(CosName, CosName)"/>.
/// </para>
/// </remarks>
public class PdfAttributeObject
{
    private readonly StructureContext _context;

    private protected PdfAttributeObject(StructureContext context, CosObject source, CosReference? reference, int revision)
    {
        _context = context;
        Stream = source as CosStream;
        Dictionary = ViewReading.DictionaryOf(source) ?? new CosDictionary();
        Reference = reference;
        Revision = revision;
    }

    /// <summary>Gets the attribute object's dictionary (a stream's dictionary for the legacy stream form).</summary>
    /// <remarks>ISO 32000-2 §14.7.6.1, Table 360.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the stream when the attribute object is the legacy stream form, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.6.1: "Each attribute object shall be either a dictionary or a stream."</remarks>
    public CosStream? Stream { get; }

    /// <summary>Gets the indirect reference the attribute object was reached through, if any.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the owner (<c>O</c>), or <see langword="null"/> when the entry is missing.</summary>
    /// <remarks>ISO 32000-2 §14.7.6.1, Table 360 (required); Table 376 lists the standard owners.</remarks>
    public CosName? Owner => ViewReading.Name(Document, Dictionary, StructureNames.O);

    /// <summary>Gets the namespace that owns the attributes when <see cref="Owner"/> is <c>NSO</c>, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.6.1, Table 360 (<c>NS</c>, PDF 2.0, an indirect reference to a namespace dictionary).</remarks>
    public PdfStructureNamespace? Namespace =>
        StructureNames.NSO.Equals(Owner) && Dictionary.TryGetValue(StructureNames.NS, out CosObject? value) && Context.Resolve(value) is CosDictionary ns
            ? Context.Namespace(ns, value as CosReference)
            : null;

    /// <summary>Gets the revision number of the attribute object (deprecated in PDF 2.0); 0 when none was given.</summary>
    /// <remarks>ISO 32000-2 §14.7.6.3.</remarks>
    public int Revision { get; }

    /// <summary>Gets the document the attribute object belongs to.</summary>
    private protected PdfDocument Document => _context.Document;

    /// <summary>Gets the shared structure state.</summary>
    private protected StructureContext Context => _context;

    /// <summary>Returns the value of the attribute <paramref name="name"/> in this object, resolved, or <see langword="null"/> when absent.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value.</returns>
    /// <remarks>ISO 32000-2 §14.7.6.1.</remarks>
    public CosObject? GetValue(CosName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ViewReading.Get(Document, Dictionary, name);
    }

    /// <summary>Creates the view for an attribute object, typed by its owner.</summary>
    internal static PdfAttributeObject Create(StructureContext context, CosObject source, CosReference? reference, int revision)
    {
        CosDictionary dictionary = ViewReading.DictionaryOf(source) ?? new CosDictionary();
        CosName? owner = ViewReading.Name(context.Document, dictionary, StructureNames.O);
        return owner switch
        {
            _ when StructureNames.Layout.Equals(owner) => new PdfLayoutAttributes(context, source, reference, revision),
            _ when StructureNames.List.Equals(owner) => new PdfListAttributes(context, source, reference, revision),
            _ when StructureNames.Table.Equals(owner) => new PdfTableAttributes(context, source, reference, revision),
            _ when StructureNames.PrintField.Equals(owner) => new PdfPrintFieldAttributes(context, source, reference, revision),
            _ when StructureNames.Artifact.Equals(owner) => new PdfArtifactAttributes(context, source, reference, revision),
            _ when StructureNames.UserProperties.Equals(owner) => new PdfUserPropertiesAttributes(context, source, reference, revision),
            _ => new PdfAttributeObject(context, source, reference, revision),
        };
    }

    /// <summary>A name-valued attribute as a string.</summary>
    private protected string? NameValue(CosName name) => ViewReading.Name(Document, Dictionary, name)?.Value;

    /// <summary>A number-valued attribute.</summary>
    private protected double? NumberValue(CosName name) => ViewReading.Number(Document, Dictionary, name);

    /// <summary>An integer-valued attribute.</summary>
    private protected int? IntegerValue(CosName name) => ViewReading.Int32(Document, Dictionary, name);

    /// <summary>A text-string-valued attribute.</summary>
    private protected string? TextValue(CosName name) => ViewReading.Text(Document, Dictionary, name);

    /// <summary>An array of numbers.</summary>
    private protected IReadOnlyList<double>? NumbersValue(CosName name) => ViewReading.Numbers(Document, Dictionary, name);

    /// <summary>A rectangle.</summary>
    private protected PdfRectangle? RectangleValue(CosName name) => ViewReading.Rectangle(Document, Dictionary, name);
}
