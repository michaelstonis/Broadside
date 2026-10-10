using Broadside.Content;

namespace Broadside;

/// <summary>Content interpretation (issue #56): what the interpreter derives from the document's resources once.</summary>
public sealed partial class PdfDocument
{
    private ContentResourceCache? _contentResources;

    /// <summary>Gets the decoded nested content streams and the read graphics state parameter dictionaries, shared by every run.</summary>
    internal ContentResourceCache ContentResources
    {
        get
        {
            if (_contentResources is { } cache)
            {
                return cache;
            }

            Interlocked.CompareExchange(ref _contentResources, new ContentResourceCache(this), null);
            return _contentResources;
        }
    }
}
