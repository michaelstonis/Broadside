# Fuzzing coverage report

Issue #274, for v1.0. Every parser and codec has a fuzz target (`tests/Broadside.Fuzz`); this report says how much of each one its
seed corpus reaches, and records the last full run of every target. It sits next to the [threat model](threat-model.md) and the
[unsafe code and pooled buffer review](unsafe-code-review.md).

## Which code each target owns

`tests/Broadside.Fuzz/parsers.txt` maps every parser and codec of `Broadside` and `Broadside.Rendering` to the targets that
exercise it. A parser or codec is any type behind the filter or font program parser extension point, any type whose name ends in
`Parser`, `Reader`, `Decoder`, `Tokenizer` or `Lexer`, and the parsers added in Phases 3 to 5 that follow neither rule: ICC
profiles (`IccProfile`), the default appearance tokenizer (`PdfDefaultAppearance`), the OpenType layout tables (`LayoutTable`,
`GdefTable`, `KernTable`), CMS signed data, time-stamp responses, OCSP responses and CRLs (`CmsSignature`, `TimestampProtocol`,
`OcspResponse`, `RevocationList`), PNG and JPEG input to the canvas (`PngReader`, `JpegInfo`) and range responses
(`HttpRangeSource`). `FuzzTargetMapTests` (in `tests/Broadside.Rendering.Tests`, part of every CI run) fails when a parser or codec
has no line in the map, when a line names a target that is not registered, and when a line names a type that no longer exists.

The viewer packages parse no untrusted bytes themselves: documents, files and addresses go to the core, and the Blazor viewer hands
JPEG data to the browser's decoder (`BrowserImageDecoder`, whose eligibility checks are the core's, fuzz target
`platform-image-eligibility`), so they add no target; the [review](unsafe-code-review.md#viewers) lists what they do with buffers.

This pass added the two targets the map found missing: `jpeg-reader` (JPEG input to the canvas) and `range-response` (a remote file
against an HTTP server whose answers the input controls). See `tests/Broadside.Fuzz/README.md`.

## Threshold and method

**Threshold: 60 % line coverage of the parser scope**, where the parser scope of a target is every line of the types it owns: the
types whose line in `parsers.txt` names it first (nested types and lambdas included). Other targets on a line also reach the type;
they are not judged on it. A fuzzer starts from these seeds and grows the corpus by coverage, so the seeds
must already reach well into the parser; error branches for each malformed structure are what the fuzzer itself adds. Targets that
own no parser (the whole-document, rendering, writing and extraction targets) exercise code that is not a parser; their
row shows the coverage of the two assemblies only and has no threshold.

`tests/Broadside.Fuzz/Coverage/coverage.sh` measures each target: it writes the target's seeds with `--seeds` (exactly what the
scheduled libFuzzer run starts from, here from `tests/Corpus` alone), then runs the target once over those seeds, its regression
seeds (`tests/Broadside.Fuzz/Seeds/<target>`) and `tests/Corpus`, under coverlet (`coverlet.console`, a local tool in
`.config/dotnet-tools.json`), and `report.py` writes the table. Coverage is of the corpus, not of a fuzzing run: what libFuzzer
reaches beyond it only adds to these numbers.

## Coverage, 2026-11-24

Measured on macOS arm64 with the seeds `--seeds` writes from `tests/Corpus` (no real-world seed corpora), the regression seeds and
`tests/Corpus`. "Seeds" counts the distinct seeds `--seeds` wrote; "Library coverage" is line coverage of `Broadside.dll` and
`Broadside.Rendering.dll` together.

| Target | Seeds | Parser scope (types) | Scope lines | Scope coverage | Verdict | Library coverage |
|---|---:|---|---:|---:|---|---:|
| `cff-subset` | 190 | (owns no parser) | | | n/a | 10.0% |
| `cmap` | 247 | CMapParser, Reader | 465 | 56.6% | below | 1.1% |
| `cms` | 1 | CmsSignature | 188 | 63.8% | meets | 0.2% |
| `colorspace` | 190 | (owns no parser) | | | n/a | 8.3% |
| `content-interpreter` | 129 | (owns no parser) | | | n/a | 8.5% |
| `content-lexer` | 129 | ContentReader | 261 | 91.6% | meets | 0.9% |
| `content-objects` | 129 | (owns no parser) | | | n/a | 9.8% |
| `content-rewrite` | 129 | (owns no parser) | | | n/a | 8.2% |
| `crl` | 1 | RevocationList | 107 | 70.1% | meets | 0.1% |
| `decrypt` | 84 | (owns no parser) | | | n/a | 0.6% |
| `default-appearance` | 4 | PdfDefaultAppearance | 161 | 92.5% | meets | 1.3% |
| `display-list` | 129 | (owns no parser) | | | n/a | 14.3% |
| `document` | 190 | AssociatedFileReader, EntryReader, NameTreeReader, NumberTreeReader, PageTreeReader, RevisionReader, TextStringDecoder, TreeReader, XrefTableReader | 1201 | 43.0% | below | 32.8% |
| `encrypted-document` | 190 | (owns no parser) | | | n/a | 5.2% |
| `figure-detection` | 190 | (owns no parser) | | | n/a | 11.6% |
| `filter-ascii85` | 2 | Ascii85DecodeFilter | 50 | 98.0% | meets | 0.3% |
| `filter-asciihex` | 1 | AsciiHexDecodeFilter | 42 | 100.0% | meets | 0.3% |
| `filter-ccitt` | 190 | CcittBitReader, CcittFaxDecodeFilter, CcittFaxDecoder, CcittLineDecoder, MmrDecoder | 612 | 66.8% | meets | 1.1% |
| `filter-dct` | 46 | ArithmeticDecoder, DctDecodeFilter, JpegBitReader, JpegDecoder, JpegMarkerReader, JpegScanDecoder | 804 | 81.8% | meets | 2.8% |
| `filter-dct-encode` | 190 | (owns no parser) | | | n/a | 2.1% |
| `filter-encode` | 129 | (owns no parser) | | | n/a | 1.1% |
| `filter-flate` | 10 | FlateDecodeFilter | 115 | 42.6% | below | 0.3% |
| `filter-jbig2` | 5 | Jbig2BitReader, Jbig2DecodeFilter, Jbig2IntegerDecoder, Jbig2PageDecoder, MqDecoder | 838 | 55.3% | below | 2.6% |
| `filter-jpx` | 190 | JpxCodeBlockDecoder, JpxCodestreamReader, JpxDecodeFilter, JpxDecoder, JpxPacketHeaderReader, JpxRawDecoder, JpxTileDecoder | 1686 | 69.2% | meets | 2.8% |
| `filter-lzw` | 1 | LzwDecodeFilter | 85 | 87.1% | meets | 0.4% |
| `filter-predictor` | 1 | (owns no parser) | | | n/a | 0.4% |
| `filter-runlength` | 1 | RunLengthDecodeFilter | 31 | 96.8% | meets | 0.3% |
| `filter-sliced` | 190 | StreamDecoder | 346 | 44.8% | below | 0.8% |
| `first-page-first` | 190 | LinearizationReader | 84 | 81.0% | meets | 5.5% |
| `flatten` | 190 | (owns no parser) | | | n/a | 10.5% |
| `font-cff` | 190 | CffDictReader, CffFontProgramParser | 110 | 69.1% | meets | 1.5% |
| `font-index` | 190 | HandleFontFileReader | 14 | 0.0% | below | 0.0% |
| `font-scanner` | 190 | FontFileReader, FontNameParser, MemoryFontFileReader | 92 | 60.9% | meets | 0.2% |
| `font-truetype` | 190 | TrueTypeFontProgramParser | 40 | 82.5% | meets | 1.2% |
| `font-type1` | 190 | PostScriptTokenizer, Type1FontProgramParser, Type1ProgramReader | 387 | 80.6% | meets | 1.3% |
| `form-fill` | 190 | (owns no parser) | | | n/a | 15.0% |
| `function-sampled` | 190 | (owns no parser) | | | n/a | 2.5% |
| `function-type4` | 190 | PostScriptLexer | 48 | 60.4% | meets | 2.4% |
| `gpos` | 192 | GdefTable, KernTable | 64 | 78.1% | meets | 1.1% |
| `gsub` | 192 | LayoutTable | 88 | 81.8% | meets | 1.0% |
| `hint-tables` | 3 | HintBitReader, HintTableReader | 167 | 62.9% | meets | 0.2% |
| `icc-profile` | 14 | IccProfile | 233 | 82.8% | meets | 1.6% |
| `image-decode` | 190 | (owns no parser) | | | n/a | 17.2% |
| `inline-image-end` | 190 | (owns no parser) | | | n/a | 8.2% |
| `jpeg-reader` | 46 | JpegInfo | 84 | 60.7% | meets | 4.7% |
| `layout-detection` | 190 | (owns no parser) | | | n/a | 22.9% |
| `lexer` | 190 | CosLexer | 80 | 87.5% | meets | 0.1% |
| `mac-token` | 2 | (owns no parser) | | | n/a | 0.1% |
| `object-parser` | 190 | CosParser | 320 | 80.9% | meets | 0.8% |
| `object-stream` | 3 | (owns no parser) | | | n/a | 0.5% |
| `ocsp-response` | 1 | OcspResponse | 121 | 73.6% | meets | 0.1% |
| `optional-content` | 190 | (owns no parser) | | | n/a | 5.2% |
| `pdf-date` | 190 | (owns no parser) | | | n/a | 0.0% |
| `platform-image-eligibility` | 190 | (owns no parser) | | | n/a | 0.6% |
| `png-reader` | 265 | PngReader | 344 | 89.0% | meets | 4.0% |
| `public-key` | 190 | (owns no parser) | | | n/a | 2.6% |
| `range-response` | 190 | HttpRangeSource | 215 | 84.2% | meets | 17.4% |
| `rasterize` | 129 | (owns no parser) | | | n/a | 17.9% |
| `rasterize-page` | 190 | (owns no parser) | | | n/a | 40.1% |
| `repair` | 190 | (owns no parser) | | | n/a | 1.5% |
| `save` | 190 | (owns no parser) | | | n/a | 10.0% |
| `shading-mesh` | 190 | MeshBitReader, MeshDecoder, ShadingReader | 261 | 51.7% | below | 0.2% |
| `signature-validation` | 191 | (owns no parser) | | | n/a | 5.9% |
| `structure-tree` | 190 | AttributeReader, KidReader | 141 | 54.6% | below | 16.0% |
| `table-detection` | 190 | (owns no parser) | | | n/a | 10.9% |
| `text-extraction` | 129 | (owns no parser) | | | n/a | 11.4% |
| `timestamp-response` | 1 | TimestampProtocol | 130 | 46.2% | below | 0.1% |
| `tounicode` | 190 | (owns no parser) | | | n/a | 1.0% |
| `truetype-subset` | 190 | (owns no parser) | | | n/a | 9.3% |
| `type1-to-cff` | 190 | (owns no parser) | | | n/a | 10.5% |
| `windowed-document` | 190 | CrossReferenceReader | 257 | 65.0% | meets | 5.3% |
| `xmp` | 16 | XmpPacketReader | 113 | 68.1% | meets | 0.8% |
| `xmp-xml` | 190 | XmpXmlReader | 360 | 14.4% | below | 0.1% |
| `xref-stream` | 5 | XrefStreamReader | 186 | 69.9% | meets | 0.8% |

**Summary.** 74 targets; 40 own a parser, of which 30 meet the threshold and 10 are below it; 34 own none. This pass added seeds
where the corpus had none or too few, and the rows above include them: a signed file with its chain, its CMS signed data, a CRL and
an OCSP response (`signature-validation`, `cms`, `crl`, `ocsp-response`: 4 to 9 % before, 64 to 74 % after), the JPEG and PNG test
suites (`jpeg-reader` 51 to 61 %, `png-reader` 50 to 89 %, `filter-dct` 68 to 82 %), test fonts with layout tables (`gpos` 0 to
78 %, `gsub` 43 to 82 %) and the predefined CMaps (`cmap` 54 to 57 %).

**Below the threshold**, each with the seeds that would raise it (follow-up after v1.0; the scheduled libFuzzer run grows every
corpus from these seeds, so these numbers are the floor):

| Target | Coverage | Why | Seeds to add |
|---|---:|---|---|
| `font-index` | 0 % | `HandleFontFileReader` reads font files from the host's disk through a handle; no fuzz input reaches it. The bytes it reads are parsed by `FontFaceScanner`, which `font-scanner` covers through `MemoryFontFileReader` (61 %) | none: input and output only |
| `xmp-xml` | 14 % | the corpus has few XMP packets, and most are the library's own | XMP packets from the real-world corpora (`pdf20examples`, which CI already passes to `--seeds`) |
| `document` | 43 % | name trees, number trees, associated files and embedded file entries are rare in `tests/Corpus` | files with name and number trees, embedded files and associated files (the CI seed corpora have them) |
| `filter-flate` | 43 % | the corpus streams use few Flate block types and no dictionaries | stored, fixed-Huffman and preset-dictionary zlib streams |
| `filter-sliced` | 45 % | the sliced decoder's resumption paths need long, multi-filter streams | multi-filter chains with predictors |
| `timestamp-response` | 46 % | one valid response; the rejection paths are reached only by mutation | responses with each PKIStatus, with and without certificates and nonces |
| `shading-mesh` | 52 % | few mesh shadings of types 6 and 7 in the corpus | mesh shadings of every type with flags 1 to 3 and Decode arrays |
| `structure-tree` | 55 % | attribute objects and their classes are rare | tagged files with attribute objects, class maps and object references |
| `filter-jbig2` | 55 % | five JBIG2 streams; halftone, pattern and refinement segments absent | streams of each segment type |
| `cmap` | 57 % | `usecmap`, `cidchar` and `notdef` ranges are rare in the predefined CMaps | embedded CMaps using every operator |

## Full run

Every registered target (74) ran once with `--smoke <target> 30` on 2026-11-24 (macOS arm64, Release build of
`tests/Broadside.Fuzz`, one target at a time under the machine's build lock): first every regression seed and every `tests/Corpus`
file, then 30 seconds of random mutations of them (`cff-subset` ran for 60 seconds). **No finding** in the run of record: every
target ended with `no failures`, from 1,369 mutated inputs (`public-key`, which decrypts with RSA) to 34.6 million (`hint-tables`).
The targets whose seeds changed afterwards (`cms`, `crl`, `ocsp-response`, `signature-validation`, `jpeg-reader`, `png-reader`,
`filter-dct`, `gpos`, `gsub`, `cmap`) ran again with the new seeds, also clean.

Before that run, the new `range-response` target found two problems in its first minutes, both fixed in this pass and kept as
regression seeds in `tests/Broadside.Fuzz/Seeds/range-response/`:

- Opening a remote linearized file whose hint tables count pages its damaged page tree lacks threw `ArgumentOutOfRangeException`
  from the page indexer (fixed: the open reads the first page through the hints without the indexer; test
  `RangeRequestSourceTests.A_linearized_file_whose_hints_count_pages_its_page_tree_lacks_opens_remotely`).
- Its own invariant was too strict: such a file reads through its hint tables remotely and through its page tree from memory, so
  the page counts can differ; the target now compares them only for files that are not linearized.

This is a smoke run, without coverage feedback. The scheduled libFuzzer run (`.github/workflows/fuzz.yml`, weekly, 80 minutes per
target on every core) is the long run; its summary job tabulates executions and findings per target.
