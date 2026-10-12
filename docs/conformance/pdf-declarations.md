# PDF Declarations

Clause titles verified against the document's table of contents. Split rows to deeper levels as clauses are implemented. Status: `not started` | `partial` | `done` | `n/a`.

| Clause | Title | Status | Implementing types | Tests | Notes |
|---|---|---|---|---|---|
| 5 | Relationship with PDF specifications and features | n/a | | | Informative |
| 6 | Established PDF Declarations | n/a | | | Any URI is exposed; the pdfa.org list is not built in |
| 7 | PDF Declaration syntax (namespace, example) | done | `PdfDeclaration`, `PdfDocument`, `PdfObjectMetadata` | `Broadside.Tests.Files.DeclarationTests.Document_declarations_come_from_the_catalog_XMP_with_trimmed_URIs_and_claims`, `Broadside.Tests.Files.DeclarationTests.An_object_level_declaration_is_read_from_the_objects_metadata`, `Broadside.Tests.Files.DeclarationTests.A_document_without_declarations_has_none` | Document- and object-level scope; http and https namespaces; `declarations.pdf` |
| 8 | PDF Declaration requirements (schema, property value types) | done | `PdfDeclaration`, `PdfDeclarationClaim` | `Broadside.Tests.Files.DeclarationTests.Document_declarations_come_from_the_catalog_XMP_with_trimmed_URIs_and_claims` | Tables 1-3; parseType Resource and nested rdf:Description structures |
| 9 | PDF Declarations in a PDF/A context | n/a | | | Extension schema not validated (PDF/A input validation is out of scope) |
