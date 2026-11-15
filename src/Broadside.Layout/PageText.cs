using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>A value filled in per page when a header or footer is drawn: <see cref="Number"/>, <see cref="Count"/> or <see cref="Label"/>.</summary>
/// <remarks>Interpolate it into a <see cref="PageText"/>: <c>$"Page {PageField.Number} of {PageField.Count}"</c>. ISO 32000-2 §12.4.2 (page labels).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly struct PageField : IEquatable<PageField>
{
    private readonly byte _kind;

    private PageField(byte kind) => _kind = kind;

    /// <summary>Gets the page's number: 1 for the first page the builder lays out.</summary>
    public static PageField Number => new(1);

    /// <summary>Gets the number of pages the builder laid out.</summary>
    public static PageField Count => new(2);

    /// <summary>Gets the page's label (ISO 32000-2 §12.4.2) when the document has page labels, else its number.</summary>
    public static PageField Label => new(3);

    /// <summary>The engine's field text for this value.</summary>
    internal string Field => _kind switch
    {
        1 => HeaderFooter.PageNumberField,
        2 => HeaderFooter.PageCountField,
        3 => HeaderFooter.PageLabelField,
        _ => throw new InvalidOperationException("The page field is not one of PageField.Number, PageField.Count or PageField.Label."),
    };

    /// <summary>Compares two fields.</summary>
    /// <param name="left">The first field.</param>
    /// <param name="right">The second field.</param>
    /// <returns>Whether they are the same field.</returns>
    public static bool operator ==(PageField left, PageField right) => left.Equals(right);

    /// <summary>Compares two fields.</summary>
    /// <param name="left">The first field.</param>
    /// <param name="right">The second field.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(PageField left, PageField right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PageField other) => _kind == other._kind;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PageField other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _kind;

    /// <inheritdoc/>
    public override string ToString() => _kind switch
    {
        1 => nameof(Number),
        2 => nameof(Count),
        3 => nameof(Label),
        _ => "None",
    };
}

/// <summary>
/// The text of a header or footer slot: literal text and <see cref="PageField"/> values. A plain string is always literal (a string
/// holding <c>{page}</c> prints <c>{page}</c>, so data can never inject a field); an interpolated string fills a <see cref="PageField"/> in
/// per page and formats every other hole as the string would.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.2.2: headers and footers are drawn as pagination artifacts.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
[InterpolatedStringHandler]
public readonly struct PageText : IEquatable<PageText>
{
    private readonly StringBuilder? _builder;
    private readonly string? _encoded;

    /// <summary>Starts an interpolated page text; the compiler calls this.</summary>
    /// <param name="literalLength">The length of the literal parts.</param>
    /// <param name="formattedCount">The number of holes.</param>
    public PageText(int literalLength, int formattedCount)
    {
        _ = formattedCount;
        _builder = new StringBuilder(literalLength + 16);
    }

    private PageText(string encoded) => _encoded = encoded;

    /// <summary>Gets the text with every field written as its placeholder (<c>{page}</c>, <c>{pages}</c>, <c>{label}</c>) and every literal brace doubled.</summary>
    internal string Encoded => _encoded ?? _builder?.ToString() ?? string.Empty;

    /// <summary>Returns a page text that prints <paramref name="text"/> literally.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The page text.</returns>
    public static PageText FromString(string? text) => new(Escape(text ?? string.Empty));

    /// <summary>Converts a string to a page text that prints it literally.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator PageText(string? text) => FromString(text);

    /// <summary>Appends literal text; the compiler calls this.</summary>
    /// <param name="value">The text.</param>
    public void AppendLiteral(string value) => AppendEscaped(value);

    /// <summary>Appends a field filled in per page; the compiler calls this.</summary>
    /// <param name="field">The field.</param>
    public void AppendFormatted(PageField field) => _builder?.Append(field.Field);

    /// <summary>Appends a value as literal text, formatted as a string interpolation would; the compiler calls this.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    public void AppendFormatted<T>(T value) => AppendEscaped(value?.ToString());

    /// <summary>Appends a value as literal text with a format string; the compiler calls this.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted<T>(T value, string? format) => AppendEscaped(value is IFormattable formattable ? formattable.ToString(format, null) : value?.ToString());

    /// <summary>Appends a string as literal text; the compiler calls this.</summary>
    /// <param name="value">The text.</param>
    public void AppendFormatted(string? value) => AppendEscaped(value);

    /// <inheritdoc/>
    public bool Equals(PageText other) => string.Equals(Encoded, other.Encoded, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PageText other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Encoded);

    /// <summary>Returns the text with each field shown as its placeholder.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Encoded;

    /// <summary>Compares two page texts.</summary>
    /// <param name="left">The first text.</param>
    /// <param name="right">The second text.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool operator ==(PageText left, PageText right) => left.Equals(right);

    /// <summary>Compares two page texts.</summary>
    /// <param name="left">The first text.</param>
    /// <param name="right">The second text.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(PageText left, PageText right) => !left.Equals(right);

    private static string Escape(string text) => text.Contains('{', StringComparison.Ordinal) ? text.Replace("{", "{{", StringComparison.Ordinal) : text;

    private void AppendEscaped(string? value)
    {
        if (string.IsNullOrEmpty(value) || _builder is null)
        {
            return;
        }

        _builder.Append(Escape(value));
    }
}
