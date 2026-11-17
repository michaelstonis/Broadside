# Core size: Native AOT size probes (#361)

What the core costs in a shipped app, measured as the size of a Native AOT executable per scenario; where the bytes go; what this
issue changed; and what an app can switch off. The probes are gated in CI (`bench/baselines/gate/README.md`, "Size probes"): a probe
that grows by more than 1% fails the pull request.

## Method

- **Probes.** `bench/SizeProbes`: one console app per scenario, each calling only the public API its scenario needs.
  `bench/size-probes.sh` publishes each with `dotnet publish -c Release --use-current-runtime` (Native AOT, trimmed,
  `IlcTreatWarningsAsErrors`), runs it over a corpus file and records the size of the executable. The SDK's defaults apply: no
  `OptimizationPreference`, stack trace data kept, debug symbols in a separate `.dSYM` that is not counted. The repository builds
  with `InvariantGlobalization`, so the probes do too.
- **Machine.** 2026-11-14, MacBook Pro, Apple M4 Max, macOS 27, .NET SDK 10.0.402, `osx-arm64`. Native AOT output for the same
  commit and SDK is byte-for-byte repeatable; the CI gate's 1% tolerance is for linker and runtime patch differences.
- **Analysis.** The breakdowns come from the compiler's map file and dependency graph (`-p:IlcGenerateMapFile=true
  -p:IlcGenerateDgmlFile=true`), summed by assembly and namespace, and from walking the graph back from a node to the entry point
  to find what keeps it.

## Results

Before is commit 577eca6b (sweep 7B's base), after is this issue's branch.

| Probe | Scenario | Before (bytes) | After (bytes) | Change |
|---|---|---:|---:|---:|
| `empty` | the runtime alone | 1,102,752 | 1,102,752 | 0 |
| `open-metadata` | open, page count, document information | 4,684,056 | 4,617,832 | -66,224 (-1.4%) |
| `extract-text` | open, extract the text of every page | 6,489,832 | 6,423,768 | -66,064 (-1.0%) |
| `render` | open, render page 1 with the software rasterizer | 6,987,040 | 6,904,448 | -82,592 (-1.2%) |
| `fill-form` | open, set a text field, save | 12,142,304 | 12,059,728 | -82,576 (-0.7%) |
| `save` | open, set the title, save | 11,545,104 | 11,462,512 | -82,592 (-0.7%) |
| `sign` | open, sign with a certificate | 11,978,784 | 11,896,192 | -82,592 (-0.7%) |

`Broadside.dll` (Release): 3,923,456 bytes before, 3,706,368 after (-217,088, -5.5%); gzipped (`gzip -9`) 1,412,429 before,
1,376,287 after. Custom attributes 22,926 before, 17,814 after; the `#US` heap (string literals) 593,292 bytes before, 491,940 after;
`#Blob` 350,672 before, 248,556 after.

Reading text is below rendering (6.42 MB against 6.90 MB), but not well below: text extraction runs the same content interpreter,
font machinery and color handling that rendering does, and rendering adds only the rasterizer (about 0.5 MB).

## Where the bytes go

**Opening a file (3.5 MB above the empty app).** The largest parts:

- **The image codecs, about 0.83 MB.** The JPX, JBIG2, DCT and CCITT decoders. Until #544 `FilterRegistry.Defaults` constructed
  every standard filter, so they were reachable from any stream decode, including the cross-reference stream read while opening.
  They are now registrations of the options: an app that builds its options with `PdfOptions.CreateWithoutImageCodecs()` does not
  carry them (see "Image codecs through the options" below).
- **Encryption, about 0.4 MB.** The standard and public-key security handlers, the integrity check and the BCL cryptography they
  use (`System.Security.Cryptography`, `System.Formats.Asn1`, `BigInteger`): whether a file is encrypted is only known once it is
  read, so every app that opens files carries them.
- The BCL itself (collections, LINQ, frozen collections, compression for `FlateDecode`, memory-mapped files), the parser, the
  object model and the diagnostics.

**Extracting text (1.8 MB more).** The font program parsers (TrueType, CFF, Type 1: widths and encodings come from embedded
programs), CMaps and the Adobe Glyph List, the content interpreter with its graphics state (color spaces, functions and shadings
are resolved as the content uses them, and their diagnostics are part of what extraction reports), and the structure tree for
tagged reading order.

**Saving, filling a form or signing (6.8 to 7.4 MB more than opening).** Of that:

- **The networking stack, about 2.5 MB.** Saving reads the document's XMP metadata to keep it in step with the document
  information. XMP is read with the BCL's `XmlReader`, created through `XmlReaderSettings`, whose default-resolver path constructs
  an `XmlUrlResolver`, which can fetch `http` URLs through `HttpClient`. Broadside sets no resolver and prohibits document type
  declarations, so nothing is ever fetched, but the trimmer cannot know that. `new XmlTextReader(...)` was tried instead: it reaches
  the same resolver through `XmlTextReaderImpl.GetTempResolver`, so there is no library-side way around it short of not using
  `System.Xml` (see the recommendations).
- **`System.Private.Xml`, about 0.6 MB**, of which XML Schema validation (`XsdValidatingReader` and the schema classes, 185 KB of
  code alone) is kept by the same `XmlReaderSettings` path although no packet is validated.
- The writer, the PDF/A and PDF/UA conformance checks (a document that declares PDF/A is checked when it is saved), the font
  parsers they use, and the signature code with its CMS support.

**Generic instantiations.** Each value-type instantiation of a generic is compiled separately. In `render` the largest groups
are `ConcurrentDictionary<,>` (9 instantiations, 42 KB), `ArrayPool<T>.Shared` (19 instantiations, 40 KB), `List<T>` (23, 26 KB),
the caches' `OnceCache<,>` (10, 15 KB) and the rasterizer's deliberately specialized compositing and clipping loops
(`GroupLayer.Span`, `RasterClip.Apply`, 22 KB together). Sharing them would mean boxing, `object`-typed caches or byte pools with
casts on the hot paths; speed wins over size, so they stay. Together they are a few percent of a probe.

**Metadata.** Of the 22,926 custom attributes, 11,307 are `CompilerGeneratedAttribute` (on every auto-property's backing field
and accessors; they go only by turning auto-properties into fields, which is not worth the churn), 9,568 were nullable metadata
(now 4,455, see below), 794 `IsReadOnlyAttribute` and 505 `TupleElementNamesAttribute`. Native AOT keeps none of them unless
reflection asks, so they cost only in the IL package.

## What was changed

Together, the first two save 66 to 83 KB per probe; the third shrinks the IL package only.

- **The diagnostic catalog no longer roots every catalog class.** `DiagnosticCatalog` builds its name index lazily (a nested
  class), so its static initialization was not the problem; but `Diagnostic.Kind` looks a name up in it, and the three places that
  pass a diagnostic on (the stream decoder for third-party filters, the security handler context, the shared CMap cache) read
  `Kind`, which kept the index and with it every area's catalog class and every code. They now pass on the typed code the
  diagnostic was made with, or its plain string code, whose `Kind` is then looked up only if someone reads it, with the same
  result. The public shape of the catalog is unchanged.
- **The Adobe Glyph List's names are UTF-8 data.** The 4,281 glyph names (50,000 ASCII characters) were one `string` literal,
  100 KB as UTF-16 in the assembly and a frozen string in a native image; they are now a `"..."u8` literal, read in place from the
  assembly's data section, compared byte for char. The generator (`src/Broadside/Fonts/Data/generate.py`) writes them. The
  Unicode values (about 4,400 characters) stay a string: callers receive them as `ReadOnlySpan<char>`. The other generated tables
  are already arrays of numbers stored as data.
- **Nullable metadata only where a consumer can see it** (`<Features>nullablePublicOnly</Features>` for everything under `src/`):
  5,113 fewer attributes and 102 KB less `#Blob` heap in `Broadside.dll`. Internal members keep theirs, because the assembly
  names its test projects in `InternalsVisibleTo`.

## Image codecs through the options (#544)

The image codecs are part of the options object. `new PdfOptions()` (and every entry point without options, such as
`PdfDocument.Open(path)`) registers all four, as before; `PdfOptions.CreateWithoutImageCodecs()` registers none, and
`UseImageCodecs()` or `UseFilter(new DctDecodeFilter())` adds them back, all or one at a time. A stream that names a codec the
options left out is reported with `FilterCodecNotRegistered` (Information, so strict mode still opens the file) and is left
encoded from that filter on, like any stream the engine cannot decode: its image is not drawn, everything else reads as usual.

For the trimmer to drop them, nothing on the path an app without codecs takes may name a codec type, so three static paths moved:
the built-in registry (`FilterRegistry`) holds only the general-purpose filters and the codecs live in a separate class
(`ImageCodecRegistry`) reached only from the public `PdfOptions` constructor, `UseImageCodecs` and a stand-alone `FilterContext`;
the stand-alone `FilterContext`'s decoder takes its filters from that constructor instead of naming them where the decoder is
created on demand (a document's contexts share that getter, which kept every codec in the first attempt); and the default engine
of the option-less entry points is created in a nested class, so `PdfEngine`'s static initialization does not construct default
options. The name checks (`FilterNames.IsImageCodec`) compare names only and keep no codec.

Measured on the same machine and SDK as above (2026-11-17, base commit 2bc2b766):

| Probe | Scenario | Before (bytes) | After (bytes) | Change |
|---|---|---:|---:|---:|
| `open-metadata` | open, page count, document information; default options | 4,568,168 | 4,568,168 | 0 |
| `open-metadata-no-image-codecs` | the same with `PdfOptions.CreateWithoutImageCodecs()` | | 3,738,784 | -829,384 (-18.2%) against `open-metadata` |

The other probes use the default options and are unchanged (within 16 bytes). What remains of the JPEG code in the probe without
codecs is the default DCT encoder's constructor and quantization tables (`StreamEncoderRegistry` constructs it), a small fraction; its encode
path is not reached.

## What an app can switch off

These are app settings, not library ones: each changes how the whole app behaves, so a library cannot choose them for it.

| Probe | After (default) | `XmlResolverIsNetworkingEnabledByDefault=false` | All four settings below |
|---|---:|---:|---:|
| `empty` | 1,102,752 | 1,102,752 | 970,336 |
| `open-metadata` | 4,617,832 | 4,617,832 | 4,154,488 |
| `extract-text` | 6,423,768 | 6,423,768 | 5,894,088 |
| `render` | 6,904,448 | 6,904,448 | 6,308,704 |
| `fill-form` | 12,059,728 | 9,575,008 | 8,780,304 |
| `save` | 11,462,512 | 8,994,336 | 8,232,800 |
| `sign` | 11,896,192 | 9,444,544 | 8,633,376 |

- `<XmlResolverIsNetworkingEnabledByDefault>false</XmlResolverIsNetworkingEnabledByDefault>`: the default XML resolver no
  longer reaches the network, which removes the networking stack described above (about 2.5 MB, a fifth of the app) from any app
  that saves, fills forms or signs. It affects the app's own `XmlReader` use too.
- `<UseSystemResourceKeys>true</UseSystemResourceKeys>`: BCL exception messages become resource keys. Broadside's own diagnostic
  and exception messages are unaffected.
- `<StackTraceSupport>false</StackTraceSupport>`: no method names in stack traces.
- `<OptimizationPreference>Size</OptimizationPreference>`: favors size over speed in code generation; measure rendering speed
  before choosing it.

## Recommendations

What is left is reachable by design: a file's content decides at run time which codec, handler or checker runs, so the trimmer
must keep them all. Two changes would cut the rest, both bigger than this issue and both needing a decision first (the first is now done):

1. **Image codecs chosen through the options.** Done in #544 (see above): `PdfOptions.CreateWithoutImageCodecs()` saves about
   0.83 MB (18% of `open-metadata`).
2. **XMP read without `System.Xml`.** A small reader for the RDF/XML subset XMP uses (no DTDs, no entities beyond the predefined
   ones, no resolver) would remove about 3 MB from every app that saves, fills or signs, with no app setting. The risk is the
   leniency contract: which damaged packets read and what their diagnostics say must not change, so it needs its own issue with the
   real-world corpus as the oracle.

Splitting rarely used subsystems (PDF/A conversion, signature validation) into packages is not needed for these: those two
subsystems are a small part of the probes above.
