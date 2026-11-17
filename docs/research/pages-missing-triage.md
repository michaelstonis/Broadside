# A catalog without a usable page tree: rebuild policy and triage (issue #258)

Triaged 2026-11-14 with pdf.js 5.4.530 (`pdfjs-dist` legacy build under Node), poppler 26.10.0 (`pdfinfo`) and qpdf 12.4.2 (`qpdf --show-npages`). PDFium was not available on the machine, so it is not in the table.

## Policy

ISO 32000-2 §7.7.2 (Table 29) makes the catalog's `Pages` entry required; §7.7.3 defines the page tree it leads to. Lenient reading recovers the pages in two steps, each recorded as a `PagesMissing` diagnostic:

1. **When opening.** If the catalog's `Pages` entry is absent, or names an object the cross-reference information does not list in use, the cross-reference information is rebuilt by scanning the file (as for a trailer whose `Root` is unusable). The rebuild is kept only when the scan finds a catalog with a page tree; the diagnostic is a Warning, and whatever the rebuild itself reports is kept with it. This check reads the cross-reference entry only, so opening a file still parses the catalog and nothing else.
2. **When the pages are first read.** If `Pages` still does not resolve to a dictionary, the pages are rebuilt from the objects the file holds: each page tree node (`/Type /Pages`) without a parent is walked as a root, in object number order; then each page object (`/Type /Page`) no walk reached is a page, in object number order, inheriting attributes through its `Parent` chain. The diagnostic is a Warning when this finds pages and an Error when it finds none (the document then has no pages, as before).

Strict mode throws `PagesMissing` in both places instead of repairing.

Step 1 gives the page counts pdf.js and poppler give (poppler says so: it reconstructs the cross-reference table when an object it needs is not listed). Step 2 goes further than any of the three reference readers, for files whose catalog leads nowhere at all; on the real-world corpora it only applies to fuzzed files that none of them opens.

## Triage of the real-world files whose output changes

These are every file of the real-world gate (pdf.js, PDFBox, qpdf, PDFium and PDF 2.0 example corpora) that reported `PagesMissing` before the change; no other file's page count or diagnostics change. The PR #83 notes counted ten such files; nine remain on the current integration branch.

| File | Before | After | Step | pdf.js | poppler | qpdf | Verdict |
|---|---|---|---|---|---|---|---|
| `pdfjs/PDFBOX-3148-2-fuzzed.pdf` | 0 | 1 | 1 | 1 | 1 | 1 (with warnings) | Improvement: agrees with all three. |
| `pdfjs/issue9418.pdf` | 0 | 1 | 1 | 1 | fails | fails | Improvement: agrees with pdf.js, whose regression file it is. |
| `pdfjs/poppler-395-0-fuzzed.pdf` | 0 | 1 | 2 | fails (invalid Root reference) | fails | fails | Improvement: the file's one page object, with its MediaBox and content stream, is recovered; no reference reader opens the file. |
| `qpdf/append-xref-loop.pdf` | 0 | 30 | 1 | 30 | 30 | 30 (with warnings) | Improvement: agrees with all three. |
| `qpdf/i3.pdf` | 0 | 1 | 1 | 1 | 1 | 1 | Improvement: agrees with all three. |
| `qpdf/issue-119.pdf` | 0 | 0 | 2 | fails | fails | fails | Neutral: no page objects survive the fuzzing. |
| `qpdf/issue-120.pdf` | 0 | 1 | 2 | fails | fails | fails | Neutral: a fuzzed page object (`/Type /Page` with garbled keys) is recovered as a blank US Letter page with `PageMediaBoxMissing` and `PageResourcesMissing`; nothing renders, and no reader opens the file. |
| `qpdf/issue-143.pdf` | 0 | 0 | 2 | fails | fails | fails | Neutral: no page objects survive the fuzzing. |
| `qpdf/minimal-linearize-pass1.pdf` | 0 | 1 | 1 | 1 | 1 | fails (no /Root) | Improvement: agrees with pdf.js and poppler, and restores the page count it had before PR #83. |

No regressions. The `PagesMissing` severity changes from Error to Warning on the seven files whose pages are recovered.

## Diagnostic changes in the real-world gate

The gate walks every page, so recovered pages bring their own diagnostics, and a rebuilt cross-reference table drops those the wrong table caused. The snapshot changes (files carrying each code) are exactly these:

| File | Codes added | Codes removed | Why |
|---|---|---|---|
| `pdfjs/PDFBOX-3148-2-fuzzed.pdf` | `FilterAbbreviationNotAllowed`, `FilterDataTruncated`, `XmpMalformed` | `FieldReferenceInvalid` | Reported on the recovered page's streams and the metadata the rebuilt table reaches; the field reference was to an object the damaged table listed wrongly. |
| `pdfjs/issue9418.pdf` | | `InfoDictionaryInvalid`, `ObjectStreamInvalid`, `StreamLengthInvalid` | Each came from an object the file's own table placed wrongly; the rebuilt table reads them right. |
| `pdfjs/poppler-395-0-fuzzed.pdf` | `AnnotationPageMismatch` | | Reported on the recovered page's annotations, whose page entry does not name the page as recovered. |
| `qpdf/i3.pdf` | `DateUnreadable` | | Reported on an object the recovered page leads to. |
| `qpdf/issue-120.pdf` | `PageMediaBoxMissing`, `PageResourcesMissing`, `XrefEntryOffsetInvalid` | | The fuzzed page object's keys are garbled; its content stream's entry points nowhere. |

`qpdf/append-xref-loop.pdf`, `qpdf/minimal-linearize-pass1.pdf`, `qpdf/issue-119.pdf` and `qpdf/issue-143.pdf` keep the same set of codes; only their page counts or the `PagesMissing` severity change.
