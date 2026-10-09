namespace Broadside;

/// <summary>The form of an XMP property value.</summary>
/// <remarks>ISO 16684-1 §6.3 (data model) and §7.5-7.7 (serialization).</remarks>
public enum XmpPropertyKind
{
    /// <summary>A simple value: text, or a URI written with <c>rdf:resource</c>.</summary>
    Simple,

    /// <summary>A structure: named fields, each itself a property.</summary>
    Structure,

    /// <summary>An unordered array, <c>rdf:Bag</c>.</summary>
    UnorderedArray,

    /// <summary>An ordered array, <c>rdf:Seq</c>.</summary>
    OrderedArray,

    /// <summary>An alternative array, <c>rdf:Alt</c>: alternatives of one value, such as one per language.</summary>
    Alternative,
}
