# Specs/References

Freely available normative and informative references used by the PDF library, downloaded on 2026-10-09.
Every file in this directory was verified to start with `%PDF` and to be larger than 50 KB; HTML error pages were discarded.
Files were fetched with `curl -L` (browser User-Agent) unless noted; two sites block curl's TLS fingerprint and were fetched with Python `urllib` instead.
Nothing here has been opened or executed, only byte-inspected.

The core ISO documents (ISO 32000-2:2020, ISO/TS 32001-32005, WTPDF 1.0, PDF Declarations, PDF 2.0 Application Notes 001-003) live one level up in `Specs/`.

## Downloaded

### Fonts (Adobe)

| File | Document | Source URL | Size (bytes) |
|---|---|---|---|
| `T1_SPEC.pdf` | Adobe Type 1 Font Format specification | https://adobe-type-tools.github.io/font-tech-notes/pdfs/T1_SPEC.pdf | 1,812,144 |
| `5176.CFF.pdf` | Technical Note #5176, The Compact Font Format Specification | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5176.CFF.pdf | 1,062,494 |
| `5177.Type2.pdf` | Technical Note #5177, The Type 2 Charstring Format | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5177.Type2.pdf | 199,112 |
| `5004.AFM_Spec.pdf` | Technical Note #5004, Adobe Font Metrics File Format Specification | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5004.AFM_Spec.pdf | 245,931 |
| `5014.CIDFont_Spec.pdf` | Technical Note #5014, Adobe CMap and CIDFont Files Specification | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5014.CIDFont_Spec.pdf | 554,217 |
| `5092.CID_Overview.pdf` | Technical Note #5092, CID-Keyed Font Technology Overview | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5092.CID_Overview.pdf | 2,097,134 |
| `5099.CMapResources.pdf` | Technical Note #5099, Developing CMap Resources for CID-Keyed Fonts | https://adobe-type-tools.github.io/font-tech-notes/pdfs/5099.CMapResources.pdf | 469,666 |

The full index of Adobe font technical notes is https://adobe-type-tools.github.io/font-tech-notes/ (also has #5012 Type 42, #5015 Type 1 supplement, #5180 sfnt, TN5609 PS3 fonts, if needed later).

### Image codecs and filters

| File | Document | Source URL | Size (bytes) |
|---|---|---|---|
| `5116.DCT_Filter.pdf` | Adobe Technical Note #5116, Supporting the DCT Filters in PostScript Level 2 (24 Nov 1992) | https://sunsite.icm.edu.pl/packages/lprng/RESOURCES/ADOBE/5116.DCT_Filter.pdf (third-party mirror; not on adobe-type-tools index, 404 there) | 218,450 |
| `T-REC-T.81-199209_w3c.pdf` | ITU-T T.81 (09/1992), JPEG, Digital compression and coding of continuous-tone still images (ISO/IEC 10918-1) | Wayback copy of the W3C-hosted PDF: https://web.archive.org/web/20250103063510id_/https://www.w3.org/Graphics/JPEG/itu-t81.pdf (original https://www.w3.org/Graphics/JPEG/itu-t81.pdf returns 403 to non-browser clients) | 1,058,883 |
| `T-REC-T.86-199806.pdf` | ITU-T T.86 (06/1998), JPEG extensions (ISO/IEC 10918-3), covers SPIFF and extended JPEG | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.86-199806-S!!PDF-E&type=items | 386,475 |
| `T-REC-T.88-200002.pdf` | ITU-T T.88 (02/2000), JBIG2, Lossy/lossless coding of bi-level images (ISO/IEC 14492:2001). Superseded edition; see "Not obtained" for the 2018 edition | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.88-200002-S!!PDF-E&type=items | 1,219,588 |
| `T-REC-T.800-200208.pdf` | ITU-T T.800 (08/2002), JPEG 2000 Part 1 core coding system (ISO/IEC 15444-1). The edition PDF 1.x/2.0 cite | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.800-200208-S!!PDF-E&type=items | 1,979,107 |
| `T-REC-T.800-201906.pdf` | ITU-T T.800 (06/2019), JPEG 2000 Part 1, consolidated later edition (superseded by 07/2024, which is paid) | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.800-201906-S!!PDF-E&type=items | 10,969,421 |
| `T-REC-T.4-200307.pdf` | ITU-T T.4 (07/2003), Group 3 facsimile coding (CCITTFaxDecode K>=0) | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.4-200307-I!!PDF-E&type=items | 580,049 |
| `T-REC-T.6-198811.pdf` | ITU-T T.6 (11/1988), Group 4 facsimile coding (CCITTFaxDecode K<0) | https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-T.6-198811-I!!PDF-E&type=items | 112,837 |

### Colour

| File | Document | Source URL | Size (bytes) |
|---|---|---|---|
| `ICC.1-2022-05.pdf` | ICC.1:2022 (Profile version 4.4.0.0), ICC profile format specification (= ISO 15076-1:2023) | https://www.color.org/specification/ICC.1-2022-05.pdf (redirects to archive.color.org) | 905,961 |

### PostScript, metadata, historical PDF references (Adobe)

| File | Document | Source URL | Size (bytes) |
|---|---|---|---|
| `PLRM.pdf` | PostScript Language Reference, Third Edition (1999). Needed for Type 4 PostScript calculator functions | Wayback copy of Adobe's own hosting: https://web.archive.org/web/20220308141146id_/https://www.adobe.com/content/dam/acom/en/devnet/actionscript/articles/PLRM.pdf (live Adobe URLs now return 403/404). A byte-different mirror also exists at https://ftp.math.utah.edu/u/ma/hohn/linux/PLRM.pdf | 7,841,329 |
| `XMPSpecificationPart1.pdf` | XMP Specification Part 1: Data model, serialization and core properties (April 2012) | https://github.com/adobe/XMP-Toolkit-SDK/raw/main/docs/XMPSpecificationPart1.pdf | 511,034 |
| `XMPSpecificationPart2.pdf` | XMP Specification Part 2: Additional properties | https://github.com/adobe/XMP-Toolkit-SDK/raw/main/docs/XMPSpecificationPart2.pdf | 445,056 |
| `XMPSpecificationPart3.pdf` | XMP Specification Part 3: Storage in files | https://github.com/adobe/XMP-Toolkit-SDK/raw/main/docs/XMPSpecificationPart3.pdf | 1,366,689 |
| `pdf_reference_1-7.pdf` | PDF Reference, sixth edition, version 1.7 (Adobe, November 2006) | Wayback copy of Adobe's hosting: https://web.archive.org/web/20210423224834id_/https://www.adobe.com/content/dam/acom/en/devnet/acrobat/pdfs/pdf_reference_1-7.pdf (live URL returns 403; opensource.adobe.com `pdfstandards/pdfreference1.7old.pdf` returns 404) | 55,779,577 |
| `adobe_supplement_iso32000_EL3.pdf` | Adobe Supplement to ISO 32000, BaseVersion 1.7, ExtensionLevel 3 (Acrobat 9, June 2008) | https://web.archive.org/web/20180725161216id_/https://www.adobe.com/content/dam/acom/en/devnet/acrobat/pdfs/adobe_supplement_iso32000.pdf (the 2021 Wayback snapshot of the same URL is truncated at exactly 1 MiB; the 2018 snapshot is complete and ends with `%%EOF`) | 1,490,032 |
| `adobe_supplement_iso32000_1_EL5.pdf` | Adobe Supplement to ISO 32000-1, BaseVersion 1.7, ExtensionLevel 5 (Acrobat 9.1, 2009) | https://web.archive.org/web/20210419151453id_/https://www.adobe.com/content/dam/acom/en/devnet/acrobat/pdfs/adobe_supplement_iso32000_1.pdf | 323,997 |

### PDF Association companion material (free)

| File | Document | Source URL | Size (bytes) |
|---|---|---|---|
| `Matterhorn-Protocol-1-1.pdf` | The Matterhorn Protocol 1.1 (PDF/UA-1 checkpoints and failure conditions) | https://pdfa.org/download-area/publications/Matterhorn-Protocol-1-1.pdf (fetched with Python urllib; pdfa.org's WAF returns 403 to curl) | 318,881 |
| `PDFA_in_a_Nutshell_211.pdf` | PDF/A in a Nutshell 2.0 (PDF/A-1, -2, -3 overview) | https://pdfa.org/wp-content/uploads/2013/05/PDFA_in_a_Nutshell_211.pdf (fetched with Python urllib) | 1,352,247 |

## Not obtained

| Item | Reason | Best URL to obtain manually |
|---|---|---|
| ISO 14289-1:2014 (PDF/UA-1), ISO 14289-2:2024 (PDF/UA-2) | Free ("sponsored access", $0.00) from the PDF Association store, but delivery is a web-shop checkout (add to cart, enter email, place a $0 order). Not automated because it submits a form with personal data. | https://www.pdfa-inc.org/product/pdf-ua-bundle/ (bundle also includes ISO/TS 32005, WTPDF, Tagged PDF Best Practice Guide: Syntax, PDF Declarations). Announcement: https://pdfa.org/sponsored-standards/ |
| ITU-T T.81 from ITU directly | ITU page states the text is joint with ISO/IEC and "only available through payment" (TIES login required). The W3C mirror copy above is the same 1992 text. | https://www.itu.int/rec/T-REC-T.81-199209-I/en ; W3C copy https://www.w3.org/Graphics/JPEG/itu-t81.pdf (open in a browser) |
| ITU-T T.88 (08/2018), current JBIG2 edition | Only the "Zip (Software)" item is public; the PDF is payment/TIES-only (joint ISO/IEC 14492 text). The 2000 edition above is free and is what ISO 32000 cites, but the 2018 edition consolidates Amd 1-3 (e.g. extended halftone templates, colour). | https://www.itu.int/rec/T-REC-T.88-201808-I/en |
| ITU-T T.800 (07/2024), current JPEG 2000 Part 1 edition | Payment only ("agreement with our partners"). 2002 and 2019 editions above are free. | https://www.itu.int/rec/T-REC-T.800-202407-I/en ; or ISO/IEC 15444-1:2024 |
| Adobe Technical Note #5603 (JPEG 2000 / JPX in PDF) | No such Adobe technical note could be located by number or subject; JPXDecode is specified in ISO 32000-2 clause 7.4.9 and the Adobe Supplement EL3/EL5 above. | n/a |
| Adobe Supplement / PDF Reference from live Adobe hosts | adobe.com returns 403 to non-browser clients; opensource.adobe.com paths changed (404). Wayback copies used instead. | https://opensource.adobe.com/dc-acrobat-sdk-docs/ (browse for current paths) |

## HTML-only references (no download)

| Reference | URL | Notes |
|---|---|---|
| Apple TrueType Reference Manual | https://developer.apple.com/fonts/TrueType-Reference-Manual/ | HTML only; no PDF published by Apple. |
| Microsoft OpenType Specification (1.9.x) | https://learn.microsoft.com/en-us/typography/opentype/spec/ | HTML only; Microsoft publishes no complete PDF (only the obsolete 1.4 had a self-extracting archive). The ISO twin, ISO/IEC 14496-22 "Open Font Format", is a free PDF-in-ZIP from ISO's Publicly Available Standards page https://standards.iso.org/ittf/PubliclyAvailableStandards/ (requires clicking through a licence page; not fetched by script). |
| veraPDF validation rules (PDF/A-1..4, PDF/UA-1/2, WTPDF 1.0) | https://docs.verapdf.org/validation/ ; rule pages: https://github.com/veraPDF/veraPDF-validation-profiles/wiki (PDFA-Part-1-rules, PDFA-Parts-2-and-3-rules, PDFA-Part-4-rules, PDFUA-Part-1-rules, PDFUA-Part-2-rules, WTPDF-1.0-rules); XML profiles: https://github.com/veraPDF/veraPDF-validation-profiles ; test corpus: https://github.com/veraPDF/veraPDF-corpus | HTML/XML/Git, not PDFs. |
| PDF Association errata / issue resolutions | https://pdf-issues.pdfa.org/ | HTML. |
| Matterhorn Protocol 1.02 (older) | https://www.pdfa.org/download-area/accessibility/MatterhornProtocol_1-02.pdf | Not downloaded; 1.1 supersedes it. |
