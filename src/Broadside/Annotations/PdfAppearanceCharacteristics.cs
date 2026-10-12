using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>An appearance characteristics dictionary: what a processor needs to construct a widget's or screen's appearance. A live view over <c>MK</c>.</summary>
/// <remarks>ISO 32000-2 §12.5.6.19, Table 192. Captions and icons apply to button fields; the colours and rotation to any widget.</remarks>
public sealed class PdfAppearanceCharacteristics
{
    private readonly PdfAnnotation _owner;

    internal PdfAppearanceCharacteristics(PdfAnnotation owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the appearance characteristics dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the number of degrees the annotation is rotated counterclockwise relative to the page (<c>R</c>): a multiple of 90; default 0.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192. A value that is not an integer multiple of 90 reads as 0 with a diagnostic.</remarks>
    public int Rotation
    {
        get
        {
            CosObject? value = Get(AnnotationNames.R);
            if (value is null)
            {
                return 0;
            }

            if (AnnotationValues.ReadInteger(_owner.Document, value) is { } degrees && degrees % 90 == 0)
            {
                return ((degrees % 360) + 360) % 360;
            }

            _owner.Report(DiagnosticCodes.AnnotationValueInvalid, "The appearance characteristics' rotation R is not a multiple of 90; it reads as 0.");
            return 0;
        }
    }

    /// <summary>Gets the border colour (<c>BC</c>), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfDeviceColor? BorderColor => ReadColor(AnnotationNames.BC);

    /// <summary>Gets the background colour (<c>BG</c>), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfDeviceColor? BackgroundColor => ReadColor(AnnotationNames.BG);

    /// <summary>Gets the normal caption (<c>CA</c>, button fields), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public string? NormalCaption => ReadText(AnnotationNames.NormalCaption);

    /// <summary>Gets the rollover caption (<c>RC</c>, push buttons), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public string? RolloverCaption => ReadText(AnnotationNames.RC);

    /// <summary>Gets the alternate (down) caption (<c>AC</c>, push buttons), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public string? AlternateCaption => ReadText(AnnotationNames.AC);

    /// <summary>Gets the normal icon (<c>I</c>, push buttons), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFormXObject? NormalIcon => ReadForm(AnnotationNames.I);

    /// <summary>Gets the rollover icon (<c>RI</c>, push buttons), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFormXObject? RolloverIcon => ReadForm(AnnotationNames.RI);

    /// <summary>Gets the alternate (down) icon (<c>IX</c>, push buttons), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFormXObject? AlternateIcon => ReadForm(AnnotationNames.IX);

    /// <summary>Gets the icon fit dictionary (<c>IF</c>, push buttons), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192, and §12.7.8.2, Table 250.</remarks>
    public CosDictionary? IconFit => _owner.Document.Resolve(Get(AnnotationNames.IF)) as CosDictionary;

    /// <summary>Gets where the caption is placed relative to the icon (<c>TP</c>, push buttons): 0 caption only (the default) to 6 overlaid.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192. A value outside 0 to 6 reads as 0 with a diagnostic.</remarks>
    public int TextPosition
    {
        get
        {
            CosObject? value = Get(AnnotationNames.TP);
            if (value is null)
            {
                return 0;
            }

            if (AnnotationValues.ReadInteger(_owner.Document, value) is { } position and >= 0 and <= 6)
            {
                return position;
            }

            _owner.Report(DiagnosticCodes.AnnotationValueInvalid, "The appearance characteristics' TP is not an integer from 0 to 6; it reads as 0.");
            return 0;
        }
    }

    private CosObject? Get(CosName key) => EntryReader.Get(_owner.Document, Dictionary, key);

    private PdfDeviceColor? ReadColor(CosName key)
    {
        PdfDeviceColor? color = AnnotationValues.ReadColor(_owner.Document, Get(key), out bool valid);
        if (!valid)
        {
            _owner.Report(DiagnosticCodes.ColorArrayInvalid, $"The appearance characteristics' {key.Value} entry is not an array of 0, 1, 3 or 4 numbers; it reads as absent.");
        }

        return color;
    }

    private string? ReadText(CosName key) =>
        EntryReader.Text(Get(key), key, new EntryReport(_owner.Document, DiagnosticCodes.AnnotationValueInvalid, _owner.DiagnosticReference, "The appearance characteristics"));

    private PdfFormXObject? ReadForm(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosStream:
                return PdfFormXObject.Create(_owner.Document, Dictionary[key]);
            default:
                _owner.Report(DiagnosticCodes.AnnotationValueInvalid, $"The appearance characteristics' {key.Value} entry shall be a form XObject; it is ignored.");
                return null;
        }
    }
}
