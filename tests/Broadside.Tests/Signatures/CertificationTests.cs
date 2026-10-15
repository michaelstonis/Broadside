using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.TestSupport;
using static Broadside.Tests.Signatures.SignatureValidationTests;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Certification signatures, signature field locks and modification detection through <see cref="PdfDocument.Sign"/> and
/// <see cref="PdfDocument.ValidateSignatures"/>: ISO 32000-2 §12.8.2.2 (DocMDP), §12.8.2.4 (FieldMDP), §12.7.5.5 (field locks).
/// Each later change is made through the public document API and saved as an incremental update, the way a later processor would.
/// </summary>
public sealed class CertificationTests : IDisposable
{
    private const string Author = "Signature1";

    private static readonly CosName FormNamesLock = new("Lock");

    private static readonly Dictionary<string, Action<PdfDocument>> Changes = new()
    {
        ["add a document security store"] = static document =>
        {
            var certificate = new CosStream([], "certificate"u8.ToArray());
            var store = new CosDictionary { [new CosName("Certs")] = new CosArray([document.AddObject(certificate)]) };
            document.Catalog[new CosName("DSS")] = document.AddObject(store);
        },
        ["add a document timestamp"] = static document =>
        {
            var timestamp = new CosDictionary
            {
                [new CosName("Type")] = new CosName("DocTimeStamp"),
                [new CosName("Filter")] = new CosName("Adobe.PPKLite"),
                [new CosName("SubFilter")] = new CosName("ETSI.RFC3161"),
            };
            var field = new CosDictionary
            {
                [new CosName("FT")] = new CosName("Sig"),
                [new CosName("T")] = new CosString("Timestamp"u8.ToArray()),
                [new CosName("V")] = document.AddObject(timestamp),
            };
            ((CosArray)document.Resolve(document.AcroForm!.Dictionary[new CosName("Fields")])).Add(document.AddObject(field));
        },
        ["change the document information"] = static document =>
        {
            var metadata = new CosStream(new CosDictionary { [new CosName("Type")] = new CosName("Metadata"), [new CosName("Subtype")] = new CosName("XML") }, "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray());
            document.Catalog[new CosName("Metadata")] = document.AddObject(metadata);
        },
        ["fill a field"] = static document => TextField(document, "person.last").SetValue("Hopper"),
        ["add an annotation"] = AddAnnotation,
        ["add a field"] = static document =>
        {
            var field = new CosDictionary { [new CosName("FT")] = new CosName("Tx"), [new CosName("T")] = new CosString("extra"u8.ToArray()) };
            ((CosArray)document.Resolve(document.AcroForm!.Dictionary[new CosName("Fields")])).Add(document.AddObject(field));
        },
        ["rotate a page"] = static document => document.Pages[0].Dictionary[new CosName("Rotate")] = new CosInteger(90),
        ["change the page mode"] = static document => document.Catalog[new CosName("PageMode")] = new CosName("UseOutlines"),
    };

    private readonly X509Certificate2 _author = TestCertificates.SelfSigned("CN=Broadside author");

    /// <summary>
    /// The table of change kinds (<see cref="PdfModificationKind"/>): one change of each kind, made after a certification signature
    /// with each permission, and whether it is permitted.
    /// </summary>
    public static TheoryData<string, PdfCertificationPermissions, PdfModificationKind, bool> ChangeKinds => new()
    {
        { "add a document security store", PdfCertificationPermissions.NoChanges, PdfModificationKind.DocumentSecurityStore, true },
        { "add a document timestamp", PdfCertificationPermissions.NoChanges, PdfModificationKind.DocumentTimestamp, true },
        { "change the document information", PdfCertificationPermissions.NoChanges, PdfModificationKind.DocumentInformation, true },
        { "fill a field", PdfCertificationPermissions.NoChanges, PdfModificationKind.FormFilling, false },
        { "fill a field", PdfCertificationPermissions.FormFilling, PdfModificationKind.FormFilling, true },
        { "sign", PdfCertificationPermissions.FormFilling, PdfModificationKind.Signing, true },
        { "sign", PdfCertificationPermissions.FormFillingAndAnnotations, PdfModificationKind.Signing, true },
        { "add an annotation", PdfCertificationPermissions.FormFilling, PdfModificationKind.Annotations, false },
        { "add an annotation", PdfCertificationPermissions.FormFillingAndAnnotations, PdfModificationKind.Annotations, true },
        { "add a field", PdfCertificationPermissions.FormFillingAndAnnotations, PdfModificationKind.FormStructure, false },
        { "rotate a page", PdfCertificationPermissions.FormFillingAndAnnotations, PdfModificationKind.Pages, false },
        { "change the page mode", PdfCertificationPermissions.FormFillingAndAnnotations, PdfModificationKind.Other, false },
    };

    public void Dispose() => _author.Dispose();

    [Fact]
    public void A_document_certified_for_form_filling_stays_valid_after_a_field_is_filled()
    {
        byte[] certified = Certify(PdfCertificationPermissions.FormFilling);

        byte[] filled = Update(certified, static document => TextField(document, "person.last").SetValue("Hopper"));

        PdfSignatureValidation validation = Validate(filled, Author);
        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Empty(validation.Problems);
        Assert.Equal(PdfCertificationPermissions.FormFilling, validation.CertificationPermissions);
        Assert.Equal(1, validation.SignedRevision);
        Assert.False(validation.CoversWholeFile);
        Assert.Contains(validation.Modifications, static change => change is { Kind: PdfModificationKind.FormFilling, FieldName: "person.last", Revision: 2, IsPermitted: true });
        Assert.All(validation.Modifications, static change => Assert.True(change.IsPermitted, change.ToString()));
    }

    [Fact]
    public void A_document_certified_for_form_filling_reports_a_violation_once_an_annotation_is_added()
    {
        byte[] certified = Certify(PdfCertificationPermissions.FormFilling);
        byte[] filled = Update(certified, static document => TextField(document, "person.last").SetValue("Hopper"));

        byte[] annotated = Update(filled, AddAnnotation);

        PdfSignatureValidation validation = Validate(annotated, Author);
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        PdfSignatureProblem problem = Assert.Single(validation.Problems);
        Assert.Equal(PdfSignatureProblemKind.ModificationNotPermitted, problem.Kind);
        Assert.Contains("Revision 3", problem.Message, StringComparison.Ordinal);
        PdfModification added = Assert.Single(validation.Modifications, static change => !change.IsPermitted);
        Assert.Equal(PdfModificationKind.Annotations, added.Kind);
        Assert.Equal(3, added.Revision);
        Assert.Contains(validation.Modifications, static change => change is { Kind: PdfModificationKind.FormFilling, Revision: 2, IsPermitted: true });
    }

    [Theory]
    [MemberData(nameof(ChangeKinds))]
    public void Each_kind_of_change_is_classified_and_permitted_as_the_table_says(string change, PdfCertificationPermissions permissions, PdfModificationKind kind, bool permitted)
    {
        byte[] certified = Certify(permissions);

        byte[] changed = change == "sign"
            ? SignAgain(certified, new PdfSignatureOptions().WithFieldName("Approver"))
            : Update(certified, Changes[change]);

        PdfSignatureValidation validation = Validate(changed, Author);
        PdfModification[] ofKind = [.. validation.Modifications.Where(modification => modification.Kind == kind)];
        Assert.NotEmpty(ofKind);
        Assert.All(ofKind, modification => Assert.Equal(permitted, modification.IsPermitted));
        Assert.All(validation.Modifications.Where(modification => modification.Kind != kind), static modification => Assert.Equal(PdfModificationKind.DocumentInformation, modification.Kind));
        Assert.Equal(permitted ? PdfSignatureStatus.Valid : PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal(permitted, !validation.Problems.Any(static problem => problem.Kind == PdfSignatureProblemKind.ModificationNotPermitted));
    }

    [Fact]
    public void A_signature_after_a_certification_that_permits_no_changes_is_not_permitted()
    {
        byte[] certified = Certify(PdfCertificationPermissions.NoChanges);
        using (PdfDocument document = PdfDocument.Open(certified))
        {
            Assert.Throws<InvalidOperationException>(() => Sign(document, new PdfCertificateSigner(_author)));
        }

        // A processor that does not enforce the permissions (here: the catalog's Perms entry, which asks for enforcement, removed
        // first) can still sign; the certification then reports both changes.
        byte[] signed;
        using (PdfDocument document = PdfDocument.Open(certified))
        {
            document.Catalog.Remove(new CosName("Perms"));
            signed = Sign(document, new PdfCertificateSigner(_author));
        }

        PdfSignatureValidation validation = Validate(signed, Author);
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Contains(validation.Modifications, static change => change is { Kind: PdfModificationKind.Signing, IsPermitted: false });
        Assert.Contains(validation.Modifications, static change => change is { Kind: PdfModificationKind.Other, IsPermitted: false });
    }

    [Fact]
    public void Annotations_can_be_added_modified_and_deleted_after_a_certification_that_permits_them()
    {
        byte[] certified = Certify(PdfCertificationPermissions.FormFillingAndAnnotations);
        byte[] added = Update(certified, AddAnnotation);
        byte[] modified = Update(added, static document => LastAnnotation(document)[new CosName("Contents")] = new CosString("Changed"u8.ToArray()));
        byte[] deleted = Update(modified, static document =>
        {
            var annotations = (CosArray)document.Resolve(document.Pages[0].Dictionary[new CosName("Annots")]);
            var reference = (CosReference)annotations[^1];
            annotations.RemoveAt(annotations.Count - 1);
            document.RemoveObject(reference);
        });

        PdfSignatureValidation validation = Validate(deleted, Author);

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal([2, 3, 4], validation.Modifications.Where(static change => change.Kind == PdfModificationKind.Annotations).Select(static change => change.Revision).Distinct());
        Assert.All(validation.Modifications, static change => Assert.True(change.IsPermitted, change.ToString()));
    }

    [Fact]
    public void Without_a_certification_annotations_are_permitted_after_an_approval_signature_and_page_changes_are_not()
    {
        byte[] approved = SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_author));
        byte[] annotated = Update(approved, AddAnnotation);
        Assert.Equal(PdfSignatureStatus.Valid, Validate(annotated, Author).Status);
        Assert.Null(Validate(annotated, Author).CertificationPermissions);

        byte[] rotated = Update(annotated, Changes["rotate a page"]);

        PdfSignatureValidation validation = Validate(rotated, Author);
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.ModificationNotPermitted], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal([PdfModificationKind.Pages], validation.Modifications.Where(static change => !change.IsPermitted).Select(static change => change.Kind));
    }

    [Fact]
    public void Signing_a_field_with_a_lock_locks_the_fields_it_names_and_leaves_the_others_free()
    {
        // acroform-fields.pdf's signature field 'sig' has a lock dictionary: Include (name) (person.first), P 2.
        byte[] signed = SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_author), new PdfSignatureOptions().WithFieldName("sig"));

        byte[] free = Update(signed, static document => TextField(document, "person.last").SetValue("Hopper"));
        Assert.Equal(PdfSignatureStatus.Valid, Validate(free, "sig").Status);

        byte[] locked = Update(free, static document => TextField(document, "person.first").SetValue("Grace"));

        PdfSignatureValidation validation = Validate(locked, "sig");
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        PdfSignatureProblem problem = Assert.Single(validation.Problems);
        Assert.Equal(PdfSignatureProblemKind.LockedFieldModified, problem.Kind);
        Assert.Contains("'person.first'", problem.Message, StringComparison.Ordinal);
        Assert.Contains(validation.Modifications, static change => change is { Kind: PdfModificationKind.FormFilling, FieldName: "person.first", IsPermitted: false });

        // The signature copies the lock into a FieldMDP signature reference (§12.8.2.4).
        using PdfDocument document = PdfDocument.Open(locked);
        CosDictionary reference = SignatureReference(document, "sig", "FieldMDP");
        var parameters = (CosDictionary)reference[new CosName("TransformParams")];
        Assert.Equal(new CosName("Include"), parameters[new CosName("Action")]);
        Assert.Equal(["name", "person.first"], ((CosArray)parameters[new CosName("Fields")]).Cast<CosString>().Select(static name => name.DecodeText()));
        Assert.Equal(document.Trailer[new CosName("Root")], reference[new CosName("Data")]);
    }

    [Fact]
    public void A_lock_given_in_the_options_is_written_to_the_new_field_and_locks_the_fields_under_a_named_one()
    {
        byte[] signed = SignCorpusFile(
            "acroform-fields.pdf",
            new PdfCertificateSigner(_author),
            new PdfSignatureOptions().WithFieldName("Reviewer").WithFieldLock(PdfFieldLockOptions.Include("person")));
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            var field = (PdfSignatureField)document.AcroForm!.FindField("Reviewer")!;
            Assert.NotNull(field.Lock);
            Assert.Equal(PdfSignatureFieldLockAction.Include, field.Lock.Action);
            Assert.Equal(["person"], field.Lock.FieldNames);
            Assert.Null(field.Lock.Permissions);
            Assert.IsType<CosReference>(field.Dictionary[FormNamesLock]);
        }

        byte[] nameFilled = Update(signed, static document => TextField(document, "name").SetValue("Grace Hopper"));
        Assert.Equal(PdfSignatureStatus.Valid, Validate(nameFilled, "Reviewer").Status);

        byte[] lastFilled = Update(nameFilled, static document => TextField(document, "person.last").SetValue("Hopper"));
        PdfSignatureValidation validation = Validate(lastFilled, "Reviewer");
        Assert.Equal([PdfSignatureProblemKind.LockedFieldModified], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_lock_with_permissions_narrows_what_later_updates_may_change()
    {
        byte[] signed = SignCorpusFile(
            "acroform-fields.pdf",
            new PdfCertificateSigner(_author),
            new PdfSignatureOptions().WithFieldLock(PdfFieldLockOptions.Exclude("name").WithPermissions(PdfCertificationPermissions.FormFilling)));
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            PdfSignatureFieldLock fieldLock = ((PdfSignatureField)document.AcroForm!.FindField(Author)!).Lock!;
            Assert.Equal(PdfSignatureFieldLockAction.Exclude, fieldLock.Action);
            Assert.Equal(2, fieldLock.Permissions);
            Assert.Equal(new PdfVersion(2, 0), document.Version);
        }

        byte[] filled = Update(signed, static document => TextField(document, "name").SetValue("Grace Hopper"));
        Assert.Equal(PdfSignatureStatus.Valid, Validate(filled, Author).Status);

        byte[] annotated = Update(filled, AddAnnotation);
        PdfSignatureValidation validation = Validate(annotated, Author);
        Assert.Equal([PdfSignatureProblemKind.ModificationNotPermitted], validation.Problems.Select(static problem => problem.Kind));
        Assert.Null(validation.CertificationPermissions);
    }

    [Fact]
    public void A_certification_signature_writes_its_docmdp_reference_and_the_catalog_permissions_and_verifies_with_pdfsig()
    {
        byte[] certified = Certify(PdfCertificationPermissions.FormFillingAndAnnotations);

        using PdfDocument document = PdfDocument.Open(certified);
        CosDictionary reference = SignatureReference(document, Author, "DocMDP");
        var parameters = (CosDictionary)reference[new CosName("TransformParams")];
        Assert.Equal(new CosInteger(3), parameters[new CosName("P")]);
        Assert.Equal(new CosName("1.2"), parameters[new CosName("V")]);
        Assert.Equal(new CosName("SigRef"), reference[new CosName("Type")]);
        var perms = (CosDictionary)document.Resolve(document.Catalog[new CosName("Perms")]);
        CosObject docMdp = perms[new CosName("DocMDP")];
        Assert.IsType<CosReference>(docMdp);
        Assert.Same(((PdfSignatureField)document.AcroForm!.FindField(Author)!).SignatureDictionary, document.Resolve(docMdp));
        Assert.Equal(PdfSignatureStatus.Valid, Assert.Single(document.ValidateSignatures(Trusting(_author))).Status);

        string report = Pdfsig.Run(certified);
        Assert.Equal(1, Pdfsig.ValidSignatures(report));
    }

    [Fact]
    public void What_certification_and_locks_do_not_allow_is_refused_before_anything_changes()
    {
        byte[] approved = SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_author));
        using (PdfDocument document = PdfDocument.Open(approved))
        {
            Assert.Throws<InvalidOperationException>(() => Sign(document, new PdfCertificateSigner(_author), new PdfSignatureOptions().WithCertification(PdfCertificationPermissions.FormFilling)));
            Assert.False(document.Catalog.ContainsKey(new CosName("Perms")));
        }

        using (PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf")))
        {
            Assert.Throws<InvalidOperationException>(() => Sign(document, new PdfCertificateSigner(_author), new PdfSignatureOptions().WithFieldName("sig").WithFieldLock(PdfFieldLockOptions.All())));
            Assert.False(((PdfSignatureField)document.AcroForm!.FindField("sig")!).IsSigned);
        }

        Assert.Throws<ArgumentException>(() => PdfFieldLockOptions.Include("name", ""));
    }

    [Fact]
    public void A_byte_range_that_does_not_end_at_the_end_of_a_revision_is_reported()
    {
        byte[] certified = Certify(PdfCertificationPermissions.FormFilling);
        long[] range = SignedFile.Last(certified).ByteRange;
        byte[] filled = Update(certified, static document => TextField(document, "person.last").SetValue("Hopper"));

        // The second range reaches 40 bytes into the update.
        byte[] stretched = SignedFileEditor.RewriteByteRange(filled, range[0], range[1], range[2], range[3] + 40);

        PdfSignatureValidation validation = Validate(stretched, Author);
        Assert.Null(validation.SignedRevision);
        Assert.Empty(validation.Modifications);
        Assert.Contains(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.SignedRevisionUnknown);
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
    }

    private byte[] Certify(PdfCertificationPermissions permissions) =>
        SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_author), new PdfSignatureOptions().WithCertification(permissions));

    private byte[] SignAgain(byte[] file, PdfSignatureOptions options)
    {
        using PdfDocument document = PdfDocument.Open(file);
        return Sign(document, new PdfCertificateSigner(_author), options);
    }

    private PdfSignatureValidation Validate(byte[] file, string fieldName)
    {
        using PdfDocument document = PdfDocument.Open(file);
        return Assert.Single(document.ValidateSignatures(Trusting(_author)), validation => validation.FieldName == fieldName);
    }

    /// <summary>Opens <paramref name="file"/>, applies <paramref name="change"/> and saves it as an incremental update.</summary>
    private static byte[] Update(byte[] file, Action<PdfDocument> change)
    {
        using PdfDocument document = PdfDocument.Open(file);
        change(document);
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true));
        return output.ToArray();
    }

    /// <summary>Adds a text annotation to the first page.</summary>
    private static void AddAnnotation(PdfDocument document)
    {
        var annotation = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Text"),
            [new CosName("Rect")] = new CosArray([new CosInteger(10), new CosInteger(10), new CosInteger(30), new CosInteger(30)]),
            [new CosName("Contents")] = new CosString("A comment"u8.ToArray()),
        };
        CosDictionary page = document.Pages[0].Dictionary;
        ((CosArray)document.Resolve(page[new CosName("Annots")])).Add(document.AddObject(annotation));
    }

    private static CosDictionary LastAnnotation(PdfDocument document) =>
        (CosDictionary)document.Resolve(((CosArray)document.Resolve(document.Pages[0].Dictionary[new CosName("Annots")]))[^1]);

    private static CosDictionary SignatureReference(PdfDocument document, string fieldName, string method)
    {
        CosDictionary signature = ((PdfSignatureField)document.AcroForm!.FindField(fieldName)!).SignatureDictionary!;
        return ((CosArray)signature[new CosName("Reference")]).Cast<CosDictionary>().Single(reference => reference[new CosName("TransformMethod")].Equals(new CosName(method)));
    }

    private static PdfTextField TextField(PdfDocument document, string name) => (PdfTextField)document.AcroForm!.FindField(name)!;
}
