# Target .NET 10 and later only

The library targets `net10.0` exclusively, with no `net8.0` or `netstandard2.0` targets. This is a greenfield project for the .NET ecosystem going forward, and the parser, codecs and rasterizer depend heavily on `Span<T>`, `ref struct`, generic math, `SearchValues` and other features that polyfilling for older targets would cripple. Adding an older target later is possible; designing for one from the start would shape every low-level API around the weakest runtime.

## Consequences

- Consumers on .NET Framework or .NET 8 cannot use the library.
- MAUI, Uno and Avalonia integrations require their .NET 10 releases.
