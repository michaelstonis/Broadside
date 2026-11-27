using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The entries or elements of a set of live COS containers at one moment: direct dictionaries and arrays are deep-copied, indirect
/// references and streams are kept as they are (an appearance stream is never copied). <see cref="Restore"/> writes them back into
/// the same containers, in the same order, so the <c>Pdf*</c> views over them stay valid.
/// </summary>
/// <remarks>ISO 32000-2 §7.3.7 (dictionaries), §7.3.6 (arrays), §7.3.10 (indirect objects).</remarks>
internal sealed class CosSnapshot
{
    private readonly (CosObject Live, CosObject Copy)[] _states;

    private CosSnapshot((CosObject Live, CosObject Copy)[] states) => _states = states;

    /// <summary>Captures the containers' current state; each container once, in the order first given.</summary>
    public static CosSnapshot Capture(IEnumerable<CosObject> containers)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var states = new List<(CosObject, CosObject)>();
        foreach (CosObject container in containers)
        {
            if (container is (CosDictionary or CosArray) && seen.Add(container))
            {
                states.Add((container, Copy(container)));
            }
        }

        return new CosSnapshot([.. states]);
    }

    /// <summary>Gets whether every container is still as captured (entry order aside).</summary>
    public bool Matches()
    {
        foreach ((CosObject live, CosObject copy) in _states)
        {
            if (!CosObject.DeepEquals(live, copy))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the captured state back into the containers that differ from it, each from a fresh copy so the snapshot can be restored
    /// again. A container already as captured is left alone, so it stays unchanged for the writer and for the caches over it.
    /// </summary>
    public void Restore()
    {
        foreach ((CosObject live, CosObject copy) in _states)
        {
            if (CosObject.DeepEquals(live, copy))
            {
                continue;
            }

            switch (live)
            {
                case CosDictionary dictionary:
                    dictionary.Clear();
                    foreach ((CosName key, CosObject value) in (CosDictionary)copy)
                    {
                        dictionary.Add(key, Copy(value));
                    }

                    break;
                case CosArray array:
                    array.Clear();
                    foreach (CosObject value in (CosArray)copy)
                    {
                        array.Add(Copy(value));
                    }

                    break;
            }
        }
    }

    private static CosObject Copy(CosObject value)
    {
        switch (value)
        {
            case CosDictionary dictionary:
                var copy = new CosDictionary();
                foreach ((CosName key, CosObject entry) in dictionary)
                {
                    copy.Add(key, Copy(entry));
                }

                return copy;
            case CosArray array:
                var items = new CosArray();
                foreach (CosObject item in array)
                {
                    items.Add(Copy(item));
                }

                return items;
            default:
                return value; // references, streams and immutable scalars
        }
    }
}

/// <summary>The page of a step is no longer in the document (a page organizer removed it): the step and all older ones are dropped.</summary>
internal sealed class EditStepPageRemovedException : InvalidOperationException
{
    public EditStepPageRemovedException()
        : base("The page the edit was made on is no longer in the document.")
    {
    }

    public EditStepPageRemovedException(string message)
        : base(message)
    {
    }

    public EditStepPageRemovedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Where a built-in step draws again: an area of a page, found by the page view so a moved page is still found.</summary>
/// <param name="Page">The page.</param>
/// <param name="Area">The area, or <see langword="null"/> for the whole page.</param>
/// <param name="Layers">The layers.</param>
/// <param name="Required">Whether the step cannot apply without the page (an annotation's page, not a widget's).</param>
internal readonly record struct StepArea(PdfPage Page, PdfRectangle? Area, DisplayLayers Layers, bool Required);

/// <summary>
/// A built-in step: the state of the COS containers an edit changed, before and after it. Undo checks the containers are as the edit
/// left them and writes the state before back; redo the reverse. The annotation steps capture the page dictionary, its <c>Annots</c>
/// array and every annotation dictionary it lists, so an add or a remove (with the popup and the <c>IRT</c> replies it took) restores the
/// array's order exactly; the field steps capture the field, its ancestors, its widgets and the interactive form dictionary.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.2 (annotation dictionaries), §12.7.4.1 (field dictionaries).</remarks>
internal sealed class StateEditStep : IEditStep
{
    private static readonly CosName Annots = new("Annots");

    private readonly CosSnapshot _before;
    private readonly List<StepArea> _areas;
    private CosSnapshot _after;

    public StateEditStep(string name, CosSnapshot before, CosSnapshot after, IEnumerable<StepArea> areas, object? mergeKey = null)
    {
        Name = name;
        _before = before;
        _after = after;
        _areas = [.. areas];
        MergeKey = mergeKey;
    }

    public string Name { get; }

    /// <summary>Gets what a later step must edit to merge into this one (a field's dictionary), or <see langword="null"/>.</summary>
    public object? MergeKey { get; }

    public IReadOnlyList<PageInvalidation> Undo(PdfDocument document) => Apply(document, _after, _before);

    public IReadOnlyList<PageInvalidation> Redo(PdfDocument document) => Apply(document, _before, _after);

    /// <summary>Takes a later edit of the same thing into this step: its state after, and its areas.</summary>
    public void Merge(StateEditStep later)
    {
        _after = later._after;
        _areas.AddRange(later._areas);
    }

    /// <summary>The state of the page's annotations: what an annotation step captures before and after the edit.</summary>
    public static CosSnapshot CapturePage(PdfDocument document, PdfPage page, params CosDictionary[] more)
    {
        var containers = new List<CosObject> { page.Dictionary };
        if (page.Dictionary.TryGetValue(Annots, out CosObject? own) && document.Resolve(own) is CosArray annots)
        {
            containers.Add(annots);
            foreach (CosObject element in annots)
            {
                if (document.Resolve(element) is CosDictionary annotation)
                {
                    containers.Add(annotation);
                }
            }
        }

        containers.AddRange(more);
        return CosSnapshot.Capture(containers);
    }

    /// <summary>The state of some fields: what a field step captures before and after the edit.</summary>
    public static CosSnapshot CaptureFields(PdfDocument document, IEnumerable<PdfTerminalField> fields)
    {
        var containers = new List<CosObject>();
        if (document.AcroForm is { } form)
        {
            containers.Add(form.Dictionary);
            if (form.DefaultResources is { } resources)
            {
                containers.Add(resources);
            }
        }

        foreach (PdfTerminalField field in fields)
        {
            for (PdfField? node = field; node is not null; node = node.Parent)
            {
                containers.Add(node.Dictionary);
            }

            foreach (PdfWidgetAnnotation widget in field.Widgets)
            {
                containers.Add(widget.Dictionary);
            }
        }

        return CosSnapshot.Capture(containers);
    }

    private PageInvalidation[] Apply(PdfDocument document, CosSnapshot expected, CosSnapshot target)
    {
        var invalidations = new List<PageInvalidation>(_areas.Count);
        foreach (StepArea area in _areas)
        {
            int index = document.Pages.IndexOf(area.Page);
            if (index < 0)
            {
                if (area.Required)
                {
                    throw new EditStepPageRemovedException();
                }

                continue;
            }

            invalidations.Add(new PageInvalidation(index, area.Area, area.Layers));
        }

        if (!expected.Matches())
        {
            throw new InvalidOperationException($"\"{Name}\" cannot be undone or redone: what it edited was changed outside the edit history.");
        }

        target.Restore();
        return [.. invalidations];
    }
}

/// <summary>Several steps undone and redone as one (<see cref="EditHistory.BeginGroup"/>): undone latest first, redone in order.</summary>
internal sealed class GroupEditStep(string name, IReadOnlyList<IEditStep> steps) : IEditStep
{
    public string Name { get; } = name;

    public IReadOnlyList<IEditStep> Steps { get; } = steps;

    public IReadOnlyList<PageInvalidation> Undo(PdfDocument document)
    {
        var invalidations = new List<PageInvalidation>();
        for (int i = Steps.Count - 1; i >= 0; i--)
        {
            try
            {
                invalidations.AddRange(Steps[i].Undo(document));
            }
            catch
            {
                // Put back what was undone, so a failure leaves the document as it was.
                for (int j = i + 1; j < Steps.Count; j++)
                {
                    TryApply(() => Steps[j].Redo(document));
                }

                throw;
            }
        }

        return invalidations;
    }

    public IReadOnlyList<PageInvalidation> Redo(PdfDocument document)
    {
        var invalidations = new List<PageInvalidation>();
        for (int i = 0; i < Steps.Count; i++)
        {
            try
            {
                invalidations.AddRange(Steps[i].Redo(document));
            }
            catch
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    TryApply(() => Steps[j].Undo(document));
                }

                throw;
            }
        }

        return invalidations;
    }

    private static void TryApply(Func<IReadOnlyList<PageInvalidation>> apply)
    {
        try
        {
            _ = apply();
        }
#pragma warning disable CA1031 // best effort: the failure that started the roll back is the one reported
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
