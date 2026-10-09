# Broadside

A fully managed .NET implementation of the PDF specification (ISO 32000-2 and its extensions), covering reading, writing, editing and rendering, intended to be the single PDF dependency a .NET application needs.

## Language

### Capabilities

**Read**:
Parsing an existing PDF file into the object model, including damaged or non-conformant files.
_Avoid_: Load, import, open (as a capability name)

**Write**:
Producing a new PDF file from scratch.
_Avoid_: Generate, create, export

**Edit**:
Modifying an existing PDF file and saving the result, preserving everything not touched.
_Avoid_: Manipulate, modify, update

**Render**:
Turning a page into drawing operations on a surface, whether that surface is a bitmap, a GPU canvas or a platform graphics context.
_Avoid_: Rasterize (that is one kind of rendering), draw, paint

### Object model

**COS object**:
One of the primitive values a PDF file is built from, exactly as stored: boolean, number, string, name, array, dictionary, stream, null, or indirect reference. The low-level layer of the object model.
_Avoid_: Primitive, raw object, PDF object

**Document model**:
The typed, high-level view over COS objects: Document, Page, Font, Image, Annotation, Form field and so on. A document-model object is a view over its COS object, never a copy of it.
_Avoid_: DOM, high-level API, semantic layer

### Parsing

**Diagnostic**:
A record of one deviation from the specification found while reading a file and what was done about it. Diagnostics are collected on the document, never thrown.
_Avoid_: Warning, error, log entry

**Strict mode**:
The opt-in reading posture in which the first deviation from the specification is an error instead of a diagnostic.
_Avoid_: Validation mode, pedantic mode

**Lenient mode**:
The default reading posture: repair what can be repaired, record a diagnostic, continue.
_Avoid_: Tolerant mode, recovery mode

**Filter**:
A stream encoding named in a stream's `/Filter` entry, such as FlateDecode, DCTDecode or JBIG2Decode, and the codec that decodes or encodes it. Image codecs are filters.
_Avoid_: Codec (as a top-level term), decoder, compressor

### Fonts

**Font program**:
The embedded or substituted glyph data in one of the formats PDF allows: TrueType, OpenType, CFF, Type 1, or Type 3 content streams.
_Avoid_: Font file, typeface, font data

**Standard 14 fonts**:
The fourteen fonts (Helvetica, Times, Courier families, Symbol, ZapfDingbats) a PDF may use without embedding. Their metrics ship in the core; their glyphs ship in a separate package.
_Avoid_: Base 14, built-in fonts, default fonts

**Font resolver**:
The extension point that finds a font program for a font that is not embedded, whether from the Standard 14 package, the operating system, or a consumer-supplied source.
_Avoid_: Font provider, font loader, font fallback

### Rendering

**Display list**:
The device-independent, fully resolved sequence of drawing operations produced by interpreting a page's content, which every backend consumes. Fonts, images and colors are resolved before an operation enters the display list.
_Avoid_: Command buffer, scene graph, render tree


**Backend**:
A concrete implementation of the rendering surface contract that knows how to draw on one technology, such as the software rasterizer, SkiaSharp or CoreGraphics.
_Avoid_: Renderer (ambiguous with the rendering engine), driver, target

**Software rasterizer**:
The backend written entirely in managed code with no native or GPU dependency. It is the reference backend that rendering tests are measured against.
_Avoid_: CPU renderer, fallback renderer

**Native backend**:
A backend that draws through a platform's own graphics API: CoreGraphics on Apple platforms, Direct2D on Windows, Android Canvas on Android.
_Avoid_: Platform renderer, OS renderer

### Text and layout

**Layout tree**:
The hierarchy of logical content (paragraphs, headings, lists, tables with cells, figures, reading order) recovered from a page. For a tagged file it comes from the structure tree; for an untagged file it is detected from geometry. Both produce the same shape.
_Avoid_: Document structure (ambiguous with the structure tree), semantic tree, layout analysis result

**Structure tree**:
The tagged-PDF hierarchy stored in the file itself, as defined by ISO 32000-2 clause 14.7.
_Avoid_: Tag tree, logical structure (as a term), accessibility tree

### Conformance

**Conformance map**:
The per-specification, per-clause record of what the library implements, maintained by whoever implements each clause and used to pick the next piece of work.
_Avoid_: Feature matrix, coverage report, checklist


**Fully managed**:
Written in C# with no dependency outside packages Microsoft ships that carry no native or platform-specific component (the Base Class Library, `System.*` and `Microsoft.Extensions.*` packages). Applies to everything that interprets the PDF file itself: parsing, writing, fonts, image codecs, cryptography and content interpretation. Backends other than the software rasterizer are exempt.
_Avoid_: Pure .NET, native-free, zero-dependency (the backends have dependencies)

**Extension point**:
A contract in the core library that a consumer or a separate package can supply an alternative implementation for, such as a stream filter, a font program parser or a backend. The core always ships a fully managed default for every extension point that the PDF file itself depends on.
_Avoid_: Plugin, provider, hook

**Canonical model**:
PDF 2.0 (ISO 32000-2) as the single object model all files are read into and written from. Earlier versions are read as PDF 2.0 with features absent; on write, the file's version is raised to the minimum the used features require.
_Avoid_: Version-specific model, 1.7 mode

**Parse-and-preserve**:
The posture toward out-of-scope features (JavaScript, XFA, 3D, rich media, multimedia): their objects are read, kept and written back unchanged, but never executed or rendered.
_Avoid_: Ignore, strip, unsupported
