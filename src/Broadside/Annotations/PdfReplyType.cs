namespace Broadside.Annotations;

/// <summary>The relationship between a markup annotation and the one it is in reply to, its <c>RT</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.5.6.2, Table 172 (PDF 1.6). Any other name reads as the default, <see cref="Reply"/>.</remarks>
public enum PdfReplyType
{
    /// <summary><c>R</c>: a reply to the annotation (the default).</summary>
    Reply,

    /// <summary><c>Group</c>: grouped with the annotation; the group's attributes come from the primary annotation.</summary>
    Group,
}
