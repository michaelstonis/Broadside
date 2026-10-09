using Broadside.Filters;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// The configuration of a <see cref="PdfEngine"/>, set fluently: <c>Use*</c> methods select an implementation or behavior,
/// <c>With*</c> methods set a value.
/// </summary>
/// <remarks>
/// An engine copies its options when it is constructed, so changing an options object afterwards does not change any engine built
/// from it. The properties are settable so the same type binds through the options pattern of dependency injection.
/// </remarks>
public sealed class PdfOptions
{
    /// <summary>The default of <see cref="MaxDecodedStreamLength"/>: 1 GiB.</summary>
    public const long DefaultMaxDecodedStreamLength = 1L << 30;

    private readonly List<IStreamFilter> _filters = [];
    private long _maxDecodedStreamLength = DefaultMaxDecodedStreamLength;

    /// <summary>Gets or sets how deviations from the specification are treated. The default is <see cref="PdfReadingMode.Lenient"/>.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; set; } = PdfReadingMode.Lenient;

    /// <summary>Repairs deviations and records them as diagnostics on the document. The default.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseLenient()
    {
        ReadingMode = PdfReadingMode.Lenient;
        return this;
    }

    /// <summary>Throws a <see cref="Diagnostics.DiagnosticException"/> for the first deviation.</summary>
    /// <returns>These options.</returns>
    public PdfOptions UseStrict()
    {
        ReadingMode = PdfReadingMode.Strict;
        return this;
    }

    /// <summary>Gets or sets the most bytes one stream may decode to; longer output is truncated with a diagnostic. Default 1 GiB.</summary>
    /// <remarks>ISO 32000-2 §7.4. Guards against decompression bombs; a stream's <c>DL</c> entry is only a hint (§7.3.8.2, Table 5).</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxDecodedStreamLength
    {
        get => _maxDecodedStreamLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxDecodedStreamLength = value;
        }
    }

    /// <summary>Gets the filters registered with <see cref="UseFilter"/>, in registration order.</summary>
    /// <remarks>ISO 32000-2 §7.4.1.</remarks>
    public IReadOnlyList<IStreamFilter> Filters => _filters;

    /// <summary>
    /// Uses <paramref name="filter"/> for every stream whose <c>Filter</c> entry names <see cref="IStreamFilter.Name"/>, replacing the
    /// managed default of that name or adding a filter the defaults lack. A later registration of the same name wins.
    /// </summary>
    /// <param name="filter">The filter. Shared by every document and thread of the engine: it must keep no state between calls.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">
    /// The filter's name is <c>Crypt</c>, which the security handler decodes (§7.4.10), or an inline-image abbreviation such as
    /// <c>Fl</c> (§8.9.7, Table 92), which is always read as the full name it stands for.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.4.1. The filter extension point (ADR 0001).</remarks>
    public PdfOptions UseFilter(IStreamFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        CosName name = filter.Name ?? throw new ArgumentException("The filter has no name.", nameof(filter));
        if (name.Equals(FilterNames.Crypt) || FilterNames.TryExpandAbbreviation(name, out _))
        {
            throw new ArgumentException(
                $"A filter cannot be registered under /{name.Value}: Crypt belongs to the security handler and abbreviations are read as full names.",
                nameof(filter));
        }

        _filters.Add(filter);
        return this;
    }

    /// <summary>Sets <see cref="MaxDecodedStreamLength"/>.</summary>
    /// <param name="maxLength">The most bytes one stream may decode to.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is not positive.</exception>
    /// <remarks>ISO 32000-2 §7.4.</remarks>
    public PdfOptions WithMaxDecodedStreamLength(long maxLength)
    {
        MaxDecodedStreamLength = maxLength;
        return this;
    }
}
