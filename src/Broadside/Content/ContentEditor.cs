namespace Broadside.Content;

/// <summary>
/// The editor processor: run through <see cref="PdfPage.RewriteContent(ContentEditor)"/> or
/// <see cref="Graphics.PdfFormXObject.RewriteContent(ContentEditor)"/>, it sees every operator of the page's or form's own content with
/// its byte range (<see cref="CurrentOperator"/>, <see cref="GetOperator"/>) alongside the usual events, and removes, replaces,
/// inserts before or after, or wraps operators. When the run ends the content is written again as one stream: untouched bytes are
/// copied as they were, edits take the place of the operators they name.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.8.2: content is a sequence of operators, each preceded by its operands, so an operator's range (first operand
/// through keyword) can be removed, replaced or surrounded without reading anything else again. A page's <c>Contents</c> array is
/// read and rewritten as one stream, its parts joined by the line feed the interpreter reads between them (Table 31: the parts
/// behave as if concatenated with at least one white-space character between them).
/// </para>
/// <para>
/// Operators are numbered from 0 in the order they appear in the stream being rewritten; operators of nested content (form
/// XObjects, Type 3 glyphs, patterns, soft masks) are not numbered, and while their events arrive <see cref="CurrentOperator"/> is
/// the operator of the rewritten stream that started them, such as the <c>Do</c> of a form. Edits may name any operator already
/// numbered, during the run or in <see cref="ContentProcessor.EndRun"/>; they are applied together after the run.
/// </para>
/// <para>
/// Order of edits at one operator: text inserted before it is written in the order it was inserted, after the opening text of every
/// wrap that starts there; text inserted after it in insertion order, before the closing text of every wrap that ends there. Wraps
/// nest: one that covers more operators encloses one that covers fewer, and of two wraps over the same operators the later
/// encloses the earlier. The last <see cref="Remove"/> or <see cref="Replace"/> of an operator wins. Inserted and replacing text is
/// separated from its neighbours by a line feed, and a removal that would join two tokens leaves a space between them, so the
/// tokens on each side of an edit read as they did.
/// </para>
/// <para>
/// The bookkeeping is done by the rewrite itself, so an editor overrides any <see cref="ContentProcessor"/> member freely without
/// calling the base. Run any other way (<see cref="PdfPage.ProcessContent(ContentProcessor)"/>), an editor numbers nothing and its
/// edit methods throw. One editor serves one rewrite at a time and keeps its buffers for the next, so a reused editor allocates
/// nothing per operator.
/// </para>
/// </remarks>
public abstract class ContentEditor : ContentProcessor
{
    private readonly OperatorRecorder _recorder;
    private CompositeContentProcessor? _run;
    private ReadOnlyMemory<byte> _content;
    private ContentPart[]? _parts;
    private Operator[] _operators = [];
    private int _operatorCount;
    private Edit[] _edits = [];
    private int _editCount;
    private byte[] _text = [];
    private int _textLength;
    private readonly ContentWriter _output = new();
    private bool _afterInsert;
    private int _sourceEnd;
    private bool _active;

    /// <summary>Initializes a new instance of the <see cref="ContentEditor"/> class.</summary>
    protected ContentEditor() => _recorder = new OperatorRecorder(this);

    /// <summary>
    /// Gets the number of the operator of the rewritten stream that is executing (the one whose events are being reported, or that
    /// started the nested content being reported); -1 before the first operator and outside a rewrite.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public int CurrentOperator => _active ? _operatorCount - 1 : -1;

    /// <summary>Gets the number of operators numbered so far in the running rewrite; 0 outside a rewrite.</summary>
    public int OperatorCount => _active ? _operatorCount : 0;

    /// <summary>Gets the content being rewritten: the decoded stream, or the parts of a <c>Contents</c> array joined; empty outside a rewrite.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, Table 31.</remarks>
    public ReadOnlyMemory<byte> Content => _active ? _content : ReadOnlyMemory<byte>.Empty;

    /// <summary>Returns where an operator of the rewritten stream is.</summary>
    /// <param name="index">The operator's number, from 0.</param>
    /// <returns>Its code and byte range in <see cref="Content"/>.</returns>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a number given so far.</exception>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public ContentOperatorRange GetOperator(int index)
    {
        CheckIndex(index);
        Operator op = _operators[index];
        return new ContentOperatorRange(op.Code, op.Start, op.End - op.Start);
    }

    /// <summary>Removes an operator and its operands.</summary>
    /// <param name="index">The operator's number.</param>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a number given so far.</exception>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public void Remove(int index)
    {
        CheckIndex(index);
        AddEdit(index, EditOrder.Replace, 0, []);
    }

    /// <summary>Replaces an operator and its operands with <paramref name="content"/>.</summary>
    /// <param name="index">The operator's number.</param>
    /// <param name="content">Content stream syntax to write instead; copied. Empty removes the operator.</param>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a number given so far.</exception>
    /// <remarks>ISO 32000-2 §7.8.2. Replacing an operator with its own bytes leaves it, and the bytes around it, exactly as they were.</remarks>
    public void Replace(int index, ReadOnlySpan<byte> content)
    {
        CheckIndex(index);
        AddEdit(index, EditOrder.Replace, 0, content);
    }

    /// <summary>Inserts <paramref name="content"/> before an operator's first operand.</summary>
    /// <param name="index">The operator's number.</param>
    /// <param name="content">Content stream syntax; copied.</param>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a number given so far.</exception>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public void InsertBefore(int index, ReadOnlySpan<byte> content)
    {
        CheckIndex(index);
        AddEdit(index, EditOrder.InsertBefore, 0, content);
    }

    /// <summary>Inserts <paramref name="content"/> after an operator's keyword.</summary>
    /// <param name="index">The operator's number.</param>
    /// <param name="content">Content stream syntax; copied.</param>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a number given so far.</exception>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public void InsertAfter(int index, ReadOnlySpan<byte> content)
    {
        CheckIndex(index);
        AddEdit(index, EditOrder.InsertAfter, 0, content);
    }

    /// <summary>
    /// Surrounds the operators <paramref name="first"/> through <paramref name="last"/>, such as a text object in a marked-content
    /// sequence (<c>/P &lt;&lt;/MCID 0&gt;&gt; BDC</c> … <c>EMC</c>) or a group of paints in <c>q</c> … <c>Q</c>.
    /// </summary>
    /// <param name="first">The number of the first operator inside.</param>
    /// <param name="last">The number of the last operator inside; at least <paramref name="first"/>.</param>
    /// <param name="before">Content stream syntax written before the first operator; copied.</param>
    /// <param name="after">Content stream syntax written after the last operator; copied.</param>
    /// <exception cref="InvalidOperationException">Called outside a rewrite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A number is not one given so far, or <paramref name="last"/> is before <paramref name="first"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.8.2; §14.6 (marked content), §8.4.2 (the state stack). Wraps that overlap without one containing the other
    /// produce content whose sequences do not nest; the caller keeps them nested.
    /// </remarks>
    public void Wrap(int first, int last, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        CheckIndex(first);
        CheckIndex(last);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
        AddEdit(first, EditOrder.WrapOpen, -last, before);
        AddEdit(last, EditOrder.WrapClose, -first, after);
    }

    /// <summary>Gets the content written by the last <see cref="Finish"/> that had edits.</summary>
    internal ReadOnlySpan<byte> Output => _output.Written;

    /// <summary>
    /// Starts a rewrite of <paramref name="content"/>, whose parts begin where <paramref name="parts"/> say (null: one part at 0),
    /// and returns the processor to run it with: the editor behind the recorder that numbers the operators.
    /// </summary>
    internal ContentProcessor Begin(ReadOnlyMemory<byte> content, ContentPart[]? parts)
    {
        if (_active)
        {
            throw new InvalidOperationException("The editor is already rewriting content; an editor serves one rewrite at a time.");
        }

        _active = true;
        _content = content;
        _parts = parts;
        _operatorCount = 0;
        _editCount = 0;
        _textLength = 0;
        _output.Clear();
        return _run ??= new CompositeContentProcessor(_recorder, this);
    }

    /// <summary>Applies the edits of the run to its content, into <see cref="Output"/>. Returns false, writing nothing, when there are none.</summary>
    internal bool Finish()
    {
        if (_editCount == 0)
        {
            return false;
        }

        _edits.AsSpan(0, _editCount).Sort(static (x, y) => Compare(x, y));
        Write();
        return true;
    }

    /// <summary>Ends the rewrite, keeping the buffers and letting go of the content.</summary>
    internal void End()
    {
        _active = false;
        _content = default;
        _parts = null;
    }

    /// <summary>Orders edits by operator, then by where they go, then (wraps) widest outside, then by insertion.</summary>
    private static int Compare(in Edit x, in Edit y)
    {
        int result = x.Operator.CompareTo(y.Operator);
        result = result != 0 ? result : ((int)x.Order).CompareTo((int)y.Order);
        result = result != 0 ? result : x.Key.CompareTo(y.Key);
        return result != 0 ? result : x.Sequence.CompareTo(y.Sequence);
    }

    private static bool IsEndOfLine(int value) => value is '\n' or '\r';

    private static bool IsRegular(int value) => value >= 0 && Objects.CosLexer.IsRegular((byte)value);

    private void CheckIndex(int index)
    {
        if (!_active)
        {
            throw new InvalidOperationException("Edits can be made only while the editor rewrites content (PdfPage.RewriteContent, PdfFormXObject.RewriteContent).");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _operatorCount);
    }

    private void Record(in ContentOperator op)
    {
        int partStart = _parts is { Length: > 0 } parts && op.PartIndex < parts.Length ? parts[op.PartIndex].Start : 0;
        int start = partStart + op.Offset;
        if (_operatorCount == _operators.Length)
        {
            Array.Resize(ref _operators, Math.Max(64, _operators.Length * 2));
        }

        _operators[_operatorCount++] = new Operator(op.Code, start, start + op.Length);
    }

    private void AddEdit(int index, EditOrder order, int key, ReadOnlySpan<byte> text)
    {
        if (_editCount == _edits.Length)
        {
            Array.Resize(ref _edits, Math.Max(16, _edits.Length * 2));
        }

        int textStart = _textLength;
        if (text.Length > _text.Length - _textLength)
        {
            Array.Resize(ref _text, Math.Max(Math.Max(256, _text.Length * 2), _textLength + text.Length));
        }

        text.CopyTo(_text.AsSpan(_textLength));
        _textLength += text.Length;

        // Wraps opening at one operator are written outermost first, so the later of two equal wraps sorts first there.
        int sequence = order == EditOrder.WrapOpen ? -_editCount : _editCount;
        _edits[_editCount++] = new Edit(index, order, key, sequence, textStart, text.Length);
    }

    /// <summary>Writes the content with the sorted edits applied: the bytes between and inside untouched operators are copied.</summary>
    private void Write()
    {
        ReadOnlySpan<byte> source = _content.Span;
        ReadOnlySpan<Edit> edits = _edits.AsSpan(0, _editCount);
        _output.Clear();
        _output.Reserve(source.Length + _textLength + 16);
        _afterInsert = false;
        _sourceEnd = -1;
        int position = 0;
        int next = 0;
        while (next < edits.Length)
        {
            int index = edits[next].Operator;
            Operator op = _operators[index];
            int start = Math.Clamp(op.Start, position, source.Length);
            int end = Math.Clamp(op.End, start, source.Length);
            CopySource(source, position, start);
            while (next < edits.Length && edits[next].Operator == index && edits[next].Order < EditOrder.Replace)
            {
                Insert(Text(edits[next++]));
            }

            int replacement = -1;
            while (next < edits.Length && edits[next].Operator == index && edits[next].Order == EditOrder.Replace)
            {
                replacement = next++;
            }

            if (replacement < 0 || Text(edits[replacement]).SequenceEqual(source[start..end]))
            {
                CopySource(source, start, end);
            }
            else
            {
                Insert(Text(edits[replacement]));
            }

            while (next < edits.Length && edits[next].Operator == index)
            {
                Insert(Text(edits[next++]));
            }

            position = end;
        }

        CopySource(source, position, source.Length);
    }

    private ReadOnlySpan<byte> Text(in Edit edit) => _text.AsSpan(edit.TextStart, edit.TextLength);

    /// <summary>
    /// Copies source bytes. After inserted text a line feed separates them unless one side already ends or starts a line (inserted
    /// text may end in a comment); where a removal brought two bytes together that were not adjacent, a space keeps two regular
    /// characters from joining into one token.
    /// </summary>
    private void CopySource(ReadOnlySpan<byte> source, int start, int end)
    {
        if (end <= start)
        {
            return;
        }

        ReadOnlySpan<byte> chunk = source[start..end];
        if (_afterInsert)
        {
            if (!IsEndOfLine(_output.Last) && !IsEndOfLine(chunk[0]))
            {
                _output.Write((byte)'\n');
            }
        }
        else if (_sourceEnd != start && IsRegular(_output.Last) && IsRegular(chunk[0]))
        {
            _output.Write((byte)' ');
        }

        _output.Write(chunk);
        _afterInsert = false;
        _sourceEnd = end;
    }

    /// <summary>Writes inserted or replacing text on its own: a line feed before it unless white-space already ends the output, and after it (see <see cref="CopySource"/>).</summary>
    private void Insert(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (_output.Length > 0 && (_afterInsert ? !IsEndOfLine(_output.Last) && !IsEndOfLine(text[0]) : !Objects.CosLexer.IsWhitespace((byte)_output.Last)))
        {
            _output.Write((byte)'\n');
        }

        _output.Write(text);
        _afterInsert = true;
        _sourceEnd = -1;
    }

    /// <summary>Where an edit goes relative to its operator; the values sort in writing order.</summary>
    private enum EditOrder
    {
        WrapOpen,
        InsertBefore,
        Replace,
        InsertAfter,
        WrapClose,
    }

    private readonly record struct Operator(ContentOperatorCode Code, int Start, int End);

    /// <param name="Operator">The operator's number.</param>
    /// <param name="Order">Where the edit goes.</param>
    /// <param name="Key">For a wrap, minus the operator at its other end, so wider wraps sort outside narrower ones; else 0.</param>
    /// <param name="Sequence">The edit's number, negated for an opening wrap, so equal wraps nest by insertion order.</param>
    /// <param name="TextStart">Where the edit's text starts in the text buffer.</param>
    /// <param name="TextLength">The length of the edit's text.</param>
    private readonly record struct Edit(int Operator, EditOrder Order, int Key, int Sequence, int TextStart, int TextLength);

    /// <summary>
    /// Runs ahead of the editor in the rewrite and numbers the operators of the rewritten stream (depth 0). It skips nested content
    /// itself, so a form the editor skips is not run at all.
    /// </summary>
    private sealed class OperatorRecorder(ContentEditor editor) : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Operators | ContentEvents.Forms;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (context.Depth == 0)
            {
                editor.Record(op);
            }
        }

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Skip;
    }
}
