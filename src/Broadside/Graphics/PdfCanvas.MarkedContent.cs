using Broadside.Objects;

namespace Broadside.Graphics;

public sealed partial class PdfCanvas
{
    /// <summary>For each open marked-content sequence, innermost last: true when it began inside a text object.</summary>
    private readonly List<bool> _marked = [];

    /// <summary>Gets the number of marked-content sequences begun and not yet ended.</summary>
    public int MarkedContentDepth => _marked.Count;

    /// <summary>Begins a marked-content sequence without a property list (<c>BMC</c>), such as a generic <c>Artifact</c>.</summary>
    /// <param name="tag">The tag: the role of the sequence, such as <c>Artifact</c> or a structure type.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.6.1, Table 352: end it with <see cref="EndMarkedContent"/>; sequences nest, and nest separately from text objects.</remarks>
    public void BeginMarkedContent(string tag) => BeginMarked(tag, properties: null);

    /// <summary>Begins a marked-content sequence with an inline property list (<c>BDC</c>), such as an artifact's <c>Type</c> and <c>Subtype</c> or an <c>MCID</c>.</summary>
    /// <param name="tag">The tag: the role of the sequence, such as <c>Artifact</c> or a structure type.</param>
    /// <param name="properties">The property list, written inline: it shall hold direct objects only.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is empty, or <paramref name="properties"/> holds an indirect reference or a stream.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.6.1, Table 352, §14.6.2 (property lists) and §14.8.2.2.2, Table 363 (artifact property lists).</remarks>
    public void BeginMarkedContent(string tag, CosDictionary properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        BeginMarked(tag, properties);
    }

    /// <summary>Ends the innermost marked-content sequence (<c>EMC</c>).</summary>
    /// <exception cref="InvalidOperationException">No sequence is open, the sequence began on the other side of a text object's <c>BT</c> or <c>ET</c>, a path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.6.1: marked-content sequences and text objects shall be properly (separately) nested.</remarks>
    public void EndMarkedContent()
    {
        ThrowIfPathOpen();
        if (_marked.Count == 0)
        {
            throw new InvalidOperationException("No marked-content sequence is open; EndMarkedContent shall follow BeginMarkedContent (ISO 32000-2 §14.6.1).");
        }

        if (_marked[^1] != _inText)
        {
            throw new InvalidOperationException("A marked-content sequence and a text object shall be properly nested (ISO 32000-2 §14.6.1): end the inner one first.");
        }

        _marked.RemoveAt(_marked.Count - 1);
        _writer.Operator("EMC");
    }

    private void BeginMarked(string tag, CosDictionary? properties)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        ThrowIfPathOpen();
        if (properties is not null && !IsDirect(properties))
        {
            throw new ArgumentException("An inline property list shall hold direct objects only (ISO 32000-2 §14.6.2).", nameof(properties));
        }

        _writer.Name(new CosName(tag));
        if (properties is null)
        {
            _writer.Operator("BMC");
        }
        else
        {
            CosWriter.Write(properties, _writer);
            _writer.Write((byte)' ');
            _writer.Operator("BDC");
        }

        _marked.Add(_inText);
    }

    /// <summary>Writes <c>EMC</c> for the open sequences that began inside the open text object (before <c>ET</c>), or for all of them.</summary>
    private void CloseMarked(bool insideTextOnly)
    {
        while (_marked.Count > 0 && (!insideTextOnly || _marked[^1]))
        {
            _marked.RemoveAt(_marked.Count - 1);
            _writer.Operator("EMC");
        }
    }

    /// <summary>Throws when a sequence begun inside the open text object is still open at <c>ET</c>.</summary>
    private void ThrowIfMarkedOpenInText()
    {
        if (_marked.Count > 0 && _marked[^1])
        {
            throw new InvalidOperationException("A marked-content sequence begun inside the text object is still open; end it before EndText (ISO 32000-2 §14.6.1).");
        }
    }

    private static bool IsDirect(CosObject value) => value switch
    {
        CosReference or CosStream => false,
        CosDictionary dictionary => dictionary.All(static entry => IsDirect(entry.Value)),
        CosArray array => array.All(IsDirect),
        _ => true,
    };
}
