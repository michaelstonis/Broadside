# Sample documents: coverage matrix

Which realistic sample document (`samples/documents/pdf`, listed in `MANIFEST.md`) exercises each use case Broadside supports. The
samples are whole documents for the demos, the realistic benchmark tier and the browser smoke test. The one-feature files in
`tests/Corpus/` and the fetched real-world corpora (`corpus/`, see `tools/CorpusFetcher`) cover the same ground minimally and for
conformance. They are named in the last column only where they are the only coverage.

Sample names are file names without `.pdf`. While #451 is open, a name that is not yet in `pdf/` is a planned sample.

Every row names at least one sample, or says why none is needed. Features that Broadside keeps without interpreting
(parse-and-preserve: JavaScript, XFA, 3D, rich media, multimedia) are covered only so that a viewer shows the file and a save keeps
them.

## Reading and rendering

| Use case | Samples | Notes |
|---|---|---|
| Classic xref table, single revision | `invoice-colourful`, `catalogue`, `dictionary`, `report-1996` | |
| Cross-reference streams and object streams | `novel-pocket`, `novel-hardback`, `textbook-linearized` | |
| Incremental updates (several revisions) | `invoice-conservative-filled`, `showcase`, `contract-signed`, `paper-reviewed`, `application-form-xfa` | |
| Linearized file, first page first over range requests | `textbook-linearized` | |
| Damaged file: broken offsets, wrong `startxref` (reconstruction) | `showcase-broken-xref` | Repairs are reported as diagnostics |
| Damaged file: truncated download (no trailer) | `textbook-truncated` | |
| PDF 1.2 era file (no compression of objects, LZW, ASCII85, RunLength, ASCIIHex) | `report-1996` | |
| PDF 2.0 file | `invoice-conservative`, `brochure`, `novel-large-print`, `dictionary-aes256-gcm` | |
| Very large document (1000+ pages) | `shakespeare` | Not rendered page by page in the browser smoke test (time budget) |
| Huge vector page (hundreds of thousands of path segments) | `contour-map` | |
| Mixed page sizes, `/Rotate` 90/180/270, CropBox and non-zero MediaBox origin | `board-pack` | |
| TrimBox, BleedBox, slug | `catalogue`, `brochure` | |
| Page labels | `novel-pocket`, `novel-hardback`, `novel-large-print`, `showcase`, `board-pack` | |
| Outline (bookmarks) | `novel-*`, `newsletter`, `paper`, `showcase`, `shakespeare` | |
| Internal and external links, named destinations | `catalogue`, `textbook`, `showcase`, `paper-reviewed` | |
| Optional content (layers, one hidden by default) | `showcase`, `contour-map` | |
| TrueType (subset, CIDFontType2) | `invoice-conservative`, `newsletter`, `paper` | |
| CFF and OpenType CFF (Type1C, CIDFontType0C) | `textbook`, `novel-*`, `dictionary` | |
| CID-keyed CFF (Adobe-Japan1 or Identity ordering) | `rashomon-vertical`, `leaflet-multilingual` | |
| Vertical writing (`Identity-V`, W2/DW2) | `rashomon-vertical` | |
| Non-embedded CJK font with a predefined CMap | `rashomon-vertical` (colophon) | Needs the bundled CMaps and a font resolver |
| Type 1 fonts (embedded `FontFile`) | `report-1996` | |
| Type 3 fonts (bitmap glyphs, as from dvips) | `report-1996` | |
| Standard 14 fonts, not embedded (WinAnsi, MacRoman, Differences, Symbol, ZapfDingbats) | `report-1996`, `showcase` (stamp) | |
| Complex-script shaping: Arabic, Hebrew (right to left), Devanagari, Thai | `leaflet-multilingual`, `notice-trilingual` | |
| CJK horizontal text (Chinese, Japanese, Korean) | `leaflet-multilingual` | |
| Math fonts and equations | `paper`, `textbook` | |
| DCT (JPEG) images, CMYK JPEGs, ICC-tagged JPEGs | `catalogue`, `newsletter`, `brochure` | |
| Images with SMask, PNG cut-outs | `catalogue`, `newsletter`, `brochure` | |
| CCITT G4 images | `scan-memo-g4` | |
| JBIG2 images (symbol mode, with globals) | `scan-memo-jbig2` | |
| JPEG 2000 (JPX) images | `scan-moon-memo-jpx` | |
| Indexed, Lab, CalRGB, CalGray color | `color-guide` | |
| Separation and DeviceN (duotone, NChannel) | `brochure` (Separation), `color-guide` (DeviceN) | |
| ICCBased color | `newsletter`, `catalogue`, `brochure`, `color-guide` | |
| Shadings: axial, radial, mesh; tiling patterns | `brochure`, `invoice-colourful`, `showcase`, `catalogue` | |
| Transparency: blend modes, soft masks, knockout groups | `brochure`, `newsletter`, `catalogue` | |
| Overprint | `color-guide` | |
| Annotations: text, free text, line, square, circle, polygon, polyline, ink | `paper-reviewed` | |
| Annotations: highlight, underline, squiggly, strike-out, caret | `paper-reviewed` | |
| Annotations: stamp, popup with replies, file attachment, redact (unapplied) | `paper-reviewed` | |
| Link variants: URI, GoTo page, named destination, GoToR, Launch, JavaScript | `paper-reviewed`, `showcase` | Launch and JavaScript are kept, not run |

## Text and structure

| Use case | Samples | Notes |
|---|---|---|
| Text extraction, reading order, multi-column | `newsletter`, `dictionary`, `paper` | |
| Tagged PDF, structure tree, reading order from tags | `textbook`, `newsletter`, `paper`, `novel-hardback`, `invoice-conservative` | |
| PDF/UA-1 | `novel-large-print` | |
| Invisible OCR text over scanned images (render mode 3) | `scan-memo-g4`, `scan-memo-jbig2`, `scan-moon-memo-jpx` | |
| Right-to-left extraction and search | `leaflet-multilingual`, `notice-trilingual` | |
| Vertical text extraction | `rashomon-vertical` | |
| Ligatures, small caps, ToUnicode | `novel-*` | |

## Writing and editing (Broadside's own writer made these)

| Use case | Samples | Notes |
|---|---|---|
| Flow layout, tables that repeat their header | `invoice-conservative`, `novel-large-print` | |
| PdfCanvas drawing, print production | `brochure`, `contour-map`, `color-guide` | |
| Shaped text (Arabic, Hebrew) | `notice-trilingual` | |
| Incremental update of another producer's file | `showcase`, `paper-reviewed`, `application-form-xfa` | |
| Adding annotations | `paper-reviewed`, `contract-signed` | |
| Merging, page assembly, rotating pages | `board-pack` | |
| Embedded files and associated files | `invoice-pdfa3`, `portfolio` | |
| Portfolio (collection with schema and folders) | `portfolio` | |
| PDF/A-2b conversion | `paper-pdfa2b` | |
| PDF/A-3b with an embedded XML invoice | `invoice-pdfa3` | |
| Page labels, outline, optional content written | `novel-large-print`, `showcase` | |

## Forms

| Use case | Samples | Notes |
|---|---|---|
| Text fields (single line, multi-line, comb, read-only, required) | `invoice-conservative`, `application-form` | |
| Check boxes | `invoice-conservative`, `application-form` | |
| Radio buttons | `application-form` | |
| Combo box (editable) and list box (multi-select) | `application-form` | |
| Push buttons (reset, submit, JavaScript) | `application-form` | |
| Unsigned signature field | `application-form` | |
| Calculated and formatted fields (`AFNumber_Format`, `AFSimple_Calculate`, `/CO`) | `application-form` | Broadside does not run JavaScript; the stored values and appearances show |
| Filled form saved as an incremental update | `invoice-conservative-filled` | |
| XFA (hybrid form, AcroForm fallback) | `application-form-xfa` | Parse-and-preserve |
| Document-level JavaScript | `application-form` | Parse-and-preserve |

## Security and signatures

| Use case | Samples | Notes |
|---|---|---|
| RC4 128-bit, owner password only, printing not allowed | `paper-rc4` | |
| AES-128, owner password only, copying not allowed | `textbook-aes128` | |
| AES-256, user and owner passwords (opening needs a password) | `invoice-conservative-aes256` | Test-only passwords, see `MANIFEST.md` |
| AES-256-GCM with integrity protection (PDF 2.0) | `dictionary-aes256-gcm` | |
| Public-key (certificate) encryption | `invoice-colourful-certificate` | Test-only key in `contract/test-pki/` |
| Certification signature (DocMDP) | `contract-signed` | |
| Approval signature in a later revision | `contract-signed` | |
| Allowed change after signing (annotation in a third revision) | `contract-signed` | |
| RC4 40-bit and other legacy variants | none | `tests/Corpus/encrypted-rc4-40*.pdf`; no realistic producer writes them today |
| Long-term validation (DSS, timestamps) | none | Needs a live TSA and OCSP/CRL responders; covered by unit tests |

## Viewer scenarios

| Use case | Samples | Notes |
|---|---|---|
| Fast first page, scrolling a long document | `novel-*`, `shakespeare` | |
| Remote file over range requests | `textbook-linearized` | |
| Deep zoom on vector detail | `contour-map`, `brochure` | |
| Heavy images | `catalogue`, `newsletter`, `scan-*` | |
| Password prompt | `invoice-conservative-aes256`, `invoice-colourful-certificate` | The demo galleries have no password prompt yet, so these are not listed there |
| Form filling | `application-form`, `invoice-conservative` | |
| Annotation layer | `paper-reviewed`, `contract-signed` | |
| Signatures panel | `contract-signed` | |
| Attachments panel | `portfolio`, `invoice-pdfa3` | |
| Layers panel | `showcase`, `contour-map` | |
| Diagnostics for a repaired file | `showcase-broken-xref`, `textbook-truncated` | |

## Out of scope

3D (U3D, PRC), rich media, multimedia playback and printing subsystems are parse-and-preserve or not part of Broadside. No realistic
producer available as a development tool writes them, so no sample covers them; `tests/Corpus/actions-preserved.pdf` and the
fetched corpora check that they survive a save.
