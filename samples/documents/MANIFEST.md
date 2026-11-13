# Sample documents: manifest

These are realistic, multi-page documents for the demos, the realistic benchmark tier and the browser smoke test (#363). They are the
kinds of documents HTML-to-PDF and DTP tools produce, but all of their content is original or in the public domain. None of their text,
images, fonts or designs comes from another product's samples.

- **Text** is written for the Broadside project, unless a row below says it is in the public domain in the United States.
- **Images** are in the public domain or released under CC0. Every one is listed below with its source.
- **Fonts** are under the SIL Open Font License 1.1 or the Apache License 2.0. Each family's licence is next to its files in `fonts/`, and
  the producers embed subsets.
- **Names, addresses, prices and bank details** in the catalogue and the invoices are invented.

`build.sh` regenerates every PDF into `pdf/`, which is committed while the whole set stays under about 40 MB (about 29 MB with
the additions of #451, the 1,168-page complete works 5 MB of it; it is committed, not built on demand, so that the galleries and the
benchmarks have it). Pass names to rebuild
only some of them, for example `samples/documents/build.sh textbook`. The producers are development tools only and never package
dependencies. WeasyPrint and qpdf output is byte-for-byte reproducible. Broadside's own output differs between builds only in its
random file identifier, except that encryption and signatures also draw random salts, keys and padding. Chrome and Ghostscript write
the time they ran.

`COVERAGE.md` lists which document exercises each use case Broadside supports (#451).

The Blazor and MAUI demo galleries open with them (the textbook first), `bench/Broadside.Benchmarks.Realistic` is their benchmark
tier (outside the regression gate), `tests/Broadside.Viewer.Blazor.BrowserTests/SampleDocumentTests.cs` renders every page in the
browser within a time budget, and `docs/benchmarks/sample-documents-baseline.md` records per-page render times.

## Documents

| File | Kind | Producer | Pages | Size | What it exercises |
|---|---|---|---:|---:|---|
| `catalogue.pdf` | Product catalogue | WeasyPrint | 11 | 7.8 MB | A4 with a 3 mm bleed (TrimBox and BleedBox), full-bleed 2–4 MP DCT photographs, DeviceCMYK JPEGs, `device-cmyk()` colours, PNG cut-outs with an SMask, semi-transparent overlays and gradients (soft masks), rotated stickers, rounded boxes, internal links with leaders and `target-counter()` page numbers, a CSS grid |
| `invoice-colourful.pdf` | Invoice | WeasyPrint | 2 | 38 KB | Gradients, an inline SVG logo with a gradient, rounded table headers, a long table that breaks across pages with a repeated header, `counter(pages)` |
| `invoice-conservative.pdf` | Invoice | Broadside (flow layout and PdfCanvas) | 3 | 101 KB | PDF 2.0, tagged, subset TrueType (IBM Plex Serif and Mono), a vector letterhead, a 30-row table that repeats its header row, an AcroForm remittance slip (text fields, check boxes, a multi-line field) |
| `invoice-conservative-filled.pdf` | Invoice, filled in | Broadside (incremental update) | 3 | 105 KB | The same file with its fields filled in and appearances generated, saved as an incremental update |
| `textbook.pdf` | Textbook chapter | WeasyPrint (`--pdf-tags`) | 8 | 340 KB | Tagged, CFF-based OpenType (Libertinus) with a math font, sidenotes floated into a wide outer margin, footnotes, floated figures and tables, a figure that spans into the margin, inline SVG diagrams and a plot, a photograph, running heads from `string-set`, numbered cross-references |
| `textbook-linearized.pdf` | Textbook chapter | qpdf (linearized, object streams) | 8 | 349 KB | The same chapter, linearized for first-page-first loading over HTTP range requests |
| `newsletter.pdf` | Club newsletter | Headless Chrome (Skia PDF) | 4 | 2.9 MB | Tagged, with an outline from the headings; CSS multi-column with column rules, a drop cap, pull quotes, rotated spine text, JPEGs that carry an ICC profile (ICCBased images), a PNG cut-out, an SVG bar chart, a CSS grid, `mix-blend-mode` multiply and screen |
| `paper.pdf` | Two-column scientific paper | Headless Chrome (Skia PDF) | 2 | 160 KB | Tagged, with an outline; MathML equations set in STIX Two Math (fractions, scripts, radicals, an integral with limits), two-column text, SVG plots with clipping and dashes, tables, references |
| `paper-ghostscript.pdf` | Two-column scientific paper | Ghostscript `pdfwrite` (`/prepress`) | 2 | 77 KB | The same paper redistilled: re-encoded fonts and streams from a second producer |
| `dictionary.pdf` | Dictionary | WeasyPrint | 17 | 273 KB | Dense three-column 6.9 pt text with column rules, running heads with the first and last headword on each page (`string-set`), a rotated thumb tab on every page (a fixed element), six font families |
| `brochure.pdf` | Planetarium brochure | Broadside (PdfCanvas) | 4 | 2.1 MB | PDF 2.0, print-ready: MediaBox with a slug, BleedBox, TrimBox, crop marks in the registration colour (Separation All), a spot colour (Separation Lumen Gold), CMYK, axial, radial and Coons-patch mesh shadings, a tiling pattern, nested transforms, all sixteen blend modes, luminosity and alpha soft masks, a knockout group, an ICC-tagged JPEG and a PNG with an SMask |
| `novel-pocket.pdf` | Novel, pocket edition | WeasyPrint, then qpdf | 170 | 593 KB | 110 × 178 mm; the whole of The Wind in the Willows in EB Garamond (CFF) with ligatures, small caps, a contents page with leaders and page numbers, bookmarks, page labels (roman front matter), object streams |
| `novel-hardback.pdf` | Novel, hardback edition | WeasyPrint (`--pdf-tags`), then qpdf | 180 | 743 KB | 152 × 229 mm, tagged, mirrored margins, running heads that differ on left and right pages, chapters opening on right-hand pages, blank pages, page labels, object streams |
| `novel-large-print.pdf` | Novel, large-print edition | Broadside (flow layout) | 219 | 743 KB | PDF 2.0 and PDF/UA-1: Atkinson Hyperlegible at 16 pt, tagged as it is laid out, a contents page whose page numbers come from a first layout pass (found by Broadside's own text extraction), an outline, page labels that match the printed folios |
| `contract-signed.pdf` | Services agreement, signed | Broadside (flow layout, `Sign`, incremental updates) | 3 | 118 KB | Four revisions: the unsigned agreement with two signature fields; a certification signature (DocMDP, form filling and annotations allowed) with a visible appearance; the client's name and date filled in and an approval signature; a sticky note added after signing (an allowed change). Signed with the test PKI below |
| `paper-rc4.pdf` | Two-column scientific paper, encrypted | Broadside | 2 | 163 KB | RC4 128-bit (V 2, R 3), owner password only: opens without a prompt; printing and changes not allowed |
| `textbook-aes128.pdf` | Textbook chapter, encrypted | Broadside | 8 | 442 KB | AES-128 (V 4, R 4, crypt filters), owner password only; copying and changes not allowed |
| `invoice-conservative-aes256.pdf` | Invoice, encrypted | Broadside | 3 | 113 KB | AES-256 (V 5, R 6) with a user password: opening needs `broadside-user` (or the owner password); printing and form filling allowed |
| `dictionary-aes256-gcm.pdf` | Dictionary, encrypted | Broadside | 17 | 293 KB | PDF 2.0 AES-256-GCM (ISO/TS 32003) with integrity protection (ISO/TS 32004), owner password only. Most other readers cannot open it yet |
| `invoice-colourful-certificate.pdf` | Invoice, encrypted to a certificate | Broadside | 2 | 54 KB | Public-key security handler (`Adobe.PubSec`): opening needs the client's test certificate and key (`contract/test-pki/client.p12`); printing allowed |
| `application-form.pdf` | Application form | Broadside (PdfCanvas, `PdfAcroForm`, COS) | 2 | 60 KB | Every AcroForm field type: text (comb, read-only, required, multi-line), check boxes, radio buttons, an editable combo box, a multi-select list box, push buttons (reset, JavaScript print, submit to a URL) and an unsigned signature field; Acrobat-style keystroke, format and calculate scripts (`AFNumber_Format`, `AFSimple_Calculate`, `AFDate_FormatEx`) with a calculation order (`/CO`), and a document-level script. Broadside keeps the scripts and does not run them |
| `application-form-xfa.pdf` | Application form, hybrid | Broadside (incremental update) | 2 | 61 KB | The same form with an XFA form (preamble, config, template, datasets, postamble packets) added to the AcroForm in a second revision |
| `paper-reviewed.pdf` | Scientific paper, reviewed | Chrome, then a Broadside incremental update | 3 | 179 KB | A referee's markup placed with Broadside's text extraction: highlight with a popup and two threaded replies (one a review state), underline, squiggly, strike-out, caret, ink, square, circle, free text, a line with an arrowhead, polygon, polyline, a stamp, a file attachment (CSV), an unapplied redaction, and a new report page whose links are URI, GoTo, a named destination, GoToR, Launch and JavaScript actions |
| `leaflet-multilingual.pdf` | Library visitor leaflet | WeasyPrint | 3 | 131 KB | Twelve languages in one file, shaped by HarfBuzz: Arabic and Hebrew (right to left, `dir="rtl"`, Latin digits inside), Devanagari, Thai, Simplified Chinese, Japanese and Korean (CID-keyed CFF from Noto Sans CJK), Greek, Cyrillic, Vietnamese; seventeen embedded subset fonts, a CSS grid, a table mixing scripts |
| `notice-trilingual.pdf` | Utility notice | Broadside (flow layout, `TextShaping`) | 1 | 41 KB | PDF 2.0, tagged: English, Arabic and Hebrew paragraphs shaped by Broadside itself, right-to-left paragraphs with Latin digits |
| `rashomon-vertical.pdf` | Japanese novella, paperback | Broadside, then a COS pass | 14 | 177 KB | Vertical writing: Noto Serif CJK JP (CID-keyed CFF) with `Identity-V` and `W2`/`DW2`, glyphs shaped with `vert`, ruby beside the kanji, a Latin word turned sideways, right-to-left binding (`Direction R2L`, `TwoPageRight`); the colophon is horizontal in Ryumin-Light, not embedded, with the predefined `UniJIS-UTF16-H` CMap |
| `report-1996.pdf` | Technical memorandum | `tools/report_1996.py` (writes the PDF directly) | 3 | 51 KB | A PDF 1.2-era file: classic xref, Info only; content streams LZW then ASCII85; EB Garamond converted to an embedded Type 1 font (`FontFile`, eexec); a Type 3 font of 300 dpi bitmap glyphs (inline image masks in ASCIIHex) for a logger printout; Helvetica-Bold, Courier, Times-Roman (MacRoman with Differences), Symbol and ZapfDingbats not embedded; a RunLength-encoded gray image |
| `scan-memo-g4.pdf` | Scanned memo, 1967 | tesseract 5.5 (OCR) | 9 | 427 KB | Bilevel 300 ppi page images in CCITT Group 4 under an invisible text layer (render mode 3, GlyphLessFont) |
| `scan-memo-jbig2.pdf` | Scanned memo, 1967 | jbig2enc, then Broadside | 9 | 357 KB | The same pages in JBIG2 symbol mode, sharing one symbol dictionary (`JBIG2Globals`), under the same text layer |
| `scan-moon-memo-jpx.pdf` | Scanned memo in color, 1969 | tesseract 5.5 (OCR) | 2 | 842 KB | Color 300 ppi page images in JPEG 2000 (JPXDecode) under an invisible text layer |
| `color-guide.pdf` | Ink maker's color card | Broadside (PdfCanvas, COS) | 2 | 69 KB | Lab swatches, CalRGB and CalGray ramps next to DeviceRGB and DeviceGray, an Indexed image and Indexed fills, a two-ink DeviceN duotone, a five-ink DeviceN (process and a spot) tint chart with a PostScript calculator tint transform, and overprint (an ExtGState with OP, op and OPM 1) |
| `board-pack.pdf` | Board meeting pack | Broadside (PdfCanvas, page assembly) | 11 | 133 KB | US Letter, A4, A3 landscape, US Legal, an 80 mm receipt and a 36 × 24 in poster (`UserUnit` 2) in one file; two pages taken from the invoice; `/Rotate` 90, 180 and 270 with content drawn to read upright; a MediaBox away from the origin with a smaller CropBox; page labels and an outline per item |
| `portfolio.pdf` | Project portfolio | Broadside (embedded files, COS) | 1 | 159 KB | A collection (portfolio) with a schema (name, description, category, due date, size), three folders, a sort order and an initial document; six embedded files (three PDFs from this set, CSV and text) with collection items; a cover sheet; `PageMode UseAttachments`; the Adobe extension level 3 declaration |
| `invoice-pdfa3.pdf` | Invoice, archival | Broadside (`ConvertToPdfA`) | 3 | 142 KB | The conservative invoice as PDF/A-3b, with the invoice data as an embedded XML associated file (`AF`, relationship Alternative), as electronic invoices carry it |
| `paper-pdfa2b.pdf` | Two-column scientific paper, archival | Broadside (`ConvertToPdfA`) | 2 | 165 KB | Chrome's paper converted to PDF/A-2b: output intent, XMP metadata, and whatever else the converter had to change |
| `shakespeare.pdf` | Complete works | Broadside (PdfCanvas) | 1,168 | 5.0 MB | The large document: 44 works in two columns of 7.6 pt EB Garamond (CFF), each from a new page, with running heads, folios, page labels and an outline |
| `contour-map.pdf` | Topographic map | Broadside (PdfCanvas) | 1 | 2.9 MB | The huge vector page: an A1 sheet with about 400,000 contour segments (marching squares over generated terrain), a kilometer grid, spot heights, and three optional content groups |
| `showcase-broken-xref.pdf` | Feature showcase, damaged | `tools/damage.py` | 5 | 113 KB | `showcase.pdf` after a line was inserted after the header: every cross-reference offset in both revisions is 54 bytes short and the last `startxref` is wrong, so a reader must rebuild the table by scanning |
| `novel-large-print-truncated.pdf` | Novel, damaged | `tools/damage.py` | 157 of 219 | 446 KB | `novel-large-print.pdf` cut at 60 percent, as an interrupted download leaves it: no trailer or cross-reference stream, an unterminated stream, the last objects missing. Broadside recovers 157 pages; poppler and qpdf give up |
| `showcase.pdf` | Feature showcase | WeasyPrint, then a Broadside incremental update | 5 | 113 KB | Inline SVG (joins and caps, linear and radial gradients, a pattern, clipping, curves), nine kinds of CSS border and background, five counter styles, footnotes, bookmarks, internal and external links; Broadside adds two optional content groups (one hidden by default) and page labels in a second revision |

## Passwords and test keys

Everything here is **test only** and protects nothing; it is published so that tests, benchmarks and anyone else can open the files.

- The password-encrypted samples share the owner password `broadside-owner`. `invoice-conservative-aes256.pdf` also has the user
  password `broadside-user`.
- `contract/test-pki/` is a throwaway public-key infrastructure that `generator/TestPki.cs` made: `root-ca.cer`, a self-signed root
  ("Broadside Sample Documents Test Root (TEST ONLY)"), and `provider.p12` and `client.p12`, two end-entity certificates with their
  RSA keys, issued by it and valid from 2026 to 2036. The PKCS #12 password is `broadside-test`. They are committed so that rebuilding
  keeps the same certificates. Delete the folder and the next build makes new ones.

## Images

The originals are not stored in the repository. `tools/prepare_images.py` makes the committed images from them: it converts each
photograph to sRGB and scales it to at most 4.2 MP. It writes the RGB versions as JPEGs carrying an sRGB profile that LittleCMS
generates. The `-cmyk` versions are CMYK JPEGs, separated with a simple grey-component replacement. The `-cutout` versions are PNG files
with an alpha channel.

| File | Original | Source | Author | Licence |
|---|---|---|---|---|
| `catalogue/images/earth.jpg`, `earth-cutout.png` | The Earth seen from Apollo 17 (AS17-148-22727) | <https://commons.wikimedia.org/wiki/File:The_Earth_seen_from_Apollo_17.jpg> | NASA, Apollo 17 crew | Public domain (NASA) |
| `catalogue/images/earthrise.jpg` | Earthrise (AS08-14-2383) | <https://commons.wikimedia.org/wiki/File:NASA-Apollo8-Dec24-Earthrise.jpg> | NASA, Bill Anders | Public domain (NASA) |
| `catalogue/images/aldrin.jpg` | Buzz Aldrin on the Moon (AS11-40-5903) | <https://commons.wikimedia.org/wiki/File:Aldrin_Apollo_11_original.jpg> | NASA, Neil Armstrong | Public domain (NASA) |
| `catalogue/images/mccandless.jpg`, `mccandless-cutout.png` | Bruce McCandless II during EVA, 1984 | <https://commons.wikimedia.org/wiki/File:Bruce_McCandless_II_during_EVA_in_1984.jpg> | NASA | Public domain (NASA) |
| `catalogue/images/atlantis.jpg` | Atlantis taking off on STS-27 | <https://commons.wikimedia.org/wiki/File:Atlantis_taking_off_on_STS-27.jpg> | NASA | Public domain (NASA) |
| `catalogue/images/columbia-cmyk.jpg` | Space Shuttle Columbia launching (STS-1) | <https://commons.wikimedia.org/wiki/File:Space_Shuttle_Columbia_launching.jpg> | NASA | Public domain (NASA) |
| `catalogue/images/aurora-cmyk.jpg` | Aurora australis from the ISS | <https://commons.wikimedia.org/wiki/File:Aurora_Australis_From_ISS.JPG> | NASA, ISS Expedition 23 crew | Public domain (NASA) |
| `catalogue/images/rover-cmyk.jpg` | Apollo 15 Lunar Rover and Irwin | <https://commons.wikimedia.org/wiki/File:Apollo_15_Lunar_Rover_and_Irwin.jpg> | NASA, David Scott | Public domain (NASA) |

| `newsletter/images/teton.jpg` | Grand Teton National Park (33106145651) | <https://commons.wikimedia.org/wiki/File:Grand_Teton_National_Park_(33106145651).jpg> | Grand Teton National Park (NPS) | Public domain (US government) |
| `newsletter/images/curlew.jpg`, `curlew-badge.png` | Long-billed curlew (52099434724) | <https://commons.wikimedia.org/wiki/File:Long-billed_curlew_(52099434724).jpg> | U.S. Fish and Wildlife Service | Public domain (US government) |
| `newsletter/images/swans.jpg` | Swans at sunset, Bear River Migratory Bird Refuge (55249054581) | <https://commons.wikimedia.org/wiki/File:Usfws-swans-sunset-bear-river-migratory-bird-refuge-JeffAdams_(55249054581).jpg> | Jeff Adams, U.S. Fish and Wildlife Service | Public domain (US government) |
| `newsletter/images/birders.jpg` | Bird observation at Aransas National Wildlife Refuge (54181689566) | <https://commons.wikimedia.org/wiki/File:Bird_observation_at_Aransas_National_Wildlife_Refuge_by_Steve_Hillebrand_USFWS_(54181689566).jpg> | Steve Hillebrand, U.S. Fish and Wildlife Service | Public domain (US government) |
| `newsletter/images/sandpiper.jpg` | Pectoral sandpiper in the midnight sun (48751730448) | <https://commons.wikimedia.org/wiki/File:Pectoral_Sandpiper_in_the_midnight_sun_-_Arctic_Bird_Fest_2019_(48751730448).jpg> | U.S. Fish and Wildlife Service | Public domain (US government) |
| `newsletter/images/albatross.jpg` | Wisdom returns, Midway Atoll, 2015 (23201478771) | <https://commons.wikimedia.org/wiki/File:USFWS_WisdomReturns11.21.2015_Photo_by_Kiah_Walker-USFWS_(23201478771).jpg> | Kiah Walker, U.S. Fish and Wildlife Service | Public domain (US government) |

The newsletter images were downloaded at Wikimedia's 1920-pixel (swans: 1280-pixel) thumbnail size. The textbook reuses
`catalogue/images/earthrise.jpg`, and the brochure reuses `catalogue/images/earth.jpg`, `aldrin.jpg` and `mccandless-cutout.png`. Every other illustration (the logos, the diagrams, the plot, the QR-like pattern)
is drawn in SVG or with PdfCanvas for this project.

## Scans

The scanned samples are real documents from the U.S. National Archives, works of the U.S. government and in the public domain in
the United States ("No Copyright - United States", as the National Archives catalog and the Wikimedia Commons file pages state).
`tools/prepare_scans.py` makes `scans/` from the originals, which are not stored in the repository.

| Files | Original | Source | Author | License |
|---|---|---|---|---|
| `scans/memo-01.tif` to `memo-09.tif` | Memorandum from James J. Gehrig to all committee members including a summary of the Phillips report, April 26, 1967 (NARA 25496804) | <https://catalog.archives.gov/id/25496804>, via <https://commons.wikimedia.org/wiki/File:Memorandum_from_James_J._Gehrig_to_All_Committee_Members_Including_a_Summary_of_the_Phillips_Report_-_DPLA_-_c08d09d3b8aecb92883a32fa3d28ded1.pdf> | U.S. Senate, Committee on Aeronautical and Space Sciences | Public domain (U.S. government) |
| `scans/moon-01.jp2`, `moon-02.jp2` | Memorandum from speechwriter William Safire to President Nixon ("In event of moon disaster"), July 18, 1969 (NARA 6922351) | <https://catalog.archives.gov/id/6922351>, via <https://commons.wikimedia.org/wiki/File:Memorandum_from_Speechwriter_William_Safire_to_President_Nixon_-_DPLA_-_59737d7b7830ac53d6541b2e61abd477.pdf> | The White House (William Safire) | Public domain (U.S. government) |

## Text

| Document | Text |
|---|---|
| Catalogue, invoices, textbook, newsletter, paper, brochure, showcase, agreement, application form, referee's report | Written for the Broadside project. The textbook's physical data (orbital heights, periods, planetary semi-major axes) are standard published values. The newsletter's club, members and counts and the paper's authors and measurements are invented; the facts about Wisdom the albatross are as published by the U.S. Fish and Wildlife Service. |
| Novel (three editions) | *The Wind in the Willows* by Kenneth Grahame (1908), public domain in the United States. Text from Project Gutenberg eBook #289 (<https://www.gutenberg.org/ebooks/289>) without its header, footer, licence or illustration markers, converted by `tools/willows_extract.py` into `novel/willows.html` and `novel/willows.json`. |
| Technical memo, color card | Written for the Broadside project; the authority, the weir, the ink maker and every measurement are invented. |
| Leaflet, notice | Written for the Broadside project, in twelve languages; the library, the water utility and the phone number are invented. |
| Rashōmon | 「羅生門」 by Akutagawa Ryūnosuke (first published 1915), public domain in Japan and the United States. Text from Aozora Bunko (<https://www.aozora.gr.jp/cards/000879/card127.html>, file `127_ruby_150.zip`, input by 平山誠 and 野口英司, proofread by もりみつじゅんじ), converted by `tools/rashomon_extract.py` into `rashomon/rashomon.json`. The colophon keeps Aozora Bunko's credits for the source edition and its volunteers. |
| Complete works | *The Complete Works of William Shakespeare*, public domain in the United States. Text from Project Gutenberg eBook #100 (<https://www.gutenberg.org/ebooks/100>) without its header, footer or license; `build.sh` downloads it for the build and does not store it. The committed file was made from the text as of November 9, 2026. |
| Board pack, portfolio, invoice XML, map | Written or generated for the Broadside project; the authority, its projects and the terrain are invented. |
| Dictionary | 508 entries, *Qua* to *Quinizarin*, from *Webster's Unabridged Dictionary* (G. & C. Merriam, 1913 edition), public domain in the United States. Text from Project Gutenberg eBook #29765 (<https://www.gutenberg.org/ebooks/29765>) without its header, footer or licence, converted by `tools/webster_extract.py`, which leaves out eight entries whose definitions are offensive today. |

## Fonts

| Family | Files | Source | Licence |
|---|---|---|---|
| Bebas Neue | `fonts/bebasneue/` | <https://github.com/google/fonts/tree/main/ofl/bebasneue> | SIL OFL 1.1 |
| Fira Sans, Fira Sans Condensed | `fonts/firasans/`, `fonts/firasanscondensed/` | <https://github.com/google/fonts/tree/main/ofl/firasans> | SIL OFL 1.1 |
| Poppins | `fonts/poppins/` | <https://github.com/google/fonts/tree/main/ofl/poppins> | SIL OFL 1.1 |
| IBM Plex Serif, IBM Plex Mono | `fonts/ibmplexserif/`, `fonts/ibmplexmono/` | <https://github.com/google/fonts/tree/main/ofl/ibmplexserif> | SIL OFL 1.1 |
| Libertinus Serif, Sans and Math (7.051) | `fonts/libertinus/` | <https://github.com/alerque/libertinus/releases/tag/v7.051> | SIL OFL 1.1 |
| Abril Fatface | `fonts/abrilfatface/` | <https://github.com/google/fonts/tree/main/ofl/abrilfatface> | SIL OFL 1.1 |
| PT Serif, PT Sans | `fonts/ptserif/`, `fonts/ptsans/` | <https://github.com/google/fonts/tree/main/ofl/ptserif> | SIL OFL 1.1 |
| Old Standard TT | `fonts/oldstandardtt/` | <https://github.com/google/fonts/tree/main/ofl/oldstandardtt> | SIL OFL 1.1 |
| STIX Two Math | `fonts/stixtwomath/` | <https://github.com/google/fonts/tree/main/ofl/stixtwomath> | SIL OFL 1.1 |
| Atkinson Hyperlegible | `fonts/atkinsonhyperlegible/` | <https://github.com/google/fonts/tree/main/ofl/atkinsonhyperlegible> | SIL OFL 1.1 |
| EB Garamond (OpenType CFF) | `fonts/ebgaramond/` | <https://github.com/octaviopardo/EBGaramond12/tree/master/fonts/otf> | SIL OFL 1.1 |
| Noto Sans, Noto Naskh Arabic, Noto Sans Hebrew, Noto Sans Devanagari, Noto Sans Thai (subsets) | `fonts/notosans/`, `fonts/notonaskharabic/`, `fonts/notosanshebrew/`, `fonts/notosansdevanagari/`, `fonts/notosansthai/` | <https://github.com/google/fonts/tree/main/ofl> (the variable fonts, instanced at 400 and 700) | SIL OFL 1.1 |
| Noto Sans CJK SC, JP and KR, Noto Serif CJK JP (subsets, CID-keyed CFF) | `fonts/notosanscjk/`, `fonts/notoserifcjk/` | <https://github.com/notofonts/noto-cjk> (`Sans/OTF/*/NotoSansCJK??-Regular.otf`, `Serif/OTF/Japanese/NotoSerifCJKjp-Regular.otf`) | SIL OFL 1.1 |

The Noto fonts are committed as subsets that hold only the characters the leaflet, the notice and Rashōmon use (the upstream CJK
fonts are 16 to 25 MB each): `tools/subset_fonts.py <folder of upstream fonts>` makes them, and the vertical metrics the vertical
sample needs (`fonts/notoserifcjk/vertical-metrics.json`), with fontTools 4.62. Run it again after changing those documents' text.

## Producers

The committed files were made with:

| Producer | Version |
|---|---|
| WeasyPrint | 68.1 (Homebrew) |
| qpdf | 12.4.2 |
| Headless Chrome | 156 (Skia/PDF m156) |
| Ghostscript | 10.08.0 |
| pdftotext (poppler, used by build.sh to check the novel's front matter) | 26.10.0 |
| Broadside | this repository (`generator/`, .NET 10 SDK) |
| Pillow (image preparation) | 12.1.0 |
| fontTools (font subsets, the Type 1 conversion) | 4.62.1 |
| tesseract | 5.5.3 (leptonica 1.87.0, the `eng` model) |
| jbig2enc | 0.32 |
