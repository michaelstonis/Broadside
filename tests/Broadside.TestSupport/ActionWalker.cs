using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// Reads every action the document model reaches (issue #73): the catalog's OpenAction and AA, every page's AA, every annotation's A
/// and AA, every outline item's action and every document-level script, each through its whole Next tree and every typed property.
/// Public API only; shared by <see cref="DocumentWalker"/> and the <c>document</c> fuzz target (linked as source there).
/// </summary>
/// <remarks>ISO 32000-2 §12.6, §12.7.6, §7.7.2, §7.7.4.</remarks>
public static class ActionWalker
{
    private static readonly CosName AnnotsKey = new("Annots");
    private static readonly CosName AKey = new("A");
    private static readonly CosName AAKey = new("AA");

    /// <summary>Walks every action of <paramref name="document"/>; throws <see cref="InvalidOperationException"/> when a tree yields an action twice.</summary>
    /// <param name="document">An open document.</param>
    /// <returns>How many actions were read.</returns>
    public static int Walk(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int count = 0;
        _ = document.OpenDestination;
        _ = document.UriBase;
        count += Read(document.OpenAction);
        count += Read(document.AdditionalActions);
        foreach (PdfPage page in document.Pages)
        {
            count += Read(page.AdditionalActions);
            if (document.Resolve(page.Dictionary.TryGetValue(AnnotsKey, out CosObject? annots) ? annots : null) is not CosArray array)
            {
                continue;
            }

            foreach (CosObject element in array)
            {
                if (document.Resolve(element) is CosDictionary annotation)
                {
                    var owner = element as CosReference;
                    count += Read(document.GetAction(annotation.TryGetValue(AKey, out CosObject? a) ? a : null, owner));
                    CosObject? aa = annotation.TryGetValue(AAKey, out CosObject? value) ? value : null;
                    count += Read(document.GetAnnotationAdditionalActions(aa, owner));
                    count += Read(document.GetFieldAdditionalActions(aa, owner));
                }
            }
        }

        var outline = new Stack<PdfOutlineItem>(document.Outline?.Items ?? []);
        while (outline.TryPop(out PdfOutlineItem? item))
        {
            count += Read(item.Action);
            foreach (PdfOutlineItem child in item.Children)
            {
                outline.Push(child);
            }
        }

        foreach (KeyValuePair<CosString, PdfJavaScriptAction> script in document.Names?.JavaScriptActions ?? [])
        {
            count += Read(script.Value);
        }

        return count;
    }

    private static int Read(PdfAdditionalActions? actions)
    {
        int count = 0;
        foreach (KeyValuePair<CosName, PdfAction> entry in actions?.Actions ?? [])
        {
            count += Read(entry.Value);
        }

        return count;
    }

    private static int Read(PdfAction? root)
    {
        if (root is null)
        {
            return 0;
        }

        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfAction action in root.EnumerateActionTree())
        {
            if (!seen.Add(action.Dictionary))
            {
                throw new InvalidOperationException("An action tree yielded the same action twice.");
            }

            ReadProperties(action);
        }

        return seen.Count;
    }

    private static void ReadProperties(PdfAction action)
    {
        _ = (action.Kind, action.ActionType, action.Reference);
        switch (action)
        {
            case PdfGoToAction goTo:
                _ = (goTo.Destination, goTo.StructureDestination);
                break;
            case PdfRemoteGoToAction remote:
                _ = (remote.File?.FileName, remote.Destination, remote.StructureDestination, remote.NewWindow);
                break;
            case PdfEmbeddedGoToAction embedded:
                _ = (embedded.File?.FileName, embedded.Destination, embedded.NewWindow);
                for (PdfEmbeddedTarget? target = embedded.Target; target is not null; target = target.Target)
                {
                    _ = (target.Relationship, target.EmbeddedFileName, target.PageNumber, target.PageName, target.AnnotationIndex, target.AnnotationName);
                }

                break;
            case PdfGoToDocumentPartAction part:
                _ = part.DocumentPart;
                break;
            case PdfLaunchAction launch:
                _ = (launch.File?.FileName, launch.Mac, launch.Unix, launch.NewWindow);
                _ = (launch.Windows?.FileName, launch.Windows?.DefaultDirectory, launch.Windows?.Operation, launch.Windows?.Parameters);
                break;
            case PdfThreadAction thread:
                _ = (thread.File?.FileName, thread.ThreadDictionary, thread.ThreadIndex, thread.ThreadTitle, thread.BeadDictionary, thread.BeadIndex);
                break;
            case PdfUriAction uri:
                _ = (uri.Uri, uri.UriObject, uri.IsMap, uri.ResolveUri());
                break;
            case PdfSoundAction sound:
                _ = (sound.Sound, sound.Volume, sound.IsSynchronous, sound.Repeats, sound.Mixes);
                break;
            case PdfMovieAction movie:
                _ = (movie.Annotation, movie.Title, movie.Operation);
                break;
            case PdfHideAction hide:
                _ = (hide.Targets.Select(target => (target.Dictionary, target.FieldName)).ToList(), hide.Hide);
                break;
            case PdfNamedAction named:
                _ = (named.Name, named.Operation);
                break;
            case PdfSubmitFormAction submit:
                _ = (submit.File?.IsUrl, submit.Fields?.Count, submit.Flags, submit.CharacterSet);
                break;
            case PdfResetFormAction reset:
                _ = (reset.Fields?.Count, reset.Flags);
                break;
            case PdfImportDataAction import:
                _ = import.File?.FileName;
                break;
            case PdfSetOcgStateAction state:
                _ = (state.Changes.Count, state.PreserveRadioButtons);
                break;
            case PdfRenditionAction rendition:
                _ = (rendition.Rendition, rendition.ScreenAnnotation, rendition.Operation, rendition.Script);
                break;
            case PdfTransitionAction transition:
                _ = transition.Transition;
                break;
            case PdfGoTo3DViewAction view:
                _ = (view.Annotation, view.ViewDictionary, view.ViewIndex, view.ViewName, view.ViewKeyword);
                break;
            case PdfJavaScriptAction script:
                _ = script.Script;
                break;
            case PdfRichMediaExecuteAction richMedia:
                _ = (richMedia.Annotation, richMedia.Instance, richMedia.Command?.Name, richMedia.Command?.Arguments.Count);
                break;
        }
    }
}
