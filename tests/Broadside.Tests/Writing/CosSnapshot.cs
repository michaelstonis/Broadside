using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// The text of a document's whole COS graph, read through the public API only: its trailer, then every object number that resolves
/// to an object, with that object's PDF syntax (stream data included). Two snapshots of one document are equal exactly when nothing
/// in the graph changed: no entry set, removed or added, no stream's data replaced, no object added or removed.
/// </summary>
internal static class CosSnapshot
{
    /// <summary>How far past the highest number the trailer reaches to look for objects nothing refers to (an added, unlinked one).</summary>
    private const int Margin = 64;

    public static string Of(PdfDocument document)
    {
        var text = new StringBuilder();
        text.Append("version ").Append(document.Version).Append('\n');
        text.Append("trailer ").Append(document.Trailer).Append('\n');
        int highest = CosGraph.Reachable(document).Select(reference => reference.ObjectNumber).DefaultIfEmpty(0).Max();
        int count = 0;
        for (int number = 1; number <= highest + Margin; number++)
        {
            for (int generation = 0; generation <= 1; generation++)
            {
                CosObject value = document.Resolve(new CosReference(number, generation));
                if (value is not CosNull)
                {
                    count++;
                    text.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj ").Append(value).Append('\n');
                }
            }
        }

        text.Append("objects ").Append(count).Append('\n');
        return text.ToString();
    }
}
