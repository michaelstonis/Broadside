namespace Broadside.TestSupport;

/// <summary>One Font DICT of a CID-keyed font's FDArray (5176 §18) with its Private DICT values and local subroutines, for <see cref="CffBuilder"/>.</summary>
public sealed class CffFontDict
{
    /// <summary>Gets or sets the FontName, or <see langword="null"/> for a generated one.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the Font DICT's FontMatrix, or <see langword="null"/> to leave it out.</summary>
    public double[]? FontMatrix { get; set; }

    /// <summary>Gets or sets the Private DICT's defaultWidthX (written when not 0).</summary>
    public int DefaultWidthX { get; set; }

    /// <summary>Gets or sets the Private DICT's nominalWidthX (written when not 0).</summary>
    public int NominalWidthX { get; set; }

    /// <summary>Gets the local subroutines (written only when there is at least one).</summary>
    public List<byte[]> LocalSubrs { get; } = [];

    /// <summary>Gets or sets a value indicating whether the Font DICT has a Private entry.</summary>
    public bool WritePrivate { get; set; } = true;

    internal byte[] BuildDict(int fontName, int privateSize, int privateOffset)
    {
        var dict = new List<byte>(CffBuilder.DictInteger(fontName)) { 12, 38 };
        if (FontMatrix is { } matrix)
        {
            foreach (double value in matrix)
            {
                dict.AddRange(CffBuilder.DictReal(value));
            }

            dict.AddRange([12, 7]);
        }

        if (WritePrivate)
        {
            dict.AddRange(CffBuilder.DictInteger32(privateSize));
            dict.AddRange(CffBuilder.DictInteger32(privateOffset));
            dict.Add(18);
        }

        return [.. dict];
    }

    internal byte[] BuildPrivate()
    {
        var bytes = new List<byte>();
        if (DefaultWidthX != 0)
        {
            bytes.AddRange(CffBuilder.DictInteger(DefaultWidthX));
            bytes.Add(20);
        }

        if (NominalWidthX != 0)
        {
            bytes.AddRange(CffBuilder.DictInteger(NominalWidthX));
            bytes.Add(21);
        }

        if (LocalSubrs.Count > 0)
        {
            // The local Subrs INDEX follows the Private DICT, so its offset is the DICT's length (5-byte operand + operator).
            bytes.AddRange(CffBuilder.DictInteger32(bytes.Count + 6));
            bytes.Add(19);
        }

        return [.. bytes];
    }
}
