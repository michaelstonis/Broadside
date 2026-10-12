using System.Text;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>XFA resources are detected and kept, never rendered (ISO 32000-2 Annex K, §12.7.3 Table 224, §7.7.2 Table 29).</summary>
public sealed class XfaFormTests
{
    [Fact]
    public void Opening_a_form_with_an_xfa_resource_records_an_information_diagnostic_and_exposes_the_packets()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-xfa.pdf"));

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("XfaFormPresent", DiagnosticSeverity.Information, 4), (diagnostic.Code, diagnostic.Severity, diagnostic.ObjectReference!.ObjectNumber));

        PdfXfaForm xfa = document.AcroForm!.Xfa!;
        Assert.False(xfa.IsDynamic);
        Assert.Equal(["xdp:xdp", "template", "datasets", "</xdp:xdp>"], xfa.Packets.Select(packet => packet.Name));
        Assert.Equal(12, xfa.Packets[1].Reference!.ObjectNumber);
        Assert.StartsWith("<template", Encoding.UTF8.GetString(document.DecodeStream(xfa.Packets[1].Stream).Span), StringComparison.Ordinal);
        Assert.Equal("Ada", ((PdfTextField)Assert.Single(document.AcroForm.Fields)).Value);
        Assert.Single(document.Diagnostics);
    }

    [Fact]
    public void Strict_mode_opens_an_xfa_form_because_information_is_never_thrown()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-xfa.pdf"), new PdfOptions().UseStrict());

        Assert.Equal("XfaFormPresent", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_dynamic_xfa_form_is_distinguished_by_the_catalogs_needs_rendering()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R /NeedsRendering true >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Fields [] /XFA 5 0 R >>",
            "<< /Length 10 >>\nstream\n<xdp:xdp/>\nendstream");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        Assert.Equal(["XfaFormPresent", "XfaFormDynamic"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.All(document.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
        PdfXfaForm xfa = document.AcroForm!.Xfa!;
        Assert.True(xfa.IsDynamic);
        PdfXfaPacket packet = Assert.Single(xfa.Packets);
        Assert.Null(packet.Name);
        Assert.Empty(document.AcroForm.Fields);
    }

    [Fact]
    public void A_malformed_packet_array_skips_the_bad_pairs_with_a_warning()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] /XFA [(template) 4 0 R /datasets 4 0 R (config)] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Length 10 >>\nstream\n<template>\nendstream");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(["template"], document.AcroForm!.Xfa!.Packets.Select(packet => packet.Name));
        Assert.Equal(["XfaFormPresent", "XfaPacketsInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }
}
