namespace Broadside.Viewer;

/// <summary>Tells a shell that a page's thumbnail could not be drawn.</summary>
/// <remarks>Spec #33 viewer capability 10.</remarks>
public sealed class ThumbnailFailedEventArgs : EventArgs
{
    internal ThumbnailFailedEventArgs(int pageIndex, Exception exception)
    {
        PageIndex = pageIndex;
        Exception = exception;
    }

    /// <summary>Gets the zero-based page.</summary>
    public int PageIndex { get; }

    /// <summary>Gets what went wrong.</summary>
    public Exception Exception { get; }
}
