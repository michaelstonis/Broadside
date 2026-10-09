# Agent skills

Each directory holds a `SKILL.md`: YAML frontmatter (`name`, `description`) followed by step-by-step instructions, a worked example that references files in this repository, and a checklist. Invoke the matching skill before starting that kind of task (`CLAUDE.md`, "Skills"). Skills assume you have read `CLAUDE.md` and nothing else.

| Skill | Use it when |
|---|---|
| [`picking-up-an-issue`](picking-up-an-issue/SKILL.md) | Starting any unit of work: find an unblocked issue, claim it, worktree and branch, verify, commit, PR, merge policy, clean up. |
| [`updating-the-conformance-map`](updating-the-conformance-map/SKILL.md) | A PR touches a spec clause: find the row, cell formats, third-level splits, XML doc citations, the checker. |
| [`writing-a-corpus-file`](writing-a-corpus-file/SKILL.md) | A test needs a minimal PDF that `tests/Corpus/` does not have: `generate.py`, verification, README, `Corpus.cs`, snapshot. |
| [`adding-a-benchmark`](adding-a-benchmark/SKILL.md) | A PR touches a hot path: `[MemoryDiagnoser]` class, zero-allocation proof, dry/short/full runs, the regression gate. |
| [`adding-a-fuzz-target`](adding-a-fuzz-target/SKILL.md) | A PR adds or changes a parser or codec: register a SharpFuzz target, seeds, smoke mode. |
| [`adding-a-filter`](adding-a-filter/SKILL.md) | Implementing a stream filter or image codec behind the filter extension point (design brief until #38 lands). |
| [`adding-a-font-program-parser`](adding-a-font-program-parser/SKILL.md) | Implementing a TrueType, CFF, Type 1 or OpenType parser behind the font program extension point (design brief until #50 lands). |
| [`adding-a-rendering-backend`](adding-a-rendering-backend/SKILL.md) | Implementing a backend that consumes display lists (design brief until Phase 3A lands). |

Skills whose extension point does not exist in code yet say so at the top and must have their contract section replaced with the real signatures in the PR that defines the contract.
