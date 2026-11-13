# Fixture spec: citations

Every citation rule passes: a public type citing its row's clause, an internal type citing it on a member, a row one level below a clause a type cites, and a row naming a type of an unchecked package.

| Clause | Title | Status | Implementing types | Tests | Notes |
|---|---|---|---|---|---|
| 1.2 | Parsing | done | `Foo` | `Foo.Tests.FooTests.Parses` | |
| 2.1.1 | Rejecting | partial | `Parser` | | |
| 2.1.1.4 | General | partial | `Parser` | | |
| 3 | Drawing | partial | `Bar` | | |
