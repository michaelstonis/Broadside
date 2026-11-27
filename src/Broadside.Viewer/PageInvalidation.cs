using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>What an undone or redone edit (<see cref="IEditStep"/>) changed on a page: the area and layers to draw again.</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Area">The changed area in the page's default user space, or <see langword="null"/> for the whole page.</param>
/// <param name="Layers">The layers that changed, such as <see cref="DisplayLayers.Annotations"/> or <see cref="DisplayLayers.FormFields"/>.</param>
/// <remarks>Not a spec feature; the area is in default user space (ISO 32000-2 §8.3.2.3).</remarks>
public readonly record struct PageInvalidation(int PageIndex, PdfRectangle? Area, DisplayLayers Layers);
