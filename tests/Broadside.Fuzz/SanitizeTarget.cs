using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>sanitize</c> target (issue #648): opens the input as a whole file, sanitizes it with every removal on (all action kinds,
/// invisible text, comments, optional content flattened) and saves it with the result's save options, then reopens it. Invariants:
/// sanitizing throws only <see cref="InvalidOperationException"/>, for an encrypted file opened without owner access; the saved file
/// opens with the same page count and nothing reachable from its trailer names a JavaScript or launch action, an <c>AF</c>, an
/// <c>EmbeddedFiles</c> tree, an <c>XFA</c> form or a <c>Metadata</c> stream.
/// </summary>
/// <remarks>ISO 32000-2 §12.6, §7.7.4, §12.7.3, §7.11.4, §14.13, §14.3, §8.11 and §9.3.6.</remarks>
internal static class SanitizeTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().UseSystemFontResolver(null));

    private static readonly CosName[] Forbidden = [new("AF"), new("EmbeddedFiles"), new("XFA"), new("Metadata")];

    private static readonly CosName S = new("S");

    private static readonly CosName AA = new("AA");

    private static readonly CosName Next = new("Next");

    private static readonly CosName[] ActionEntries = [new("A"), new("PA"), new("OpenAction")];

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document is null)
        {
            return;
        }

        int pages = document.Pages.Count;
        PdfSanitizeOptions options = new PdfSanitizeOptions()
            .WithActions(PdfSanitizeActions.All)
            .WithRemoveInvisibleText(true)
            .WithRemoveComments(true)
            .WithOptionalContent(PdfSanitizeOptionalContent.FlattenVisible);
        PdfSanitization result;
        if (document.Security is { Access: PdfAccessLevel.User })
        {
            try
            {
                _ = document.Sanitize(options);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new InvalidOperationException("A document opened with the user password was sanitized.");
        }

        result = document.Sanitize(options);
        if (document.IsEncrypted)
        {
            return;
        }

        using var output = new MemoryStream();
        document.Save(output, result.CreateSaveOptions());
        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages; the input had {pages}.");
        }

        Check(saved);
    }

    /// <summary>Throws when anything reachable from the trailer is what sanitizing removes.</summary>
    private static void Check(PdfDocument document)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject>([document.Trailer]);
        while (pending.Count > 0)
        {
            CosObject current = document.Resolve(pending.Pop());
            if (!seen.Add(current))
            {
                continue;
            }

            switch (current)
            {
                case CosStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                case CosDictionary dictionary:
                    foreach (CosName key in Forbidden)
                    {
                        if (dictionary.ContainsKey(key))
                        {
                            throw new InvalidOperationException($"A {key.Value} entry is reachable after sanitizing.");
                        }
                    }

                    foreach (CosName key in ActionEntries)
                    {
                        CheckAction(document, dictionary.TryGetValue(key, out CosObject? action) ? action : null);
                    }

                    if (dictionary.TryGetValue(AA, out CosObject? triggers) && document.Resolve(triggers) is CosDictionary additional)
                    {
                        foreach (CosObject trigger in additional.Values)
                        {
                            CheckAction(document, trigger);
                        }
                    }

                    foreach (CosObject value in dictionary.Values)
                    {
                        pending.Push(value);
                    }

                    break;
                case CosArray array:
                    foreach (CosObject item in array)
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }
    }

    /// <summary>Throws when an action entry's value, or an item of it, is a JavaScript or launch action (§12.6.4.6, §12.6.4.17).</summary>
    private static void CheckAction(PdfDocument document, CosObject? value, int depth = 0)
    {
        if (depth > 64)
        {
            return;
        }

        switch (document.Resolve(value))
        {
            case CosDictionary action when document.Resolve(action.TryGetValue(S, out CosObject? type) ? type : null) is CosName { Value: "JavaScript" or "Launch" } name:
                throw new InvalidOperationException($"A {name.Value} action is reachable after sanitizing.");
            case CosDictionary action:
                CheckAction(document, action.TryGetValue(Next, out CosObject? next) ? next : null, depth + 1);
                break;
            case CosArray actions:
                foreach (CosObject item in actions)
                {
                    if (document.Resolve(item) is CosDictionary)
                    {
                        CheckAction(document, item, depth + 1);
                    }
                }

                break;
        }
    }
}
