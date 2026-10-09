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

Today every row is at the second level (`7.2`, `7.3`, ...), all `not started`.

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

For the other documents use their own prefix: `ISO/TS 32003 §5`, `PDF 2.0 Application Note 002 §3`, `Well-Tagged PDF 1.0 §4.2`.

## 5. Run the checker

`tools/ConformanceCheck` arrives with issue #7. Once it has landed:

```sh
dotnet run --project tools/ConformanceCheck -- --root . --summary
```

It fails on a `done` row without a test and prints counts per status; CI runs it on every PR. Until it lands, check by hand that each row you touched has backticked identifiers in both cells and that every test named exists (`grep -rn 'Tokenizes_names' tests/`).

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
- [ ] Each implementing public type and member carries `<remarks>ISO 32000-2 §x.y.z</remarks>`
- [ ] `dotnet run --project tools/ConformanceCheck -- --root . --summary` passes (once #7 has landed)
- [ ] The PR body lists the clauses as they appear in the map
