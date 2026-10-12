# Phase 4 tickets (Write and Edit)

Phase parent [#87](https://github.com/michaelstonis/Broadside/issues/87), under the spec [#33](https://github.com/michaelstonis/Broadside/issues/33). Tickets are vertical tracer-bullet slices with native *blocked by* relationships on GitHub, which is the source of truth for status and edges; this table is a snapshot for orientation. Blockers listed here are tickets in Phases 3 to 6 only; Phase 1 and 2 work they build on is named in each issue body.

| Issue | Title | Track | Blocked by |
|---|---|---|---|
| [#141](https://github.com/michaelstonis/Broadside/issues/141) | Version raising and strict-mode refusal | 4A-canvas | none |
| [#142](https://github.com/michaelstonis/Broadside/issues/142) | Filter encoders: Flate, predictors and ASCII | 4A-canvas | none |
| [#143](https://github.com/michaelstonis/Broadside/issues/143) | DCT baseline encoder | 4A-canvas | [#142](https://github.com/michaelstonis/Broadside/issues/142) |
| [#144](https://github.com/michaelstonis/Broadside/issues/144) | Full rewrite with renumbering and garbage collection | 4A-canvas | none |
| [#145](https://github.com/michaelstonis/Broadside/issues/145) | Incremental-update save | 4A-canvas | none |
| [#146](https://github.com/michaelstonis/Broadside/issues/146) | Name-tree and number-tree writing | 4A-canvas | none |
| [#147](https://github.com/michaelstonis/Broadside/issues/147) | Content stream rewriting | 4A-canvas | none |
| [#148](https://github.com/michaelstonis/Broadside/issues/148) | Canvas: paths, colour and graphics state | 4A-canvas | [#141](https://github.com/michaelstonis/Broadside/issues/141), [#142](https://github.com/michaelstonis/Broadside/issues/142) |
| [#149](https://github.com/michaelstonis/Broadside/issues/149) | Canvas text with Standard 14 fonts | 4A-canvas | [#148](https://github.com/michaelstonis/Broadside/issues/148) |
| [#150](https://github.com/michaelstonis/Broadside/issues/150) | TrueType embedding with subsetting | 4A-canvas | [#149](https://github.com/michaelstonis/Broadside/issues/149) |
| [#151](https://github.com/michaelstonis/Broadside/issues/151) | CFF and OpenType-CFF subsetting | 4A-canvas | [#150](https://github.com/michaelstonis/Broadside/issues/150) |
| [#152](https://github.com/michaelstonis/Broadside/issues/152) | Type 1 to CFF conversion | 4A-canvas | [#151](https://github.com/michaelstonis/Broadside/issues/151) |
| [#153](https://github.com/michaelstonis/Broadside/issues/153) | Images from PNG, JPEG and raw buffers | 4A-canvas | [#143](https://github.com/michaelstonis/Broadside/issues/143), [#148](https://github.com/michaelstonis/Broadside/issues/148) |
| [#154](https://github.com/michaelstonis/Broadside/issues/154) | Form XObjects, transparency groups and soft masks | 4A-canvas | [#148](https://github.com/michaelstonis/Broadside/issues/148) |
| [#155](https://github.com/michaelstonis/Broadside/issues/155) | Shadings and patterns | 4A-canvas | [#148](https://github.com/michaelstonis/Broadside/issues/148) |
| [#156](https://github.com/michaelstonis/Broadside/issues/156) | Structure tree writing | 4A-canvas | [#146](https://github.com/michaelstonis/Broadside/issues/146) |
| [#157](https://github.com/michaelstonis/Broadside/issues/157) | Marked content, tags and optional content on the canvas | 4A-canvas | [#148](https://github.com/michaelstonis/Broadside/issues/148), [#156](https://github.com/michaelstonis/Broadside/issues/156) |
| [#158](https://github.com/michaelstonis/Broadside/issues/158) | Insert, remove, reorder and rotate pages | 4B-edit | [#144](https://github.com/michaelstonis/Broadside/issues/144) |
| [#159](https://github.com/michaelstonis/Broadside/issues/159) | Cross-document object copy with reference remapping | 4B-edit | none |
| [#160](https://github.com/michaelstonis/Broadside/issues/160) | Merge and split documents | 4B-edit | [#146](https://github.com/michaelstonis/Broadside/issues/146), [#158](https://github.com/michaelstonis/Broadside/issues/158), [#159](https://github.com/michaelstonis/Broadside/issues/159) |
| [#161](https://github.com/michaelstonis/Broadside/issues/161) | Tagged pages keep their structure through copy, merge and split | 4B-edit | [#156](https://github.com/michaelstonis/Broadside/issues/156), [#160](https://github.com/michaelstonis/Broadside/issues/160) |
| [#162](https://github.com/michaelstonis/Broadside/issues/162) | Metadata writing | 4B-edit | none |
| [#163](https://github.com/michaelstonis/Broadside/issues/163) | Attachments and associated files | 4B-edit | [#146](https://github.com/michaelstonis/Broadside/issues/146) |
| [#164](https://github.com/michaelstonis/Broadside/issues/164) | Outlines, destinations and page labels | 4B-edit | [#146](https://github.com/michaelstonis/Broadside/issues/146) |
| [#165](https://github.com/michaelstonis/Broadside/issues/165) | Add, edit and remove annotations | 4B-edit | [#149](https://github.com/michaelstonis/Broadside/issues/149) |
| [#166](https://github.com/michaelstonis/Broadside/issues/166) | Default appearance tokenizer | 4C-forms | none |
| [#167](https://github.com/michaelstonis/Broadside/issues/167) | Fill text fields and regenerate their appearances | 4C-forms | [#150](https://github.com/michaelstonis/Broadside/issues/150), [#166](https://github.com/michaelstonis/Broadside/issues/166) |
| [#168](https://github.com/michaelstonis/Broadside/issues/168) | Fill check boxes, radio buttons and choice fields | 4C-forms | [#167](https://github.com/michaelstonis/Broadside/issues/167) |
| [#169](https://github.com/michaelstonis/Broadside/issues/169) | Create form fields | 4C-forms | [#168](https://github.com/michaelstonis/Broadside/issues/168) |
| [#170](https://github.com/michaelstonis/Broadside/issues/170) | Flatten forms and annotations | 4C-forms | [#167](https://github.com/michaelstonis/Broadside/issues/167) |
| [#171](https://github.com/michaelstonis/Broadside/issues/171) | Layout package: paragraphs, line breaking and page breaking | 4D-flow-layout | [#149](https://github.com/michaelstonis/Broadside/issues/149) |
| [#172](https://github.com/michaelstonis/Broadside/issues/172) | Unicode line breaking (UAX #14) | 4D-flow-layout | [#171](https://github.com/michaelstonis/Broadside/issues/171) |
| [#173](https://github.com/michaelstonis/Broadside/issues/173) | Bidirectional text (UAX #9) | 4D-flow-layout | [#172](https://github.com/michaelstonis/Broadside/issues/172) |
| [#174](https://github.com/michaelstonis/Broadside/issues/174) | OpenType substitution (GSUB) | 4D-flow-layout | [#150](https://github.com/michaelstonis/Broadside/issues/150), [#171](https://github.com/michaelstonis/Broadside/issues/171) |
| [#175](https://github.com/michaelstonis/Broadside/issues/175) | OpenType positioning (GPOS) | 4D-flow-layout | [#174](https://github.com/michaelstonis/Broadside/issues/174) |
| [#176](https://github.com/michaelstonis/Broadside/issues/176) | Inline styling, lists and images in flow | 4D-flow-layout | [#153](https://github.com/michaelstonis/Broadside/issues/153), [#171](https://github.com/michaelstonis/Broadside/issues/171) |
| [#177](https://github.com/michaelstonis/Broadside/issues/177) | Tables with spans and page breaks | 4D-flow-layout | [#171](https://github.com/michaelstonis/Broadside/issues/171) |
| [#178](https://github.com/michaelstonis/Broadside/issues/178) | Page templates, headers and footers | 4D-flow-layout | [#171](https://github.com/michaelstonis/Broadside/issues/171) |
| [#179](https://github.com/michaelstonis/Broadside/issues/179) | Tagging by construction | 4D-flow-layout | [#157](https://github.com/michaelstonis/Broadside/issues/157), [#176](https://github.com/michaelstonis/Broadside/issues/176), [#177](https://github.com/michaelstonis/Broadside/issues/177), [#178](https://github.com/michaelstonis/Broadside/issues/178) |
| [#180](https://github.com/michaelstonis/Broadside/issues/180) | Encrypted save with the standard security handler | 4E-encryption-write | none |
| [#181](https://github.com/michaelstonis/Broadside/issues/181) | AES-GCM, integrity protection and encrypted incremental save | 4E-encryption-write | [#145](https://github.com/michaelstonis/Broadside/issues/145), [#180](https://github.com/michaelstonis/Broadside/issues/180) |
| [#182](https://github.com/michaelstonis/Broadside/issues/182) | Public-key security handler write | 4E-encryption-write | [#180](https://github.com/michaelstonis/Broadside/issues/180) |
| [#228](https://github.com/michaelstonis/Broadside/issues/228) | Linearized writing | 4B-edit | [#144](https://github.com/michaelstonis/Broadside/issues/144) |
| [#230](https://github.com/michaelstonis/Broadside/issues/230) | Shaping for Indic, Southeast Asian and other complex scripts (earmarked) | 4D-flow-layout | [#174](https://github.com/michaelstonis/Broadside/issues/174), [#175](https://github.com/michaelstonis/Broadside/issues/175) |
