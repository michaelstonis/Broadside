# Minimal corpus

Hand-written PDF files, one feature per file, for the Phase 1 unit tests. Every file is as small as it can be while remaining a complete document: a catalog, a page tree and one page (two in `page-tree-inherited.pdf`), plus only the objects the feature needs.

All files are produced by `generate.py`:

```sh
python3 -I tests/Corpus/generate.py          # rewrites every PDF in place
python3 -I tests/Corpus/generate.py /tmp/out # or into another directory
```

The script uses only the standard library. It computes cross-reference offsets and stream lengths, and it implements the codecs and ciphers the corpus needs (LZW, ASCII85, ASCIIHex, RunLength, PNG predictor, RC4, AES-128/256, the standard security handler algorithms of ISO 32000-2 clause 7.6.4). It is deterministic: the values the specification asks a writer to draw from a random source (file identifiers, AES initialization vectors, R6 salts and file key) are fixed constants derived from the file name, so regenerating the corpus produces byte-identical files and `git diff` shows exactly what a change to the script did. A real writer must use a cryptographically secure random source for those values; the corpus trades that for reproducibility.

`generate.py` self-tests its codecs against known answers before writing anything (the LZW example in clause 7.4.4.2, FIPS-197 AES vectors, an RC4 vector).

Clause numbers below refer to ISO 32000-2:2020.

## Well-formed files

Verification was run with qpdf 12.4.2 (`qpdf --check`), poppler (`pdfinfo`, `pdftotext`, `pdffonts`, `pdftoppm`) and fontconfig (`fc-scan`). "clean" means `qpdf --check` exits 0 with no warnings and `pdfinfo` reports one Letter page (two for the page-tree file) without a syntax message.

| File | Feature | Clauses | How to verify | Result |
|---|---|---|---|---|
| `empty-page.pdf` | One blank Letter page, classic cross-reference table, `%PDF-1.7` header. The baseline every other file is a variation of. | 7.5.2, 7.5.4, 7.5.5, 7.7.2, 7.7.3 | `qpdf --check`, `pdfinfo` | clean |
| `pdf20-header.pdf` | `%PDF-2.0` header and catalog `/Version /2.0`. | 7.5.2, 7.7.2 Table 29 | `pdfinfo` shows `PDF version: 2.0` | clean |
| `text-standard14.pdf` | One line of text in non-embedded Helvetica with `/WinAnsiEncoding`. | 9.6.2, 9.6.5, 9.4.3 | `pdftotext` prints `Hello, Broadside` | clean |
| `text-truetype-embedded.pdf` | One line of text in an embedded TrueType font (`/FontFile2`, `/Length1`). The font program is synthesized by the script: `.notdef`, `H` and `I` drawn as rectangles, tables `head hhea maxp OS/2 hmtx cmap(3,1 format 4) loca glyf name post`. It is not a real typeface and carries no license. | 9.6.3, 9.8.2, 9.9 Table 125 | `pdftotext` prints `HI`; `pdffonts` shows `TrueType WinAnsi emb=yes`; `fc-scan` on the extracted stream reports `Broadside Minimal Regular TrueType charset=48-49`; `pdftoppm` renders two block glyphs | clean |
| `xref-stream.pdf` | Cross-reference stream (`/Type /XRef`, `/W [1 2 1]`, uncompressed), no classic table, no `trailer` keyword. | 7.5.8.2, 7.5.8.3 | `qpdf --check`; `qpdf --qdf --object-streams=disable` | clean |
| `object-stream.pdf` | Catalog, page tree and page stored in an object stream (`/Type /ObjStm`, `/N 3`, `/First`), located through type 2 entries in a cross-reference stream. | 7.5.7, 7.5.8.3 Table 18 | `qpdf --qdf --object-streams=disable` lists objects 1-3 as ordinary objects | clean |
| `incremental-update.pdf` | `empty-page.pdf` followed by one update section that replaces object 3 with an A4 `/MediaBox`; update trailer carries `/Prev`. | 7.5.6 | `pdfinfo` shows `595 x 842 pts (A4)` | clean |
| `hybrid-xref.pdf` | Classic table for objects 1-3 (4-6 listed free), then an object stream (5) holding the `/Info` dictionary (4), a cross-reference stream (6, `/Index [4 3]`) describing it, and an empty update section whose trailer has `/Prev` and `/XRefStm`. Mirrors the example in 7.5.8.4. A reader that follows `/XRefStm` sees the title; one that does not sees no `/Info`. | 7.5.8.4 Table 19 | `pdfinfo` shows `Title: hybrid` | clean |
| `flate-stream.pdf` | Content stream with `/Filter /FlateDecode`. | 7.4.4 | `pdftotext` prints `FlateDecode` | clean |
| `lzw-stream.pdf` | Content stream with `/Filter /LZWDecode`, `EarlyChange` 1 (default), clear code first, EOD last. | 7.4.4.2, Table 8 | `pdftotext` prints `LZWDecode` | clean |
| `ascii85-stream.pdf` | Content stream with `/Filter /ASCII85Decode`, `~>` terminator. | 7.4.3 | `pdftotext` prints `ASCII85Decode` | clean |
| `asciihex-stream.pdf` | Content stream with `/Filter /ASCIIHexDecode`, `>` terminator. | 7.4.2 | `pdftotext` prints `ASCIIHexDecode` | clean |
| `runlength-stream.pdf` | Content stream with `/Filter /RunLengthDecode` (literal runs, one repeated run, 128 EOD). | 7.4.5 | `pdftotext` prints `RunLengthDecode` | clean |
| `filter-chain.pdf` | Content stream with `/Filter [/ASCII85Decode /FlateDecode]`: ASCII85 is applied first on decode. | 7.4.1 | `pdftotext` prints `ASCII85 then Flate` | clean |
| `png-predictor.pdf` | Cross-reference stream encoded with Flate and `/DecodeParms << /Predictor 12 /Columns 4 >>` (PNG Up predictor, one filter byte per 4-byte row). | 7.4.4.4 Table 10, 7.5.8 | `qpdf --check` | clean |
| `encrypted-rc4-40.pdf` | Standard security handler R2 (`/V 1`, 40-bit RC4). Empty user password, owner password `owner`, `/P -4`. | 7.6.2, 7.6.3.2 Algorithm 1, 7.6.4.3.2 Algorithm 2, 7.6.4.4.2-7.6.4.4.3 Algorithms 3-4 | `qpdf --check`, `qpdf --password=owner --check`, `qpdf --show-encryption`; `pdftotext` prints `RC4 40-bit (R2)` | clean; qpdf reports `R = 2`, empty user password, all permissions allowed |
| `encrypted-rc4-128.pdf` | R3 (`/V 2 /Length 128`, 128-bit RC4). | as above plus 7.6.4.4.4 Algorithm 5 | as above; `pdftotext` prints `RC4 128-bit (R3)` | clean; `R = 3` |
| `encrypted-aes-128.pdf` | R4 (`/V 4`, crypt filter `/StdCF` with `/CFM /AESV2`, AES-128-CBC, IV prefixed, PKCS#5 padding, `sAlT` in the object key). | as above plus 7.6.5 Tables 25-27 | as above; `pdftotext` prints `AES-128 (R4)` | clean; `R = 4`, `AESv2` |
| `encrypted-aes-256.pdf` | R6 (`/V 5`, `/CFM /AESV3`, AES-256, `/O /U /OE /UE /Perms`), `%PDF-2.0`. | 7.6.3.3 Algorithm 1.A, 7.6.4.3.3-7.6.4.3.4 Algorithms 2.A and 2.B, 7.6.4.4.7-7.6.4.4.9 Algorithms 8-10 | as above; `pdftotext` prints `AES-256 (R6)` | clean; `R = 6`, `AESv3` |
| `inline-image.pdf` | Content stream with a 2x2 8-bit DeviceGray inline image (`BI /W 2 /H 2 /CS /G /BPC 8 ID ... EI`) scaled to 100x100 at (72, 600). | 8.9.7 | `pdftoppm` renders a checkerboard (black top-left and bottom-right) | clean |
| `page-tree-inherited.pdf` | Root `/Pages` (2) with one intermediate `/Pages` node (3) that carries `/MediaBox` and `/Resources`; the two leaf pages (4, 5) have neither. | 7.7.3.2, 7.7.3.4 | `pdfinfo` shows 2 Letter pages; `pdftotext` prints `Page 1` and `Page 2` (the font comes from the inherited resources) | clean |
| `annotations-link.pdf` | One `/Link` annotation with a `/URI` action in the page's `/Annots`. | 12.5.2, 12.5.6.5, 12.6.4.8 | `qpdf --check`; `qpdf --json --json-key=qpdf` shows the `/Link` dictionary | clean |
| `outline.pdf` | `/Outlines` root with two sibling items linked by `/First /Last /Next /Prev /Parent`, each with an explicit destination; `/PageMode /UseOutlines`. | 12.3.3, 12.3.2.2 | `qpdf --check`; `qpdf --json --json-key=outlines` lists both items with their destinations | clean |
| `name-tree-dests.pdf` | `/Names << /Dests ... >>` name tree with a root `/Kids` node and two leaves carrying `/Limits` and `/Names` for `alpha`, `beta`, `gamma`. | 7.9.6, 12.3.2.3 | `qpdf --check` | clean |
| `metadata-xmp.pdf` | Catalog `/Metadata` stream (`/Type /Metadata /Subtype /XML`) with a minimal XMP packet whose `dc:title` is `Broadside`, plus a trailer `/Info` dictionary with the same `/Title`. | 14.3.2, 14.3.3 | `pdfinfo` shows `Title: Broadside` | clean |

The crypt filter `/Length` entries in the R4 and R6 files are `16` and `32` (bytes), the form Acrobat and qpdf write and that Table 25 describes for the standard security handler; the same table also says the key "shall have the value of 128/256". Readers should accept both forms.

## Deliberately broken files

Each file is `empty-page.pdf` (or `text-standard14.pdf`) with exactly one defect. The "lenient mode" column says what a Phase 1 reader is expected to do under ADR 0005: repair, record one `Diagnostic`, and expose the same document as the intact file. In strict mode the same condition is an error. The diagnostic names are the ones Phase 1 is expected to define; adjust this table if the final names differ.

| File | Defect | Clauses | What qpdf 12.4.2 and poppler report | Lenient mode |
|---|---|---|---|---|
| `broken-xref-offsets.pdf` | Every in-use entry in the cross-reference table is 10 bytes past the object it points to. | 7.5.4 | qpdf (exit 3): `file is damaged`, `(object 1 0, offset 19): expected n n obj`, `Attempting to reconstruct cross-reference table`. pdfinfo opens the file and prints `Internal Error: xref num 1 not found but needed, try to reconstruct`. | Reconstruct the table by scanning for `N G obj` headers; record `XrefEntryOffsetInvalid` (or one `XrefReconstructed`) and open the document with one Letter page. |
| `missing-endobj.pdf` | Object 3 (the page) has no `endobj`; the `xref` keyword follows its dictionary directly. | 7.3.10 | qpdf (exit 3): `(object 3 0, offset 196): expected endobj`. pdfinfo: clean. | Parse the object, treat the next keyword as the end of the object, record `MissingEndobj` for object 3; the page is intact. |
| `wrong-stream-length.pdf` | The content stream says `/Length 10` but holds 47 bytes; `endstream` is at the correct place. | 7.3.8.2 | qpdf (exit 3): `(object 4 0, offset 284): expected endstream`, `attempting to recover stream length`, `recovered stream length: 47`. pdftotext prints `Length is wrong` and then `Syntax Error (331): Missing 'endstream' or incorrect stream length`. | Recover the length by searching for `endstream` (47 bytes), record `StreamLengthInvalid` for object 4; `pdftotext`-equivalent output is `Length is wrong`. |
| `no-xref.pdf` | No `xref`, `trailer` or `startxref` at all; the objects are followed by `%%EOF`. | 7.5.4, 7.5.5 | qpdf (exit 3): `can't find startxref`, `Attempting to reconstruct cross-reference table`, `unable to find trailer dictionary while recovering damaged file`, then opens the document (it finds the catalog by scanning). pdfinfo and pdftotext fail (exit 1): `Couldn't find trailer dictionary`, `Couldn't read xref table`. | Reconstruct the table by scanning, locate the catalog by scanning for `/Type /Catalog`, record `StartxrefMissing` and `TrailerMissing`; the document opens with one Letter page. |
| `startxref-wrong.pdf` | `startxref` points 100 bytes past the real `xref` keyword (into the table body). | 7.5.5 | qpdf (exit 3): `(offset 303): xref not found`, `Attempting to reconstruct cross-reference table`. pdfinfo: clean. | Search near the stated offset for the `xref` keyword (or reconstruct by scanning), record `StartxrefInvalid`; the document opens with one Letter page. |

## Adding a file

Add a `gen_<name>` function to `generate.py`, register it in `FILES`, run the script, verify with the tools above, and add a row here with the clause(s) it exercises and what the external tools report. Keep each file to one feature; if a test needs two features combined, that is a new file.
