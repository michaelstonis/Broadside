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
random file identifier.

## Documents

| File | Kind | Producer | Pages | Size | What it exercises |
|---|---|---|---:|---:|---|
| `catalogue.pdf` | Product catalogue | WeasyPrint | 11 | 7.8 MB | A4 with a 3 mm bleed (TrimBox and BleedBox), full-bleed 2–4 MP DCT photographs, DeviceCMYK JPEGs, `device-cmyk()` colours, PNG cut-outs with an SMask, semi-transparent overlays and gradients (soft masks), rotated stickers, rounded boxes, internal links with leaders and `target-counter()` page numbers, a CSS grid |
| `invoice-colourful.pdf` | Invoice | WeasyPrint | 2 | 38 KB | Gradients, an inline SVG logo with a gradient, rounded table headers, a long table that breaks across pages with a repeated header, `counter(pages)` |
| `invoice-conservative.pdf` | Invoice | Broadside (flow layout and PdfCanvas) | 3 | 101 KB | PDF 2.0, tagged, subset TrueType (IBM Plex Serif and Mono), a vector letterhead, a 30-row table that repeats its header row, an AcroForm remittance slip (text fields, check boxes, a multi-line field) |
| `invoice-conservative-filled.pdf` | Invoice, filled in | Broadside (incremental update) | 3 | 105 KB | The same file with its fields filled in and appearances generated, saved as an incremental update |
| `textbook.pdf` | Textbook chapter | WeasyPrint (`--pdf-tags`) | 8 | 340 KB | Tagged, CFF-based OpenType (Libertinus) with a math font, sidenotes floated into a wide outer margin, footnotes, floated figures and tables, a figure that spans into the margin, inline SVG diagrams and a plot, a photograph, running heads from `string-set`, numbered cross-references |
| `textbook-linearized.pdf` | Textbook chapter | qpdf (linearized, object streams) | 8 | 349 KB | The same chapter, linearized for first-page-first loading over HTTP range requests |

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

The textbook reuses `catalogue/images/earthrise.jpg`. Every other illustration (the logos, the diagrams, the plot, the QR-like pattern)
is drawn in SVG or with PdfCanvas for this project.

## Text

| Document | Text |
|---|---|
| Catalogue, invoices, textbook | Written for the Broadside project. The textbook's physical data (orbital heights, periods, planetary semi-major axes) are standard published values. |

## Fonts

| Family | Files | Source | Licence |
|---|---|---|---|
| Bebas Neue | `fonts/bebasneue/` | <https://github.com/google/fonts/tree/main/ofl/bebasneue> | SIL OFL 1.1 |
| Fira Sans, Fira Sans Condensed | `fonts/firasans/`, `fonts/firasanscondensed/` | <https://github.com/google/fonts/tree/main/ofl/firasans> | SIL OFL 1.1 |
| Poppins | `fonts/poppins/` | <https://github.com/google/fonts/tree/main/ofl/poppins> | SIL OFL 1.1 |
| IBM Plex Serif, IBM Plex Mono | `fonts/ibmplexserif/`, `fonts/ibmplexmono/` | <https://github.com/google/fonts/tree/main/ofl/ibmplexserif> | SIL OFL 1.1 |
| Libertinus Serif, Sans and Math (7.051) | `fonts/libertinus/` | <https://github.com/alerque/libertinus/releases/tag/v7.051> | SIL OFL 1.1 |

## Producers

The committed files were made with:

| Producer | Version |
|---|---|
| WeasyPrint | 68.1 (Homebrew) |
| qpdf | 12.4.2 |
| Broadside | this repository (`generator/`, .NET 10 SDK) |
| Pillow (image preparation) | 12.1.0 |
