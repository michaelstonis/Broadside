# Phase 7 tickets (Hardening and v1 release)

Phase parent [#231](https://github.com/michaelstonis/Broadside/issues/231), under the spec [#33](https://github.com/michaelstonis/Broadside/issues/33). Tickets are vertical tracer-bullet slices with native *blocked by* relationships on GitHub, which is the source of truth for status and edges; this table is a snapshot for orientation.

Owner decisions that shape this phase:

- v1.0 is the library and the rendering backends. The viewer packages (phase parent [#89](https://github.com/michaelstonis/Broadside/issues/89)) ship as previews on their own track, and the v1.0 gate does not wait on them.
- The first public NuGet preview ships once Phase 3 renders real-world pages (gated on [#138](https://github.com/michaelstonis/Broadside/issues/138)).
- 1.x support: fixes land in the latest minor; security fixes are backported to the previous minor for 6 months; Semantic Versioning, no breaking changes within 1.x.

Tracks: **7A** coverage gaps from the spec coverage audit, **7B** hardening deferred from the Phase 1–2 review, **7C** release engineering.

| Issue | Title | Track | Blocked by |
|---|---|---|---|
| [#233](https://github.com/michaelstonis/Broadside/issues/233) | Software rendering is bit-identical on every platform | 7A-coverage | [#120](https://github.com/michaelstonis/Broadside/issues/120) |
| [#234](https://github.com/michaelstonis/Broadside/issues/234) | Unencrypted wrapper documents: reading | 7A-coverage | — |
| [#235](https://github.com/michaelstonis/Broadside/issues/235) | Unencrypted wrapper documents: writing | 7A-coverage | [#234](https://github.com/michaelstonis/Broadside/issues/234), [#163](https://github.com/michaelstonis/Broadside/issues/163), [#180](https://github.com/michaelstonis/Broadside/issues/180) |
| [#236](https://github.com/michaelstonis/Broadside/issues/236) | Order 3 sampled functions | 7A-coverage | — |
| [#237](https://github.com/michaelstonis/Broadside/issues/237) | Multi-gigabyte files keep memory bounded on open and save | 7A-coverage | — |
| [#238](https://github.com/michaelstonis/Broadside/issues/238) | Remaining silent file-structure repairs record diagnostics | 7A-coverage | — |
| [#239](https://github.com/michaelstonis/Broadside/issues/239) | Every option and diagnostic reaches the hosted engine | 7A-coverage | [#95](https://github.com/michaelstonis/Broadside/issues/95) |
| [#240](https://github.com/michaelstonis/Broadside/issues/240) | Clause citations are checked, not trusted | 7A-coverage | — |
| [#241](https://github.com/michaelstonis/Broadside/issues/241) | Conformance map closure for v1 | 7A-coverage | [#240](https://github.com/michaelstonis/Broadside/issues/240), [#86](https://github.com/michaelstonis/Broadside/issues/86), [#87](https://github.com/michaelstonis/Broadside/issues/87), [#88](https://github.com/michaelstonis/Broadside/issues/88) |
| [#242](https://github.com/michaelstonis/Broadside/issues/242) | Diagnostic codes get a public, typed catalogue (expand) | 7B-hardening | — |
| [#243](https://github.com/michaelstonis/Broadside/issues/243) | Diagnostic codes migrate: file structure, objects, security and catalog | 7B-hardening | [#242](https://github.com/michaelstonis/Broadside/issues/242) |
| [#244](https://github.com/michaelstonis/Broadside/issues/244) | Diagnostic codes migrate: filters and images | 7B-hardening | [#242](https://github.com/michaelstonis/Broadside/issues/242) |
| [#245](https://github.com/michaelstonis/Broadside/issues/245) | Diagnostic codes migrate: fonts, CMaps and text | 7B-hardening | [#242](https://github.com/michaelstonis/Broadside/issues/242) |
| [#246](https://github.com/michaelstonis/Broadside/issues/246) | Diagnostic codes migrate: content, colour and the document model | 7B-hardening | [#242](https://github.com/michaelstonis/Broadside/issues/242) |
| [#247](https://github.com/michaelstonis/Broadside/issues/247) | The internal diagnostic catalogue is retired (contract) | 7B-hardening | [#243](https://github.com/michaelstonis/Broadside/issues/243), [#244](https://github.com/michaelstonis/Broadside/issues/244), [#245](https://github.com/michaelstonis/Broadside/issues/245), [#246](https://github.com/michaelstonis/Broadside/issues/246) |
| [#248](https://github.com/michaelstonis/Broadside/issues/248) | Extension-point contexts report typed codes | 7B-hardening | [#242](https://github.com/michaelstonis/Broadside/issues/242) |
| [#249](https://github.com/michaelstonis/Broadside/issues/249) | Associated files are read through their owners | 7B-hardening | — |
| [#250](https://github.com/michaelstonis/Broadside/issues/250) | Async entry points mirror their synchronous overloads | 7B-hardening | — |
| [#251](https://github.com/michaelstonis/Broadside/issues/251) | Image API tidy before the freeze | 7B-hardening | — |
| [#252](https://github.com/michaelstonis/Broadside/issues/252) | The DCT decoder is split by responsibility | 7B-hardening | — |
| [#253](https://github.com/michaelstonis/Broadside/issues/253) | Annotation subtypes own their entries (expand) | 7B-hardening | — |
| [#254](https://github.com/michaelstonis/Broadside/issues/254) | Annotation subtypes own their entries (contract) | 7B-hardening | [#253](https://github.com/michaelstonis/Broadside/issues/253) |
| [#255](https://github.com/michaelstonis/Broadside/issues/255) | JPEG 2000 tier-1 decoding speed | 7B-hardening | — |
| [#256](https://github.com/michaelstonis/Broadside/issues/256) | Predefined CMaps and UCS2 tables cached per engine | 7B-hardening | — |
| [#257](https://github.com/michaelstonis/Broadside/issues/257) | Graphics-state copies are measured and made cheap | 7B-hardening | — |
| [#258](https://github.com/michaelstonis/Broadside/issues/258) | A linearized file with a missing page tree | 7B-hardening | — |
| [#259](https://github.com/michaelstonis/Broadside/issues/259) | JPEG 2000 fragment-table reassembly | 7B-hardening | — |
| [#261](https://github.com/michaelstonis/Broadside/issues/261) | API review checklist and automated API guards | 7C-release | — |
| [#262](https://github.com/michaelstonis/Broadside/issues/262) | Public API freeze for v1 | 7C-release | [#247](https://github.com/michaelstonis/Broadside/issues/247), [#248](https://github.com/michaelstonis/Broadside/issues/248), [#249](https://github.com/michaelstonis/Broadside/issues/249), [#250](https://github.com/michaelstonis/Broadside/issues/250), [#251](https://github.com/michaelstonis/Broadside/issues/251), [#254](https://github.com/michaelstonis/Broadside/issues/254), [#261](https://github.com/michaelstonis/Broadside/issues/261), [#86](https://github.com/michaelstonis/Broadside/issues/86), [#87](https://github.com/michaelstonis/Broadside/issues/87), [#88](https://github.com/michaelstonis/Broadside/issues/88) |
| [#263](https://github.com/michaelstonis/Broadside/issues/263) | Published diagnostic-code catalogue with stability guarantees | 7C-release | [#247](https://github.com/michaelstonis/Broadside/issues/247), [#248](https://github.com/michaelstonis/Broadside/issues/248) |
| [#264](https://github.com/michaelstonis/Broadside/issues/264) | Versioning, release notes and commit hygiene | 7C-release | — |
| [#265](https://github.com/michaelstonis/Broadside/issues/265) | Package validation and supply-chain metadata | 7C-release | — |
| [#266](https://github.com/michaelstonis/Broadside/issues/266) | Trusted publishing and the first preview release | 7C-release | [#264](https://github.com/michaelstonis/Broadside/issues/264), [#265](https://github.com/michaelstonis/Broadside/issues/265), [#138](https://github.com/michaelstonis/Broadside/issues/138) |
| [#267](https://github.com/michaelstonis/Broadside/issues/267) | Documentation site for v1: structure, samples, getting started and reading | 7C-release | — |
| [#268](https://github.com/michaelstonis/Broadside/issues/268) | Rendering guide for every backend | 7C-release | [#267](https://github.com/michaelstonis/Broadside/issues/267), [#86](https://github.com/michaelstonis/Broadside/issues/86) |
| [#269](https://github.com/michaelstonis/Broadside/issues/269) | Writing and editing guides | 7C-release | [#267](https://github.com/michaelstonis/Broadside/issues/267), [#87](https://github.com/michaelstonis/Broadside/issues/87) |
| [#270](https://github.com/michaelstonis/Broadside/issues/270) | Extraction, accessibility, conformance and signatures guides | 7C-release | [#267](https://github.com/michaelstonis/Broadside/issues/267), [#88](https://github.com/michaelstonis/Broadside/issues/88) |
| [#271](https://github.com/michaelstonis/Broadside/issues/271) | Migration guides from PdfSharp, iText and PdfPig | 7C-release | [#269](https://github.com/michaelstonis/Broadside/issues/269), [#270](https://github.com/michaelstonis/Broadside/issues/270) |
| [#272](https://github.com/michaelstonis/Broadside/issues/272) | Trimming, Native AOT and Blazor WebAssembly verification | 7C-release | [#233](https://github.com/michaelstonis/Broadside/issues/233), [#100](https://github.com/michaelstonis/Broadside/issues/100), [#183](https://github.com/michaelstonis/Broadside/issues/183) |
| [#273](https://github.com/michaelstonis/Broadside/issues/273) | Threat model, resource limits and the security process | 7C-release | — |
| [#274](https://github.com/michaelstonis/Broadside/issues/274) | Fuzzing coverage report and security review pass | 7C-release | [#273](https://github.com/michaelstonis/Broadside/issues/273), [#86](https://github.com/michaelstonis/Broadside/issues/86), [#87](https://github.com/michaelstonis/Broadside/issues/87), [#88](https://github.com/michaelstonis/Broadside/issues/88) |
| [#275](https://github.com/michaelstonis/Broadside/issues/275) | Performance comparison with PDFium and pdf.js for v1 | 7C-release | [#272](https://github.com/michaelstonis/Broadside/issues/272), [#140](https://github.com/michaelstonis/Broadside/issues/140), [#184](https://github.com/michaelstonis/Broadside/issues/184) |
| [#276](https://github.com/michaelstonis/Broadside/issues/276) | v1.0 release-candidate gate | 7C-release | [#235](https://github.com/michaelstonis/Broadside/issues/235), [#236](https://github.com/michaelstonis/Broadside/issues/236), [#237](https://github.com/michaelstonis/Broadside/issues/237), [#238](https://github.com/michaelstonis/Broadside/issues/238), [#239](https://github.com/michaelstonis/Broadside/issues/239), [#241](https://github.com/michaelstonis/Broadside/issues/241), [#252](https://github.com/michaelstonis/Broadside/issues/252), [#255](https://github.com/michaelstonis/Broadside/issues/255), [#256](https://github.com/michaelstonis/Broadside/issues/256), [#257](https://github.com/michaelstonis/Broadside/issues/257), [#258](https://github.com/michaelstonis/Broadside/issues/258), [#259](https://github.com/michaelstonis/Broadside/issues/259), [#262](https://github.com/michaelstonis/Broadside/issues/262), [#263](https://github.com/michaelstonis/Broadside/issues/263), [#266](https://github.com/michaelstonis/Broadside/issues/266), [#268](https://github.com/michaelstonis/Broadside/issues/268), [#271](https://github.com/michaelstonis/Broadside/issues/271), [#274](https://github.com/michaelstonis/Broadside/issues/274), [#275](https://github.com/michaelstonis/Broadside/issues/275) |

## Post-v1

Parent [#232](https://github.com/michaelstonis/Broadside/issues/232). Earmarked by the owner and parked until after v1.0; not ready for agents.

| Issue | Title | Track | Blocked by |
|---|---|---|---|
| [#230](https://github.com/michaelstonis/Broadside/issues/230) | Shaping for Indic, Southeast Asian and other complex scripts (earmarked) | 4D-flow-layout | see the issue |
| [#260](https://github.com/michaelstonis/Broadside/issues/260) | JBIG2 extended templates (earmarked) | 7B-hardening | — |
