---
title: "API review checklist"
---

# API review checklist

Every pull request that changes a shipping package's public API (its `PublicAPI.Unshipped.txt` changes) is checked against this list, and the v1 API freeze reviews every shipping package against it. Rules marked **guarded** are enforced by tests that run in CI over every shipping package's public surface; the rest need a reviewer's judgement.

## Which packages

The shipping packages for v1 are `Broadside`, `Broadside.Rendering`, `Broadside.Rendering.Skia`, `Broadside.Rendering.WebGL`, `Broadside.Rendering.Android`, `Broadside.Rendering.CoreGraphics`, `Broadside.Rendering.Direct2D`, `Broadside.Fonts.Standard14`, `Broadside.Fonts.Cmaps` and `Broadside.Layout`. The viewer packages (`Broadside.Viewer` and its UI shells) stay previews after v1, on their own track, and are outside this checklist and its guards.

The list lives in one place, `ShippingPackages` in [`tests/Broadside.ApiGuard.Tests`](https://github.com/michaelstonis/Broadside/tree/main/tests/Broadside.ApiGuard.Tests), and a test fails when a project under `src/` is neither on it nor a viewer package. The packages that build on every operating system are checked by reflection over their assemblies. The three native backends build only where their platform workloads are installed, so they are checked from their `PublicAPI` files by the rules a signature line can show (namespaces, prefixes, glossary words, the cancellation token, `Async` naming, optional parameters, collections, nullability); a reviewer checks the rest by hand, which is a few types per backend.

## How the guards report

A breach fails the test of its rule with the member and the reason. Fix it, or, when a reviewer accepts the exception, add it to `KnownViolations` in the same test project with a comment saying why. A listed breach that is gone also fails, so the list only shrinks: removing an entry is part of fixing it. Each rule's test has a fixture that breaks it, in `RuleFixtureTests`.

## The checklist

### Names

- **Glossary terms.** Types and members use the terms in [`CONTEXT.md`](https://github.com/michaelstonis/Broadside/blob/main/CONTEXT.md) exactly, never a word it lists under *Avoid*. Guarded in part: no type name contains an avoided noun (Renderer, Driver, Provider, Plugin, Hook, Codec, Compressor, Typeface, Base14, Tolerant, Pedantic, Dom), and no method name starts with an avoided verb (Load, Import, Generate, Export, Manipulate, Modify, Rasterize). The reviewer checks the rest, including words the PDF specification itself uses for operators and entries (paint, draw, export value), which are allowed where they name that thing.
- **The verb set.** The top-level operations are `Open`, `Create`, `Save`, `Render` and `Extract`. A new operation that does one of these uses that verb. Review.
- **Prefixes.** **Guarded.** `Cos` is the prefix of the COS layer (`Broadside.Objects`) and only of it, and every type there carries it. `Pdf` marks document-model types (a public view over a COS dictionary or stream is held to it), and types in the Rendering, Text and Layout areas carry neither prefix. An extension class may be named after the type it extends (`PdfImageRenderingExtensions`). Extension-point contexts (`*Context`) expose the dictionary they work on without being document-model views.
- **Namespaces.** **Guarded.** Every public type is in the root namespace (the document model) or under one of the areas the spec names: Objects, Filters, Fonts, Images, Graphics, Content, Rendering, Text, Layout, Structure, Forms, Annotations, Security, Signatures, Writing, Diagnostics. Dependency-injection registration lives in `Microsoft.Extensions.DependencyInjection`, the .NET convention.

### Options and extension points

- **`Use*` and `With*`.** **Guarded.** On an options type, every fluent method is `Use*`, which selects an implementation (it takes one, or nothing), or `With*`, which sets a value (it takes no implementation; collections and `IProgress<T>` callbacks are values). Extension methods that return an options type follow the same rule. The hosting guard (`OptionsGuard` in `Broadside.Rendering.Tests`) also checks that every option reaches the hosted engine and that value options bind from configuration, and that no public static state exists.
- **Extension points are interfaces with a managed default.** **Guarded.** Filters, font program parsers, the font resolver, color management, backends and security handlers are interfaces; the shipping packages hold a concrete managed implementation of each; and an options type registers each through a `Use*` method. A public abstract class a consumer can derive from (a public or protected constructor) is reported as an extension point that is not an interface. A new extension point needs a written decision first.

### Types

- **Sealed unless designed for inheritance.** **Guarded.** A public class is `sealed` or `static`, unless it is abstract, declares a visible virtual or abstract member or a protected constructor, or another public type derives from it. Review that an unsealed class really is meant to be derived from, and that its virtual members are documented as such.
- **Nullable annotations.** **Guarded.** Every `PublicAPI` file opens with `#nullable enable` and no member is oblivious (a `~` line). Review that the annotations say what the member does: a parameter that accepts `null` is `?`, and a result that can be `null` says when.
- **Read-only collections.** **Guarded.** What the surface hands out (property and field types, results, `out` parameters) is never a mutable collection type (`List<T>`, `Dictionary<K, V>`, `IList<T>`, `ICollection<T>`, `IDictionary<K, V>`, `ISet<T>`, ...), at any depth of generic arguments, and no property is an array. Use `IReadOnlyList<T>`, `IReadOnlyDictionary<K, V>`, `IEnumerable<T>` or a memory type. A member that implements a .NET collection interface keeps that interface's signature.

### Methods

- **Cancellation token last.** **Guarded.** A method that takes a `CancellationToken` takes exactly one, as its last parameter, named `cancellationToken`.
- **`Async` only at I/O boundaries.** **Guarded in part.** A method is named `*Async` exactly when it returns `Task`, `ValueTask`, their generic forms or `IAsyncEnumerable<T>`, and every such method takes a cancellation token. Every `Open` and `Save` overload has an `OpenAsync` and `SaveAsync` counterpart with the same parameters plus a trailing `CancellationToken cancellationToken = default`, and the reverse (`AsyncEntryPointTests` in `Broadside.Tests`). The reviewer checks that an asynchronous method really sits at an I/O boundary (a file, a stream, the network, a platform decoder), not around CPU-bound work.
- **No optional parameters on overloaded methods.** **Guarded.** A method or constructor with overloads has no optional parameters, except a trailing `CancellationToken cancellationToken = default`. Options are overloads (`Open(path)` and `Open(path, options)`), never a defaulted parameter beside an overload.
- **Documented exceptions.** Review. Every exception a member lets reach its caller is listed with `<exception cref="...">` and the condition that raises it. Problems found while reading a file are diagnostics, not exceptions, except in strict mode.

### Documentation and tracking

- **Clause citations.** **Guarded.** Every public type that implements a specification concept cites its clause in its XML documentation (`<remarks>ISO 32000-2 §x.y.z</remarks>`, or the relevant technical specification or reference), and a type named in a conformance row cites that row's clause. Checked by `tools/ConformanceCheck --citations` and `CitationCheckerTests`, with the exemptions in `docs/conformance/citation-policy.json`.
- **Public API files.** **Guarded** by the build: a public member missing from, or stale in, `PublicAPI.Unshipped.txt` is a build error. The pull request says what was added or removed and why.
- **XML documentation.** **Guarded** by the build: a public member without XML documentation is a build error. Review that it says what the member does, its units and its threading behavior where they matter.
- **PDF version.** Review. A feature that writes something declares the minimum PDF version it needs.
