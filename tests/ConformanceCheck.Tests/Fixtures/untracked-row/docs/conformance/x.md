# Fixture spec: rows that are neither done nor n/a and name no tracking issue

The partial row says what is missing but names no issue, and the untouched row has empty notes: two violations. The rows naming `Tracked by #12` and `Tracked by #34, #56` pass, as do the done and n/a rows, which need none.

| Clause | Title | Status | Implementing types | Tests | Notes |
|---|---|---|---|---|---|
| 1 | Parsing | done | `Foo` | `Foo.Tests.FooTests.Parses` | |
| 2 | Rejecting | partial | `FooOptions` | `Foo.Tests.FooTests.Rejects` | Only integers are rejected (see #12). |
| 3 | Later | not started | | | |
| 4 | Scripting | n/a | | | JavaScript execution is out of scope. |
| 5 | Formatting | partial | `Foo` | `Foo.Tests.FooTests.Parses` | Only integers are formatted. Tracked by #12. |
| 6 | Printing | not started | | | Tracked by #34, #56. |
