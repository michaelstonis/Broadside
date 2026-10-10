using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>UserProperties</c>: application-specific properties of an element, such as those of a CAD object.</summary>
/// <remarks>
/// ISO 32000-2 §14.7.6.4, Tables 361 and 362. The document's <c>MarkInfo</c> <c>UserProperties</c> shall be true when an element
/// carries user properties (<see cref="PdfMarkInfo.UserProperties"/>). A <c>P</c> entry that is not a dictionary, or lacks
/// <c>N</c> or <c>V</c>, is left out of <see cref="Properties"/>.
/// </remarks>
public sealed class PdfUserPropertiesAttributes : PdfAttributeObject
{
    internal PdfUserPropertiesAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>Gets the user properties (<c>P</c>), in order.</summary>
    /// <remarks>ISO 32000-2 Table 361 (<c>P</c>) and Table 362 (<c>N</c>, <c>V</c>, <c>F</c>, <c>H</c>).</remarks>
    public IReadOnlyList<PdfUserProperty> Properties
    {
        get
        {
            if (GetValue(StructureNames.P) is not CosArray array)
            {
                return [];
            }

            var properties = new List<PdfUserProperty>(array.Count);
            foreach (CosObject item in array)
            {
                if (Document.Resolve(item) is CosDictionary property
                    && ViewReading.Text(Document, property, StructureNames.N) is { } name
                    && ViewReading.Get(Document, property, StructureNames.V) is { } value)
                {
                    properties.Add(new PdfUserProperty(
                        property,
                        name,
                        value,
                        ViewReading.Text(Document, property, StructureNames.F),
                        ViewReading.Boolean(Document, property, StructureNames.H) ?? false));
                }
            }

            return properties;
        }
    }
}
