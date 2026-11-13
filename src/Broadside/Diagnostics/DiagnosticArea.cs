namespace Broadside.Diagnostics;

/// <summary>The area of the library a <see cref="DiagnosticCode"/> belongs to; each area's codes are listed by one catalog class.</summary>
/// <remarks>The catalog class of an area is named after it, such as <see cref="FileStructureDiagnostics"/> for <see cref="FileStructure"/>.</remarks>
public enum DiagnosticArea
{
    /// <summary>A code outside Broadside's catalog, such as one a third-party filter, font program parser or security handler reports.</summary>
    None = 0,

    /// <summary>The file structure (<see cref="FileStructureDiagnostics"/>; ISO 32000-2 §7.5).</summary>
    FileStructure,

    /// <summary>Object syntax (<see cref="ObjectSyntaxDiagnostics"/>; ISO 32000-2 §7.2, §7.3).</summary>
    ObjectSyntax,

    /// <summary>Encryption and security handlers (<see cref="SecurityDiagnostics"/>; ISO 32000-2 §7.6).</summary>
    Security,

    /// <summary>The document catalog, page labels, document information and metadata (<see cref="DocumentDiagnostics"/>; ISO 32000-2 §7.7.2).</summary>
    Document,

    /// <summary>The page tree and page objects (<see cref="PageDiagnostics"/>; ISO 32000-2 §7.7.3).</summary>
    Page,

    /// <summary>Saving and conformance levels (<see cref="SavingDiagnostics"/>; ISO 32000-2 §7.5).</summary>
    Saving,

    /// <summary>Name trees, number trees, destinations and outlines (<see cref="NavigationDiagnostics"/>; ISO 32000-2 §7.9.6, §12.3).</summary>
    Navigation,

    /// <summary>Optional content (<see cref="OptionalContentDiagnostics"/>; ISO 32000-2 §8.11).</summary>
    OptionalContent,

    /// <summary>File specifications, embedded and associated files (<see cref="FileSpecificationDiagnostics"/>; ISO 32000-2 §7.11, §14.13).</summary>
    FileSpecification,

    /// <summary>Stream filters (<see cref="FilterDiagnostics"/>; ISO 32000-2 §7.4).</summary>
    Filter,

    /// <summary>The DCTDecode filter (<see cref="DctDiagnostics"/>; ISO 32000-2 §7.4.8).</summary>
    Dct,

    /// <summary>The CCITTFaxDecode filter (<see cref="CcittDiagnostics"/>; ISO 32000-2 §7.4.6).</summary>
    Ccitt,

    /// <summary>The JBIG2Decode filter (<see cref="Jbig2Diagnostics"/>; ISO 32000-2 §7.4.7).</summary>
    Jbig2,

    /// <summary>The JPXDecode filter (<see cref="JpxDiagnostics"/>; ISO 32000-2 §7.4.9).</summary>
    Jpx,

    /// <summary>Images (<see cref="ImageDiagnostics"/>; ISO 32000-2 §8.9).</summary>
    Image,

    /// <summary>Fonts (<see cref="FontDiagnostics"/>; ISO 32000-2 §9.5 to §9.9).</summary>
    Font,

    /// <summary>Charstrings (<see cref="CharStringDiagnostics"/>; Adobe Technical Note #5177).</summary>
    CharString,

    /// <summary>CFF font programs (<see cref="CffDiagnostics"/>; Adobe Technical Note #5176).</summary>
    Cff,

    /// <summary>Type 1 font programs (<see cref="Type1Diagnostics"/>; ISO 32000-2 §9.9).</summary>
    Type1,

    /// <summary>Type 3 fonts (<see cref="Type3Diagnostics"/>; ISO 32000-2 §9.6.4).</summary>
    Type3,

    /// <summary>CMaps (<see cref="CMapDiagnostics"/>; ISO 32000-2 §9.7.5).</summary>
    CMap,

    /// <summary>Mapping character codes to Unicode (<see cref="TextDiagnostics"/>; ISO 32000-2 §9.10).</summary>
    Text,

    /// <summary>Content streams (<see cref="ContentDiagnostics"/>; ISO 32000-2 §7.8.2, §8).</summary>
    Content,

    /// <summary>Functions (<see cref="FunctionDiagnostics"/>; ISO 32000-2 §7.10).</summary>
    Function,

    /// <summary>Color spaces and color operators (<see cref="ColorDiagnostics"/>; ISO 32000-2 §8.6).</summary>
    Color,

    /// <summary>Shadings and patterns (<see cref="ShadingDiagnostics"/>; ISO 32000-2 §8.7).</summary>
    Shading,

    /// <summary>Actions (<see cref="ActionDiagnostics"/>; ISO 32000-2 §12.6).</summary>
    Action,

    /// <summary>Annotations (<see cref="AnnotationDiagnostics"/>; ISO 32000-2 §12.5).</summary>
    Annotation,

    /// <summary>Interactive forms (<see cref="FormDiagnostics"/>; ISO 32000-2 §12.7).</summary>
    Form,

    /// <summary>Logical structure and tagged PDF (<see cref="StructureDiagnostics"/>; ISO 32000-2 §14.6 to §14.9).</summary>
    Structure,
}
