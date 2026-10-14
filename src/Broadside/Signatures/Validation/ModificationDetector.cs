using System.Globalization;
using Broadside.Annotations;
using Broadside.Filters;
using Broadside.Forms;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Signatures.Validation;

/// <summary>
/// Modification detection: finds the revision each signature signed, compares every later revision with the one before it, classifies
/// each change (<see cref="PdfModificationKind"/>), and decides for each signature whether the permissions in effect and its field
/// locks allow the changes made after it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.1 (the byte range of a signature in an incremental update ends at the <c>%%EOF</c> that terminates it),
/// §12.8.2.2.2 ("PDF processors may compare the signed and current versions of the document to see whether there have been
/// modifications to any objects that are not permitted by the transform parameters"), §12.8.2.4 (FieldMDP signatures are validated
/// "in a similar manner"), §12.7.5.5, Table 236 (field locks and their <c>P</c>: permissions in effect only narrow; without a
/// certification signature they start as 3), §12.8.4.4 and Table 257 (DSS and document timestamp updates are not changes).
/// </para>
/// <para>
/// The comparison is per object: every object an update's cross-reference sections list is compared with what the revision before
/// held, and what changed is classified by what the object was (a field, a widget, a page, an annotation, ...). A new object is not
/// a change by itself: it matters only through an existing object that comes to refer to it, which is classified. Objects the walk
/// from the catalog does not reach (outlines, the structure tree, name trees) are <see cref="PdfModificationKind.Other"/>.
/// </para>
/// </remarks>
internal sealed class ModificationDetector
{
    private static readonly CosName Dss = new("DSS");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName Extensions = new("Extensions");
    private static readonly CosName Info = new("Info");
    private static readonly CosName Encrypt = new("Encrypt");
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName XRef = new("XRef");
    private static readonly CosName RV = new("RV");
    private static readonly CosName I = new("I");
    private static readonly CosName TI = new("TI");
    private static readonly CosName AS = new("AS");
    private static readonly CosName AP = new("AP");
    private static readonly CosName Ff = new("Ff");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName NeedAppearances = new("NeedAppearances");

    private readonly PdfSource _source;
    private readonly ObjectLoader _loader;
    private readonly StreamDecoder _streams;
    private readonly IReadOnlyList<PdfRevision> _revisions;
    private readonly Func<CosObject?, CosObject> _resolve;
    private readonly Dictionary<int, RevisionState> _states = [];
    private readonly Dictionary<int, IReadOnlyList<Change>> _changes = [];

    private ModificationDetector(PdfSource source, ObjectLoader loader, StreamDecoder streams, IReadOnlyList<PdfRevision> revisions, Func<CosObject?, CosObject> resolve)
    {
        _source = source;
        _loader = loader;
        _streams = streams;
        _revisions = revisions;
        _resolve = resolve;
    }

    /// <summary>Adds modification detection to <paramref name="validations"/>, which are in signing order.</summary>
    public static IReadOnlyList<PdfSignatureValidation> Apply(
        PdfSource source,
        ObjectLoader loader,
        StreamDecoder streams,
        IReadOnlyList<PdfRevision> revisions,
        IReadOnlyList<PdfSignatureValidation> validations,
        Func<CosObject?, CosObject> resolve)
    {
        if (validations.Count == 0 || loader.CrossReference.Revisions.Count != revisions.Count)
        {
            return validations;
        }

        return new ModificationDetector(source, loader, streams, revisions, resolve).Apply(validations);
    }

    private PdfSignatureValidation[] Apply(IReadOnlyList<PdfSignatureValidation> validations)
    {
        var signatures = new Signature[validations.Count];
        bool earlierSignature = false;
        for (int index = 0; index < validations.Count; index++)
        {
            PdfSignatureValidation validation = validations[index];
            CosDictionary? dictionary = validation.Field.SignatureDictionary;
            bool timestamp = dictionary is not null && TransformMethods.IsDocumentTimestamp(dictionary, _resolve);
            int? revision = SignedRevision(validation);

            // §12.8.1: one certification signature, which approval signatures follow; a DocMDP reference in a later one is ignored.
            PdfCertificationPermissions? certification = !earlierSignature && !timestamp && dictionary is not null
                ? TransformMethods.DocMdpPermissions(dictionary, _resolve)
                : null;
            earlierSignature |= !timestamp;
            FieldLock? fieldLock = dictionary is null ? null : TransformMethods.FieldMdpLock(dictionary, _resolve);
            FieldLock? lockDictionary = _resolve(validation.Field.Dictionary.GetValueOrDefault(FormNames.Lock)) is CosDictionary locked
                ? TransformMethods.ReadLock(locked, _resolve)
                : null;
            signatures[index] = new Signature(validation, revision, certification, fieldLock ?? lockDictionary, lockDictionary?.Permissions);
        }

        var results = new PdfSignatureValidation[validations.Count];
        for (int index = 0; index < signatures.Length; index++)
        {
            results[index] = Evaluate(signatures[index], signatures);
        }

        return results;
    }

    private PdfSignatureValidation Evaluate(Signature signature, Signature[] all)
    {
        PdfSignatureValidation validation = signature.Validation;
        if (signature.Revision is not { } signed)
        {
            bool rangeRead = validation.ByteRange.Count >= 2 && !validation.Problems.Any(static problem =>
                problem.Kind is PdfSignatureProblemKind.ByteRangeMissing or PdfSignatureProblemKind.ByteRangeInvalid);
            return validation.WithModifications(
                null,
                signature.Certification,
                [],
                rangeRead
                    ? [new(PdfSignatureProblemKind.SignedRevisionUnknown, $"The byte ranges end at offset {RangeEnd(validation).ToString(CultureInfo.InvariantCulture)}, which is not the end of a revision of the file (a %%EOF marker terminating a trailer); what was signed cannot be compared with what follows.")]
                    : []);
        }

        var modifications = new List<PdfModification>();
        var problems = new List<PdfSignatureProblem>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string>? lockedExisting = null;
        for (int revision = signed + 1; revision < _revisions.Count; revision++)
        {
            PdfCertificationPermissions permissions = PermissionsAt(revision, all);
            foreach (Change change in ChangesOf(revision))
            {
                bool locked = false;
                if (signature.Lock is { } fieldLock && change.FieldName is { } name
                    && change.Kind is PdfModificationKind.FormFilling or PdfModificationKind.Signing or PdfModificationKind.DocumentTimestamp)
                {
                    // A lock covers the fields the signed revision has (§12.7.5.5: "form fields whose values shall no longer be changed").
                    lockedExisting ??= [.. State(signed).FieldNames];
                    locked = lockedExisting.Contains(name) && fieldLock.Locks(name);
                }

                bool permitted = !locked && Permits(permissions, change.Kind);
                modifications.Add(new PdfModification(revision, change.Kind, change.Object, change.FieldName, change.Description, permitted));
                if (locked)
                {
                    string message = $"Revision {revision.ToString(CultureInfo.InvariantCulture)} changed the field '{change.FieldName}', which the signature locks ({change.Description}).";
                    if (reported.Add(string.Create(CultureInfo.InvariantCulture, $"{revision} {change.FieldName}")))
                    {
                        problems.Add(new(PdfSignatureProblemKind.LockedFieldModified, message));
                    }
                }
                else if (!permitted)
                {
                    string message = $"Revision {revision.ToString(CultureInfo.InvariantCulture)}: {change.Description} ({change.Kind}), which the permissions in effect ({(int)permissions}, {permissions}) do not allow.";
                    if (reported.Add(message))
                    {
                        problems.Add(new(PdfSignatureProblemKind.ModificationNotPermitted, message));
                    }
                }
            }
        }

        return validation.WithModifications(signed, signature.Certification, modifications, problems);
    }

    /// <summary>
    /// The permissions in effect for the changes revision <paramref name="revision"/> makes: 3 without a certification signature,
    /// else its <c>P</c>, narrowed by the <c>P</c> of every field lock signed in an earlier revision (Table 236).
    /// </summary>
    private static PdfCertificationPermissions PermissionsAt(int revision, Signature[] all)
    {
        PdfCertificationPermissions permissions = PdfCertificationPermissions.FormFillingAndAnnotations;
        foreach (Signature signature in all)
        {
            if (signature.Revision is not { } signed || signed >= revision)
            {
                continue;
            }

            if (signature.Certification is { } certification && certification < permissions)
            {
                permissions = certification;
            }

            if (signature.LockPermissions is { } narrowed && narrowed < permissions)
            {
                permissions = narrowed;
            }
        }

        return permissions;
    }

    /// <summary>Whether <paramref name="permissions"/> allow a change of <paramref name="kind"/> (Table 257; <see cref="PdfModificationKind"/>).</summary>
    private static bool Permits(PdfCertificationPermissions permissions, PdfModificationKind kind) => kind switch
    {
        PdfModificationKind.DocumentSecurityStore or PdfModificationKind.DocumentTimestamp or PdfModificationKind.DocumentInformation => true,
        PdfModificationKind.Signing or PdfModificationKind.FormFilling => permissions >= PdfCertificationPermissions.FormFilling,
        PdfModificationKind.Annotations => permissions >= PdfCertificationPermissions.FormFillingAndAnnotations,
        _ => false,
    };

    private static long RangeEnd(PdfSignatureValidation validation)
    {
        IReadOnlyList<long> range = validation.ByteRange;
        return range[^2] + range[^1];
    }

    /// <summary>
    /// The revision whose end the byte ranges end at: its <c>%%EOF</c>, with or without the end-of-line marker after it (§12.8.1), or
    /// <see langword="null"/>.
    /// </summary>
    private int? SignedRevision(PdfSignatureValidation validation)
    {
        IReadOnlyList<long> range = validation.ByteRange;
        if (range.Count < 2 || range.Count % 2 != 0 || validation.Problems.Any(static problem =>
            problem.Kind is PdfSignatureProblemKind.ByteRangeMissing or PdfSignatureProblemKind.ByteRangeInvalid))
        {
            return null;
        }

        long end = RangeEnd(validation);
        Span<byte> marker = stackalloc byte[5];
        Span<byte> between = stackalloc byte[2];
        for (int index = 0; index < _revisions.Count; index++)
        {
            long length = _revisions[index].Length;
            if (end > length || end < length - 2 || end < marker.Length)
            {
                continue;
            }

            // Only end-of-line bytes between the range's end and the revision's, and %%EOF just before them.
            int count = (int)(length - end);
            if (_source.Read(end, between[..count]) != count || between[..count].ContainsAnyExcept((byte)'\r', (byte)'\n'))
            {
                continue;
            }

            long markerEnd = end;
            while (markerEnd > length - 2 && markerEnd > 0 && IsEndOfLine(markerEnd - 1))
            {
                markerEnd--;
            }

            if (markerEnd >= marker.Length && _source.Read(markerEnd - marker.Length, marker) == marker.Length && marker.SequenceEqual("%%EOF"u8))
            {
                return index;
            }
        }

        return null;

        bool IsEndOfLine(long offset)
        {
            Span<byte> one = stackalloc byte[1];
            return _source.Read(offset, one) == 1 && one[0] is (byte)'\r' or (byte)'\n';
        }
    }

    private RevisionState State(int index)
    {
        if (!_states.TryGetValue(index, out RevisionState? state))
        {
            state = new RevisionState(index, _source, _loader, _streams);
            _states[index] = state;
        }

        return state;
    }

    /// <summary>What revision <paramref name="revision"/> changed relative to the revision before it.</summary>
    private IReadOnlyList<Change> ChangesOf(int revision)
    {
        if (!_changes.TryGetValue(revision, out IReadOnlyList<Change>? changes))
        {
            changes = new RevisionComparison(State(revision - 1), State(revision), _loader.CrossReference.Revisions[revision]).Compare();
            _changes[revision] = changes;
        }

        return changes;
    }

    /// <summary>A signature as modification detection sees it.</summary>
    private sealed record Signature(
        PdfSignatureValidation Validation,
        int? Revision,
        PdfCertificationPermissions? Certification,
        FieldLock? Lock,
        PdfCertificationPermissions? LockPermissions);

    /// <summary>One classified change.</summary>
    internal sealed record Change(PdfModificationKind Kind, CosReference? Object, string? FieldName, string Description);

    /// <summary>The comparison of one revision with the one before it.</summary>
    private sealed class RevisionComparison(RevisionState older, RevisionState newer, XrefRevision update)
    {
        private readonly List<Change> _changes = [];
        private Dictionary<int, bool>? _newlySigned;

        /// <summary>
        /// Gets the signature fields this revision signs, by field object number, each with whether it is a document timestamp: a
        /// signature field whose value the older revision does not have.
        /// </summary>
        private Dictionary<int, bool> NewlySigned => _newlySigned ??= FindNewlySigned();

        public List<Change> Compare()
        {
            CompareTrailers();
            var numbers = new SortedSet<int>();
            foreach (XrefSection section in update.Sections)
            {
                numbers.UnionWith(section.Entries.Keys);
            }

            foreach (int number in numbers)
            {
                (CosReference Reference, CosObject Value)? before = older.Load(number);
                (CosReference Reference, CosObject Value)? after = newer.Load(number);
                if (before is null || IsContainer(before.Value.Value) || (after is { } current && IsContainer(current.Value)))
                {
                    // A new object is judged through the existing objects that come to refer to it; object and cross-reference
                    // streams are containers, whose members are listed on their own.
                    continue;
                }

                (CosReference reference, CosObject oldValue) = before.Value;
                RoleInfo role = older.RoleOf(number);
                if (after is not { } replaced || !replaced.Reference.Equals(reference))
                {
                    Deleted(reference, role);
                    continue;
                }

                if (CosEquality.DeepEquals(oldValue, replaced.Value))
                {
                    continue;
                }

                if (role.Role == ObjectRole.None)
                {
                    role = newer.RoleOf(number);
                }

                Modified(reference, role, oldValue, replaced.Value);
            }

            return _changes;
        }

        private void Add(PdfModificationKind kind, CosReference? reference, string? fieldName, string description) =>
            _changes.Add(new Change(kind, reference, fieldName, description));

        private static bool IsContainer(CosObject value) =>
            value is CosStream stream && stream.Dictionary.GetValueOrDefault(KnownNames.Type) is CosName type && (ObjStm.Equals(type) || XRef.Equals(type));

        private void CompareTrailers()
        {
            CosDictionary before = older.Trailer;
            CosDictionary after = newer.Trailer;
            if (!CosEquality.DeepEquals(before.GetValueOrDefault(KnownNames.Root), after.GetValueOrDefault(KnownNames.Root)))
            {
                Add(PdfModificationKind.Other, null, null, "the trailer's Root names another catalog");
            }

            if (!CosEquality.DeepEquals(before.GetValueOrDefault(Info), after.GetValueOrDefault(Info)))
            {
                Add(PdfModificationKind.DocumentInformation, null, null, "the trailer's Info names another document information dictionary");
            }

            if (!CosEquality.DeepEquals(before.GetValueOrDefault(Encrypt), after.GetValueOrDefault(Encrypt)))
            {
                Add(PdfModificationKind.Other, null, null, "the trailer's Encrypt changed");
            }
        }

        private void Deleted(CosReference reference, RoleInfo role)
        {
            (PdfModificationKind kind, string what) = role.Role switch
            {
                ObjectRole.Annotation => (PdfModificationKind.Annotations, "an annotation"),
                ObjectRole.DocumentInformation => (PdfModificationKind.DocumentInformation, "the document information"),
                ObjectRole.SecurityStore => (PdfModificationKind.DocumentSecurityStore, "part of the document security store"),
                ObjectRole.Field or ObjectRole.Widget or ObjectRole.FieldArray or ObjectRole.FieldLockOrSeedValue => (PdfModificationKind.FormStructure, "a field or widget"),
                ObjectRole.FieldValue or ObjectRole.FormResources => (PdfModificationKind.FormFilling, "a field value or form resource"),
                ObjectRole.Appearance => (FieldKind(role), "an appearance"),
                ObjectRole.Page or ObjectRole.PageTreeNode or ObjectRole.PageContent or ObjectRole.AnnotationArray => (PdfModificationKind.Pages, "a page object"),
                _ => (PdfModificationKind.Other, "an object"),
            };
            Add(kind, reference, role.FieldName, $"{what} was deleted");
        }

        private void Modified(CosReference reference, RoleInfo role, CosObject before, CosObject after)
        {
            CosDictionary? oldDictionary = before as CosDictionary;
            CosDictionary? newDictionary = after as CosDictionary;
            switch (role.Role)
            {
                case ObjectRole.Catalog when oldDictionary is not null && newDictionary is not null:
                    CompareCatalogs(reference, oldDictionary, newDictionary);
                    break;
                case ObjectRole.AcroForm when oldDictionary is not null && newDictionary is not null:
                    CompareForms(reference, oldDictionary, newDictionary);
                    break;
                case ObjectRole.Field when oldDictionary is not null && newDictionary is not null:
                    CompareFields(reference, role, oldDictionary, newDictionary);
                    break;
                case ObjectRole.Widget when oldDictionary is not null && newDictionary is not null:
                    CompareWidgets(reference, role, oldDictionary, newDictionary);
                    break;
                case ObjectRole.FieldArray when before is CosArray oldArray && after is CosArray newArray:
                    CompareFieldArrays(reference, role.FieldName, oldArray, newArray);
                    break;
                case ObjectRole.Page when oldDictionary is not null && newDictionary is not null:
                    ComparePages(reference, oldDictionary, newDictionary);
                    break;
                case ObjectRole.AnnotationArray when before is CosArray oldAnnotations && after is CosArray newAnnotations:
                    CompareAnnotationArrays(reference, oldAnnotations, newAnnotations);
                    break;
                case ObjectRole.Annotation:
                    Add(PdfModificationKind.Annotations, reference, null, "an annotation was modified");
                    break;
                case ObjectRole.FieldValue:
                    Add(PdfModificationKind.FormFilling, reference, role.FieldName, "the field's value changed");
                    break;
                case ObjectRole.Appearance:
                    Add(FieldKind(role), reference, role.FieldName, "a widget's appearance changed");
                    break;
                case ObjectRole.FormResources:
                    Add(PdfModificationKind.FormFilling, reference, null, "the form's default resources changed");
                    break;
                case ObjectRole.DocumentInformation:
                    Add(PdfModificationKind.DocumentInformation, reference, null, "the document information or metadata changed");
                    break;
                case ObjectRole.SecurityStore:
                    Add(PdfModificationKind.DocumentSecurityStore, reference, null, "the document security store changed");
                    break;
                case ObjectRole.FieldLockOrSeedValue:
                    Add(PdfModificationKind.FormStructure, reference, role.FieldName, "a signature field's lock or seed value changed");
                    break;
                case ObjectRole.SignatureDictionary:
                    Add(PdfModificationKind.Other, reference, role.FieldName, "an existing signature dictionary changed");
                    break;
                case ObjectRole.PageTreeNode:
                    Add(PdfModificationKind.Pages, reference, null, "the page tree changed: pages added, removed or reordered");
                    break;
                case ObjectRole.PageContent:
                    Add(PdfModificationKind.Pages, reference, null, "a page's content or resources changed");
                    break;
                default:
                    Add(PdfModificationKind.Other, reference, role.FieldName, $"an object the signed document {(role.Role == ObjectRole.None ? "does not reach through its catalog, pages, form or annotations" : $"holds as {role.Role}")} changed");
                    break;
            }
        }

        /// <summary>The kind of a change to a field's widget or appearance: signing when this revision signs the field, else form filling.</summary>
        private PdfModificationKind FieldKind(RoleInfo role)
        {
            if (role.FieldName is null)
            {
                return role.Role == ObjectRole.Appearance ? PdfModificationKind.FormFilling : PdfModificationKind.Annotations;
            }

            return role.IsSignatureField && NewlySigned.TryGetValue(role.FieldObject, out bool timestamp)
                ? timestamp ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signing
                : PdfModificationKind.FormFilling;
        }

        /// <summary>Signing, or a document timestamp when every field this revision signs is one.</summary>
        private PdfModificationKind SigningKind() =>
            NewlySigned.Count > 0 && NewlySigned.Values.All(static timestamp => timestamp) ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signing;

        private Dictionary<int, bool> FindNewlySigned()
        {
            var signed = new Dictionary<int, bool>();
            foreach (int number in newer.CrossReference.Entries.Keys)
            {
                RoleInfo role = newer.RoleOf(number);
                if (role is not { Role: ObjectRole.Field, IsSignatureField: true })
                {
                    continue;
                }

                (bool after, bool timestamp) = newer.SignatureOf(number);
                if (after && !older.SignatureOf(number).Signed)
                {
                    signed[number] = timestamp;
                }
            }

            return signed;
        }

        private void CompareCatalogs(CosReference reference, CosDictionary before, CosDictionary after)
        {
            foreach (CosName key in ChangedKeys(before, after))
            {
                if (key.Equals(Dss))
                {
                    Add(PdfModificationKind.DocumentSecurityStore, reference, null, "the catalog's DSS changed");
                }
                else if (key.Equals(KnownNames.Version) || key.Equals(Extensions) || key.Equals(Metadata))
                {
                    Add(PdfModificationKind.DocumentInformation, reference, null, $"the catalog's {key.Value} changed");
                }
                else if (key.Equals(FormNames.AcroForm) && before.GetValueOrDefault(key) is CosDictionary oldForm && after.GetValueOrDefault(key) is CosDictionary newForm)
                {
                    CompareForms(reference, oldForm, newForm);
                }
                else if (key.Equals(FormNames.AcroForm))
                {
                    Add(PdfModificationKind.FormStructure, reference, null, "the catalog's AcroForm names another interactive form");
                }
                else
                {
                    Add(PdfModificationKind.Other, reference, null, $"the catalog's {key.Value} changed");
                }
            }
        }

        private void CompareForms(CosReference reference, CosDictionary before, CosDictionary after)
        {
            foreach (CosName key in ChangedKeys(before, after))
            {
                if (key.Equals(FormNames.Fields))
                {
                    if (older.Resolve(before.GetValueOrDefault(key)) is CosArray oldFields && newer.Resolve(after.GetValueOrDefault(key)) is CosArray newFields)
                    {
                        CompareFieldArrays(reference, null, oldFields, newFields);
                    }
                    else
                    {
                        Add(PdfModificationKind.FormStructure, reference, null, "the form's Fields changed");
                    }
                }
                else if (key.Equals(FormNames.SigFlags))
                {
                    Add(SigningKind(), reference, null, "the form's SigFlags changed");
                }
                else if (key.Equals(NeedAppearances) || key.Equals(FormNames.DA) || key.Equals(FormNames.DR))
                {
                    Add(PdfModificationKind.FormFilling, reference, null, $"the form's {key.Value} changed");
                }
                else
                {
                    Add(PdfModificationKind.FormStructure, reference, null, $"the form's {key.Value} changed");
                }
            }
        }

        private void CompareFields(CosReference reference, RoleInfo role, CosDictionary before, CosDictionary after)
        {
            string? name = role.FieldName;
            foreach (CosName key in ChangedKeys(before, after))
            {
                if (key.Equals(FormNames.V) && role.IsSignatureField)
                {
                    if (NewlySigned.TryGetValue(reference.ObjectNumber, out bool timestamp))
                    {
                        Add(timestamp ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signing, reference, name, "the signature field was signed");
                    }
                    else
                    {
                        Add(PdfModificationKind.Other, reference, name, "the signature field's signature was replaced or removed");
                    }
                }
                else if (key.Equals(FormNames.V) || key.Equals(RV) || key.Equals(I) || key.Equals(TI))
                {
                    Add(PdfModificationKind.FormFilling, reference, name, $"the field's {key.Value} changed");
                }
                else if (key.Equals(AS) || key.Equals(AP))
                {
                    Add(FieldKind(role), reference, name, $"the field's widget {key.Value} changed");
                }
                else if (key.Equals(Kids) && older.Resolve(before.GetValueOrDefault(key)) is CosArray oldKids && newer.Resolve(after.GetValueOrDefault(key)) is CosArray newKids)
                {
                    CompareFieldArrays(reference, name, oldKids, newKids);
                }
                else if (key.Equals(Ff) && IsReadOnlyAdded(before, after))
                {
                    // Signing with a lock marks the locked fields read-only (§12.7.4.1, Table 227, bit 1).
                    Add(PdfModificationKind.Signing, reference, name, "the field was marked read-only");
                }
                else
                {
                    Add(PdfModificationKind.FormStructure, reference, name, $"the field's {key.Value} changed");
                }
            }
        }

        private static bool IsReadOnlyAdded(CosDictionary before, CosDictionary after)
        {
            long oldFlags = before.GetValueOrDefault(Ff) is CosInteger oldValue ? oldValue.Value : 0;
            return after.GetValueOrDefault(Ff) is CosInteger newValue && newValue.Value == (oldFlags | 1) && newValue.Value != oldFlags;
        }

        private void CompareWidgets(CosReference reference, RoleInfo role, CosDictionary before, CosDictionary after)
        {
            foreach (CosName key in ChangedKeys(before, after))
            {
                if (key.Equals(AS) || key.Equals(AP))
                {
                    Add(FieldKind(role), reference, role.FieldName, $"a widget's {key.Value} changed");
                }
                else
                {
                    Add(PdfModificationKind.FormStructure, reference, role.FieldName, $"a widget's {key.Value} changed");
                }
            }
        }

        private void CompareFieldArrays(CosReference reference, string? parent, CosArray before, CosArray after)
        {
            (List<CosObject> added, List<CosObject> removed) = Difference(before, after);
            foreach (CosObject element in added)
            {
                RoleInfo role = element is CosReference addedReference ? newer.RoleOf(addedReference.ObjectNumber) : default;
                if (role.IsSignatureField && NewlySigned.TryGetValue(role.FieldObject, out bool timestamp))
                {
                    Add(timestamp ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signing, reference, role.FieldName, "a signed signature field was added");
                }
                else
                {
                    Add(PdfModificationKind.FormStructure, reference, role.FieldName ?? parent, role.Role == ObjectRole.Widget ? "a widget was added to a field" : "a field was added");
                }
            }

            foreach (CosObject element in removed)
            {
                RoleInfo role = element is CosReference removedReference ? older.RoleOf(removedReference.ObjectNumber) : default;
                Add(PdfModificationKind.FormStructure, reference, role.FieldName ?? parent, role.Role == ObjectRole.Widget ? "a widget was removed from a field" : "a field was removed");
            }

            if (added.Count == 0 && removed.Count == 0)
            {
                Add(PdfModificationKind.FormStructure, reference, parent, "fields were reordered");
            }
        }

        private void ComparePages(CosReference reference, CosDictionary before, CosDictionary after)
        {
            foreach (CosName key in ChangedKeys(before, after))
            {
                if (key.Equals(AnnotationNames.Annots))
                {
                    CosArray oldAnnotations = older.Resolve(before.GetValueOrDefault(key)) as CosArray ?? [];
                    CosArray newAnnotations = newer.Resolve(after.GetValueOrDefault(key)) as CosArray ?? [];
                    CompareAnnotationArrays(reference, oldAnnotations, newAnnotations);
                }
                else
                {
                    Add(PdfModificationKind.Pages, reference, null, $"the page's {key.Value} changed");
                }
            }
        }

        private void CompareAnnotationArrays(CosReference reference, CosArray before, CosArray after)
        {
            (List<CosObject> added, List<CosObject> removed) = Difference(before, after);
            foreach (CosObject element in added)
            {
                RoleInfo role = element is CosReference addedReference ? newer.RoleOf(addedReference.ObjectNumber) : default;
                if (role.Role is ObjectRole.Field or ObjectRole.Widget)
                {
                    if (role.IsSignatureField && NewlySigned.TryGetValue(role.FieldObject, out bool timestamp))
                    {
                        Add(timestamp ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signing, reference, role.FieldName, "a signed signature field's widget was added to a page");
                    }
                    else
                    {
                        Add(PdfModificationKind.FormStructure, reference, role.FieldName, "a widget was added to a page");
                    }
                }
                else
                {
                    Add(PdfModificationKind.Annotations, reference, null, "an annotation was added to a page");
                }
            }

            foreach (CosObject element in removed)
            {
                RoleInfo role = element is CosReference removedReference ? older.RoleOf(removedReference.ObjectNumber) : default;
                Add(
                    role.Role is ObjectRole.Field or ObjectRole.Widget ? PdfModificationKind.FormStructure : PdfModificationKind.Annotations,
                    reference,
                    role.FieldName,
                    role.Role is ObjectRole.Field or ObjectRole.Widget ? "a widget was removed from a page" : "an annotation was removed from a page");
            }

            if (added.Count == 0 && removed.Count == 0)
            {
                Add(PdfModificationKind.Annotations, reference, null, "a page's annotations were reordered or changed in place");
            }
        }

        /// <summary>The elements one array has and the other has not, references compared by number and direct objects by value.</summary>
        private static (List<CosObject> Added, List<CosObject> Removed) Difference(CosArray before, CosArray after)
        {
            HashSet<CosReference> beforeReferences = [.. before.OfType<CosReference>()];
            HashSet<CosReference> afterReferences = [.. after.OfType<CosReference>()];
            var added = after.Where(element => element is CosReference reference ? !beforeReferences.Contains(reference) : !before.Any(other => CosEquality.DeepEquals(element, other))).ToList();
            var removed = before.Where(element => element is CosReference reference ? !afterReferences.Contains(reference) : !after.Any(other => CosEquality.DeepEquals(element, other))).ToList();
            return (added, removed);
        }

        private static IEnumerable<CosName> ChangedKeys(CosDictionary before, CosDictionary after)
        {
            foreach ((CosName key, CosObject value) in before)
            {
                if (!after.TryGetValue(key, out CosObject? other) || !CosEquality.DeepEquals(value, other))
                {
                    yield return key;
                }
            }

            foreach (CosName key in after.Keys)
            {
                if (!before.ContainsKey(key))
                {
                    yield return key;
                }
            }
        }
    }
}
