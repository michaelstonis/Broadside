namespace Broadside.Content;

/// <summary>
/// Fans one run out to several processors, so that, for example, a text extractor and a geometry collector share one pass over the
/// content. Each processor receives only the events it asks for, in the order the processors were given.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2. <see cref="Events"/> is the union of the processors' events. A processor that answers
/// <see cref="ContentVisit.Skip"/> to a nested stream (a form, a Type 3 glyph) receives none of its events and not its end event,
/// while the others still do; the composite skips a nested stream only when every processor skips it.
/// </remarks>
public sealed class CompositeContentProcessor : ContentProcessor
{
    private readonly ContentProcessor[] _processors;
    private readonly ContentEvents[] _events;
    private readonly int[] _skipDepth;

    /// <summary>Initializes a new instance of the <see cref="CompositeContentProcessor"/> class.</summary>
    /// <param name="processors">The processors, in the order they receive each event.</param>
    /// <exception cref="ArgumentException">A processor is <see langword="null"/>.</exception>
    public CompositeContentProcessor(params ContentProcessor[] processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        if (Array.IndexOf(processors, null) >= 0)
        {
            throw new ArgumentException("A processor is null.", nameof(processors));
        }

        _processors = [.. processors];
        _events = new ContentEvents[_processors.Length];
        _skipDepth = new int[_processors.Length];
    }

    /// <summary>Gets the processors, in the order they receive each event.</summary>
    public IReadOnlyList<ContentProcessor> Processors => _processors;

    /// <inheritdoc/>
    public override ContentEvents Events
    {
        get
        {
            ContentEvents union = ContentEvents.None;
            foreach (ContentProcessor processor in _processors)
            {
                union |= processor.Events;
            }

            return union;
        }
    }

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            _events[index] = _processors[index].Events;
            _skipDepth[index] = 0;
            _processors[index].BeginRun(context);
        }
    }

    /// <inheritdoc/>
    public override void EndRun(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            _processors[index].EndRun(context);
        }
    }

    /// <inheritdoc/>
    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Operators))
            {
                _processors[index].VisitOperator(op, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void SaveState(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.StateStack))
            {
                _processors[index].SaveState(context);
            }
        }
    }

    /// <inheritdoc/>
    public override void RestoreState(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.StateStack))
            {
                _processors[index].RestoreState(context);
            }
        }
    }

    /// <inheritdoc/>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Paths))
            {
                _processors[index].PaintPath(path, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Clips))
            {
                _processors[index].IntersectClip(clip, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void BeginText(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Text))
            {
                _processors[index].BeginText(context);
            }
        }
    }

    /// <inheritdoc/>
    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Glyphs))
            {
                _processors[index].ShowGlyph(glyph, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void EndText(ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Text))
            {
                _processors[index].EndText(context);
            }
        }
    }

    /// <inheritdoc/>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Images))
            {
                _processors[index].PaintImage(image, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void PaintShading(in ShadingEvent shading, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.Shadings))
            {
                _processors[index].PaintShading(shading, context);
            }
        }
    }

    /// <inheritdoc/>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        bool anyEnters = false;
        for (int index = 0; index < _processors.Length; index++)
        {
            if (_skipDepth[index] > 0)
            {
                _skipDepth[index]++;
            }
            else if ((_events[index] & ContentEvents.Forms) != 0 && _processors[index].BeginForm(form, context) == ContentVisit.Skip)
            {
                _skipDepth[index] = 1;
            }
            else
            {
                anyEnters = true;
            }
        }

        return anyEnters ? ContentVisit.Enter : ContentVisit.Skip;
    }

    /// <inheritdoc/>
    public override void EndForm(in FormEvent form, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (_skipDepth[index] > 0)
            {
                _skipDepth[index]--;
            }
            else if ((_events[index] & ContentEvents.Forms) != 0)
            {
                _processors[index].EndForm(form, context);
            }
        }
    }

    /// <inheritdoc/>
    public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
        bool anyEnters = false;
        for (int index = 0; index < _processors.Length; index++)
        {
            if (_skipDepth[index] > 0)
            {
                _skipDepth[index]++;
            }
            else if ((_events[index] & ContentEvents.Type3GlyphContent) == 0 || _processors[index].BeginType3Glyph(glyph, context) == ContentVisit.Skip)
            {
                _skipDepth[index] = 1;
            }
            else
            {
                anyEnters = true;
            }
        }

        return anyEnters ? ContentVisit.Enter : ContentVisit.Skip;
    }

    /// <inheritdoc/>
    public override void EndType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (_skipDepth[index] > 0)
            {
                _skipDepth[index]--;
            }
            else
            {
                _processors[index].EndType3Glyph(glyph, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.MarkedContent))
            {
                _processors[index].BeginMarkedContent(mc, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.MarkedContent))
            {
                _processors[index].EndMarkedContent(mc, context);
            }
        }
    }

    /// <inheritdoc/>
    public override void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context)
    {
        for (int index = 0; index < _processors.Length; index++)
        {
            if (Wants(index, ContentEvents.MarkedContent))
            {
                _processors[index].MarkedContentPoint(mc, context);
            }
        }
    }

    private bool Wants(int index, ContentEvents events) => _skipDepth[index] == 0 && (_events[index] & events) != 0;
}
