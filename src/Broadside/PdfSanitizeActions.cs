namespace Broadside;

/// <summary>The kinds of action <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/> removes.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4, Table 201. The kinds not named here (GoTo, GoToDp, Thread, Hide, Named, ResetForm, SetOCGState, Trans) move
/// within the document or change only how it is shown, and are kept. An action whose type is not one of Table 201's, or that has no
/// <c>S</c> entry, is removed whenever any kind is.
/// </remarks>
[Flags]
public enum PdfSanitizeActions
{
    /// <summary>No action is removed.</summary>
    None = 0,

    /// <summary>JavaScript actions (§12.6.4.17), with the document-level scripts of the name dictionary's <c>JavaScript</c> tree and the scripts of form fields' format, keystroke, validate and calculate triggers.</summary>
    JavaScript = 1,

    /// <summary>Launch actions (§12.6.4.6), which start an application or open a file.</summary>
    Launch = 2,

    /// <summary>URI actions (§12.6.4.8), which open a web address.</summary>
    Uri = 4,

    /// <summary>Submit-form actions (§12.7.6.2), which send field values to an address.</summary>
    SubmitForm = 8,

    /// <summary>Import-data actions (§12.7.6.4), which read field values from a file.</summary>
    ImportData = 16,

    /// <summary>Remote and embedded go-to actions, GoToR and GoToE (§12.6.4.3, §12.6.4.4), which open another file.</summary>
    RemoteGoTo = 32,

    /// <summary>Movie, Sound, Rendition, RichMediaExecute and GoTo3DView actions (§12.6.4.9 to §12.6.4.15, §13.7.2.3), which play media or drive 3D and rich-media content.</summary>
    Multimedia = 64,

    /// <summary>Every kind but <see cref="Uri"/>: the default, which keeps links to web addresses.</summary>
    Default = JavaScript | Launch | SubmitForm | ImportData | RemoteGoTo | Multimedia,

    /// <summary>Every kind.</summary>
    All = Default | Uri,
}
