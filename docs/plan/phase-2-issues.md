# Phase 2 tickets (Model and codecs)

Milestone "Phase 2: Model and codecs". Tickets are vertical tracer-bullet slices of the spec [#33](https://github.com/michaelstonis/Broadside/issues/33): each one is demoable through the public document API against corpus files, which is the spec's primary test seam. GitHub is the source of truth for status and blocking edges (native sub-issues of #33 and native *blocked by* relationships); this table is a snapshot for orientation. Each issue body holds the acceptance criteria.

Four tracks run in parallel once the real-world corpus gate (#47) closes: 2A fonts, 2B images, 2C document model, 2D content interpreter. Phase 3 to 6 tickets are cut with a separate to-tickets pass once Phase 2 is underway, because their slices depend on the API shapes Phases 1 and 2 produce.

| Issue | Title | Track | Blocked by |
|---|---|---|---|
| [#49](https://github.com/michaelstonis/Broadside/issues/49) | Standard 14 metrics and simple-font encodings | 2A-fonts | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#50](https://github.com/michaelstonis/Broadside/issues/50) | Embedded TrueType glyph outlines | 2A-fonts | [#49](https://github.com/michaelstonis/Broadside/issues/49) |
| [#51](https://github.com/michaelstonis/Broadside/issues/51) | CFF and OpenType-CFF glyph outlines | 2A-fonts | [#50](https://github.com/michaelstonis/Broadside/issues/50) |
| [#52](https://github.com/michaelstonis/Broadside/issues/52) | Type 1 glyph outlines | 2A-fonts | [#50](https://github.com/michaelstonis/Broadside/issues/50) |
| [#53](https://github.com/michaelstonis/Broadside/issues/53) | Composite fonts with CIDFontType2 and CMaps | 2A-fonts | [#50](https://github.com/michaelstonis/Broadside/issues/50) |
| [#54](https://github.com/michaelstonis/Broadside/issues/54) | CID-keyed CFF and predefined CMaps package | 2A-fonts | [#51](https://github.com/michaelstonis/Broadside/issues/51), [#53](https://github.com/michaelstonis/Broadside/issues/53) |
| [#55](https://github.com/michaelstonis/Broadside/issues/55) | Content stream interpretation: paths, transforms and clipping | 2D-content | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#56](https://github.com/michaelstonis/Broadside/issues/56) | Text, form XObjects, inline images and graphics-state parameters | 2D-content | [#49](https://github.com/michaelstonis/Broadside/issues/49), [#55](https://github.com/michaelstonis/Broadside/issues/55) |
| [#57](https://github.com/michaelstonis/Broadside/issues/57) | Type 3 fonts | 2A-fonts | [#49](https://github.com/michaelstonis/Broadside/issues/49), [#56](https://github.com/michaelstonis/Broadside/issues/56) |
| [#58](https://github.com/michaelstonis/Broadside/issues/58) | Character codes to Unicode for every font type | 2A-fonts | [#54](https://github.com/michaelstonis/Broadside/issues/54) |
| [#59](https://github.com/michaelstonis/Broadside/issues/59) | Standard 14 glyph package and the font resolver | 2A-fonts | [#50](https://github.com/michaelstonis/Broadside/issues/50) |
| [#60](https://github.com/michaelstonis/Broadside/issues/60) | Image XObjects, masks and inline images | 2B-images | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#61](https://github.com/michaelstonis/Broadside/issues/61) | DCT baseline decoding | 2B-images | [#60](https://github.com/michaelstonis/Broadside/issues/60) |
| [#62](https://github.com/michaelstonis/Broadside/issues/62) | DCT progressive, CMYK and YCCK | 2B-images | [#61](https://github.com/michaelstonis/Broadside/issues/61) |
| [#63](https://github.com/michaelstonis/Broadside/issues/63) | CCITT Group 3 and Group 4 decoding | 2B-images | [#60](https://github.com/michaelstonis/Broadside/issues/60) |
| [#64](https://github.com/michaelstonis/Broadside/issues/64) | JBIG2 generic regions and MMR | 2B-images | [#63](https://github.com/michaelstonis/Broadside/issues/63) |
| [#65](https://github.com/michaelstonis/Broadside/issues/65) | JBIG2 symbol, text, refinement and halftone regions | 2B-images | [#64](https://github.com/michaelstonis/Broadside/issues/64) |
| [#67](https://github.com/michaelstonis/Broadside/issues/67) | JPEG 2000 codestream, single tile, reversible 5/3 | 2B-images | [#60](https://github.com/michaelstonis/Broadside/issues/60) |
| [#68](https://github.com/michaelstonis/Broadside/issues/68) | JPEG 2000 full Part 1 decoding | 2B-images | [#67](https://github.com/michaelstonis/Broadside/issues/67) |
| [#69](https://github.com/michaelstonis/Broadside/issues/69) | Catalog essentials: page labels, viewer preferences, Info and XMP | 2C-document-model | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#70](https://github.com/michaelstonis/Broadside/issues/70) | Name trees, number trees, destinations and outlines | 2C-document-model | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#71](https://github.com/michaelstonis/Broadside/issues/71) | Annotations, every subtype | 2C-document-model | [#70](https://github.com/michaelstonis/Broadside/issues/70) |
| [#73](https://github.com/michaelstonis/Broadside/issues/73) | Actions, with parse-and-preserve for scripts and multimedia | 2C-document-model | [#70](https://github.com/michaelstonis/Broadside/issues/70) |
| [#74](https://github.com/michaelstonis/Broadside/issues/74) | Interactive forms: reading fields | 2C-document-model | [#71](https://github.com/michaelstonis/Broadside/issues/71) |
| [#75](https://github.com/michaelstonis/Broadside/issues/75) | Structure tree and marked content | 2C-document-model | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#76](https://github.com/michaelstonis/Broadside/issues/76) | Optional content, embedded and associated files, metadata locations, declarations | 2C-document-model | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
| [#77](https://github.com/michaelstonis/Broadside/issues/77) | Color spaces and the color-management extension point | 2D-content | [#55](https://github.com/michaelstonis/Broadside/issues/55) |
| [#78](https://github.com/michaelstonis/Broadside/issues/78) | Functions: sampled, exponential, stitching and PostScript calculator | 2D-content | [#55](https://github.com/michaelstonis/Broadside/issues/55) |
| [#79](https://github.com/michaelstonis/Broadside/issues/79) | Shadings and patterns as resolved models | 2D-content | [#77](https://github.com/michaelstonis/Broadside/issues/77), [#78](https://github.com/michaelstonis/Broadside/issues/78) |
| [#80](https://github.com/michaelstonis/Broadside/issues/80) | Content interpreter corpus gate | 2D-content | [#56](https://github.com/michaelstonis/Broadside/issues/56), [#60](https://github.com/michaelstonis/Broadside/issues/60), [#79](https://github.com/michaelstonis/Broadside/issues/79) |
