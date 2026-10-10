using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.CharStrings;

/// <summary>A deviation found while interpreting a charstring. Append only: each value is a bit of <see cref="CharStringReporter"/>.</summary>
internal enum CharStringIssue
{
    /// <summary>More operands than the argument stack holds; the glyph is dropped.</summary>
    StackOverflow,

    /// <summary>Too few or too many arguments for an operator; whole argument groups are used, the rest dropped.</summary>
    ArgumentCount,

    /// <summary>A subroutine number outside the subroutine INDEX; the call is skipped.</summary>
    SubroutineOutOfRange,

    /// <summary>Subroutines nested deeper than the limit; the glyph is dropped.</summary>
    SubroutineDepth,

    /// <summary>The glyph executes more operators than the budget; the glyph is dropped.</summary>
    BudgetExceeded,

    /// <summary>A reserved operator; the argument stack is cleared and interpretation goes on.</summary>
    UnknownOperator,

    /// <summary>A component of an accented character does not exist or is itself accented; the component is skipped.</summary>
    SeacComponentMissing,

    /// <summary>The charstring (or a subroutine) ends without endchar (or return); it is ended there.</summary>
    NoEndchar,

    /// <summary>A segment is drawn before any moveto; a contour starts at the current point.</summary>
    MovetoMissing,

    /// <summary>An operand an operator cannot use (division by zero, an index outside the transient array); a neutral value is used.</summary>
    OperandInvalid,

    /// <summary>The charstring ends inside an operand or a hint mask; the glyph is dropped.</summary>
    Truncated,

    /// <summary>Type 1 only: a flex is not seven points between OtherSubrs 1 and 0; the stray point or the flex is dropped.</summary>
    FlexMalformed,

    /// <summary>Type 1 only: a multiple master blend without a matching <c>/WeightVector</c>; the first master's values are used.</summary>
    BlendUnavailable,

    /// <summary>Type 1 only: a charstring draws before <c>hsbw</c> or <c>sbw</c>, or does not start with one; side bearing and width 0.</summary>
    NoWidth,

    /// <summary>Type 1 only: a glyph has more points than <see cref="FontProgramContext.MaxGlyphPoints"/> or its coordinates overflow; the glyph is dropped.</summary>
    GlyphTooComplex,
}

/// <summary>
/// Reports charstring issues of one font program, Type 1 or Type 2: in lenient mode each kind once per program (so rendering many
/// glyphs neither floods the document's diagnostics nor allocates), in strict mode every time, so the outline request that meets one
/// throws. An issue that drops the glyph is an <see cref="DiagnosticSeverity.Error"/> (glyphs are lost); a repair is a
/// <see cref="DiagnosticSeverity.Warning"/>.
/// </summary>
/// <remarks>ADR 0005. Thread-safe: programs are shared by every thread rendering their document.</remarks>
internal sealed class CharStringReporter(FontProgramContext context, string format)
{
    private int _reported;

    /// <summary>Reports an issue of a glyph, unless the same kind was reported before in lenient mode.</summary>
    /// <param name="issue">What went wrong.</param>
    /// <param name="glyphId">The glyph being interpreted.</param>
    /// <exception cref="DiagnosticException">In strict mode.</exception>
    public void Report(CharStringIssue issue, int glyphId)
    {
        int bit = 1 << (int)issue;
        bool strict = context.ReadingMode == PdfReadingMode.Strict;
        if (!strict && (Volatile.Read(ref _reported) & bit) != 0)
        {
            return;
        }

        Interlocked.Or(ref _reported, bit);
        (string code, string text) = Describe(issue);
        context.Report(
            code,
            DropsGlyph(issue) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"Glyph {glyphId} of the {format} program: {text}"));
    }

    /// <summary>Whether an issue drops the glyph (its outline is <see cref="GlyphOutlineStatus.Invalid"/>).</summary>
    private static bool DropsGlyph(CharStringIssue issue) => issue is CharStringIssue.StackOverflow or CharStringIssue.SubroutineDepth
        or CharStringIssue.BudgetExceeded or CharStringIssue.Truncated or CharStringIssue.GlyphTooComplex;

    private static (string Code, string Text) Describe(CharStringIssue issue) => issue switch
    {
        CharStringIssue.StackOverflow => (DiagnosticCodes.FontCharstringStackOverflow, $"its charstring pushes more than {CharStringLimits.MaxArguments} operands (Adobe Technical Note #5177, Appendix B; Type 1 Font Format §6.1 with multiple master data); the glyph is dropped."),
        CharStringIssue.ArgumentCount => (DiagnosticCodes.FontCharstringArgumentCount, "an operator has too few or too many arguments (Adobe Technical Note #5177 §4; Type 1 Font Format §6.4); whole argument groups are used and the rest dropped."),
        CharStringIssue.SubroutineOutOfRange => (DiagnosticCodes.FontCharstringSubrOutOfRange, "a subroutine call names a subroutine that does not exist (Adobe Technical Note #5177 §4.7; Type 1 Font Format §8.1); the call is skipped."),
        CharStringIssue.SubroutineDepth => (DiagnosticCodes.FontCharstringSubrDepth, $"subroutines nest deeper than {CharStringLimits.MaxSubroutineDepth} levels (Adobe Technical Note #5177, Appendix B; Type 1 Font Format §8.1); the glyph is dropped."),
        CharStringIssue.BudgetExceeded => (DiagnosticCodes.FontCharstringBudgetExceeded, "the charstring executes more operators than the configured limit (FontProgramContext.MaxCharStringOperators); the glyph is dropped."),
        CharStringIssue.UnknownOperator => (DiagnosticCodes.FontCharstringUnknownOperator, "a reserved operator occurs (Adobe Technical Note #5177, Appendix A; Type 1 Font Format §6.4), or return outside a subroutine; it is ignored and the argument stack cleared."),
        CharStringIssue.SeacComponentMissing => (DiagnosticCodes.FontCharstringSeacComponentMissing, "a component of the accented character (endchar with four arguments, Adobe Technical Note #5177, Appendix C; seac, Type 1 Font Format §6.4) is not in the font, or is itself accented; the component is skipped."),
        CharStringIssue.NoEndchar => (DiagnosticCodes.FontCharstringNoEndchar, "a charstring ends without endchar, or a subroutine without return (Adobe Technical Note #5177 §3.1; Type 1 Font Format §6.4); it is ended there."),
        CharStringIssue.MovetoMissing => (DiagnosticCodes.FontCharstringMovetoMissing, "a path segment comes before any moveto (Adobe Technical Note #5177 §4.1); the contour starts at the current point."),
        CharStringIssue.OperandInvalid => (DiagnosticCodes.FontCharstringOperandInvalid, "an arithmetic or storage operator gets an operand it cannot use (Adobe Technical Note #5177 §4.4, §4.5; div, Type 1 Font Format §6.5); 0 is used."),
        CharStringIssue.FlexMalformed => (DiagnosticCodes.FontType1FlexMalformed, "a flex does not record seven points between OtherSubrs 1 and 0 (Type 1 Font Format §8.3); the stray point or the flex is dropped."),
        CharStringIssue.BlendUnavailable => (DiagnosticCodes.FontType1BlendUnavailable, "a multiple master blend has no matching WeightVector (TN 5015 §3.13); the first master's values are used."),
        CharStringIssue.NoWidth => (DiagnosticCodes.FontType1NoWidth, "the charstring does not start with hsbw or sbw (Type 1 Font Format §6.4); its side bearing and width are 0."),
        CharStringIssue.GlyphTooComplex => (DiagnosticCodes.FontType1GlyphTooComplex, "the glyph has more points than the limit (FontProgramContext.MaxGlyphPoints) or its coordinates overflow; the glyph is dropped."),
        _ => (DiagnosticCodes.FontCharstringTruncated, "the charstring ends inside an operand or a hint mask (Adobe Technical Note #5177 §3.2, §4.3; Type 1 Font Format §6.2); the glyph is dropped."),
    };
}

/// <summary>StandardEncoding's glyph names, which accented characters name their components by.</summary>
/// <remarks>
/// Adobe Technical Note #5177 Appendix C (endchar with four arguments) and the Type 1 <c>seac</c> operator: the base and accent are
/// StandardEncoding codes, never codes of the font's own or the PDF's encoding.
/// </remarks>
internal static class StandardEncodingNames
{
    /// <summary>Gets the glyph name StandardEncoding gives a code; <see langword="null"/> for an unencoded code or one outside 0 to 255.</summary>
    public static string? Get(int code)
    {
        if ((uint)code > 255)
        {
            return null;
        }

        int sid = Cff.CffStandardData.StandardEncoding[code];
        return sid == 0 ? null : Cff.CffStandardData.Strings[sid];
    }
}
