# Phase 1 issues (COS layer)

Milestone "Phase 1". Clauses are ISO 32000-2 unless stated. Rows with the same "after" value run in parallel.

| # | Title | Clauses | Acceptance | After |
|---|---|---|---|---|
| 1.1 | Lexer | §7.2 | Tokenizes every file in `tests/Corpus/` and `corpus/`; zero allocations per token in benchmark; fuzz target | – |
| 1.2 | COS object types | §7.3 | `CosBoolean`, `CosInteger`, `CosReal`, `CosString` (literal and hex, PDFDocEncoding, UTF-16BE, UTF-8 per 2.0), `CosName` (with `#xx` escapes), `CosArray`, `CosDictionary`, `CosStream`, `CosNull`, `CosReference`; dirty flag; equality semantics; unit tests per type | – |
| 1.3 | Object parser | §7.3, §7.5.7 | Direct and indirect objects, object streams; recovers from missing `endobj`; fuzz target | 1.1, 1.2 |
| 1.4 | File source abstraction | – | In-memory, memory-mapped, seekable `Stream`; non-seekable buffered at the boundary; async open/save at the boundary only | – |
| 1.5 | Cross-reference tables and streams | §7.5.4, §7.5.5, §7.5.8, §7.5.6 | Classic tables, xref streams, hybrid files, `/Prev` chains, `/XRefStm`; free-list handling; tests for each corpus file type | 1.3, 1.4 |
| 1.6 | Reconstruction and diagnostics | §7.5 (recovery), ADR 0005 | `Diagnostic` model and collection; xref rebuild by scanning for `N G obj`; `startxref` recovery; stream `/Length` recovery via `endstream` search; strict mode throws; every repair has a test corpus file | 1.5 |
| 1.7 | Object cache and concurrency | – | Lazy load by object number; read-safe concurrent access; dirty tracking preserved; stress test with parallel readers | 1.5 |
| 1.8 | Standard filters | §7.4.2–§7.4.6, §7.4.4.4 | Flate (via `System.IO.Compression`), LZW (early change), ASCIIHex, ASCII85, RunLength, TIFF and PNG predictors; filter chains; `DecodeParms`; extension-point interface `IStreamFilter` with registry; fuzz target per filter | 1.2 |
| 1.9 | Standard security handler | §7.6.2–§7.6.4, ISO/TS 32003, ISO/TS 32004 | RC4 40/128 (own implementation), AES-128 CBC, AES-256 R5 and R6 (SHA-256 hash loop), AES-GCM R7, `Identity` crypt filter, per-object keys, `EncryptMetadata`, MAC verification (32004); decrypt on read; test files for each revision | 1.8 |
| 1.10 | Public-key security handler read | §7.6.5 | PKCS#7 recipient lists via `System.Security.Cryptography.Pkcs`; decrypt with a provided certificate | 1.9 |
| 1.11 | COS serializer | §7.5, §7.3 | Writes any object graph; classic xref or xref stream; object streams; byte-exact round trip of untouched objects (test: hash of re-serialized unchanged file sections) | 1.2, 1.8 |
| 1.12 | Engine, options, DI | – | `PdfEngine`, `PdfOptions` fluent (`UseLenient`, `UseStrict`, `UseFilter`, `UseFontResolver`, ...), `services.AddBroadside()`, `IOptions<PdfOptions>`, `ILogger` wiring; tests via `ServiceCollection` | 1.8 |
| 1.13 | Document skeleton | §7.7.2, §7.7.3, §7.7.3.4 | `PdfDocument.Open/Create/Save/SaveAsync`, catalog, page tree walk with inheritance, `PdfPage` boxes and rotation, `PdfDocument.Diagnostics`; opens every corpus file in lenient mode | 1.6, 1.7, 1.11, 1.12 |
| 1.14 | Linearization read | Annex F | Parse linearization parameter dictionary and hint streams; expose `IsLinearized` and first-page object set | 1.5 |
| 1.15 | Phase 1 exit gate | – | Corpus run: every pdf.js and PDFBox file opens without exception; strict mode agrees with veraPDF on well-formed subset; 24 h fuzz run clean; conformance rows for §7.2–§7.7 marked | all |
