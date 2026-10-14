using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>
/// Every check box, radio button and choice field of the pdf.js form files set: buttons get generated appearances and their last
/// widget turned on, choices their last option selected; the document saves and the saved file re-reads every value, appearance
/// state and selection. Skips when the corpus is not fetched. ISO 32000-2 §12.7.5.2, §12.7.5.4.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldButtonAndChoiceFillTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_button_and_choice_of_the_pdfjs_forms_sets_saves_and_re_reads()
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files("pdfjs");
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'pdfjs' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var options = new PdfOptions().UseSystemFontResolver(null);
        (int buttons, int choices, int refused) = (0, 0, 0);
        foreach (string file in files)
        {
            if (!RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                (int fileButtons, int fileChoices, int fileRefused) = SetFile(file, options);
                (buttons, choices, refused) = (buttons + fileButtons, choices + fileChoices, refused + fileRefused);
            }
        }

        output.WriteLine($"{buttons} buttons and {choices} choice fields set, {refused} choice fields refused (no font for their text)");
        Assert.True(buttons >= 50, $"Only {buttons} buttons were set; the corpus looks incomplete.");
        Assert.True(choices >= 10, $"Only {choices} choice fields were set; the corpus looks incomplete.");
    }

    /// <summary>Sets every button and choice of one file, saves and re-reads them; returns how many were set and refused.</summary>
    private static (int Buttons, int Choices, int Refused) SetFile(string file, PdfOptions options)
    {
        using PdfDocument? document = OpenOrNull(file, options);
        if (document is null || document.IsEncrypted || document.AcroForm is not { } form)
        {
            return default;
        }

        var states = new Dictionary<string, (CosName Value, int Widget)?>(StringComparer.Ordinal);
        var selections = new Dictionary<string, int?>(StringComparer.Ordinal);
        int refused = 0;
        foreach (PdfToggleButtonField field in form.TerminalFields.OfType<PdfToggleButtonField>())
        {
            field.GenerateAppearances();
            IReadOnlyList<PdfWidgetAnnotation> widgets = field.Widgets;
            if (widgets.Count == 0)
            {
                continue;
            }

            field.Select(widgets[^1]);
            CosName on = field.GetOnState(widgets[^1])!;
            Assert.Equal(on, widgets[^1].AppearanceState);
            states[field.FullyQualifiedName] = states.ContainsKey(field.FullyQualifiedName) ? null : (on, widgets.Count - 1);
        }

        foreach (PdfChoiceField field in form.TerminalFields.OfType<PdfChoiceField>())
        {
            int count = field.Options.Count;
            if (count == 0)
            {
                continue;
            }

            try
            {
                field.SetSelectedIndices([count - 1]);
            }
            catch (InvalidOperationException)
            {
                refused++;
                continue;
            }

            selections[field.FullyQualifiedName] = selections.ContainsKey(field.FullyQualifiedName) ? null : count - 1;
        }

        if (states.Count + selections.Count == 0)
        {
            return default;
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray(), options);
        foreach (PdfToggleButtonField field in reopened.AcroForm!.TerminalFields.OfType<PdfToggleButtonField>())
        {
            if (states.GetValueOrDefault(field.FullyQualifiedName) is ({ } value, int index))
            {
                Assert.True(value.Equals(field.Value), $"{file} {field.FullyQualifiedName}: {field.Value.Value}");
                Assert.True(value.Equals(field.Widgets[index].AppearanceState), $"{file} {field.FullyQualifiedName}: AS");
            }
        }

        foreach (PdfChoiceField field in reopened.AcroForm.TerminalFields.OfType<PdfChoiceField>())
        {
            if (selections.GetValueOrDefault(field.FullyQualifiedName) is int index && field.Options[index].ExportValue.Length > 0)
            {
                Assert.True(field.SelectedIndices.SequenceEqual([index]), $"{file} {field.FullyQualifiedName}: [{string.Join(' ', field.SelectedIndices)}]");
            }
        }

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Code is "ButtonValueStateMismatch" or "ButtonValueUnknownState" && diagnostic.Severity != DiagnosticSeverity.Information);
        return (states.Count, selections.Count, refused);
    }

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
