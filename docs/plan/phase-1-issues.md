# Phase 1 tickets (Read foundation)

Milestone "Phase 1: COS layer". Tickets are vertical tracer-bullet slices of the spec [#33](https://github.com/michaelstonis/Broadside/issues/33): each one is demoable through the public document API against corpus files, which is the spec's primary test seam. GitHub is the source of truth for status and blocking edges (native sub-issues of #33 and native *blocked by* relationships); this table is a snapshot for orientation. Each issue body holds the acceptance criteria.

The original horizontal Phase 1 issues (#11 to #25: lexer, object types, parser, ...) were closed as superseded on 2026-10-09; each carries a comment pointing at its replacement.

| Issue | Title | Blocked by |
|---|---|---|
| [#36](https://github.com/michaelstonis/Broadside/issues/36) | Parse COS objects from bytes | none |
| [#37](https://github.com/michaelstonis/Broadside/issues/37) | Open a file and read its pages | [#36](https://github.com/michaelstonis/Broadside/issues/36) |
| [#38](https://github.com/michaelstonis/Broadside/issues/38) | Decode streams through filters | [#37](https://github.com/michaelstonis/Broadside/issues/37) |
| [#39](https://github.com/michaelstonis/Broadside/issues/39) | Cross-reference streams, object streams and hybrid files | [#38](https://github.com/michaelstonis/Broadside/issues/38) |
| [#40](https://github.com/michaelstonis/Broadside/issues/40) | Incremental updates and linearization detection | [#37](https://github.com/michaelstonis/Broadside/issues/37) |
| [#41](https://github.com/michaelstonis/Broadside/issues/41) | Lenient repair with diagnostics; strict mode throws | [#39](https://github.com/michaelstonis/Broadside/issues/39) |
| [#42](https://github.com/michaelstonis/Broadside/issues/42) | Standard security handler, all revisions | [#38](https://github.com/michaelstonis/Broadside/issues/38), [#39](https://github.com/michaelstonis/Broadside/issues/39) |
| [#43](https://github.com/michaelstonis/Broadside/issues/43) | Public-key security handler | [#42](https://github.com/michaelstonis/Broadside/issues/42) |
| [#44](https://github.com/michaelstonis/Broadside/issues/44) | Save unchanged documents and create an empty one | [#39](https://github.com/michaelstonis/Broadside/issues/39) |
| [#45](https://github.com/michaelstonis/Broadside/issues/45) | Lazy loading, object cache and the concurrency contract | [#39](https://github.com/michaelstonis/Broadside/issues/39) |
| [#46](https://github.com/michaelstonis/Broadside/issues/46) | Dependency injection parity | [#37](https://github.com/michaelstonis/Broadside/issues/37) |
| [#47](https://github.com/michaelstonis/Broadside/issues/47) | Real-world corpus gate | [#41](https://github.com/michaelstonis/Broadside/issues/41), [#42](https://github.com/michaelstonis/Broadside/issues/42), [#44](https://github.com/michaelstonis/Broadside/issues/44), [#45](https://github.com/michaelstonis/Broadside/issues/45) |
| [#48](https://github.com/michaelstonis/Broadside/issues/48) | Phase 1 fuzz run and benchmark baseline | [#47](https://github.com/michaelstonis/Broadside/issues/47) |
