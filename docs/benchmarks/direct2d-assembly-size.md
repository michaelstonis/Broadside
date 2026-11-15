# Direct2D backend assembly size (#360)

`Broadside.Rendering.Direct2D.dll` was 1.6 MB in Release, eight times `Broadside.Rendering.dll`. Measured 2026-11-14 on macOS (Release, `net10.0-windows10.0.19041.0`, built with `-p:EnableWindowsTargeting=true`; the assembly does not depend on the host), base `sweep/7b` (577eca6b). Per-type IL and metadata come from a `System.Reflection.Metadata` walk over the assembly.

## Where the bytes were

CsWin32 (0.3.360, `allowMarshaling: false`) emits every requested COM interface with its whole inheritance chain, every struct, enum and interface those signatures name (transitively), a friendly overload set per method, and, for each method, a callable wrapper stub and a vtable entry for the interface. The 27 entries of `NativeMethods.txt` produced 725 generated files and 10 MB of C#.

| | Before | Friendly overloads off | After (hand-written vtable structs) |
|---|---:|---:|---:|
| `Broadside.Rendering.Direct2D.dll` | 1,654,272 B | 1,315,328 B | **205,312 B** |
| Types (all) | 1,165 | 1,001 | 184 |
| Generated `Windows.Win32.*` types | 719 | 555 | 80 |
| Methods (all) | 11,626 | 8,987 | 1,156 |
| IL bytes (all) | 541,468 | 418,344 | 80,525 |
| Metadata bytes | 1,098,324 | 886,196 | 120,328 |

The backend's own code is 71 KB of IL; the other 470 KB of IL and 1 MB of metadata were generated interface surface. The largest single types were `ID3D11DeviceContext` (230 methods, only ever held as a pointer), `ID2D1DeviceContext` (184), `IDWriteTextLayout` (134), the four `ID2D1*RenderTarget` interfaces (about 115 each) and `IDWriteFactory3` to `5`.

## What changed

1. **Friendly overloads off** (`NativeMethods.json`: `"friendlyOverloads": { "enabled": false }`): 1.65 MB to 1.32 MB. The backend calls the raw entry points everywhere except `DWriteCreateFactory`, which now takes the interface identifier and an output pointer explicitly. Narrowing the requested interfaces does not help: `ID2D1DeviceContext` and `ID3D11Device` are the interfaces the backend calls, and each carries its base chain.
2. **Hand-written, blittable vtable structs** (`ComInterfaces.cs`): each interface the backend calls is a struct holding the vtable pointer, with function-pointer calls for only the methods the backend uses (218 methods on 42 interfaces, including `IUnknown`). Slot numbers are the interface's declaration order in the Windows SDK headers, and each was checked against the vtable CsWin32 generated for the same interface (all 218 match). No runtime dependency is added: the structs are internal and CsWin32 still supplies the value types (structures, enumerations, `HRESULT`, GUIDs), which are small.
3. **Four structures with interface-typed fields** (`ComStructs.cs`: `D2D1_LAYER_PARAMETERS`, `D2D1_LAYER_PARAMETERS1`, `D2D1_BITMAP_PROPERTIES1`, `DWRITE_GLYPH_RUN`) are declared with opaque `void*` fields. CsWin32 would otherwise bind `ID2D1Geometry`, `ID2D1Brush`, `ID2D1ColorContext` and `IDWriteFontFace` to type them. Field order and types match the SDK, so the layouts are identical.
4. **`D3D11CreateDevice`** is declared by hand (`Direct3D11Native.cs`, `LibraryImport`, opaque pointers) because the generated declaration names `ID3D11DeviceContext` and `IDXGIAdapter`.

## Trimmed, published app

A `PublishTrimmed` (`TrimMode=full`), self-contained `win-x64` console app that opens a document and renders page 1 through `Direct2DRenderer`, publishing the before and after assemblies the same way:

| `Broadside.Rendering.Direct2D.dll` after trimming | Size |
|---|---:|
| Before | 160,256 B |
| After | 157,184 B |

The trimmer already removed the unused interface surface: a trimmed app saved only about 3 KB (2%). The issue therefore concerned package size (the NuGet package and a framework-dependent, untrimmed app), where the assembly is now 12% of its former size. Measured with the .NET 10.0.402 SDK and runtime pack 10.0.14 on macOS; the trimmer's output for the Windows target does not depend on the host, but rerun it on Windows if the numbers matter for a release.

## Verification

- Built with `-warnaserror` for the backend and its test project (compile only on macOS); `dotnet format --verify-no-changes` is clean.
- The Direct2D, DirectWrite, Direct3D 11 and WIC calls can only run on Windows. The `build-test (windows-latest)` job of `.github/workflows/ci.yml` builds the whole solution and runs `Broadside.Rendering.Direct2D.Tests` (golden images, compositing, text, WIC decode), which exercises every hand-written method: a wrong slot or signature would fail those tests.
- Reproduce the sizes: build `src/Broadside.Rendering.Direct2D` in Release and read the assembly's size; the per-type breakdown was a `System.Reflection.Metadata` walk summing method-body sizes per type definition.
