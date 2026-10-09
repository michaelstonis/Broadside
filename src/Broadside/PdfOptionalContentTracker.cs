using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// Follows the marked-content nesting of a content stream and answers whether the current position is visible: the seam a content
/// interpreter calls on every <c>BDC</c>, <c>BMC</c> and <c>EMC</c>, and before drawing an optional XObject or annotation.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.11.3. A section is optional content when its tag is <c>OC</c> and its property list is a group or membership
/// dictionary (§8.11.3.2); other tags nest transparently. Content is visible only when every enclosing optional section is
/// (§8.11.2.1); use one tracker across a form XObject's content so the invoking section's visibility carries into it. Hidden content
/// is not drawn but its graphics state changes still apply (§8.11.3.1): that is the caller's part.
/// </para>
/// <para>
/// Not thread-safe: one tracker per interpretation. Pushing and popping allocate nothing after the nesting reached its maximum once.
/// </para>
/// </remarks>
public sealed class PdfOptionalContentTracker
{
    private static readonly CosName OC = FileAndLayerNames.OC;

    private readonly PdfOptionalContentProperties? _properties;
    private readonly PdfOptionalContentState? _state;
    private bool[] _hidden = new bool[16];
    private int _hiddenCount;

    /// <summary>Initializes a new instance of the <see cref="PdfOptionalContentTracker"/> class.</summary>
    /// <param name="properties">The document's optional content (<see cref="PdfDocument.OptionalContent"/>); <see langword="null"/> when it has none: everything is visible.</param>
    /// <param name="state">The group states to honour; the default states when <see langword="null"/>.</param>
    public PdfOptionalContentTracker(PdfOptionalContentProperties? properties, PdfOptionalContentState? state)
    {
        _properties = properties;
        _state = properties is null ? null : state ?? properties.GetDefaultStates();
    }

    /// <summary>Gets a value indicating whether content at the current nesting is visible.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1: visible only when every enclosing optional section is.</remarks>
    public bool IsVisible => _hiddenCount == 0;

    /// <summary>Gets the current marked-content nesting depth.</summary>
    public int Depth { get; private set; }

    /// <summary>Enters a marked-content section (<c>BDC</c> or <c>BMC</c>).</summary>
    /// <param name="tag">The marked-content tag.</param>
    /// <param name="properties">The resolved property list (a <c>BDC</c> operand or the named resource it names), or <see langword="null"/> for <c>BMC</c>.</param>
    /// <param name="inlineProperties">
    /// <see langword="true"/> when an <c>/OC</c> section's property list was written inline instead of as a named resource, which
    /// §8.11.3.2 forbids; it is still honoured, with an <c>OptionalContentInlineProperties</c> diagnostic.
    /// </param>
    /// <remarks>ISO 32000-2 §8.11.3.2 and §14.6.</remarks>
    public void BeginMarkedContent(CosName tag, CosObject? properties, bool inlineProperties = false)
    {
        ArgumentNullException.ThrowIfNull(tag);
        bool hidden = false;
        if (_properties is not null && tag.Equals(OC))
        {
            if (inlineProperties)
            {
                _properties.Document.DiagnosticSink.ReportOnce(
                    DiagnosticCodes.OptionalContentInlineProperties,
                    DiagnosticSeverity.Warning,
                    "An /OC marked-content section has an inline property list; it shall be a named resource. It is honoured.");
            }

            hidden = !_properties.IsVisible(properties, _state!);
        }

        if (Depth == _hidden.Length)
        {
            Array.Resize(ref _hidden, _hidden.Length * 2);
        }

        _hidden[Depth++] = hidden;
        if (hidden)
        {
            _hiddenCount++;
        }
    }

    /// <summary>Leaves the innermost marked-content section (<c>EMC</c>). An <c>EMC</c> without a section is ignored.</summary>
    /// <remarks>ISO 32000-2 §14.6.</remarks>
    public void EndMarkedContent()
    {
        if (Depth == 0)
        {
            return;
        }

        if (_hidden[--Depth])
        {
            _hiddenCount--;
        }
    }

    /// <summary>
    /// Returns whether an XObject or annotation with the given dictionary is visible here: the current nesting is visible and its
    /// <c>OC</c> entry, if any, says so. An annotation's flags are the caller's concern.
    /// </summary>
    /// <param name="dictionary">A form or image XObject's stream dictionary, or an annotation dictionary.</param>
    /// <returns><see langword="true"/> when it shall be drawn.</returns>
    /// <remarks>ISO 32000-2 §8.11.3.3 (Tables 87, 93 and 166, <c>OC</c>, PDF 1.5).</remarks>
    public bool IsObjectVisible(CosDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        if (!IsVisible)
        {
            return false;
        }

        return _properties is null || !dictionary.TryGetValue(OC, out CosObject? value) || _properties.IsVisible(value, _state!);
    }

    /// <summary>Forgets every open section, for reuse with another content stream.</summary>
    public void Reset()
    {
        Depth = 0;
        _hiddenCount = 0;
    }
}
