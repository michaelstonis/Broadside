using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// The report sites of the content stream, function, color, shading, action, annotation, form and structure areas use the public
/// catalog (issue #246): the internal catalog has no group for these areas and nothing names one of their codes through it.
/// </summary>
public sealed class ContentDiagnosticMigrationTests
{
    [Fact]
    public void No_report_site_for_content_functions_color_shading_actions_annotations_forms_and_structure_uses_the_internal_catalog()
    {
        DiagnosticCatalogMigrationTests.AssertMigrated(
        [
            typeof(ContentDiagnostics),
            typeof(FunctionDiagnostics),
            typeof(ColorDiagnostics),
            typeof(ShadingDiagnostics),
            typeof(ActionDiagnostics),
            typeof(AnnotationDiagnostics),
            typeof(FormDiagnostics),
            typeof(StructureDiagnostics),
        ]);
    }
}
