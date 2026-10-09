namespace Broadside;

/// <summary>The action types of ISO 32000-2 Table 201, as named by an action dictionary's <c>S</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.6.4.1, Table 201. The version in each member's remarks is the version that introduced the type.</remarks>
public enum PdfActionKind
{
    /// <summary>A type Table 201 does not list; the action is kept as it is and never performed.</summary>
    Unknown = 0,

    /// <summary><c>GoTo</c>: go to a destination in this document (PDF 1.1).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.2.</remarks>
    GoTo,

    /// <summary><c>GoToR</c>: go to a destination in another document (PDF 1.1).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.3.</remarks>
    GoToR,

    /// <summary><c>GoToE</c>: go to a destination in an embedded file (PDF 1.6).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4.</remarks>
    GoToE,

    /// <summary><c>GoToDp</c>: go to a document part (PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.5.</remarks>
    GoToDp,

    /// <summary><c>Launch</c>: launch an application or open a file (PDF 1.1). Never performed by the library.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6.</remarks>
    Launch,

    /// <summary><c>Thread</c>: begin reading an article thread (PDF 1.1).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7.</remarks>
    Thread,

    /// <summary><c>URI</c>: resolve a uniform resource identifier (PDF 1.1).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.8.</remarks>
    Uri,

    /// <summary><c>Sound</c>: play a sound (PDF 1.2; deprecated in PDF 2.0). Kept as data.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9.</remarks>
    Sound,

    /// <summary><c>Movie</c>: play a movie (PDF 1.2; deprecated in PDF 2.0). Kept as data.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.10.</remarks>
    Movie,

    /// <summary><c>Hide</c>: set or clear annotations' Hidden flags (PDF 1.2).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.11.</remarks>
    Hide,

    /// <summary><c>Named</c>: a predefined action such as <c>NextPage</c> (PDF 1.2).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.12.</remarks>
    Named,

    /// <summary><c>SubmitForm</c>: send form data to a URL (PDF 1.2). Never performed by the library.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.2.</remarks>
    SubmitForm,

    /// <summary><c>ResetForm</c>: set fields to their default values (PDF 1.2).</summary>
    /// <remarks>ISO 32000-2 §12.7.6.3.</remarks>
    ResetForm,

    /// <summary><c>ImportData</c>: import field values from a file (PDF 1.2). Never performed by the library.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.4.</remarks>
    ImportData,

    /// <summary><c>SetOCGState</c>: set the states of optional content groups (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.13.</remarks>
    SetOcgState,

    /// <summary><c>Rendition</c>: control the playing of multimedia content (PDF 1.5). Kept as data.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14.</remarks>
    Rendition,

    /// <summary><c>Trans</c>: update the display with a transition (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.15.</remarks>
    Transition,

    /// <summary><c>GoTo3DView</c>: set the view of a 3D annotation (PDF 1.6). Kept as data.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16.</remarks>
    GoTo3DView,

    /// <summary><c>JavaScript</c>: run an ECMAScript script (PDF 1.3). Kept as data; the library never runs scripts.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.17.</remarks>
    JavaScript,

    /// <summary><c>RichMediaExecute</c>: send a command to a rich media annotation's handler (PDF 2.0). Kept as data.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18.</remarks>
    RichMediaExecute,
}
