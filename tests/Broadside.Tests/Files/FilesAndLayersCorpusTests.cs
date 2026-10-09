using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>
/// Reading optional content, embedded files, collections, associated files and object metadata of every well-formed corpus file
/// records no diagnostic and changes no object (ADR 0004, ADR 0005; ISO 32000-2 §7.11, §8.11, §12.3.5, §14.3.2, §14.13).
/// </summary>
public class FilesAndLayersCorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Reading_files_and_layers_of_a_well_formed_file_records_no_diagnostic(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        ReadEverything(document);

        Assert.Empty(document.Diagnostics);
        foreach (CosReference reference in Enumerable.Range(1, 200).Select(number => new CosReference(number, 0)))
        {
            Assert.False(document.Resolve(reference).IsDirty);
        }
    }

    internal static void ReadEverything(PdfDocument document)
    {
        if (document.OptionalContent is { } optionalContent)
        {
            PdfOptionalContentState state = optionalContent.GetDefaultStates();
            foreach (PdfOptionalContentConfiguration configuration in optionalContent.Configurations)
            {
                optionalContent.GetStates(configuration, state);
                _ = (configuration.Order, configuration.RadioButtonGroups, configuration.Locked, configuration.AutoStates);
            }

            foreach (PdfPage page in document.Pages)
            {
                if (page.Resources is { } resources && document.Resolve(resources.GetValueOrDefault(new CosName("Properties"))) is CosDictionary properties)
                {
                    foreach (CosObject value in properties.Values)
                    {
                        optionalContent.IsVisible(value, state);
                        optionalContent.FindMembership(value)?.VisibilityExpression?.ToString();
                    }
                }
            }
        }

        _ = document.Declarations;
        foreach (PdfEmbeddedFileEntry entry in document.EmbeddedFiles)
        {
            Touch(entry.File);
        }

        if (document.Collection is { } collection)
        {
            _ = (collection.View, collection.Schema.Select(field => (field.Type, field.Name, field.Order)).ToList(), collection.Sort?.Ascending,
                collection.Colors?.Background, collection.Split.Direction, collection.RootFolder?.Children);
        }

        foreach (PdfAssociatedFile file in document.EnumerateAssociatedFiles(deep: true))
        {
            Touch(file.File);
        }

        foreach (PdfObjectMetadata metadata in document.EnumerateObjectMetadata(deep: true))
        {
            metadata.Decode();
            _ = metadata.Declarations;
        }

        static void Touch(PdfFileSpecification file)
        {
            _ = (file.FileName, file.SafeFileName, file.Description, file.Relationship, file.RelatedFiles, file.CollectionItem?.Keys);
            if (file.EmbeddedFile is { } embedded)
            {
                embedded.Decode();
                embedded.VerifyCheckSum();
            }
        }
    }
}
