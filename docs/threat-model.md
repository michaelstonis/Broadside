# Threat model and resource limits

This is the threat model for Broadside reading untrusted PDF files, and the inventory of every resource limit that protects it:
its default, the option that changes it (or "fixed"), and the test that proves it holds. It covers the packages that ship as 1.x:
the core (`Broadside`), `Broadside.Rendering`, the native rendering backends, `Broadside.Layout` and the font data packages. The
viewer packages (`Broadside.Viewer*`) ship as previews on their own track and are not covered here.

How to report a vulnerability, which versions get fixes and how fast: [SECURITY.md](../SECURITY.md). Guidance for applications:
[Processing untrusted files](../site/articles/untrusted-files.md).

## Assets and attacker

**Attacker.** Anyone who can make the application open a PDF: an upload, an email attachment, a URL. They control every byte of
the file, including its fonts, images, ICC profiles, embedded files, certificates and the addresses in them. When the application
opens a remote file, they may also control the server's responses.

**What we protect.**

1. **Availability of the host process.** No file may crash the process (unhandled exception, stack overflow), hang it (loop
   without end), or exhaust its memory. Work is bounded per object by the limits below, and every long operation can be canceled.
2. **The host's files and network.** A document cannot make Broadside read a file other than the one opened (and the operating
   system's fonts), write anywhere other than a temporary file the application's options call for, or send a network request,
   unless the application hands it an `HttpClient` for that purpose.
3. **Integrity of security decisions.** A signature reported `Valid` was made by the key it names over the bytes it claims;
   changes after signing are reported; nothing is trusted that the application did not trust. Decryption never yields
   plaintext that was not encrypted with the given key without reporting it.
4. **No code execution.** JavaScript, XFA, 3D, rich media and multimedia are parsed and preserved as data, never executed.

**Out of scope.** Platform code that Broadside calls rather than implements: SkiaSharp, CoreGraphics and ImageIO, Direct2D and WIC,
Android graphics and the browser's WebGL, and platform image decoders when the application registers them. Total work across a
whole document, beyond the per-object limits (use deadlines and process isolation, as the guide explains). Side channels in the
managed cryptography (timing of RC4, AES and the password algorithms on the host). Confidentiality of documents against someone
who can open them: permissions in an encrypted file are advice to the application, not enforcement.

## Properties that hold everywhere

- **Managed parsing.** Everything that interprets the file's syntax (object syntax, cross-reference data, filters and image codecs,
  font programs, CMaps, ICC profiles, functions, content streams, CMS signed data, OCSP responses and CRLs) is C# in the core,
  with no native dependency. A memory-safety bug there is limited to what `Span<T>`, `unsafe` blocks and pooled buffers allow, and
  every parser and codec is fuzzed (`tests/Broadside.Fuzz`, one target per parser; `.github/workflows/fuzz.yml`; per-target
  coverage and the last full run in the [fuzzing coverage report](fuzz-coverage.md)). Every `unsafe` block and pooled buffer is
  reviewed, with its bounds, in the [unsafe code and pooled buffer review](unsafe-code-review.md). Two things
  reach the platform through .NET's own cryptography: certificates are loaded as `X509Certificate2` and public-key envelopes are
  opened with `EnvelopedCms`, which parse with the operating system's library; and the cipher and hash primitives (AES, MD5, SHA-2)
  use the platform where it has them, the managed implementations elsewhere (always in the browser).
- **Lenient reading, never silent.** A deviation is repaired and recorded as a diagnostic; reaching a limit is a diagnostic, never
  an exception, in lenient mode. Strict mode throws `DiagnosticException` for the first deviation. Diagnostics are deduplicated by
  code, offset and object (content diagnostics once per code and object), so their number is bounded by the size of the file.
- **No recursion on file data without a depth limit, and no table sized from a number in the file.** Object numbers, counts and
  sizes read from a file are checked against the data that is actually there before anything is allocated; recursive structures
  carry a visited set or a depth limit, usually both.
- **Cancellation.** `ContentOptions.CancellationToken` (checked every 1,024 operators, at every nested run and inside image
  codecs), `RenderOptions.CancellationToken`, and the sliced APIs that run for a time budget (`DisplayListBuild`,
  `DisplayListReplay`, `ProgressiveRender`, `TextExtraction`, `LayoutExtraction`, `DisplayImage.Decode`).

## Entry points

Each entry point lists what a hostile file can aim at and the limits that apply; the limit numbers refer to the inventory below.

| Entry point | Code | Threats | Limits |
|---|---|---|---|
| File source | `PdfSource`, `HttpRangeSource`, `PdfDocument.OpenAsync` | Huge or endless non-seekable streams; a hostile range server sending more than asked | L5, L6, L7, C10–C13 |
| Lexer and object parser | `CosParser`, `CosLexer` | Deeply nested arrays and dictionaries (stack overflow); huge strings and numbers | C1 |
| Cross-reference and object loading | `CrossReferenceReader`, `XrefStreamReader`, `CrossReferenceReconstructor`, `ObjectLoader`, `ObjectStream` | Prev loops; Size and Index far beyond the data; reference cycles; chains of loads that need loads; object streams that contain themselves | C2–C6 |
| Linearization and hint tables | `LinearizationReader`, `HintTableReader`, `HintObjectLocator` | Hint counts and ranges far beyond the file | C7, C8 |
| Filters | Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors, Crypt; `StreamDecoder` | Decompression bombs; predictors with huge `Columns`; filters that decode other streams | L1, C9 |
| Image codecs | DCT (JPEG), JPX (JPEG 2000), JBIG2, CCITT fax, PNG reading for `PdfCanvas` | Dimensions far beyond the data; tile, precinct and symbol counts; decode loops | L1, L2, I1–I7 |
| Font program parsers | TrueType and OpenType, CFF and CID-keyed CFF, Type 1, Type 3; OpenType layout | Composite glyph recursion; subroutine recursion and fan-out; huge point counts; lookup recursion | F1–F12 |
| CMaps and ToUnicode | `CMapParser`, bundled predefined CMaps | Huge ranges; `usecmap` chains and cycles; long destination strings | F13–F16 |
| Content interpreter | `ContentInterpreter`, `ContentReader` | Millions of operands; `q` without `Q`; forms, patterns, Type 3 glyphs and soft masks that nest or contain themselves | L8–L10 |
| Graphics model | Colour spaces, functions, shadings, ICC profiles | Colour spaces that nest or contain themselves; function recursion and huge sample tables; PostScript stack and nesting; huge meshes; ICC tables | L3, G1–G9 |
| Document model | Page tree, name and number trees, outlines, actions, forms, structure tree, optional content, XMP, embedded files and collections | Cycles and very deep trees in every tree the catalog reaches | D1–D12 |
| Security handlers | Standard (RC4, AES-128, AES-256, revisions 2 to 6), public-key, third-party `ISecurityHandler` | Password-length abuse; malformed CMS recipients; wrong keys yielding garbage plaintext | S1, S2 |
| Signature validation | `SignatureValidator`, CMS, certificate paths, OCSP, CRL, time stamps | Byte ranges that skip content; changes after signing; forged chains; revocation fetching to attacker-chosen hosts (SSRF); huge responses | S3–S7, L12 |
| Fonts the document does not embed | `SystemFontResolver`, `IFontResolver` | Reading the host's font files; very large font files | S8, S9 |
| Display lists and caches | `DisplayList`, cache budgets | Unbounded caching across pages | L11 |
| Software rasterizer | `SoftwareRasterizer`, `Stroker`, tiling, shading, glyph masks | Huge bitmaps from huge page boxes; millions of dashes; huge coordinates; tiling and shading subdivision blowup | R1–R9 |
| Native backends | Skia, CoreGraphics, Direct2D, Android Canvas, WebGL | The same as the rasterizer, plus platform limits on surfaces and paths | N1 |

**What is never reached from a file.** JavaScript and other actions are exposed as data (`PdfAction`), never run. Launch, remote
go-to, import-data and submit-form actions are not followed. Stream data in an external file (`F`, `FFilter`) is not read: the
stream's own bytes are decoded instead, with a diagnostic. Embedded files are bytes the application may read; Broadside never
opens or runs them.

**Signature spoofing.** `ValidateSignatures` trusts no certificate unless the application lists it in `TrustAnchors`, never
consults the operating system's store, and downloads nothing unless the application sets `OnlineRevocationClient`. A signature is
`Invalid` when its byte range is missing, overlapping, outside the file, leaves a gap other than its own `Contents`, or does not end
at a revision boundary; when the digest or the signature value does not verify; or when the ESS signing-certificate attribute does
not match the signer. `CoversWholeFile` and `Modifications` report every revision added after the signature, classified against
the DocMDP and field-lock permissions. The visible appearance of a signature field is never part of the validation result.

## Resource limit inventory

"Option" is the public setting that changes the limit; "fixed" means it is a constant. Tests are in `tests/`; a test name in
**bold** was added with this inventory. Constants are named so that a maintainer can find them (`grep -rn <name> src`).

### Configurable limits

| # | Limit | Default | Option | Test |
|---|---|---|---|---|
| L1 | Bytes one stream decodes to, every filter and codec | 1 GiB (`PdfOptions.DefaultMaxDecodedLength`) | `PdfOptions.WithMaxDecodedLength` | `FilterPipelineTests.Output_beyond_the_decoded_length_limit_is_truncated_with_a_diagnostic`; **`ResourceLimitTests.Every_expanding_filter_stops_at_the_decoded_length_limit`** (Flate, LZW, RunLength, ASCIIHex, ASCII85); `DctRepairTests.An_image_larger_than_the_decoded_length_limit_is_not_decoded` |
| L2 | Pixels in one image | 2^28 (`DefaultMaxImagePixels`) | `PdfOptions.WithMaxImagePixels` | `ImageFilterFacetTests.A_header_larger_than_the_limit_is_rejected_before_the_codec_decodes_anything`, `.A_dictionary_size_larger_than_the_limit_is_rejected_before_any_filter_runs`, `.Creating_an_image_over_the_limits_reports_and_rents_nothing`; `Jbig2RepairTests.A_region_larger_than_the_image_limits_is_not_decoded`; `ThirdPartyImageFilterTests.A_geometry_over_the_limit_is_refused_by_the_context_before_any_memory_is_rented` |
| L3 | Vertices in one mesh shading | 2^22 (`DefaultMaxMeshVertices`) | `PdfOptions.WithMaxMeshVertices` | `ShadingRepairTests.The_mesh_limit_of_the_options_cuts_a_larger_mesh_with_one_size_diagnostic` |
| L4 | Reading mode | lenient | `PdfOptions.UseStrict` | `ColorRepairTests.A_deviation_is_repaired_with_one_kind_of_diagnostic_and_strict_mode_throws_it` and the other lenient/strict pairs |
| L5 | Bytes of a non-seekable stream kept in memory; the rest goes to a temporary file | 64 MiB (`DefaultStreamBufferLimit`) | `PdfOptions.WithStreamBufferLimit` | `SourceTests.A_non_seekable_stream_longer_than_the_buffer_limit_is_read_through_a_temporary_file` |
| L6 | Remote file block size | 4 KiB (`DefaultRangeBlockSize`) | `PdfOptions.WithRangeBlockSize` | `RangeRequestSourceTests` |
| L7 | Remote file bytes cached | 64 MiB (`DefaultRangeCacheLimit`) | `PdfOptions.WithRangeCacheLimit` | `RangeRequestSourceTests` |
| L8 | Operands held before one content operator | 65,536 (`ContentOptions.DefaultMaxOperands`) | `ContentOptions.WithMaxOperands` | **`ResourceLimitTests.Operands_beyond_the_operand_limit_are_dropped_with_a_diagnostic`** |
| L9 | Graphics state stack depth | 4,096 (`DefaultMaxSaveDepth`) | `ContentOptions.WithMaxSaveDepth` | `GraphicsStateTests.A_q_beyond_the_depth_limit_is_ignored_with_its_q` |
| L10 | Nesting of forms, tiling patterns, Type 3 glyphs, soft masks and appearances (one shared budget); self-containment is cut separately | 50 (`DefaultMaxNestingDepth`) | `ContentOptions.WithMaxNestingDepth` | `FormTests.Forms_nested_deeper_than_the_limit_are_not_run`; `PatternTests.Nesting_deeper_than_the_limit_stops_with_a_diagnostic`; `DisplayListType3Tests.A_glyph_that_would_run_inside_itself_paints_the_rest_and_records_the_recursion`; `DisplayListTilingTests.A_cell_that_paints_with_its_own_pattern_is_built_empty_and_records_the_recursion` |
| L11 | Cache budgets: decoded content, images, glyphs, display lists | 32, 64, 8, 32 MiB | `PdfOptions.WithContentCacheBudget`, `WithImageCacheBudget`, `WithGlyphCacheBudget`, `WithDisplayListCacheBudget` | `RenderCacheTests.Budgets_are_options_with_defaults_and_refuse_negative_values`, `.Decoded_content_is_reused_within_its_budget_and_not_cached_beyond_it`, `.Images_beyond_the_budget_are_evicted_least_recently_used_first`, `.An_image_larger_than_the_budget_is_painted_but_never_cached` |
| L12 | Network requests for revocation data | none (no client) | `PdfSignatureValidationOptions.UseOnlineRevocation` | `RevocationValidationTests` |
| L13 | Time | none | `ContentOptions.WithCancellation`, `RenderOptions.WithCancellation`, sliced `Continue(TimeSpan)` | `DisplayListCancellationTests.Cancelling_the_build_of_a_large_real_world_page_stops_within_10_ms`; `DisplayListProgressTests.A_time_budget_stops_a_replay_that_the_next_slice_resumes_where_it_stopped`; `ProgressiveRenderingTests.A_render_sliced_into_5_ms_budgets_and_resumed_matches_one_uninterrupted_render` |
| L14 | Platform image decoders (hand untrusted image bytes to the operating system) | off until one is registered | `PdfOptions.UsePlatformImageDecoder`, `UseManagedImageDecoding` | `PlatformImageDecoderTests` |

Limits of the parser contexts. Documents always use the defaults; a parser called directly through its extension-point contract
can be given others.

| # | Limit | Default | Option | Test |
|---|---|---|---|---|
| F1 | TrueType composite glyph depth | 16 (`FontProgramContext.DefaultMaxCompositeDepth`) | `FontProgramContext.MaxCompositeDepth` | `TrueTypeProgramTests.Components_nested_deeper_than_the_limit_are_skipped` |
| F2 | Points in one glyph | 65,536 (`DefaultMaxGlyphPoints`) | `FontProgramContext.MaxGlyphPoints` | `TrueTypeProgramTests.A_glyph_with_more_points_than_the_limit_is_dropped` |
| F3 | Charstring operators run for one glyph (subroutine fan-out) | 100,000 (`DefaultMaxCharStringOperators`) | `FontProgramContext.MaxCharStringOperators` | `Type2CharStringTests.Subroutine_fan_out_stops_at_the_operator_budget`; `Type1ProgramTests.Fan_out_through_subroutines_is_bounded`, `.The_operator_budget_is_the_contexts_MaxCharStringOperators` |
| F13 | Mappings in one CMap | 1,000,000 (`CMapContext.DefaultMaxEntries`) | `CMapContext.MaxEntries` | `CMapParserTests.Entries_past_the_limit_are_dropped_with_a_diagnostic` |
| F14 | `usecmap` chain depth (cycles cut separately) | 5 (`DefaultMaxUseCMapDepth`) | `CMapContext.MaxUseCMapDepth` | `CompositeFontTests.UseCMap_chains_deeper_than_five_levels_are_cut_with_a_diagnostic`, `.A_UseCMap_cycle_is_cut_with_a_diagnostic` |

### Fixed limits: file structure

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| C1 | Nesting of arrays and dictionaries | 256 | `CosParser.MaxDepth` | `ContainerParsingTests.Nesting_beyond_the_parser_limit_is_rejected_without_overflowing_the_stack` |
| C2 | Cross-reference entries | no more than the file has bytes | `XrefEntryBudget` | `CrossReferenceStreamTests.A_cross_reference_stream_holds_no_more_entries_than_the_file_has_bytes`, `.Rebuilding_the_cross_reference_indexes_no_more_object_stream_pairs_than_the_budget` |
| C3 | `Prev` chain loops | each section read once | visited set | `FileStructureTests.A_prev_chain_that_loops_ends_with_a_diagnostic` |
| C4 | Loads that need other loads (a stream `Length` in another object, object streams) | 32 deep | `ObjectLoader.MaxDepth` | **`ResourceLimitTests.Loads_that_need_other_loads_more_than_the_limit_deep_read_as_null_with_a_diagnostic`** |
| C5 | Objects whose load needs themselves | cut | cache cycle detection | **`ResourceLimitTests.Streams_whose_lengths_refer_to_each_other_are_read_without_recursing`**; `ObjectStreamTests.An_object_stream_whose_N_is_one_of_its_own_members_resolves_its_members_to_null` |
| C6 | Bytes read to find one object's end | 1 GiB | `ObjectLoader.MaxObjectWindow` | `SourceTests.An_object_larger_than_the_first_read_of_a_stream_is_read_whole` (growth; the cap itself has no focused test) |
| C7 | Hint table entries | 2^20 | `HintTableReader.MaxEntries` | fuzz target `hint-tables` only |
| C8 | Bytes of one hinted object range | 16 MiB | `HintObjectLocator.MaxRangeLength` | fuzz target `hint-tables` only |
| C9 | Streams a filter decodes to decode another (JBIG2Globals, third-party filters) | 8 deep | `StreamDecoder.MaxDepth` | **`ResourceLimitTests.Streams_that_need_other_streams_decoded_more_than_the_limit_deep_decode_to_nothing_with_a_diagnostic`** |
| C10 | First-page fetch of a linearized remote file | 16 MiB | `PdfDocument.MaxFirstPageFetch` | `FirstPageFirstTests.Opening_a_large_linearized_file_and_rendering_page_1_fetches_only_the_first_page_section_and_the_hint_stream` |
| C11 | One remote fetch beyond the missed object | 8 MiB | `HttpRangeSource.MaxExtentFetch` | `RangeRequestSourceTests` (extent behavior; the cap has no focused test) |
| C12 | Repeated misses before a remote source fetches more | 4 | `HttpRangeSource.MaxRepeatedMisses` | `RangeRequestSourceTests` |
| C13 | Object numbers written on save | 8,388,607 | `SavePlan.MaxObjectNumber` | `SparseObjectNumberSaveTests.An_update_of_a_file_that_holds_the_largest_number_is_refused_before_anything_is_written` and siblings |

### Fixed limits: image codecs and filters

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| I1 | Predictor row length | at most the data and L1 | `Predictor` | `PredictorTests` (fuzz finding of issue #48) |
| I2 | CCITT working memory for `Columns` | within L1 | `CcittFaxDecodeFilter.FitsLimit` | `CcittFaxDecodeFilterTests` |
| I3 | JPEG components | 4 per scan, 32 per frame | `JpegScan.MaxComponents`, `JpegFrame.MaxComponents` | `DctRepairTests` |
| I4 | JPEG 2000 tiles | 65,535 | `JpxCodestreamReader.MaxTiles` | **`ResourceLimitTests.A_JPEG_2000_codestream_with_more_tiles_than_the_limit_is_not_decoded`** |
| I5 | JPEG 2000 precincts and code-blocks in one tile; code-block size | 2^21; 4,096 samples, 1,024 a side | `JpxTileDecoder.MaxStructures`, `JpxCodeBlockDecoder.MaxBlockSamples` | no focused test (fuzz target `filter-jpx`) |
| I6 | JBIG2 symbols; Huffman prefix length | 2^20; 32 | `Jbig2SymbolDictionary.MaxSymbols`, `Jbig2HuffmanTable.MaxPrefixLength` | `Jbig2RepairTests` (region and symbol ID limits); fuzz target `filter-jbig2` |
| I7 | PNG embedded ICC profile (images added with `PdfCanvas`) | 16 MiB | `PngReader.MaxProfileLength` | no focused test (fuzz target `png-reader`) |

### Fixed limits: fonts and CMaps

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| F4 | Charstring arguments, stems, subroutine depth | 48, 96, 10 | `CharStringLimits` | `Type2CharStringTests.Too_many_operands_drop_the_glyph_with_a_diagnostic`, `.A_subroutine_that_calls_itself_stops_at_the_nesting_limit`; `Type1ProgramTests.Subroutines_nest_ten_levels_deep_as_the_shared_charstring_limit_allows` |
| F5 | Type 1 `Subrs` and glyphs | 65,536 each | `Type1ProgramReader.MaxSubrs`, `.MaxGlyphs` | no focused test (fuzz target `font-type1`) |
| F6 | TrueType components per glyph; ancestors | 4,096; 256 | `TrueTypeFontProgram.MaxComponentsPerGlyph`, `.MaxAncestorDepth` | no focused test (fuzz target `font-truetype`) |
| F7 | CID-keyed CFF font dictionaries | 256 | `CffFont.MaxFontDicts` | no focused test (fuzz target `font-cff`) |
| F8 | OpenType lookup nesting; context length; mark attachment depth | 6; 64; 64 | `LayoutApplier.MaxNesting`, `.MaxContext`, `MaxAttachmentDepth` | no focused test (fuzz targets `gsub`, `gpos`) |
| F9 | Bytes of a CFF subset charstring budget | 2^20 | `CffSubsetter.Budget` | fuzz target `cff-subset` |
| F15 | Bytes of one ToUnicode destination | 512 | `CMapParser.MaxDestinationBytes` | `ToUnicodeTests` |
| F16 | Code points from one character | 512 | `FontUnicode.MaxLength` | `ToUnicodeTests` |

### Fixed limits: graphics

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| G1 | Colour space nesting (cycles cut separately) | 8 | `ColorSpaceCache.MaxDepth` | **`ResourceLimitTests.Colour_spaces_nested_deeper_than_the_limit_stop_with_a_diagnostic`**; `ColorRepairTests` (cycle) |
| G2 | Colour components | 32 | `PdfColor.MaxComponents` | **`ResourceLimitTests.A_DeviceN_space_with_more_colourants_than_a_colour_holds_is_painted_through_its_alternate`** |
| G3 | Function nesting (stitching; cycles cut separately) | 8 | `FunctionCompilation.MaxDepth` | `StitchingFunctionTests.Stitching_functions_nest_eight_deep_but_not_nine` |
| G4 | Function inputs and outputs | 32 | `FunctionEvaluator.MaxArity` | no focused test (fuzz target `function-sampled`) |
| G5 | Sample values in a sampled function | 2^24 | `SampledCompiler.MaxSampleValues` | `SampledFunctionTests.A_size_that_would_need_too_many_samples_is_rejected_without_allocating_them` |
| G6 | PostScript calculator stack; brace nesting; compiled registers (constants included) | 100; 256; 1,024 | `PostScriptEvaluator.StackLimit`, `PostScriptCompiler.MaxNesting`, `PostScriptLowering.MaxRegisters` | `PostScriptFunctionTests.The_stack_holds_100_entries`, `.Overflowing_the_stack_fails_the_evaluation_with_outputs_of_zero_clipped_to_the_range`, `.Braces_nest_256_deep_including_the_program_but_not_257` (registers: **`PostScriptLoweringTests.A_program_of_more_distinct_literals_than_registers_runs_in_the_interpreter`**; fuzz target `function-type4`) |
| G7 | ICC lookup table channels | 15 | `IccClut.MaxChannels` | fuzz target `icc-profile` |
| G8 | Image components | 32 | `ImageGeometry.MaxComponents` | `ImageFilterFacetTests` |
| G9 | Colour space depth while walking resources | 8 | `DocumentObjectWalker.MaxColorSpaceDepth` | no focused test |

### Fixed limits: document model

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| D1 | Page tree depth (cycles cut separately) | 256 | `PageTreeReader.MaxDepth` | **`ResourceLimitTests.A_page_tree_deeper_than_the_limit_is_cut_with_a_diagnostic`**; `PageTreeTests` (cycle) |
| D2 | Name and number tree depth (cycles cut separately) | 64 | `TreeReader.DefaultMaxDepth` | `NumberTreeTests.A_chain_deeper_than_the_cap_ends_with_a_diagnostic`; `NameTreeTests` (cycle) |
| D3 | Outline depth (cycles cut separately) | 256 | `PdfOutline.MaxDepth` | `OutlineTests.An_outline_deeper_than_the_cap_ends_with_a_diagnostic` |
| D4 | Action tree depth (`Next` cycles cut separately) | 64 | `PdfAction.DefaultMaxTreeDepth` | `TriggerEventTests.The_action_tree_is_walked_depth_first_in_execution_order`, `.A_cycle_of_next_actions_is_cut_with_information_and_a_shared_action_is_yielded_once` |
| D5 | Field tree depth (cycles cut separately) | 256 | `FieldTree.MaxDepth` | `MalformedFormTests.A_field_tree_nested_deeper_than_the_limit_is_cut_with_a_diagnostic` |
| D6 | Structure tree depth; role map steps; parent tree array | 256; 32; 1,000,000 | `StructureContext.MaxDepth`, `.MaxRoleSteps`, `.MaxParentTreeArray` | `StructureRepairTests.A_hierarchy_deeper_than_the_limit_is_cut_with_a_diagnostic` (role map and parent tree: no focused test) |
| D7 | Optional content visibility expression depth and nodes; order depth | 32 and 4,096; 32 | `PdfOptionalContentProperties.MaxExpressionDepth`, `.MaxExpressionNodes`, `PdfOptionalContentConfiguration.MaxOrderDepth` | no focused test (fuzz target `optional-content`) |
| D8 | Embedded go-to target chain | 64 | `PdfEmbeddedTarget.MaxDepth` | `MalformedActionTests.A_cycle_of_embedded_target_dictionaries_is_refused_with_a_warning` (depth: no focused test) |
| D9 | XMP packet characters; property nesting | 16 Mi; 64 | `XmpPacketReader.MaxCharacters`, `.MaxDepth` | `XmpReaderOracleTests.Deep_nesting_reads_as_the_old_reader_read_it` (size: no focused test; fuzz targets `xmp`, `xmp-xml`) |
| D10 | Collection folders (cycles) | cut | visited set | `CollectionTests.A_folder_cycle_and_a_duplicate_id_are_cut_with_diagnostics` |
| D11 | Page label numerals | 64 characters | `PdfPageLabelRange.MaxNumeralLength` | no focused test |
| D12 | Objects copied between documents | iterative, no recursion | `DocumentAssembly` | `ObjectCopyTests.Arrays_nested_a_hundred_thousand_deep_copy_without_recursion` |

### Fixed limits: security handlers, signatures and host access

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| S1 | Password length (revisions 2 to 4; 5 and 6) | 32 bytes; 127 bytes | `PasswordEncoding.MaxLegacyLength`, `.MaxUnicodeLength` | `PasswordTests` |
| S2 | Public-key recipients | each tried once, CMS decoded by the managed reader | `PublicKeySecurityHandler` | `PublicKeySecurityTests`; fuzz target `public-key` |
| S3 | Certificate path length | 16 | `CertificatePath.MaxLength` | no focused test (fuzz target `signature-validation`) |
| S4 | Revocation requests per validation, schemes | 32, `http` and `https` only | `RevocationFetcher.MaxRequests` | `RevocationValidationTests` (fetching; the count cap has no focused test) |
| S5 | OCSP and CRL response size | 1 MiB; 16 MiB | `RevocationFetcher.MaxOcspLength`, `.MaxCrlLength` | no focused test |
| S6 | Time-stamp response size | 1 MiB | `PdfTimestampClient.MaximumResponseLength` | no focused test (fuzz target `timestamp-response`) |
| S7 | Byte range, digest, signature value and signing certificate checks | every signature | `SignatureValidator` | `SignatureValidationTests`, `SignatureIntegrityTests`, `TimestampValidationTests` |
| S8 | System font file size read | 256 MiB | `SystemFontResolver.MaxFileLength` | no focused test |
| S9 | System font index: files, directories, faces, index size | 100,000; 1,024; 256; 64 MiB | `SystemFontIndexFile`, `FontFileListing.MaxFiles`, `FontFaceScanner.MaxFaces` | fuzz targets `font-index`, `font-scanner` |

### Fixed limits: software rasterizer (`Broadside.Rendering`)

| # | Limit | Value | Constant | Test |
|---|---|---|---|---|
| R1 | Bitmap size | what one .NET array holds; refused before drawing | `RenderOptions` | **`RenderSizeLimitTests.A_page_too_large_for_a_bitmap_is_refused_before_anything_is_drawn_and_its_size_can_be_checked_first`** |
| R2 | Dashes per stroke; curve segments; coordinates | 2^20; 1,000; 2^40 device, 1e100 user | `Stroker.MaxDashes`, `.MaxSegments`, `.CoordinateLimit`, `.UserLimit` | `StrokeRenderingTests.Extreme_widths_dash_patterns_coordinates_and_transforms_render_without_failing`; `PageRenderingTests.Shapes_far_beyond_the_page_are_clipped_without_losing_what_they_cover` |
| R3 | Tiling pattern tiles; pixels of one cell | 2^22; 2^26 | `SoftwareRasterizer.MaxTiles`, `.MaxCellPixels` | `TilingRenderingTests` (cap: no focused test) |
| R4 | Coons and tensor patch subdivision; triangles | 64 steps; 2^21 | `MaxPatchSteps`, `MaxPatchTriangles` | `PatchShadingRenderingTests.Subdivision_adapts_to_the_scale_and_300_dpi_cuts_at_most_the_stated_factor_of_the_72_dpi_triangles` |
| R5 | Function-based shading cells; mesh subdivision | 256; 5 | `MaxFunctionCells`, `MaxSubdivision` | `ShadingRenderingTests` |
| R6 | Glyph mask size (larger glyphs fill as paths); glyph origin | 512 pixels; 2^30 | `MaxMaskSize`, `MaxOrigin` | `GlyphCacheTests` |
| R7 | Glyph cache of one rasterizer | 4 MiB | `SoftwareRasterizer.DefaultGlyphCacheBudget` (option: `PdfOptions.WithGlyphCacheBudget` for the shared cache) | `RenderCacheRenderingTests` |
| R8 | Image resampling taps | 16 | `ImagePainter.MaxTaps` | `ImageRenderingTests` |
| R9 | ICC transform cache | 64 transforms | `IccColorManagement.CacheCapacity` | `IccConversionTests` (cache: no focused test) |

### Native backends

| # | Limit | Value | Test |
|---|---|---|---|
| N1 | Each backend mirrors the rasterizer's guards before calling platform code: dash count (2^20), curve segments (1,000), device coordinates, tiling pattern depth (8), cell copies (256) and tile pixels (2^24) and count (2^20), group raster pixels (2^26), mesh raster pixels (2^22), shading textures (256 a side) and triangles (2^20 or 2^21), cached shadings (256, 64 MiB), image reduction pixels (2^22) and enlargement (256), typeface and glyph caches | per backend constants (`*Backend.Patterns.cs`, `*Backend.Groups.cs`, `ShadingBuilder`, `ShadingCache`, `ImageReducer`, `MeshRasterizer`) | Each backend's test project renders the stroke and tiling extremes of the software rasterizer's tests; the caps themselves have no focused tests |

## Limits without a focused test

Every row above that says "no focused test" is protected by fuzzing of its parser but has no test that proves the limit by
reaching it. They are fixed internal constants, each guarding a structure that only a crafted file reaches, and are tracked for
focused tests after v1: C6, C7, C8, C11, I5, I7, F5–F9, G4, G7, G9, D6 (role map, parent tree), D7, D8 (depth), D9 (size), D11,
S3–S6, S8, S9, R3 (cap), R9 (cache) and N1.
