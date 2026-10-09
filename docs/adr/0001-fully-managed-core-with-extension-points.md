# Fully managed core with extension points

Everything that interprets the PDF file itself (parsing, writing, fonts, image codecs, cryptography, content interpretation) is written in C# with no third-party dependencies. Packages Microsoft ships that carry no native or platform-specific component (the Base Class Library, `System.*`, `Microsoft.Extensions.*` abstractions for DI, options and logging) are permitted because they are the same supply chain as the runtime. This costs us our own JPEG, JPEG 2000, JBIG2 and CCITT decoders and our own TrueType, CFF and Type 1 font parsers, which is the largest single cost in the project. We accept it because the goal is to be the one PDF dependency a .NET application needs, and every native or third-party dependency is a platform-support and supply-chain liability we would be passing on to consumers.

Each such component sits behind an extension point (a public contract with a managed default) so that a consumer can substitute a native or community implementation for speed or platform reasons, and so that the default can be built after a substitute during development. Rendering backends other than the software rasterizer are exempt and may carry native dependencies, which is why they ship as separate packages.

## Considered Options

- Depend on managed community packages (ImageSharp, BouncyCastle): rejected because their licenses and release cadences would become ours, and "replace any outside library needs" would be false from day one.
- Wrap native libraries (libjpeg, OpenJPEG, FreeType) via P/Invoke: rejected because it breaks on every platform the native build does not cover, which is exactly the problem the project exists to solve.
