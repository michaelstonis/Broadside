using System.Runtime.CompilerServices;

namespace Broadside.Fonts.CharStrings;

/// <summary>The implementation limits of charstring interpreters.</summary>
/// <remarks>Adobe Technical Note #5177 (Type 2 Charstring Format), Appendix B (p.33).</remarks>
internal static class CharStringLimits
{
    /// <summary>The argument stack depth.</summary>
    public const int MaxArguments = 48;

    /// <summary>The number of stem hints.</summary>
    public const int MaxStems = 96;

    /// <summary>The subroutine nesting depth.</summary>
    public const int MaxSubroutineDepth = 10;

    /// <summary>The number of elements of the transient array.</summary>
    public const int TransientArrayLength = 32;
}

/// <summary>A charstring argument stack, held inline: interpreting a glyph allocates nothing.</summary>
/// <remarks>Adobe Technical Note #5177, Appendix B: 48 arguments.</remarks>
[InlineArray(CharStringLimits.MaxArguments)]
internal struct CharStringStack
{
    private double _element;
}

/// <summary>The transient array of the Type 2 storage operators, held inline.</summary>
/// <remarks>Adobe Technical Note #5177 §4.5 and Appendix B: 32 elements.</remarks>
[InlineArray(CharStringLimits.TransientArrayLength)]
internal struct CharStringTransientArray
{
    private double _element;
}
