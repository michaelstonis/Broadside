---
title: "Guides"
---

# Guides

The guides walk through each part of the library with code you can copy. Every snippet in them is included from a console app in [`samples/guides`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides) that CI builds and runs on every change, so the code on these pages compiles against the current API and does what the text says. The site's build fails when a page's code differs from its sample.

| Guide | What it covers | Status |
|---|---|---|
| [Getting started](getting-started.md) | Installing the packages; opening, inspecting, changing and saving a file; creating a document; rendering a page. | Written |
| [Reading](reading.md) | Paths, streams and bytes; diagnostics and strict mode; passwords and certificates; the objects under the model; concurrency; large files; dependency injection and logging. | Written |
| [Rendering](rendering.md) | Every rendering backend, render options, tiles, layers, hit-testing, color management, determinism and memory budgets. | Being written for v1 |
| [Writing](writing.md) | The page canvas, fonts and subsetting, images, transparency, and flow layout. | Being written for v1 |
| [Building documents](building-documents.md) | The document builder: reports, tables, invoices and forms as nested content, tagged and accessible. | Written |
| [Editing](editing.md) | Incremental saves, page operations, merging and splitting, forms, annotations, metadata, attachments and encryption. | Being written for v1 |
| [Extracting text](extracting-text.md) | Text with positions, reading order and the layout tree. | Being written for v1 |
| [Signatures](signatures.md) | Signing, timestamps, long-term validation, external signers and validation. | Being written for v1 |
| [Accessibility and conformance](accessibility-and-conformance.md) | Automatic tagging, PDF/UA and PDF/A. | Being written for v1 |
| [Migration](migration.md) | Moving from PdfSharp, iText and PdfPig. | After v1 |
