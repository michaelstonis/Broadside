using Broadside.Tests.Document;

namespace Broadside.Tests.OptionalContent;

/// <summary>Builds a one-page file whose catalog carries <c>/OCProperties</c> with the given entries; extra objects start at number 4.</summary>
internal static class OptionalContentPdf
{
    public static byte[] Build(string ocPropertiesEntries, params string[] objects) => new TestPdf().Build(
    [
        $"<< /Type /Catalog /Pages 2 0 R /OCProperties << {ocPropertiesEntries} >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
        .. objects,
    ]);
}
