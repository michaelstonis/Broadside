# Well-Tagged PDF (WTPDF) 1.0

Clause titles verified against the document's table of contents. Rows are also keyed by the rule identifiers of veraPDF's WTPDF 1.0 validation profiles (veraPDF 1.28, `WTPDF-1-0-Reuse.xml`, `WTPDF-1-0-Accessibility.xml`, flavours `wt1r`, `wt1a`), which share ISO 14289-2's clause numbers (`8.2.5.25-1`) and add the declaration rules 6.1.2-1 and 6.1.3-1. Output only: `PdfSaveOptions.WellTaggedConformance` checks a document as for PDF/UA-2 and declares each level (issue #205); see `iso-14289-2.md` for the rules shared with PDF/UA-2. Split rows to deeper levels as clauses are implemented. Status: `not started` | `partial` | `done` | `n/a`.

| Clause | Title | Status | Implementing types | Tests | Notes |
|---|---|---|---|---|---|
| 6.1 | Conformance levels | done | `PdfWellTaggedConformance`, `PdfDeclaration`, `PdfDocument` | `Broadside.Layout.Tests.PdfUA2FlowTests.The_save_identifies_PDF_UA_2_declares_WTPDF_and_writes_a_PDF_2_0_file_that_reads_back`, `Broadside.Layout.Tests.PdfUA2FlowTests.A_report_declared_well_tagged_passes_verapdf_PDF_UA_2_and_both_WTPDF_profiles`, `Broadside.Layout.Tests.PdfUA2FlowTests.A_figure_without_alternate_text_is_declared_for_reuse_only_and_passes_verapdf_WTPDF_reuse`, `Broadside.Layout.Tests.PdfUA2FlowTests.Saving_again_keeps_one_declaration_per_level` | 6.1.2-1, 6.1.3-1: one PDF Declaration per level in the document XMP (`pdfd:declarations`), read back by `PdfDocument.Declarations`; existing declarations are kept, none duplicated. The URIs are spelled as veraPDF tests them (`wtpdf#reuse1.0`); the specification's text has `wtpdf/#reuse1.0`. |
| 6.2 | Conformance requirements | done | `PdfDocument` | `Broadside.Layout.Tests.PdfUA2FlowTests.The_save_identifies_PDF_UA_2_declares_WTPDF_and_writes_a_PDF_2_0_file_that_reads_back` | A WTPDF save writes a PDF 2.0 file (`VersionedFeatures.PdfUA2Output`), MarkInfo Marked, DisplayDocTitle and Tabs. |
| 7 | Using other specifications and standards | done | `PdfDocument` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_report_saved_as_PDF_A_4_and_PDF_UA_2_passes_both_verapdf_profiles` | Combines with PDF/UA-2 and PDF/A-4 in one packet. |
| 8.2 | Logical structure | partial | `PdfUAChecker`, `FlowStructure` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_lenient_save_reports_the_condition_and_neither_identifies_nor_declares`, `Broadside.Layout.Tests.PdfUA2FlowTests.A_figure_without_alternate_text_is_declared_for_reuse_only_and_passes_verapdf_WTPDF_reuse` | As ISO 14289-2 8.2 (see `iso-14289-2.md`); the reuse level does not require 8.2.5.28.2-1 (Figure Alt). |
| 8.3 | Artifacts | done | `PdfUAChecker` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_report_declared_well_tagged_passes_verapdf_PDF_UA_2_and_both_WTPDF_profiles` | 8.2.2-1, as ISO 14289-2. |
| 8.4 | Text representation for content | partial | `PdfUAChecker` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_lenient_save_reports_the_condition_and_neither_identifies_nor_declares` | As ISO 14289-2 8.4. |
| 8.5 | Real content without textual semantics | not started | | | |
| 8.6 | Text string objects | not started | | | |
| 8.7 | Optional content | not started | | | |
| 8.8 | Intra-document destinations | not started | | | |
| 8.9 | Annotations | partial | `PdfUAChecker`, `PdfDocument` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_report_declared_well_tagged_passes_verapdf_PDF_UA_2_and_both_WTPDF_profiles` | As ISO 14289-2 8.9 (Tabs written, markup annotations in Annot). |
| 8.10 | Forms | not started | | | |
| 8.11 | Metadata | done | `PdfUAChecker`, `PdfDocument` | `Broadside.Layout.Tests.PdfUA2FlowTests.A_lenient_save_reports_the_condition_and_neither_identifies_nor_declares` | 8.11.1-1 (dc:title) checked; 8.11.2-1 (DisplayDocTitle, accessibility level) written. |
| 8.12 | Navigation | not started | | | |
| 8.13 | Actions | not started | | | |
| 8.14 | Use of embedded files | not started | | | |
