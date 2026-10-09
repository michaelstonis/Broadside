# Broadside: rules for agents

Broadside is a fully managed .NET PDF library (read, write, edit, render) with viewer controls to follow. Read this file, then `CONTEXT.md` (vocabulary, use it exactly), `docs/adr/` (decisions, do not relitigate them), and `docs/plan/README.md` (phases and tracks) before working.

## Where things are

- `Specs/` (gitignored, local only): ISO 32000-2 and extensions. `Specs/References/`: free font, image and color references. Cite clauses from these, never from memory, when the two disagree.
- `docs/conformance/<spec>.md`: one row per clause. You update the rows for every clause you touch in the same PR as the code.
- `docs/research/`: background reports. Read the one for your area before designing.
- `tests/Corpus/`: hand-written minimal PDFs, one feature each. `corpus/` (gitignored): real-world corpora fetched by `tools/CorpusFetcher`.

## Picking up work

1. Find an open issue in the current milestone with no assignee whose `blocked by` issues are closed.
2. Self-assign, add the `in-progress` label, create a worktree and branch named `<issue-number>-<slug>`.
3. Read the spec clauses the issue lists before writing code.
4. Open a PR that references the issue, updates the conformance map, updates `PublicAPI.Unshipped.txt`, and adds tests. CI must be green.
5. Phase 0 and 1: self-merge when green. Phase 2 onward: request review from the owner.

## Hard rules

- **Dependencies.** The core package and `Broadside.Rendering` depend only on packages Microsoft ships with no native component (BCL, `System.*`, `Microsoft.Extensions.*` abstractions). Nothing else, ever. Backends other than the software rasterizer may depend on their platform library and nothing else. See ADR 0001.
- **Target.** `net10.0` only. See ADR 0002.
- **Canonical model.** PDF 2.0 is the model. Every writable feature declares its minimum PDF version. See ADR 0003.
- **Two layers.** `Cos*` types are the file; `Pdf*` types are live views over them, never copies. See ADR 0004.
- **Leniency.** Reading repairs and records a `Diagnostic`; it throws only in strict mode. Never swallow a repair silently. See ADR 0005.
- **Display list.** Backends consume display lists; nothing renders from operators directly. See ADR 0006.
- **Spec citations.** Every public type and member implementing a spec concept carries `<remarks>ISO 32000-2 §x.y.z</remarks>` (or the relevant TS or reference). The conformance map links to it.
- **Extension points.** Filters, font program parsers, font resolvers, color management, backends and security handlers are interfaces with a managed default. Add a new one only with an ADR.
- **Out of scope** stays parse-and-preserve: JavaScript execution, XFA rendering, 3D, rich media, multimedia playback, printing subsystems, OCR.

## Code conventions

- C# latest, file-scoped namespaces, `sealed` unless designed for inheritance, nullable enabled, warnings are errors.
- `Pdf` prefix for document-model types, `Cos` prefix for COS types, no prefix inside `Rendering`, `Text`, `Layout`.
- Verbs: `Open`, `Create`, `Save`, `Render`, `Extract`. `Async` suffix only at I/O boundaries. Options: `Use*` selects an implementation, `With*` sets a value.
- Hot paths (lexer, content interpreter, rasterizer, codecs) allocate nothing per token, operator, glyph or scanline. Use `Span<T>`, `ArrayPool<T>`, structs, and `SearchValues`. Prove it with a BenchmarkDotNet benchmark and a `MemoryDiagnoser` assertion.
- Thread-safety contract: a document is safe for concurrent reads and renders when nobody mutates it; mutation is single-threaded. Caches that lazy-load must honor this.
- Tests: xUnit v3. Unit tests next to the feature; corpus tests driven by `tests/Corpus/` and `corpus/`; rendering snapshots through Verify; fuzz targets in `tests/Fuzz/` for every parser and codec.
- Commits: Conventional Commits (`feat(fonts): parse CFF charstrings`). One PR per issue.

## Do not

- Do not add a NuGet dependency to the core to save time. Implement it or stub it behind an extension point and open an issue.
- Do not mark a conformance row `done` without a linked test.
- Do not change `CONTEXT.md` terms or write an ADR without stating in the PR why the existing decision was wrong.
- Do not merge from Phase 2 onward without owner review.
