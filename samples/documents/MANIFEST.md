# Sample documents: manifest

These are realistic, multi-page documents for the demos, the realistic benchmark tier and the browser smoke test (#363). They are the
kinds of documents HTML-to-PDF and DTP tools produce, but all of their content is original or in the public domain. None of their text,
images, fonts or designs comes from another product's samples.

- **Text** is written for the Broadside project, unless a row below says it is in the public domain in the United States.
- **Images** are in the public domain or released under CC0. Every one is listed below with its source.
- **Fonts** are under the SIL Open Font License 1.1 or the Apache License 2.0. Each family's licence is next to its files in `fonts/`, and
  the producers embed subsets.
- **Names, addresses, prices and bank details** in the catalogue and the invoices are invented.

`build.sh` regenerates every PDF into `pdf/`, which is committed while the whole set stays under about 40 MB. Pass names to rebuild
only some of them, for example `samples/documents/build.sh textbook`. The producers are development tools only and never package
dependencies. WeasyPrint and qpdf output is byte-for-byte reproducible. Broadside's own output differs between builds only in its
random file identifier. Chrome and Ghostscript write the time they ran.

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
| `showcase.pdf` | Feature showcase | WeasyPrint, then a Broadside incremental update | 5 | 113 KB | Inline SVG (joins and caps, linear and radial gradients, a pattern, clipping, curves), nine kinds of CSS border and background, five counter styles, footnotes, bookmarks, internal and external links; Broadside adds two optional content groups (one hidden by default) and page labels in a second revision |

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

## Text

| Document | Text |
|---|---|
| Catalogue, invoices, textbook, newsletter, paper, brochure, showcase | Written for the Broadside project. The textbook's physical data (orbital heights, periods, planetary semi-major axes) are standard published values. The newsletter's club, members and counts and the paper's authors and measurements are invented; the facts about Wisdom the albatross are as published by the U.S. Fish and Wildlife Service. |
| Novel (three editions) | *The Wind in the Willows* by Kenneth Grahame (1908), public domain in the United States. Text from Project Gutenberg eBook #289 (<https://www.gutenberg.org/ebooks/289>) without its header, footer, licence or illustration markers, converted by `tools/willows_extract.py` into `novel/willows.html` and `novel/willows.json`. |
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
