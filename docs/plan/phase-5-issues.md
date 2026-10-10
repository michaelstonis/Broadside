# Phase 5 tickets (Cross-cutting)

Phase parent [#88](https://github.com/michaelstonis/Broadside/issues/88), under the spec [#33](https://github.com/michaelstonis/Broadside/issues/33). Tickets are vertical tracer-bullet slices with native *blocked by* relationships on GitHub, which is the source of truth for status and edges; this table is a snapshot for orientation. Blockers listed here are tickets in Phases 3 to 6 only; Phase 1 and 2 work they build on is named in each issue body.

PDF/A conformance work cites veraPDF validation-profile rule IDs (ISO 19005 is not purchased).

| Issue | Title | Track | Blocked by |
|---|---|---|---|
| [#183](https://github.com/michaelstonis/Broadside/issues/183) | Positioned text extraction | 5A-text | none |
| [#184](https://github.com/michaelstonis/Broadside/issues/184) | Words and lines | 5A-text | [#183](https://github.com/michaelstonis/Broadside/issues/183) |
| [#185](https://github.com/michaelstonis/Broadside/issues/185) | Reading order from the structure tree | 5A-text | [#184](https://github.com/michaelstonis/Broadside/issues/184) |
| [#186](https://github.com/michaelstonis/Broadside/issues/186) | Layout tree type, produced from the structure tree | 5B-layout-tree | [#185](https://github.com/michaelstonis/Broadside/issues/185) |
| [#187](https://github.com/michaelstonis/Broadside/issues/187) | Detecting blocks, columns, paragraphs and reading order | 5B-layout-tree | [#186](https://github.com/michaelstonis/Broadside/issues/186) |
| [#188](https://github.com/michaelstonis/Broadside/issues/188) | Detecting headings and lists | 5B-layout-tree | [#187](https://github.com/michaelstonis/Broadside/issues/187) |
| [#189](https://github.com/michaelstonis/Broadside/issues/189) | Detecting tables with cell spans | 5B-layout-tree | [#187](https://github.com/michaelstonis/Broadside/issues/187) |
| [#190](https://github.com/michaelstonis/Broadside/issues/190) | Detecting figures and captions; headless and Type 3 text | 5B-layout-tree | [#187](https://github.com/michaelstonis/Broadside/issues/187) |
| [#191](https://github.com/michaelstonis/Broadside/issues/191) | Auto-tagging | 5B-layout-tree | [#147](https://github.com/michaelstonis/Broadside/issues/147), [#156](https://github.com/michaelstonis/Broadside/issues/156), [#188](https://github.com/michaelstonis/Broadside/issues/188), [#189](https://github.com/michaelstonis/Broadside/issues/189), [#190](https://github.com/michaelstonis/Broadside/issues/190) |
| [#192](https://github.com/michaelstonis/Broadside/issues/192) | Managed SHA-3 | 5C-signatures | none |
| [#193](https://github.com/michaelstonis/Broadside/issues/193) | Byte-range signing with CMS | 5C-signatures | [#145](https://github.com/michaelstonis/Broadside/issues/145) |
| [#194](https://github.com/michaelstonis/Broadside/issues/194) | PAdES baseline signatures with ECDSA, RSA-PSS and SHA-3 | 5C-signatures | [#192](https://github.com/michaelstonis/Broadside/issues/192), [#193](https://github.com/michaelstonis/Broadside/issues/193) |
| [#195](https://github.com/michaelstonis/Broadside/issues/195) | RFC 3161 timestamps | 5C-signatures | [#193](https://github.com/michaelstonis/Broadside/issues/193) |
| [#196](https://github.com/michaelstonis/Broadside/issues/196) | Signature validation: integrity and certificate chains | 5C-signatures | [#193](https://github.com/michaelstonis/Broadside/issues/193) |
| [#197](https://github.com/michaelstonis/Broadside/issues/197) | Revocation and timestamp validation | 5C-signatures | [#195](https://github.com/michaelstonis/Broadside/issues/195), [#196](https://github.com/michaelstonis/Broadside/issues/196) |
| [#198](https://github.com/michaelstonis/Broadside/issues/198) | Long-term validation: DSS and LTV | 5C-signatures | [#197](https://github.com/michaelstonis/Broadside/issues/197) |
| [#199](https://github.com/michaelstonis/Broadside/issues/199) | Certification, field locks and modification detection | 5C-signatures | [#196](https://github.com/michaelstonis/Broadside/issues/196) |
| [#200](https://github.com/michaelstonis/Broadside/issues/200) | Visible signature appearances | 5C-signatures | [#149](https://github.com/michaelstonis/Broadside/issues/149), [#153](https://github.com/michaelstonis/Broadside/issues/153), [#193](https://github.com/michaelstonis/Broadside/issues/193) |
| [#201](https://github.com/michaelstonis/Broadside/issues/201) | veraPDF in CI and PDF/A-2b output | 5D-subset-standards | [#150](https://github.com/michaelstonis/Broadside/issues/150), [#162](https://github.com/michaelstonis/Broadside/issues/162) |
| [#202](https://github.com/michaelstonis/Broadside/issues/202) | PDF/A-2u and PDF/A-3b | 5D-subset-standards | [#163](https://github.com/michaelstonis/Broadside/issues/163), [#201](https://github.com/michaelstonis/Broadside/issues/201) |
| [#203](https://github.com/michaelstonis/Broadside/issues/203) | PDF/A-4 and PDF/A-4f | 5D-subset-standards | [#202](https://github.com/michaelstonis/Broadside/issues/202) |
| [#204](https://github.com/michaelstonis/Broadside/issues/204) | PDF/UA-1 output from flow layout | 5D-subset-standards | [#179](https://github.com/michaelstonis/Broadside/issues/179), [#201](https://github.com/michaelstonis/Broadside/issues/201) |
| [#205](https://github.com/michaelstonis/Broadside/issues/205) | PDF/UA-2 and Well-Tagged PDF | 5D-subset-standards | [#204](https://github.com/michaelstonis/Broadside/issues/204) |
| [#206](https://github.com/michaelstonis/Broadside/issues/206) | Auto-tagged documents checked against PDF/UA-1 | 5D-subset-standards | [#191](https://github.com/michaelstonis/Broadside/issues/191), [#204](https://github.com/michaelstonis/Broadside/issues/204) |
| [#207](https://github.com/michaelstonis/Broadside/issues/207) | Range-request source | 5E-streaming | none |
| [#208](https://github.com/michaelstonis/Broadside/issues/208) | First page first with linearization hints | 5E-streaming | [#207](https://github.com/michaelstonis/Broadside/issues/207) |
| [#229](https://github.com/michaelstonis/Broadside/issues/229) | Convert existing documents to PDF/A | 5D-subset-standards | [#201](https://github.com/michaelstonis/Broadside/issues/201), [#202](https://github.com/michaelstonis/Broadside/issues/202), [#203](https://github.com/michaelstonis/Broadside/issues/203), [#119](https://github.com/michaelstonis/Broadside/issues/119), [#150](https://github.com/michaelstonis/Broadside/issues/150) |
