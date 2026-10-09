namespace Broadside;

/// <summary>Which credential opened an encrypted document.</summary>
/// <remarks>ISO 32000-2 §7.6.4.1: the owner password gives full access; the user password (or the default, empty one) gives the access the permissions allow.</remarks>
public enum PdfAccessLevel
{
    /// <summary>User access: the document's permissions apply.</summary>
    User,

    /// <summary>Owner access: every operation is permitted.</summary>
    Owner,
}
