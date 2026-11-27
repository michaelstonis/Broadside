namespace Broadside.Text;

/// <summary>What a <see cref="TextChange"/> did to the old document's words.</summary>
/// <remarks>ISO 32000-2 §14.8.2.6.2 (word breaks): changes are runs of whole words.</remarks>
public enum TextChangeKind
{
    /// <summary>Words only the new document has.</summary>
    Inserted,

    /// <summary>Words only the old document has.</summary>
    Deleted,

    /// <summary>Words of the old document replaced by other words in the new one.</summary>
    Changed,
}
