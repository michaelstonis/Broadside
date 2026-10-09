# Fixture spec: valid

Every rule passes: a done row with a method and a class test, a partial row with tests and notes, an n/a row with notes, and an untouched row.

| Clause | Title | Status | Implementing types | Tests | Notes |
|---|---|---|---|---|---|
| 1 | Parsing | done | `Foo`, `IFoo` | `Foo.Tests.FooTests.Parses`, `Foo.Tests.FooTests` | |
| 2 | Rejecting | partial | `FooOptions` | `Foo.Tests.FooTests.Rejects` | Only integers are rejected. |
| 3 | Scripting | n/a | | | JavaScript execution is out of scope. |
| 4 | Later | not started | | | |
