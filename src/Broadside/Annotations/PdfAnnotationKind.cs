namespace Broadside.Annotations;

/// <summary>The standard annotation types, the values of an annotation dictionary's <c>Subtype</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.5.6.1, Table 171. The set is extensible (§12.5.2): any other subtype reads as <see cref="Unknown"/>.</remarks>
public enum PdfAnnotationKind
{
    /// <summary>A subtype Table 171 does not list, or no subtype at all: a <see cref="PdfUnknownAnnotation"/>.</summary>
    Unknown,

    /// <summary><c>Text</c> (PDF 1.0, markup): a sticky note. ISO 32000-2 §12.5.6.4.</summary>
    Text,

    /// <summary><c>Link</c> (PDF 1.0): a hypertext link. ISO 32000-2 §12.5.6.5.</summary>
    Link,

    /// <summary><c>FreeText</c> (PDF 1.3, markup): text displayed on the page. ISO 32000-2 §12.5.6.6.</summary>
    FreeText,

    /// <summary><c>Line</c> (PDF 1.3, markup): a straight line. ISO 32000-2 §12.5.6.7.</summary>
    Line,

    /// <summary><c>Square</c> (PDF 1.3, markup): a rectangle. ISO 32000-2 §12.5.6.8.</summary>
    Square,

    /// <summary><c>Circle</c> (PDF 1.3, markup): an ellipse. ISO 32000-2 §12.5.6.8.</summary>
    Circle,

    /// <summary><c>Polygon</c> (PDF 1.5, markup): a closed polygon. ISO 32000-2 §12.5.6.9.</summary>
    Polygon,

    /// <summary><c>PolyLine</c> (PDF 1.5, markup): an open polygon. ISO 32000-2 §12.5.6.9.</summary>
    PolyLine,

    /// <summary><c>Highlight</c> (PDF 1.3, markup): highlighted text. ISO 32000-2 §12.5.6.10.</summary>
    Highlight,

    /// <summary><c>Underline</c> (PDF 1.3, markup): underlined text. ISO 32000-2 §12.5.6.10.</summary>
    Underline,

    /// <summary><c>Squiggly</c> (PDF 1.4, markup): text with a jagged underline. ISO 32000-2 §12.5.6.10.</summary>
    Squiggly,

    /// <summary><c>StrikeOut</c> (PDF 1.3, markup): struck-out text. ISO 32000-2 §12.5.6.10.</summary>
    StrikeOut,

    /// <summary><c>Caret</c> (PDF 1.5, markup): a text insertion point. ISO 32000-2 §12.5.6.11.</summary>
    Caret,

    /// <summary><c>Stamp</c> (PDF 1.3, markup): a rubber stamp. ISO 32000-2 §12.5.6.12.</summary>
    Stamp,

    /// <summary><c>Ink</c> (PDF 1.3, markup): freehand paths. ISO 32000-2 §12.5.6.13.</summary>
    Ink,

    /// <summary><c>Popup</c> (PDF 1.3): the pop-up window of a markup annotation. ISO 32000-2 §12.5.6.14.</summary>
    Popup,

    /// <summary><c>FileAttachment</c> (PDF 1.3, markup): an attached file. ISO 32000-2 §12.5.6.15.</summary>
    FileAttachment,

    /// <summary><c>Sound</c> (PDF 1.2, markup; deprecated in PDF 2.0): a sound clip. ISO 32000-2 §12.5.6.16.</summary>
    Sound,

    /// <summary><c>Movie</c> (PDF 1.2; deprecated in PDF 2.0): a movie. ISO 32000-2 §12.5.6.17.</summary>
    Movie,

    /// <summary><c>Screen</c> (PDF 1.5): a region where media clips play. ISO 32000-2 §12.5.6.18.</summary>
    Screen,

    /// <summary><c>Widget</c> (PDF 1.2): the appearance of an interactive form field. ISO 32000-2 §12.5.6.19.</summary>
    Widget,

    /// <summary><c>PrinterMark</c> (PDF 1.4): a printer's mark. ISO 32000-2 §12.5.6.20 and §14.11.3.</summary>
    PrinterMark,

    /// <summary><c>TrapNet</c> (PDF 1.3; deprecated in PDF 2.0): a trap network. ISO 32000-2 §12.5.6.21 and §14.11.6.2.</summary>
    TrapNet,

    /// <summary><c>Watermark</c> (PDF 1.6): content printed at a fixed size and position on the media. ISO 32000-2 §12.5.6.22.</summary>
    Watermark,

    /// <summary><c>3D</c> (PDF 1.6): 3D artwork. ISO 32000-2 §12.5.6.25 and §13.6.2.</summary>
    ThreeD,

    /// <summary><c>Redact</c> (PDF 1.7, markup): content to be removed. ISO 32000-2 §12.5.6.23.</summary>
    Redact,

    /// <summary><c>Projection</c> (PDF 2.0, markup): a projection annotation. ISO 32000-2 §12.5.6.24.</summary>
    Projection,

    /// <summary><c>RichMedia</c> (PDF 2.0): rich media content. ISO 32000-2 §12.5.6.25 and §13.7.2.</summary>
    RichMedia,
}
