---
name: updating-the-conformance-map
description: "Use whenever a PR implements, extends or tests a specification clause: find its row in docs/conformance/, set the status, fill the type and test cells in the required format, split to third-level rows, cite the clause in XML docs, and run the checker."
---

# Updating the conformance map

The conformance map is the per-clause record of what Broadside implements (`CONTEXT.md`, "Conformance map"). Whoever touches a clause updates its row in the same PR as the code (`CLAUDE.md`, "Where things are"). CI fails on a `done` row without a test.

## 1. Find the file and the row

`docs/conformance/README.md` maps each specification to a file: ISO 32000-2 is `docs/conformance/iso-32000-2.md`, the technical specifications are `iso-ts-3200N.md`, the application notes `pdf20-an00N.md`, and so on. Rows are keyed by clause number without `§`:

```sh
grep -n '^| 7\.2 ' docs/conformance/iso-32000-2.md
```

Since #241 every row is resolved: `done` with tests, `n/a` with a reason, or `partial`/`not started` with the open issue that tracks the rest (`Tracked by #N`).

## 2. Row format

```
| Clause | Title | Status | Implementing types | Tests | Notes |
```

- **Clause**: the number as the spec prints it (`7.2`, `7.2.3`), no `§`, no trailing dot.
- **Title**: the heading from `Specs/`, read with `pdftotext Specs/ISO_32000-2_sponsored_EC3.pdf - | grep -nE '^7\.2\.[0-9] '`, never from memory.
- **Status**: one of `not started`, `partial`, `done`, `n/a`. `n/a` is for clauses with no implementable content or that `CLAUDE.md` lists as out of scope; say which in Notes.
- **Implementing types**: simple type names (no namespace, no type arguments), each in backticks, comma-separated: `` `CosDictionary`, `CosParser` ``. Each must be declared as a class, struct, interface, enum or record under `src/`. Leave empty for `not started` and `n/a`.
- **Tests**: fully qualified test identifiers `Namespace.Class.Method`, each in backticks, comma-separated: `` `Broadside.Tests.Objects.LexerTests.Tokenizes_names` ``. For a theory, name the method once. A `done` row needs at least one; give `partial` rows their tests too so the checker's counts mean something. The authoritative formats are in `docs/conformance/README.md`; the checker enforces them.
- **Notes**: for `partial`, what is still missing; for `n/a`, why; otherwise anything a reader needs to find the code (an ADR number, a corpus file name).
- **Tracking issue**: a `partial` or `not started` row ends its Notes with `Tracked by #N` (several: `Tracked by #N, #M`), naming an open issue for the rest of the clause; the checker fails a row without one. Link an existing open issue when one fits; otherwise open one (milestone `Post-v1` when it is not v1 work) and add it to `docs/known-limitations.md`. When your PR finishes a clause, drop the tracking issue from the row as you mark it `done`.
- **`n/a` reasons**: introductory or informative text, a heading split into sub-clauses, viewer or printer behavior, or a parse-and-preserve feature from `CLAUDE.md`'s out-of-scope list; an out-of-scope row links a round-trip test (`FullRewriteTests.Objects_the_library_does_not_interpret_survive_a_compact_rewrite_unchanged` takes a new `InlineData` case).

Keep cells on one line; the table has no multi-line cells.

## 3. When to split a row

Split a second-level row into its third-level clauses when the sub-clauses differ in status, or when one row would otherwise list several unrelated types and tests. Replace the second-level row with one row per third-level clause in clause order, titles from `Specs/`, each with its own status; sub-clauses you did not touch get `not started`. Do not go to the fourth level unless a clause is so large that a third-level row would again hide the gaps (§7.6.4 algorithms, §12.5.6 annotation subtypes).

Example for §7.2, which `Specs/` lists as 7.2.1 General, 7.2.2 Representation, 7.2.3 Character set, 7.2.4 Comments:

```
| 7.2.1 | General | n/a | | | Introductory text |
| 7.2.2 | Representation | done | `Broadside.Cos.CosLexer` | `Broadside.Tests.Objects.LexerTests.Tokenizes_names` | |
| 7.2.3 | Character set | done | `Broadside.Cos.CosLexer` | `Broadside.Tests.Objects.LexerTests.Tokenizes_names` | |
| 7.2.4 | Comments | not started | | | |
```

## 4. Cite the clause in the code

Every public type and member that implements a spec concept carries the clause in its XML docs, so the row's "Implementing types" cell can be followed back to the code:

```csharp
/// <summary>Tokenizes a PDF file body.</summary>
/// <remarks>ISO 32000-2 §7.2.</remarks>
public ref struct CosLexer { ... }

/// <summary>Reads one string object, literal or hexadecimal.</summary>
/// <remarks>ISO 32000-2 §7.3.4.</remarks>
public CosToken ReadString() { ... }
```

For the other documents use their own prefix: `ISO/TS 32003 §5`, `PDF 2.0 Application Note 002 §3`, `Well-Tagged PDF 1.0 §4.2`, `ICC.1:2022 Annex F.3`. A document without numbered clauses is cited by the row's key: `PDF 2.0 Application Note 001 §BPC-3`, `PDF 2.0 Application Note 003 §Explicit`. Read the clause number from `Specs/` (`pdftotext`), never from memory.

The check is mechanical, so write citations the way it reads them:

- **Every public type** of a checked package (`Broadside`, `Broadside.Fonts.Cmaps`, `Broadside.Fonts.Standard14`, `Broadside.Layout`, `Broadside.Rendering`) cites a specification in its own `<summary>` or `<remarks>`, in one of the forms listed in `docs/conformance/citation-policy.json` (`citationForms`). A nested type is covered by its enclosing type's citation and a member by its declaring type's, so a member's own clause is extra precision, not a requirement.
- **Every implementing type a row names** (public or internal) cites that row's clause on the type or on one of its members: the document's name, then the clause before any other document is named. `ISO 32000-2 §7.3.4 and §7.3.5` cites both clauses; in `ISO 32000-2 §8.4 and ITU-T T.81 §A.1`, `§A.1` belongs to T.81. A citation of the row's clause, of a sub-clause of it, or (for a third-level or deeper row) of its parent clause counts. When you add a type to a row, add the clause to the type's docs in the same PR.
- **A type that implements no clause** (options, engine, hosting, diagnostics infrastructure) is exempted in `citation-policy.json` (`exemptions`, a type or `Namespace.*`, with a reason) instead of being given an invented citation. A new project under `src/` is added to `packages`, checked, or unchecked with a reason (backends and the viewer are unchecked).

## 5. Run the checker

```sh
dotnet build src/Broadside.Layout/Broadside.Layout.csproj    # builds Broadside too; also build Broadside.Rendering and the Fonts packages you touched
dotnet run --project tools/ConformanceCheck -- --root . --summary --citations
dotnet run --project tools/ConformanceCheck -- --root . --site-page site/articles/conformance.md --write-site-page   # after any status change
```

The documentation site's conformance page holds per-specification counts generated from the maps; `./check-docs.sh` and CI's `conformance-map` job fail when they are stale, so regenerate them whenever a row's status changes.

Without `--citations` it checks the rows only (cell formats, statuses, a test on every `done` row, every named test and type exists) and needs no build. With `--citations` it also reads each checked package's XML documentation from `artifacts/bin/<package>/debug/<package>.xml` and applies the citation rules above; a missing build is reported as a violation. Failures print as `<file>:<line>: <message>`: an uncited public type at its line in `src/<package>/PublicAPI.Unshipped.txt`, a row whose type does not cite its clause at the row's line. CI's `conformance-map` job runs the command above on every PR, and `tests/ConformanceCheck.Tests` runs the same check as part of `dotnet test Broadside.Core.slnf`. The full rules are in `docs/conformance/README.md` (rules 1 to 13).

## Worked example: marking §7.2 partial

A lexer PR tokenizes names and numbers but not yet strings or comments. In `docs/conformance/iso-32000-2.md`, the row

```
| 7.2 | Lexical conventions | not started | | | |
```

becomes

```
| 7.2 | Lexical conventions | partial | `Broadside.Cos.CosLexer` | `Broadside.Tests.Objects.LexerTests.Tokenizes_names` | Strings (7.3.4) and comments (7.2.4) pending |
```

and `CosLexer` carries `<remarks>ISO 32000-2 §7.2.</remarks>`. When the follow-up PR finishes comments, split the row as in section 3 rather than growing the Notes cell. (`CosLexer` and `LexerTests` are illustrative names; the real ones come from issue #36.)

## Checklist

- [ ] Every clause the PR touches has a row in the right `docs/conformance/<spec>.md`
- [ ] Status is one of the four values; `done` rows name at least one test
- [ ] Types are simple names and tests are fully qualified, all backticked and comma-separated
- [ ] Split to third-level rows where statuses differ; titles copied from `Specs/`
- [ ] Each implementing type named in a row cites that row's clause in its XML docs (on the type or a member); each new public type cites a specification or is exempted with a reason in `docs/conformance/citation-policy.json`
- [ ] Every `partial` or `not started` row you touch names its tracking issue (`Tracked by #N`)
- [ ] `dotnet run --project tools/ConformanceCheck -- --root . --summary --citations` passes after building the checked packages
- [ ] The site counts are regenerated (`--site-page site/articles/conformance.md --write-site-page`) when a status changed
- [ ] The PR body lists the clauses as they appear in the map
