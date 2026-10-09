using System.Globalization;
using Broadside.Objects;

namespace Broadside;

/// <summary>How a portable collection is first presented (<c>View</c>).</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 153. Default <see cref="Details"/>.</remarks>
public enum PdfCollectionView
{
    /// <summary>Details mode: a multi-column list (<c>D</c>).</summary>
    Details,

    /// <summary>Tile mode: icons and summary information (<c>T</c>).</summary>
    Tile,

    /// <summary>Hidden: the collection list is not shown initially (<c>H</c>).</summary>
    Hidden,

    /// <summary>Navigator (<c>C</c>, PDF 2.0): presented by the collection's <c>Navigator</c>.</summary>
    Navigator,
}

/// <summary>The kind of data a collection schema field shows (<c>Subtype</c>).</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
public enum PdfCollectionFieldType
{
    /// <summary>A text field from the collection item (<c>S</c>).</summary>
    Text,

    /// <summary>A date field from the collection item (<c>D</c>).</summary>
    Date,

    /// <summary>A number field from the collection item (<c>N</c>).</summary>
    Number,

    /// <summary>The file name of the embedded file (<c>F</c>).</summary>
    FileName,

    /// <summary>The file specification's description (<c>Desc</c>).</summary>
    Description,

    /// <summary>The embedded file's modification date (<c>ModDate</c>).</summary>
    ModificationDate,

    /// <summary>The embedded file's creation date (<c>CreationDate</c>).</summary>
    CreationDate,

    /// <summary>The embedded file's size (<c>Size</c>).</summary>
    Size,

    /// <summary>The length of the embedded file stream (<c>CompressedSize</c>, PDF 2.0).</summary>
    CompressedSize,

    /// <summary>A missing or unknown subtype; see <see cref="PdfCollectionField.TypeName"/>.</summary>
    Unknown,
}

/// <summary>How a collection's user interface is split between the file list and the preview (<c>Split</c> <c>Direction</c>).</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 158 (PDF 2.0).</remarks>
public enum PdfCollectionSplitDirection
{
    /// <summary>Split horizontally (<c>H</c>).</summary>
    Horizontal,

    /// <summary>Split vertically (<c>V</c>).</summary>
    Vertical,

    /// <summary>Not split (<c>N</c>): the file list only.</summary>
    None,
}

/// <summary>An RGB colour of a collection's user interface, each component 0 to 1.</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 157 (PDF 2.0): an array of three numbers in DeviceRGB.</remarks>
public readonly struct PdfCollectionColor : IEquatable<PdfCollectionColor>
{
    /// <summary>Initializes a new instance of the <see cref="PdfCollectionColor"/> struct.</summary>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    public PdfCollectionColor(double red, double green, double blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>Gets the red component.</summary>
    public double Red { get; }

    /// <summary>Gets the green component.</summary>
    public double Green { get; }

    /// <summary>Gets the blue component.</summary>
    public double Blue { get; }

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(PdfCollectionColor left, PdfCollectionColor right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when different.</returns>
    public static bool operator !=(PdfCollectionColor left, PdfCollectionColor right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfCollectionColor other) => Red.Equals(other.Red) && Green.Equals(other.Green) && Blue.Equals(other.Blue);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfCollectionColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Red} {Green} {Blue}]");
}

/// <summary>A range of folder IDs that are free for new folders.</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 159 (<c>Free</c>, PDF 2.0).</remarks>
public readonly struct PdfCollectionIdRange : IEquatable<PdfCollectionIdRange>
{
    /// <summary>Initializes a new instance of the <see cref="PdfCollectionIdRange"/> struct.</summary>
    /// <param name="low">The lowest free ID.</param>
    /// <param name="high">The highest free ID.</param>
    public PdfCollectionIdRange(int low, int high)
    {
        Low = low;
        High = high;
    }

    /// <summary>Gets the lowest free ID.</summary>
    public int Low { get; }

    /// <summary>Gets the highest free ID.</summary>
    public int High { get; }

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(PdfCollectionIdRange left, PdfCollectionIdRange right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when different.</returns>
    public static bool operator !=(PdfCollectionIdRange left, PdfCollectionIdRange right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfCollectionIdRange other) => Low.Equals(other.Low) && High.Equals(other.High);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfCollectionIdRange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Low, High);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Low} {High}]");
}

/// <summary>A field of a collection schema: a column of the details view. A live view over the field dictionary.</summary>
/// <remarks>ISO 32000-2 §12.3.5, Tables 154 and 155.</remarks>
public sealed class PdfCollectionField
{
    private readonly PdfDocument _document;

    internal PdfCollectionField(PdfDocument document, CosName key, CosDictionary dictionary)
    {
        _document = document;
        Key = key.Value;
        Dictionary = dictionary;
    }

    /// <summary>Gets the field's key in the schema, which collection items use for their values.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 154.</remarks>
    public string Key { get; }

    /// <summary>Gets the collection field dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the kind of data the field shows (<c>Subtype</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155 (required).</remarks>
    public PdfCollectionFieldType Type => TypeName?.Value switch
    {
        "S" => PdfCollectionFieldType.Text,
        "D" => PdfCollectionFieldType.Date,
        "N" => PdfCollectionFieldType.Number,
        "F" => PdfCollectionFieldType.FileName,
        "Desc" => PdfCollectionFieldType.Description,
        "ModDate" => PdfCollectionFieldType.ModificationDate,
        "CreationDate" => PdfCollectionFieldType.CreationDate,
        "Size" => PdfCollectionFieldType.Size,
        "CompressedSize" => PdfCollectionFieldType.CompressedSize,
        _ => PdfCollectionFieldType.Unknown,
    };

    /// <summary>Gets the <c>Subtype</c> name as written, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
    public CosName? TypeName => ViewReading.Name(_document, Dictionary, FileAndLayerNames.Subtype);

    /// <summary>Gets the field's name for the user interface (<c>N</c>); empty when missing.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155 (required).</remarks>
    public string Name => ViewReading.Text(_document, Dictionary, FileAndLayerNames.N) ?? string.Empty;

    /// <summary>Gets the field's relative position among the columns (<c>O</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
    public int? Order => ViewReading.Integer(_document, Dictionary, FileAndLayerNames.O) is { } order and >= int.MinValue and <= int.MaxValue ? (int)order : null;

    /// <summary>Gets a value indicating whether the field is shown initially (<c>V</c>, default <see langword="true"/>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
    public bool IsVisible => ViewReading.Boolean(_document, Dictionary, FileAndLayerNames.V, fallback: true);

    /// <summary>Gets a value indicating whether the user interface offers to edit the field (<c>E</c>, default <see langword="false"/>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 155.</remarks>
    public bool IsEditable => ViewReading.Boolean(_document, Dictionary, FileAndLayerNames.E, fallback: false);

    /// <inheritdoc/>
    public override string ToString() => Key;
}

/// <summary>The sort order of a collection's files. A live view over the collection sort dictionary.</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 156.</remarks>
public sealed class PdfCollectionSort
{
    private readonly PdfDocument _document;

    internal PdfCollectionSort(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the collection sort dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 156.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the schema field keys to sort by, primary first; later keys break ties (<c>S</c>, a name or an array).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 156 (required).</remarks>
    public IReadOnlyList<string> Keys => ViewReading.Get(_document, Dictionary, FileAndLayerNames.S) switch
    {
        CosName name => [name.Value],
        CosArray array => [.. array.Select(_document.Resolve).OfType<CosName>().Select(name => name.Value)],
        _ => [],
    };

    /// <summary>
    /// Gets, per entry of <see cref="Keys"/>, whether that key sorts ascending (<c>A</c>, a boolean or an array; default
    /// <see langword="true"/>). A short array is padded with <see langword="true"/>; extra entries are ignored.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 156.</remarks>
    public IReadOnlyList<bool> Ascending
    {
        get
        {
            int count = Keys.Count;
            var flags = new bool[count];
            Array.Fill(flags, true);
            switch (ViewReading.Get(_document, Dictionary, FileAndLayerNames.A))
            {
                case CosBoolean single when count > 0:
                    flags[0] = single.Value;
                    break;
                case CosArray array:
                    for (int index = 0; index < Math.Min(count, array.Count); index++)
                    {
                        flags[index] = _document.Resolve(array[index]) is not CosBoolean { Value: false };
                    }

                    break;
            }

            return flags;
        }
    }
}

/// <summary>The colours of a collection's user interface. A live view over the collection colours dictionary.</summary>
/// <remarks>ISO 32000-2 §12.3.5, Table 157 (PDF 2.0). An entry that is not three numbers reads as <see langword="null"/>.</remarks>
public sealed class PdfCollectionColors
{
    private readonly PdfDocument _document;

    internal PdfCollectionColors(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the collection colours dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the background colour (<c>Background</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColor? Background => Read(FileAndLayerNames.Background);

    /// <summary>Gets the background colour of a card (<c>CardBackground</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColor? CardBackground => Read(FileAndLayerNames.CardBackground);

    /// <summary>Gets the border colour of a card (<c>CardBorder</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColor? CardBorder => Read(FileAndLayerNames.CardBorder);

    /// <summary>Gets the colour of primary text (<c>PrimaryText</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColor? PrimaryText => Read(FileAndLayerNames.PrimaryText);

    /// <summary>Gets the colour of secondary text (<c>SecondaryText</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColor? SecondaryText => Read(FileAndLayerNames.SecondaryText);

    private PdfCollectionColor? Read(CosName key) =>
        ViewReading.Get(_document, Dictionary, key) is CosArray { Count: 3 } array
        && _document.Resolve(array[0]) is CosNumber red && _document.Resolve(array[1]) is CosNumber green && _document.Resolve(array[2]) is CosNumber blue
            ? new PdfCollectionColor(red.ToDouble(), green.ToDouble(), blue.ToDouble())
            : null;
}

/// <summary>The split of a collection's user interface: direction and position, with the defaults applied.</summary>
/// <remarks>
/// ISO 32000-2 §12.3.5, Table 158 (PDF 2.0). Without a <c>Split</c> entry the direction follows the view: horizontal for details,
/// vertical for tile, none for hidden and navigator.
/// </remarks>
public sealed class PdfCollectionSplit
{
    internal PdfCollectionSplit(PdfCollectionSplitDirection direction, double? position)
    {
        Direction = direction;
        Position = position;
    }

    /// <summary>Gets the split direction (<c>Direction</c>).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 158.</remarks>
    public PdfCollectionSplitDirection Direction { get; }

    /// <summary>Gets the split position as a percentage, 0 to 100 (<c>Position</c>), or <see langword="null"/>; ignored for <see cref="PdfCollectionSplitDirection.None"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 158.</remarks>
    public double? Position { get; }
}
