using Broadside.Content;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>display-list</c> target (issue #94): runs the input as a page's decoded content through the display-list builder, then
/// replays the list twice into a checking backend. Invariants: nothing throws in lenient mode; saves and restores balance and never
/// go below zero; every path's verbs take exactly its points and every subpath starts with a moveto; every fill's colour space is
/// not a Pattern space and has its components; every image (issue #102) has samples, a colour space and a Decode map that agree on
/// its component count; two replays of one list make the same calls. Each call runs in a new empty document.
/// </summary>
/// <remarks>ADR 0006; ISO 32000-2 §8.4.2, §8.5.3, §8.6.</remarks>
internal static class DisplayListTarget
{
    // Inline images in the input are decoded into the list (issue #102); small limits keep a mutated /W /H from costing gigabytes.
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedStreamLength(1 << 20));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = Engine.Create();
        var builder = new DisplayListBuilder();
        ContentInterpreter.RunBytes(data, document, builder, ContentInterpreter.DefaultOptions);
        DisplayList list = builder.ToDisplayList(document);

        var first = new CheckingBackend();
        list.Replay(first);
        var second = new CheckingBackend();
        list.Replay(second);
        Check(first.Depth == 0, "saves and restores do not balance");
        Check(first.Calls == second.Calls && first.Checksum.Equals(second.Checksum), "two replays of one list differ");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Checks every call and folds what it received into a checksum.</summary>
    private sealed class CheckingBackend : IRenderingBackend
    {
        public int Depth { get; private set; }

        public int Calls { get; private set; }

        public double Checksum { get; private set; }

        public void SaveState()
        {
            Depth++;
            Calls++;
        }

        public void RestoreState()
        {
            Check(--Depth >= 0, "a restore without a save");
            Calls++;
        }

        public void SetTransform(in Matrix transform)
        {
            Calls++;
            Checksum += transform.A + transform.F;
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a filled path does not start with a moveto");
            int points = 0;
            for (int index = 0; index < path.Verbs.Length; index++)
            {
                PathVerb verb = path.Verbs[index];
                points += verb switch
                {
                    PathVerb.MoveTo or PathVerb.LineTo => 1,
                    PathVerb.QuadTo => 2,
                    PathVerb.CubicTo => 3,
                    _ => 0,
                };
                Check(verb != PathVerb.Close || index + 1 == path.Verbs.Length || path.Verbs[index + 1] is PathVerb.MoveTo or PathVerb.Close, "a segment after a close does not start a subpath");
            }

            Check(points == path.Points.Length, "path verbs and points disagree");
            Check(color.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "a fill in a Pattern colour space");
            Check(color.Components.Length <= PdfColor.MaxComponents, "too many components");
            Checksum += points + color.Components.Length + (int)rule;
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            Calls++;
            Check(image.Width > 0 && image.Height > 0, "an image without samples");
            Check(image.Samples.Length == (long)image.Stride * image.Height, "the samples are not Stride x Height bytes");
            Check(image.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "an image in a Pattern colour space");
            Check(image.ColorSpace.ComponentCount == image.Components && image.DecodeMap.Components == image.Components, "an image's colour space, samples and Decode disagree");
            Checksum += image.Width + image.Height + image.Components + (int)conversion.Intent;
        }
    }
}
