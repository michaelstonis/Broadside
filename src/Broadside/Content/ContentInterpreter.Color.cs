using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>The colour operators of Table 73: <c>CS cs SC SCN sc scn G g RG rg K k</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.8. <c>CS</c>/<c>cs</c> select a colour space and its initial colour; the device names and Pattern name those
/// spaces, any other name is looked up in the current resources' <c>ColorSpace</c> subdictionary. <c>SC</c>/<c>sc</c> take one
/// number per component; <c>SCN</c>/<c>scn</c> also accept Pattern (the pattern's name last, after the underlying space's
/// components for an uncoloured pattern), Separation, DeviceN and ICCBased. <c>G g RG rg K k</c> select DeviceGray, DeviceRGB or
/// DeviceCMYK and set the colour. The graphics state keeps the space the operator selected; default colour spaces apply when painting.
/// </para>
/// <para>
/// Repairs, each recorded once per run: a name not in the resources reads as DeviceGray (an abbreviation G, RGB or CMYK as the
/// device space); too few components skip the operator, too many use the last ones; <c>sc</c> in a Pattern space, <c>scn</c> with a
/// name in another space and <c>scn</c> without a name in a Pattern space are skipped (a coloured pattern's name alone selects it in
/// a Pattern space with an underlying space, §8.7.3.2); <c>SC</c>/<c>sc</c> in a space that wants
/// <c>SCN</c>/<c>scn</c> is accepted. Allocates nothing once the spaces are cached.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private readonly float[] _colorScratch = new float[PdfColor.MaxComponents];

    /// <summary>
    /// Gets or sets a value indicating whether colour operators are ignored, as inside a Type 3 glyph described with <c>d1</c> or an
    /// uncoloured tiling pattern (§8.6.8: PDF 2.0 ignores them there). Set by the nested runs of issues #57 and #79.
    /// </summary>
    internal bool IgnoresColorOperators { get; set; }

    private void ExecuteColor(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        if (IgnoresColorOperators)
        {
            Report(ContentIssue.ColorOperatorIgnored, offset, "A colour operator inside a d1 glyph or an uncoloured pattern, where colours shall not be specified; ignored.");
            return;
        }

        switch (code)
        {
            case ContentOperatorCode.SetStrokeColorSpace or ContentOperatorCode.SetFillColorSpace:
                SetColorSpace(code == ContentOperatorCode.SetStrokeColorSpace, operands[0].Bytes, offset);
                break;
            case ContentOperatorCode.SetStrokeGray or ContentOperatorCode.SetFillGray:
                SetDeviceColor(code == ContentOperatorCode.SetStrokeGray, PdfDeviceGrayColorSpace.Instance, operands);
                break;
            case ContentOperatorCode.SetStrokeRgb or ContentOperatorCode.SetFillRgb:
                SetDeviceColor(code == ContentOperatorCode.SetStrokeRgb, PdfDeviceRgbColorSpace.Instance, operands);
                break;
            case ContentOperatorCode.SetStrokeCmyk or ContentOperatorCode.SetFillCmyk:
                SetDeviceColor(code == ContentOperatorCode.SetStrokeCmyk, PdfDeviceCmykColorSpace.Instance, operands);
                break;
            case ContentOperatorCode.SetStrokeColor or ContentOperatorCode.SetFillColor:
                SetColor(code == ContentOperatorCode.SetStrokeColor, operands, extended: false, offset);
                break;
            default:
                SetColor(code == ContentOperatorCode.SetStrokeColorExtended, operands, extended: true, offset);
                break;
        }
    }

    /// <summary><c>CS</c>/<c>cs</c>: selects the space and its initial colour (Table 73).</summary>
    private void SetColorSpace(bool stroke, ReadOnlySpan<byte> name, int offset)
    {
        ColorSpaceCache spaces = _context.Document.ColorSpaces;
        PdfColorSpace? space = spaces.FindNamed(_context.Resources, name, CurrentStream ?? _fallbackReference, out NamedLookup lookup);
        if (lookup == NamedLookup.Missing)
        {
            Report(ContentIssue.ColorSpaceMissing, offset, "A colour space name that is neither a family name nor in the resources' ColorSpace dictionary; DeviceGray is used.");
        }
        else if (lookup == NamedLookup.Abbreviation)
        {
            Report(ContentIssue.ColorSpaceAbbreviated, offset, "An inline image abbreviation (G, RGB or CMYK) names a colour space outside an inline image; it is read as the device space.");
        }

        space ??= PdfDeviceGrayColorSpace.Instance;
        if (space.ComponentCount > PdfColor.MaxComponents && space is PdfDeviceNColorSpace deviceN)
        {
            Report(ContentIssue.ColorComponentLimit, offset, "A DeviceN colour space has more than 32 colourants; colours are set in its alternate space.");
            space = deviceN.Alternate;
        }

        State.SetColor(stroke, space.GetInitialColor());
    }

    /// <summary><c>G g RG rg K k</c>: the operands were checked against the operator's fixed count.</summary>
    private void SetDeviceColor(bool stroke, PdfColorSpace space, ContentOperands operands)
    {
        Span<float> components = stackalloc float[4];
        int count = space.ComponentCount;
        for (int i = 0; i < count; i++)
        {
            components[i] = Component(operands[i].Number);
        }

        State.SetColor(stroke, new PdfColor(space, components[..count]));
    }

    /// <summary><c>SC sc SCN scn</c>.</summary>
    private void SetColor(bool stroke, ContentOperands operands, bool extended, int offset)
    {
        PdfColorSpace space = stroke ? State.StrokeColor.ColorSpace : State.FillColor.ColorSpace;
        int count = operands.Count;
        bool named = operands[count - 1].Kind == ContentOperandKind.Name;
        int numbers = named ? count - 1 : count;
        if (space is PdfPatternColorSpace pattern)
        {
            if (!extended || !named)
            {
                Report(ContentIssue.ColorOperatorInvalid, offset, "In a Pattern colour space only scn and SCN with a pattern name set the colour; the operator is skipped.");
                return;
            }

            ReadOnlySpan<byte> patternName = operands[count - 1].Bytes;
            if (numbers == 0 && pattern.ComponentCount > 0 && NamesColoredPattern(patternName))
            {
                // §8.7.3.2: a coloured pattern is selected by its name alone in any Pattern space; the underlying space's components
                // are for uncoloured patterns (§8.7.3.3).
                SetPattern(stroke, pattern, [], patternName, offset);
                return;
            }

            if (TakeComponents(operands, numbers, pattern.ComponentCount, offset, out Span<float> components))
            {
                SetPattern(stroke, pattern, components, patternName, offset);
            }

            return;
        }

        if (named)
        {
            Report(ContentIssue.ColorOperatorInvalid, offset, "scn or SCN with a name in a colour space that is not Pattern; the operator is skipped.");
            return;
        }

        if (!extended && space.Family is PdfColorSpaceFamily.IccBased or PdfColorSpaceFamily.Separation or PdfColorSpaceFamily.DeviceN)
        {
            Report(ContentIssue.ColorOperatorMismatch, offset, "sc or SC in an ICCBased, Separation or DeviceN colour space, which take scn and SCN; the colour is set anyway.");
        }

        if (TakeComponents(operands, numbers, Math.Min(space.ComponentCount, PdfColor.MaxComponents), offset, out Span<float> values))
        {
            State.SetColor(stroke, new PdfColor(space, values));
        }
    }

    /// <summary>Reads the last <paramref name="expected"/> numbers; too few skip the operator, too many are reported.</summary>
    private bool TakeComponents(ContentOperands operands, int numbers, int expected, int offset, out Span<float> components)
    {
        components = default;
        if (numbers < expected)
        {
            Report(ContentIssue.ColorOperandCount, offset, "A colour operator has fewer components than its colour space; the operator is skipped.");
            return false;
        }

        if (numbers > expected)
        {
            Report(ContentIssue.ColorOperandCount, offset, "A colour operator has more components than its colour space; the last ones are used.");
        }

        components = _colorScratch.AsSpan(0, expected);
        for (int i = 0; i < expected; i++)
        {
            components[i] = Component(operands[numbers - expected + i].Number);
        }

        return true;
    }

    /// <summary>Sets a Pattern colour: the pattern from the current resources and the matrix of the stream that names it (§8.7.2).</summary>
    private void SetPattern(bool stroke, PdfPatternColorSpace space, ReadOnlySpan<float> components, ReadOnlySpan<byte> name, int offset)
    {
        ColorSpaceCache spaces = _context.Document.ColorSpaces;
        CosObject? value = spaces.FindResource(_context.Resources, ColorSpaceNames.Pattern, name, out CosName? key);
        if (value is null)
        {
            Report(ContentIssue.PatternMissing, offset, "scn names a pattern that is not in the resources' Pattern dictionary; the colour paints nothing.");
        }

        CosObject? pattern = value is null ? null : spaces.Resolve(value);
        State.SetColor(stroke, new PdfColor(space, components, key, pattern, _context.StreamBaseMatrix));
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> names a pattern that is not an uncoloured tiling pattern: a coloured tiling pattern,
    /// a shading pattern, or one whose <c>PaintType</c> the colour decides.
    /// </summary>
    private bool NamesColoredPattern(ReadOnlySpan<byte> name)
    {
        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, ColorSpaceNames.Pattern, name, out _);
        if (value is null)
        {
            return false;
        }

        return document.Shadings.GetPattern(value, CurrentStream ?? _fallbackReference) switch
        {
            null => false,
            PdfTilingPattern tiling => !tiling.IsPaintTypeKnown || tiling.PaintType == PdfTilingPaintType.Colored,
            _ => true,
        };
    }

    /// <summary>A component as stored: the operand's value, kept within the float range.</summary>
    private static float Component(double value) => (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);
}
