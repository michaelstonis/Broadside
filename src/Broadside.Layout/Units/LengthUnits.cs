using System.Diagnostics.CodeAnalysis;

namespace Broadside.Layout.Flow.Units;

/// <summary>
/// Unit suffixes for <see cref="Length"/>, opted into with <c>using Broadside.Layout.Flow.Units;</c>: <c>20.Mm</c>, <c>2.5.Cm</c>,
/// <c>1.In</c> and <c>12.Pt</c>.
/// </summary>
/// <remarks>ISO 32000-2 §8.3.2.3: lengths are stored in points, 1/72 inch.</remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "C# 14 extension blocks, which the analyzer sees as nested types.")]
[SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case", Justification = "C# 14 extension blocks, which the analyzer sees as members named alike.")]
public static class LengthUnits
{
    /// <summary>Unit suffixes on whole numbers.</summary>
    /// <param name="value">The number of units.</param>
    extension(int value)
    {
        /// <summary>Gets the number as points.</summary>
        public Length Pt => Length.Points(value);

        /// <summary>Gets the number as millimeters.</summary>
        public Length Mm => Length.Millimeters(value);

        /// <summary>Gets the number as centimeters.</summary>
        public Length Cm => Length.Centimeters(value);

        /// <summary>Gets the number as inches.</summary>
        public Length In => Length.Inches(value);
    }

    /// <summary>Unit suffixes on numbers.</summary>
    /// <param name="value">The number of units.</param>
    extension(double value)
    {
        /// <summary>Gets the number as points.</summary>
        public Length Pt => Length.Points(value);

        /// <summary>Gets the number as millimeters.</summary>
        public Length Mm => Length.Millimeters(value);

        /// <summary>Gets the number as centimeters.</summary>
        public Length Cm => Length.Centimeters(value);

        /// <summary>Gets the number as inches.</summary>
        public Length In => Length.Inches(value);
    }
}
