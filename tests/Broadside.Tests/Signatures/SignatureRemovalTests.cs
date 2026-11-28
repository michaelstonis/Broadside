using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using static Broadside.Tests.Signatures.SignatureValidationTests;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Clearing signed signature fields, removing signature fields and removing every signature (issue #660): ISO 32000-2 §12.7.5.5,
/// §12.7.3 (Table 225, <c>SigFlags</c>), §12.8.2.2 (the catalog's <c>Perms</c>), §12.8.4.3 (DSS) and §12.8.5 (document timestamps).
/// </summary>
public sealed class SignatureRemovalTests : IDisposable
{
    private static readonly CosName V = new("V");
    private static readonly CosName Perms = new("Perms");
    private static readonly CosName DocMdp = new("DocMDP");
    private static readonly CosName SigFlags = new("SigFlags");
    private static readonly CosName Dss = new("DSS");
    private static readonly CosName Lock = new("Lock");

    private readonly X509Certificate2 _signer = TestCertificates.SelfSigned("CN=Broadside removal signer");

    public void Dispose() => _signer.Dispose();

    [Fact]
    public void Clearing_an_approval_signature_leaves_an_empty_field_with_the_appearance_of_a_new_one()
    {
        byte[] signed = SignFields("approval");
        using PdfDocument document = PdfDocument.Open(signed);
        PdfSignatureField field = Field(document, "approval");
        Assert.True(field.IsSigned);
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, document.AcroForm!.SignatureFlags);

        field.ClearSignature();

        Assert.False(field.IsSigned);
        Assert.Null(field.SignatureDictionary);
        Assert.False(field.Dictionary.ContainsKey(V));
        PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
        PdfFormXObject appearance = widget.GetAppearance()!;
        Assert.Equal(new PdfRectangle(0, 0, 200, 60), appearance.BoundingBox);
        Assert.Equal(PdfSignatureFlags.SignaturesExist, document.AcroForm.SignatureFlags);
        Assert.Empty(document.ValidateSignatures());
    }

    [Fact]
    public Task The_cleared_field_s_appearance_is_the_one_a_new_signature_field_has() => Verify(ClearedAppearance());

    /// <summary>Clears a signed field, checks its appearance equals a new field's, and describes it for the snapshot.</summary>
    private string ClearedAppearance()
    {
        byte[] signed = SignFields("approval");
        using PdfDocument document = PdfDocument.Open(signed);
        PdfSignatureField field = Field(document, "approval");

        field.ClearSignature();

        PdfFormXObject appearance = field.Widgets[0].GetAppearance()!;
        using PdfDocument fresh = PdfDocument.Create();
        PdfSignatureField created = fresh.CreateAcroForm().AddSignatureField("approval", fresh.Pages[0], new PdfRectangle(72, 72, 272, 132), FieldOptions());
        PdfFormXObject expected = created.Widgets[0].GetAppearance()!;
        Assert.Equal(Encoding.Latin1.GetString(expected.Decode().Span), Encoding.Latin1.GetString(appearance.Decode().Span));
        Assert.Equal(expected.BoundingBox, appearance.BoundingBox);

        var snapshot = new StringBuilder();
        snapshot.Append("BBox: ").Append(Format(appearance.BoundingBox!.Value)).Append('\n');
        snapshot.Append(Encoding.Latin1.GetString(appearance.Decode().Span));
        return snapshot.ToString();
    }

    [Fact]
    public void Clearing_a_certification_signature_removes_the_catalog_permissions()
    {
        byte[] certified = SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_signer), new PdfSignatureOptions().WithCertification(PdfCertificationPermissions.NoChanges));
        using PdfDocument document = PdfDocument.Open(certified);
        Assert.True(document.Catalog.ContainsKey(Perms));

        Field(document, "Signature1").ClearSignature();

        Assert.False(document.Catalog.ContainsKey(Perms));
        Assert.False(Field(document, "Signature1").IsSigned);
    }

    [Fact]
    public void Clearing_a_certification_keeps_the_other_permissions_and_removing_all_reports_it()
    {
        byte[] certified = SignCorpusFile("acroform-fields.pdf", new PdfCertificateSigner(_signer), new PdfSignatureOptions().WithCertification(PdfCertificationPermissions.FormFilling));
        using PdfDocument document = PdfDocument.Open(certified);
        var permissions = (CosDictionary)document.Resolve(document.Catalog[Perms]);
        var usageRights = new CosDictionary { [new CosName("Type")] = new CosName("Sig") };
        permissions[new CosName("UR3")] = document.AddObject(usageRights);

        PdfSignatureRemoval removal = document.RemoveSignatures();

        Assert.True(removal.CertificationRemoved);
        Assert.Equal(1, removal.SignaturesRemoved);
        Assert.Equal(0, removal.FieldsRemoved);
        CosDictionary kept = Assert.IsType<CosDictionary>(document.Resolve(document.Catalog[Perms]));
        Assert.False(kept.ContainsKey(DocMdp));
        Assert.True(kept.ContainsKey(new CosName("UR3")));
    }

    [Fact]
    public void Signature_flags_are_recomputed_as_fields_are_cleared_and_removed()
    {
        byte[] signed = SignFields("first", "second");
        using PdfDocument document = PdfDocument.Open(signed);
        PdfAcroForm form = document.AcroForm!;

        Field(document, "first").ClearSignature();
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, form.SignatureFlags);

        Field(document, "second").ClearSignature();
        Assert.Equal(PdfSignatureFlags.SignaturesExist, form.SignatureFlags);

        form.RemoveField(Field(document, "first"));
        Assert.Equal(PdfSignatureFlags.SignaturesExist, form.SignatureFlags);

        form.RemoveField(Field(document, "second"));
        Assert.Equal(PdfSignatureFlags.None, form.SignatureFlags);
        Assert.False(form.Dictionary.ContainsKey(SigFlags));
    }

    [Fact]
    public void Clearing_keeps_the_field_lock_and_the_cleared_field_signs_again_with_it()
    {
        using PdfDocument created = NewDocument("locked");
        using var output = new MemoryStream();
        created.Sign(output, new PdfCertificateSigner(_signer), new PdfSignatureOptions().WithFieldName("locked").WithFieldLock(PdfFieldLockOptions.Include("other")));
        using PdfDocument document = PdfDocument.Open(output.ToArray());
        PdfSignatureField field = Field(document, "locked");
        CosObject lockReference = field.Dictionary[Lock];

        field.ClearSignature();

        Assert.Same(lockReference, field.Dictionary[Lock]);
        Assert.Equal(PdfSignatureFieldLockAction.Include, field.Lock!.Action);
        Assert.Equal(["other"], field.Lock.FieldNames);
    }

    [Fact]
    public void Removing_a_field_removes_its_widget_from_the_page_and_the_structure_tree()
    {
        using PdfDocument document = NewDocument("approval");
        PdfPage page = document.Pages[0];
        PdfSignatureField field = Field(document, "approval");
        PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
        Broadside.Structure.PdfStructureTreeRoot tree = document.CreateStructureTree();
        Broadside.Structure.PdfStructureElement form = tree.AddElement("Form");
        form.AddObjectReference(widget.Reference!, page);
        Assert.NotNull(widget.StructParent);

        document.AcroForm!.RemoveField(field);

        Assert.Empty(document.AcroForm.Fields);
        Assert.Empty(page.Annotations);
        using PdfDocument reopened = PdfDocument.Open(Saved(document, new PdfSaveOptions().WithRemoveUnreachableObjects(true)));
        Broadside.Structure.PdfStructureElement element = Assert.Single(reopened.StructureTree!.Children.OfType<Broadside.Structure.PdfStructureElement>());
        Assert.Empty(element.Children);
        Assert.Empty(reopened.Pages[0].Annotations);
        Assert.Empty(reopened.AcroForm!.Fields);
    }

    [Fact]
    public void Removing_a_signed_field_leaves_the_other_signed_field_and_refuses_a_field_of_another_document()
    {
        byte[] signed = SignFields("first", "second");
        using PdfDocument document = PdfDocument.Open(signed);
        PdfAcroForm form = document.AcroForm!;

        form.RemoveField(Field(document, "first"));

        Assert.Equal(["second"], form.TerminalFields.Select(static field => field.FullyQualifiedName));
        Assert.Single(document.Pages[0].Annotations);
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, form.SignatureFlags);
        using PdfDocument other = PdfDocument.Open(signed);
        Assert.Throws<ArgumentException>(() => form.RemoveField(Field(other, "second")));
        Assert.Throws<ArgumentNullException>(() => form.RemoveField(null!));
    }

    [Fact]
    public void Removing_every_signature_keeps_the_fields_unless_asked()
    {
        byte[] signed = SignFields("first", "second");
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            PdfSignatureRemoval removal = document.RemoveSignatures();

            Assert.Equal(2, removal.SignaturesRemoved);
            Assert.Equal(0, removal.FieldsRemoved);
            Assert.False(removal.CertificationRemoved);
            Assert.All(document.AcroForm!.TerminalFields.OfType<PdfSignatureField>(), static field => Assert.False(field.IsSigned));
            Assert.Equal(2, document.AcroForm.TerminalFields.Count);
            Assert.Equal(PdfSignatureFlags.SignaturesExist, document.AcroForm.SignatureFlags);
        }

        using (PdfDocument document = PdfDocument.Open(signed))
        {
            PdfSignatureRemoval removal = document.RemoveSignatures(new PdfSignatureRemovalOptions().WithRemoveFields(true));

            Assert.Equal(2, removal.SignaturesRemoved);
            Assert.Equal(2, removal.FieldsRemoved);
            Assert.Empty(document.AcroForm!.TerminalFields);
            Assert.Empty(document.Pages[0].Annotations);
            Assert.Equal(PdfSignatureFlags.None, document.AcroForm.SignatureFlags);
        }
    }

    [Fact]
    public void Document_timestamps_are_removed_unless_excluded_and_the_security_store_goes_only_when_nothing_is_left()
    {
        byte[] timestamped = TimestampAndStore(SignFields("approval"));

        using (PdfDocument document = PdfDocument.Open(timestamped))
        {
            PdfSignatureRemoval removal = document.RemoveSignatures(new PdfSignatureRemovalOptions().WithIncludeDocumentTimestamps(false));

            Assert.Equal(1, removal.SignaturesRemoved);
            Assert.False(removal.SecurityStoreRemoved);
            Assert.False(Field(document, "approval").IsSigned);
            Assert.True(Field(document, "timestamp").IsSigned);
            Assert.True(document.Catalog.ContainsKey(Dss));
            Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, document.AcroForm!.SignatureFlags);

            PdfSignatureRemoval rest = document.RemoveSignatures();

            Assert.Equal(1, rest.SignaturesRemoved);
            Assert.True(rest.SecurityStoreRemoved);
            Assert.False(document.Catalog.ContainsKey(Dss));
            Assert.Equal(PdfSignatureFlags.SignaturesExist, document.AcroForm.SignatureFlags);
        }

        using (PdfDocument document = PdfDocument.Open(timestamped))
        {
            PdfSignatureRemoval removal = document.RemoveSignatures(new PdfSignatureRemovalOptions().WithRemoveSecurityStore(false));

            Assert.Equal(2, removal.SignaturesRemoved);
            Assert.False(removal.SecurityStoreRemoved);
            Assert.True(document.Catalog.ContainsKey(Dss));
        }
    }

    [Fact]
    public void A_full_rewrite_after_removal_holds_no_signature_and_the_cleared_field_signs_again()
    {
        byte[] signed = SignFields("approval");
        byte[] cleared;
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            PdfSignatureRemoval removal = document.RemoveSignatures();
            PdfSaveOptions options = removal.CreateSaveOptions();
            Assert.False(options.IncrementalUpdate);
            Assert.True(options.RemoveUnreachableObjects);
            cleared = Saved(document, options);
        }

        Assert.DoesNotContain("/ByteRange", Encoding.Latin1.GetString(cleared), StringComparison.Ordinal);
        using (PdfDocument reopened = PdfDocument.Open(cleared))
        {
            Assert.Empty(reopened.ValidateSignatures());
            Assert.False(Field(reopened, "approval").IsSigned);

            byte[] resigned = Sign(reopened, new PdfCertificateSigner(_signer), new PdfSignatureOptions().WithFieldName("approval"));
            PdfSignatureValidation validation = ValidateSingle(resigned, Trusting(_signer));
            Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
            Assert.Equal("approval", validation.FieldName);
        }
    }

    [Fact]
    public void An_incremental_save_after_removal_keeps_the_signature_in_the_earlier_revision()
    {
        byte[] signed = SignFields("approval");
        using PdfDocument document = PdfDocument.Open(signed);
        document.RemoveSignatures();

        byte[] updated = Saved(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.Contains("/ByteRange", Encoding.Latin1.GetString(updated), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(updated);
        Assert.False(Field(reopened, "approval").IsSigned);
    }

    [Fact]
    public void Clearing_an_unsigned_field_changes_nothing()
    {
        using PdfDocument document = NewDocument("approval");
        string before = CosSnapshot.Of(document);

        Field(document, "approval").ClearSignature();

        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_damaged_value_is_cleared_and_a_value_shared_by_two_fields_is_cleared_from_both()
    {
        using PdfDocument document = NewDocument("damaged", "first", "second");
        CosDictionary signature = new() { [new CosName("Type")] = new CosName("Sig") };
        CosReference shared = document.AddObject(signature);
        Field(document, "damaged").Dictionary[V] = new CosInteger(7);
        Field(document, "first").Dictionary[V] = shared;
        Field(document, "second").Dictionary[V] = shared;
        document.AcroForm!.Dictionary[SigFlags] = new CosInteger(3);

        Field(document, "damaged").ClearSignature();
        Assert.False(Field(document, "damaged").Dictionary.ContainsKey(V));
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, document.AcroForm.SignatureFlags);

        Field(document, "first").ClearSignature();

        Assert.False(Field(document, "first").IsSigned);
        Assert.False(Field(document, "second").IsSigned);
        Assert.False(Field(document, "second").Dictionary.ContainsKey(V));
        Assert.Equal(PdfSignatureFlags.SignaturesExist, document.AcroForm.SignatureFlags);
    }

    [Fact]
    public void Removing_signatures_from_a_document_without_a_form_removes_nothing()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfSignatureRemoval removal = document.RemoveSignatures();

        Assert.Equal(0, removal.SignaturesRemoved);
        Assert.Equal(0, removal.FieldsRemoved);
        Assert.False(removal.CertificationRemoved);
        Assert.False(removal.SecurityStoreRemoved);
        Assert.Throws<ArgumentNullException>(() => document.RemoveSignatures(null!));
    }

    private static PdfFieldOptions FieldOptions() => new PdfFieldOptions().WithBackgroundColor(new PdfDeviceColor(0.9));

    /// <summary>A new document with a visible, unsigned signature field for each name, on the first page, one above the other.</summary>
    private static PdfDocument NewDocument(params string[] names)
    {
        PdfDocument document = PdfDocument.Create();
        PdfAcroForm form = document.CreateAcroForm();
        for (int index = 0; index < names.Length; index++)
        {
            form.AddSignatureField(names[index], document.Pages[0], new PdfRectangle(72, 72 + (100 * index), 272, 132 + (100 * index)), FieldOptions());
        }

        return document;
    }

    /// <summary>A file with a visible signature field for each name, each signed in its own revision.</summary>
    private byte[] SignFields(params string[] names)
    {
        using PdfDocument created = NewDocument(names);
        byte[] file = Saved(created, new PdfSaveOptions());
        foreach (string name in names)
        {
            using PdfDocument document = PdfDocument.Open(file);
            file = Sign(document, new PdfCertificateSigner(_signer), new PdfSignatureOptions().WithFieldName(name));
        }

        return file;
    }

    /// <summary>Adds a document timestamp in a field named <c>timestamp</c>, then a document security store, each in its own revision.</summary>
    private static byte[] TimestampAndStore(byte[] file)
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using (PdfDocument document = PdfDocument.Open(file))
        {
            using var output = new MemoryStream();
            document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address), "timestamp");
            file = output.ToArray();
        }

        using (PdfDocument document = PdfDocument.Open(file))
        {
            var certificate = new CosStream([], "certificate"u8.ToArray());
            document.Catalog[Dss] = document.AddObject(new CosDictionary { [new CosName("Certs")] = new CosArray([document.AddObject(certificate)]) });
            return Saved(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        }
    }

    private static PdfSignatureField Field(PdfDocument document, string name) => Assert.IsType<PdfSignatureField>(document.AcroForm!.FindField(name));

    private static byte[] Saved(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    private static string Format(PdfRectangle rect) => string.Create(CultureInfo.InvariantCulture, $"[{rect.Left} {rect.Bottom} {rect.Right} {rect.Top}]");
}
