namespace Broadside;

/// <summary>The relationship of an embedded go-to action's target to the current document (<c>R</c>).</summary>
/// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
public enum PdfEmbeddedTargetRelationship
{
    /// <summary><c>R</c> is missing or names neither <c>P</c> nor <c>C</c>.</summary>
    Unknown,

    /// <summary><c>P</c>: the target is the parent of the current document.</summary>
    Parent,

    /// <summary><c>C</c>: the target is a child (an embedded file) of the current document.</summary>
    Child,
}

/// <summary>The operation of a movie action (<c>Operation</c>).</summary>
/// <remarks>ISO 32000-2 §12.6.4.10, Table 213 (deprecated in PDF 2.0). Default <see cref="Play"/>.</remarks>
public enum PdfMovieOperation
{
    /// <summary>Start playing the movie.</summary>
    Play,

    /// <summary>Stop playing the movie.</summary>
    Stop,

    /// <summary>Pause a playing movie.</summary>
    Pause,

    /// <summary>Resume a paused movie.</summary>
    Resume,
}

/// <summary>The predefined action of a named action (<c>N</c>).</summary>
/// <remarks>ISO 32000-2 §12.6.4.12, Table 215. Other names are allowed but not portable; an unrecognised one "shall take no action".</remarks>
public enum PdfNamedOperation
{
    /// <summary>A name Table 215 does not list (see <see cref="PdfNamedAction.Name"/>), or none.</summary>
    Other,

    /// <summary><c>NextPage</c>: go to the next page.</summary>
    NextPage,

    /// <summary><c>PrevPage</c>: go to the previous page.</summary>
    PreviousPage,

    /// <summary><c>FirstPage</c>: go to the first page.</summary>
    FirstPage,

    /// <summary><c>LastPage</c>: go to the last page.</summary>
    LastPage,
}

/// <summary>The flags of a submit-form action (<c>Flags</c>); each member's value is its bit.</summary>
/// <remarks>ISO 32000-2 §12.7.6.2, Table 240. Bit 13 and bits above 14 are reserved.</remarks>
[Flags]
public enum PdfSubmitFormFlags
{
    /// <summary>No flag set: the default.</summary>
    None = 0,

    /// <summary>Bit 1, Include/Exclude: when set, <c>Fields</c> lists the fields to exclude rather than include.</summary>
    Exclude = 1 << 0,

    /// <summary>Bit 2, IncludeNoValueFields: submit fields that have no value too.</summary>
    IncludeNoValueFields = 1 << 1,

    /// <summary>Bit 3, ExportFormat: submit in HTML form format rather than FDF.</summary>
    ExportFormat = 1 << 2,

    /// <summary>Bit 4, GetMethod: submit with an HTTP GET request (HTML format only).</summary>
    GetMethod = 1 << 3,

    /// <summary>Bit 5, SubmitCoordinates: submit the coordinates of the mouse click.</summary>
    SubmitCoordinates = 1 << 4,

    /// <summary>Bit 6, XFDF (PDF 1.4): submit as XFDF.</summary>
    Xfdf = 1 << 5,

    /// <summary>Bit 7, IncludeAppendSaves (PDF 1.4): include the incremental updates in the FDF.</summary>
    IncludeAppendSaves = 1 << 6,

    /// <summary>Bit 8, IncludeAnnotations (PDF 1.4): include markup annotations in the FDF.</summary>
    IncludeAnnotations = 1 << 7,

    /// <summary>Bit 9, SubmitPDF (PDF 1.4): submit the whole document as PDF.</summary>
    SubmitPdf = 1 << 8,

    /// <summary>Bit 10, CanonicalFormat (PDF 1.4): submit dates in the canonical format.</summary>
    CanonicalFormat = 1 << 9,

    /// <summary>Bit 11, ExclNonUserAnnots (PDF 1.4): include only the current user's markup annotations.</summary>
    ExcludeNonUserAnnotations = 1 << 10,

    /// <summary>Bit 12, ExclFKey (PDF 1.4): leave out the FDF's F entry.</summary>
    ExcludeFKey = 1 << 11,

    /// <summary>Bit 14, EmbedForm (PDF 1.5): embed the PDF file in the FDF's F entry.</summary>
    EmbedForm = 1 << 13,
}

/// <summary>The flags of a reset-form action (<c>Flags</c>); each member's value is its bit.</summary>
/// <remarks>ISO 32000-2 §12.7.6.3, Table 242.</remarks>
[Flags]
public enum PdfResetFormFlags
{
    /// <summary>No flag set: the default.</summary>
    None = 0,

    /// <summary>Bit 1, Include/Exclude: when set, <c>Fields</c> lists the fields to exclude rather than reset.</summary>
    Exclude = 1 << 0,
}

/// <summary>What a set-OCG-state action does to the groups after a state name (<c>State</c>).</summary>
/// <remarks>ISO 32000-2 §12.6.4.13, Table 217.</remarks>
public enum PdfOcgStateOperation
{
    /// <summary><c>ON</c>: turn the groups on.</summary>
    On,

    /// <summary><c>OFF</c>: turn the groups off.</summary>
    Off,

    /// <summary><c>Toggle</c>: reverse the groups' states.</summary>
    Toggle,
}

/// <summary>The operation of a rendition action (<c>OP</c>).</summary>
/// <remarks>ISO 32000-2 §12.6.4.14, Table 218.</remarks>
public enum PdfRenditionOperation
{
    /// <summary>0: play the rendition <c>R</c> in the screen annotation <c>AN</c>, replacing any rendition it plays.</summary>
    Play = 0,

    /// <summary>1: stop the rendition playing in <c>AN</c>.</summary>
    Stop = 1,

    /// <summary>2: pause the rendition playing in <c>AN</c>.</summary>
    Pause = 2,

    /// <summary>3: resume the paused rendition in <c>AN</c>.</summary>
    Resume = 3,

    /// <summary>4: play <c>R</c> in <c>AN</c>, or resume it when it is paused.</summary>
    PlayOrResume = 4,
}
