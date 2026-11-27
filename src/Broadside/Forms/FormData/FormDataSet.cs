using Broadside.Objects;

namespace Broadside.Forms.FormData;

/// <summary>
/// What an FDF or XFDF file holds, read into one shape the importer applies: field entries by fully qualified name, and annotation
/// dictionaries with their pages, as objects of the file's own graph.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.8.3; Adobe XFDF 3.0 (ISO 19444-1).</remarks>
internal sealed class FormDataSet : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="FormDataSet"/> class.</summary>
    /// <param name="resolve">Resolves a reference of the file's graph to its object; <see cref="CosNull"/> for none.</param>
    public FormDataSet(Func<CosObject?, CosObject> resolve) => Resolve = resolve;

    /// <summary>Gets the resolver of the file's graph.</summary>
    public Func<CosObject?, CosObject> Resolve { get; }

    /// <summary>Gets the field entries, in the order of the file.</summary>
    public List<FormDataField> Fields { get; } = [];

    /// <summary>Gets the annotations, in the order of the file.</summary>
    public List<FormDataAnnotation> Annotations { get; } = [];

    /// <summary>Gets or sets what the file's objects are read from, disposed with the set.</summary>
    public IDisposable? Source { get; set; }

    /// <inheritdoc/>
    public void Dispose() => Source?.Dispose();
}

/// <summary>One field entry: the value and flags the file gives a fully qualified field name.</summary>
/// <remarks>ISO 32000-2 §12.7.8.3.2, Table 249.</remarks>
internal sealed class FormDataField(string name)
{
    /// <summary>Gets the fully qualified field name (§12.7.4.2).</summary>
    public string Name { get; } = name;

    /// <summary>Gets or sets the value (<c>V</c>), resolved: a string, a name, or an array of strings; <see langword="null"/> when the entry has none.</summary>
    public CosObject? Value { get; set; }

    /// <summary>Gets or sets the rich text value (<c>RV</c>, PDF 1.5), or <see langword="null"/>.</summary>
    public string? RichValue { get; set; }

    /// <summary>Gets or sets the field flags that replace the field's (<c>Ff</c>).</summary>
    public uint? FieldFlags { get; set; }

    /// <summary>Gets or sets the field flags to set (<c>SetFf</c>).</summary>
    public uint? SetFieldFlags { get; set; }

    /// <summary>Gets or sets the field flags to clear (<c>ClrFf</c>).</summary>
    public uint? ClearFieldFlags { get; set; }

    /// <summary>Gets or sets the widget annotation flags that replace the widgets' (<c>F</c>).</summary>
    public uint? WidgetFlags { get; set; }

    /// <summary>Gets or sets the widget annotation flags to set (<c>SetF</c>).</summary>
    public uint? SetWidgetFlags { get; set; }

    /// <summary>Gets or sets the widget annotation flags to clear (<c>ClrF</c>).</summary>
    public uint? ClearWidgetFlags { get; set; }

    /// <summary>Gets whether the entry changes anything.</summary>
    public bool HasChanges => Value is not null || RichValue is not null || FieldFlags is not null || SetFieldFlags is not null || ClearFieldFlags is not null
        || WidgetFlags is not null || SetWidgetFlags is not null || ClearWidgetFlags is not null;
}

/// <summary>One annotation of the file: its dictionary in the file's graph, the 0-based page it belongs on, and how replies name their parent.</summary>
/// <remarks>ISO 32000-2 §12.7.8.3.4, Table 254.</remarks>
internal sealed class FormDataAnnotation(int page, CosDictionary dictionary, CosReference? reference)
{
    /// <summary>Gets the 0-based page index (<c>Page</c>).</summary>
    public int Page { get; } = page;

    /// <summary>Gets the annotation dictionary, in the file's graph.</summary>
    public CosDictionary Dictionary { get; } = dictionary;

    /// <summary>Gets the reference the file's graph names the dictionary by, when it is indirect.</summary>
    public CosReference? Reference { get; } = reference;

    /// <summary>Gets or sets the <c>NM</c> of the annotation this one replies to, when the file names it by name (XFDF <c>inreplyto</c>).</summary>
    public string? InReplyToName { get; set; }
}
