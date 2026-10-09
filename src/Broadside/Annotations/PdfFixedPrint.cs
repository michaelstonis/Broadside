using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>A fixed print dictionary: how a watermark annotation is placed relative to the target media. A live view.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.22, Table 194. The rectangle is translated to the origin, transformed by <see cref="Matrix"/> and its bounding
/// box used in place of the annotation rectangle in the appearance algorithm (§12.5.5); then it is translated by
/// <see cref="HorizontalTranslation"/> and <see cref="VerticalTranslation"/>, fractions of the media's size.
/// </remarks>
public sealed class PdfFixedPrint
{
    private readonly PdfAnnotation _owner;

    internal PdfFixedPrint(PdfAnnotation owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the fixed print dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Table 194.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the matrix that transforms the annotation rectangle before rendering (<c>Matrix</c>); the identity by default.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Table 194.</remarks>
    public Matrix Matrix
    {
        get
        {
            if (!Dictionary.TryGetValue(AnnotationNames.Matrix, out CosObject? value))
            {
                return Matrix.Identity;
            }

            if (AnnotationValues.ReadMatrix(_owner.Document, value) is { } matrix)
            {
                return matrix;
            }

            _owner.Report(DiagnosticCodes.AnnotationValueInvalid, "The fixed print dictionary's Matrix is not six numbers; the identity is used.");
            return Matrix.Identity;
        }
    }

    /// <summary>Gets the horizontal translation as a fraction of the media's width (<c>H</c>; 1.0 is 100%); default 0.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Table 194.</remarks>
    public double HorizontalTranslation => ReadNumber(AnnotationNames.H);

    /// <summary>Gets the vertical translation as a fraction of the media's height (<c>V</c>; 1.0 is 100%); default 0.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Table 194.</remarks>
    public double VerticalTranslation => ReadNumber(AnnotationNames.V);

    /// <summary>Gets a value indicating whether <c>Type</c> is <c>FixedPrint</c>, as Table 194 requires.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Table 194.</remarks>
    public bool HasValidType
    {
        get
        {
            bool valid = Dictionary.TryGetValue(AnnotationNames.Type, out CosObject? type) && _owner.Document.Resolve(type) is CosName { Value: "FixedPrint" };
            if (!valid)
            {
                _owner.Report(DiagnosticCodes.AnnotationValueInvalid, "The fixed print dictionary's Type shall be FixedPrint.");
            }

            return valid;
        }
    }

    private double ReadNumber(CosName key)
    {
        if (!Dictionary.TryGetValue(key, out CosObject? value))
        {
            return 0;
        }

        if (AnnotationValues.ReadNumber(_owner.Document, value) is { } number)
        {
            return number;
        }

        _owner.Report(DiagnosticCodes.AnnotationValueInvalid, $"The fixed print dictionary's {key.Value} entry is not a number; 0 is used.");
        return 0;
    }
}
