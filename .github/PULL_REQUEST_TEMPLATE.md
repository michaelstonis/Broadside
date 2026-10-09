Closes #

## Summary

<!-- What this PR does and, just as important, what it does not. Link the spec clauses it implements as they appear in docs/conformance. -->

## Deliverables

- [ ] Code with XML docs citing clauses (`<remarks>ISO 32000-2 §x.y.z</remarks>` on every spec-implementing public type and member)
- [ ] Tests (unit, corpus, snapshot or fuzz as appropriate)
- [ ] Conformance map rows updated in `docs/conformance/` (no row marked `done` without a linked test)
- [ ] `PublicAPI.Unshipped.txt` updated if the public surface changed, and the change explained below
- [ ] Benchmark with a `MemoryDiagnoser` assertion if on a hot path (lexer, content interpreter, rasterizer, codecs)
- [ ] All commits signed off (`git commit -s`, see CONTRIBUTING.md) and follow Conventional Commits

## Public API changes

<!-- If PublicAPI.Unshipped.txt changed, say what was added or removed and why. Otherwise write "none". -->

## Verification

<!-- How you checked this works: the test classes or corpus files that prove it, benchmark numbers for hot paths, snapshot diffs for rendering. Paste the relevant command and a summary of its output. -->
