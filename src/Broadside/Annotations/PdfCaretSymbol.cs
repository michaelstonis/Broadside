namespace Broadside.Annotations;

/// <summary>The symbol associated with a caret annotation, its <c>Sy</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.5.6.11, Table 183. Default <see cref="None"/>; other names read as the default.</remarks>
public enum PdfCaretSymbol
{
    /// <summary><c>None</c>: no symbol (the default).</summary>
    None,

    /// <summary><c>P</c>: a new paragraph symbol (¶).</summary>
    Paragraph,
}
