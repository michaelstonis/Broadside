namespace Broadside;

/// <summary>Which annotations a flatten places in the page content: those a viewer shows on screen, or those a printer prints.</summary>
/// <remarks>ISO 32000-2 §12.5.3, Table 167: the Hidden, NoView and Print flags.</remarks>
public enum PdfFlattenIntent
{
    /// <summary>What a viewer shows: every annotation except those with the Hidden or NoView flag.</summary>
    View,

    /// <summary>What a printer prints: only annotations with the Print flag and without the Hidden flag.</summary>
    Print,
}
