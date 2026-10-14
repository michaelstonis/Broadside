using System.Security.Cryptography;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Writing;

namespace Broadside;

/// <content>Certification signatures and signature field locks (issue #199): ISO 32000-2 §12.8.2.2, §12.8.2.4 and §12.7.5.5.</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Decides, before anything changes, what modification detection a signature writes: a DocMDP reference for a certification
    /// signature, a FieldMDP reference for a lock (the options' new one, or the field's own), and refuses what the document's
    /// permissions or the spec do not allow.
    /// </summary>
    private ModificationDetectionPlan PlanModificationDetection(PdfSignatureOptions options)
    {
        PdfAcroForm? form = AcroForm;
        PdfSignatureField[] signed = form is null ? [] : [.. form.TerminalFields.OfType<PdfSignatureField>().Where(static field => field.IsSigned)];
        if (options.Certification is not null && signed.Length > 0)
        {
            throw new InvalidOperationException(
                "A certification signature shall be the document's first signature (ISO 32000-2 §12.8.1: approval signatures follow it); the document is already signed.");
        }

        // §12.8.2.2, Table 263: with a DocMDP permission in effect, "PDF processors shall enforce the permissions"; P 1 (or a lock's
        // P 1, Table 236) permits no further signature.
        foreach (PdfSignatureField field in signed)
        {
            PdfCertificationPermissions? docMdp = TransformMethods.DocMdpPermissions(field.SignatureDictionary!, Resolve);
            PdfCertificationPermissions? lockPermissions = field.Dictionary.TryGetValue(FormNames.Lock, out CosObject? lockValue) && Resolve(lockValue) is CosDictionary lockDictionary
                ? TransformMethods.ReadLock(lockDictionary, Resolve)?.Permissions
                : null;
            if (docMdp == PdfCertificationPermissions.NoChanges || lockPermissions == PdfCertificationPermissions.NoChanges)
            {
                throw new InvalidOperationException(
                    $"The signature of '{field.FullyQualifiedName}' permits no changes to the document (P 1, ISO 32000-2 §12.8.2.2 and Table 236): another signature would invalidate it.");
            }
        }

        FieldLock? fieldLock = null;
        bool writeLock = false;
        if (options.FieldName is not null && form?.FindField(options.FieldName) is PdfSignatureField target && !target.IsSigned
            && target.Dictionary.TryGetValue(FormNames.Lock, out CosObject? existing))
        {
            if (options.FieldLock is not null)
            {
                throw new InvalidOperationException($"The signature field '{options.FieldName}' already has a lock dictionary (ISO 32000-2 §12.7.5.5); it cannot take another.");
            }

            if (Resolve(existing) is CosDictionary lockDictionary)
            {
                fieldLock = TransformMethods.ReadLock(lockDictionary, Resolve);
            }
        }
        else if (options.FieldLock is { } requested)
        {
            fieldLock = new FieldLock(requested.Action, requested.FieldNames, requested.Permissions);
            writeLock = true;
        }

        var features = new List<VersionedFeature>();
        if (options.Certification is not null)
        {
            features.Add(VersionedFeatures.CertificationSignature);
        }

        if (fieldLock is not null)
        {
            features.Add(VersionedFeatures.SignatureFieldLock);
        }

        if (writeLock && fieldLock!.Permissions is not null)
        {
            features.Add(VersionedFeatures.SignatureFieldLockPermissions);
        }

        return new ModificationDetectionPlan(options.Certification, fieldLock, writeLock, [.. features]);
    }

    /// <summary>
    /// Writes the plan into the new signature dictionary (its <c>Reference</c> array, every value direct) and, once the dictionary is
    /// the field's value, the field's lock dictionary and the catalog's <c>Perms</c> <c>DocMDP</c> entry.
    /// </summary>
    private void ApplyModificationDetection(
        ModificationDetectionPlan plan,
        CosDictionary signature,
        CosReference signatureReference,
        CosDictionary field,
        HashAlgorithmName digestAlgorithm,
        List<Action> undo)
    {
        var references = new CosArray();
        CosName digestMethod = new(digestAlgorithm == HashAlgorithmName.SHA384 ? "SHA384" : digestAlgorithm == HashAlgorithmName.SHA512 ? "SHA512" : "SHA256");
        if (plan.Certification is { } permissions)
        {
            // Table 256 and Table 257.
            references.Add(new CosDictionary
            {
                [KnownNames.Type] = TransformMethods.SigRef,
                [TransformMethods.TransformMethod] = TransformMethods.DocMdp,
                [TransformMethods.TransformParams] = new CosDictionary
                {
                    [KnownNames.Type] = TransformMethods.TransformParams,
                    [TransformMethods.P] = new CosInteger((int)permissions),
                    [TransformMethods.V] = TransformMethods.Version12,
                },
                [TransformMethods.DigestMethod] = digestMethod,
            });
        }

        if (plan.Lock is { } fieldLock)
        {
            // §12.8.2.4: Action and Fields are copied from the lock dictionary, since everything in a byte range signature's dictionary
            // is direct; Data, required for FieldMDP, is the document the analysis is performed on.
            var parameters = new CosDictionary
            {
                [KnownNames.Type] = TransformMethods.TransformParams,
                [FormNames.Action] = TransformMethods.ActionName(fieldLock.Action),
            };
            if (fieldLock.Action != PdfSignatureFieldLockAction.All)
            {
                parameters[FormNames.Fields] = FieldNameArray(fieldLock.Names);
            }

            parameters[TransformMethods.V] = TransformMethods.Version12;
            var reference = new CosDictionary
            {
                [KnownNames.Type] = TransformMethods.SigRef,
                [TransformMethods.TransformMethod] = TransformMethods.FieldMdp,
                [TransformMethods.TransformParams] = parameters,
            };
            if (Trailer.GetValueOrDefault(KnownNames.Root) is CosReference catalog)
            {
                reference[TransformMethods.Data] = catalog;
            }

            reference[TransformMethods.DigestMethod] = digestMethod;
            references.Add(reference);

            if (plan.WriteLock)
            {
                // Table 235: Lock "shall be an indirect reference".
                var lockDictionary = new CosDictionary
                {
                    [KnownNames.Type] = TransformMethods.SigFieldLock,
                    [FormNames.Action] = TransformMethods.ActionName(fieldLock.Action),
                };
                if (fieldLock.Action != PdfSignatureFieldLockAction.All)
                {
                    lockDictionary[FormNames.Fields] = FieldNameArray(fieldLock.Names);
                }

                if (fieldLock.Permissions is { } lockPermissions)
                {
                    lockDictionary[TransformMethods.P] = new CosInteger((int)lockPermissions);
                }

                CosReference lockReference = AddObject(lockDictionary);
                undo.Add(() => RemoveObject(lockReference));
                Set(field, FormNames.Lock, lockReference, undo);
            }
        }

        if (references.Count > 0)
        {
            signature[TransformMethods.Reference] = references;
        }

        if (plan.Certification is not null)
        {
            // Table 263: DocMDP is "an indirect reference to a signature dictionary".
            if (Resolve(Catalog.GetValueOrDefault(TransformMethods.Perms)) is CosDictionary perms)
            {
                Set(perms, TransformMethods.DocMdp, signatureReference, undo);
            }
            else
            {
                Set(Catalog, TransformMethods.Perms, new CosDictionary { [TransformMethods.DocMdp] = signatureReference }, undo);
            }
        }

        static CosArray FieldNameArray(IReadOnlyList<string> names) =>
            new([.. names.Select(static name => (CosObject)new CosString(TextStringEncoder.Encode(name)))]);
    }

    /// <summary>What modification detection a signature writes (issue #199).</summary>
    /// <param name="Certification">The DocMDP permissions of a certification signature, or <see langword="null"/>.</param>
    /// <param name="Lock">The fields the signature locks, or <see langword="null"/>.</param>
    /// <param name="WriteLock">Whether the lock is new and goes into the field as its lock dictionary.</param>
    /// <param name="Features">The versioned features they need.</param>
    private sealed record ModificationDetectionPlan(PdfCertificationPermissions? Certification, FieldLock? Lock, bool WriteLock, VersionedFeature[] Features);
}
