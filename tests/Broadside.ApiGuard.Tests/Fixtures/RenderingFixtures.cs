// Fixtures for the prefix guard in an area whose types carry no Pdf or Cos prefix (an extension class may be named after the
// type it extends).
#pragma warning disable IDE0130 // The fixture namespaces are what the guards check, not the folder.
namespace Broadside.Rendering.GuardFixtures;

public sealed class PdfBadRenderType;

public sealed class GoodRenderType;

public static class PdfGoodFixtureRenderingExtensions;
