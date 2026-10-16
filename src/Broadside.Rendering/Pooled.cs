using System.Buffers;

namespace Broadside.Rendering;

/// <summary>Pooled working arrays that grow and are returned to <see cref="ArrayPool{T}.Shared"/>.</summary>
internal static class Pooled
{
    /// <summary>Replaces <paramref name="buffer"/> with a pooled array of at least <paramref name="length"/> items when it is shorter.</summary>
    public static void Ensure<T>(ref T[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Return(ref buffer);
            buffer = ArrayPool<T>.Shared.Rent(length);
        }
    }

    /// <summary>Returns <paramref name="buffer"/> to the pool and leaves it empty.</summary>
    public static void Return<T>(ref T[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<T>.Shared.Return(buffer);
        }

        buffer = [];
    }
}
