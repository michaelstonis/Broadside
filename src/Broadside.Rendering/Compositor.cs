using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Composites premultiplied BGRA source pixels over the backdrop with a constant alpha and a blend mode: every paint of the software
/// rasterizer (fills, strokes, glyphs, images, stencil masks, shadings) ends here, with the coverage of its shape.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.3.3 and §11.3.6, in premultiplied form (§11.3.6 NOTE 1): with source alpha αs = shape × alpha constant × the
/// source's own alpha (§11.3.7.2, §11.6.4.4) and backdrop alpha αb, the result is
/// αr × Cr = (1 − αs) × αb × Cb + (1 − αb) × αs × Cs + αb × αs × B(Cb, Cs) and αr = αs + αb − αs × αb (§11.3.7.3). For Normal,
/// B(Cb, Cs) = Cs and this is source-over, d = s × c + d × (1 − a × c), computed in integers with exact /255 rounding (the
/// rasterizer's opaque compositing, unchanged at alpha 1). The other modes are the separable functions of Table 134 and the
/// non-separable ones of Table 135, evaluated on unpremultiplied RGB in the rasterizer's blending colour space, RGB (§11.3.4).
/// </para>
/// <para>
/// Hot path: a value type, allocates nothing; Normal costs one multiply per run when the alpha is below 1, other modes a few dozen
/// floating-point operations per pixel. The default value is opaque Normal, so painters that take one optionally are unchanged.
/// </para>
/// </remarks>
internal readonly struct Compositor
{
    private const float Unit = 1f / 255;

    // 255 − alpha × 255, so that default(Compositor) is opaque.
    private readonly byte _transparency;

    // Whether paints go into a knockout group, where a paint of alpha 0 still knocks out what is under its shape.
    private readonly bool _knocksOut;

    /// <summary>Creates a compositor.</summary>
    /// <param name="alpha">The constant alpha, 0 to 1 (clamped).</param>
    /// <param name="mode">The blend mode.</param>
    public Compositor(double alpha, BlendMode mode)
    {
        double clamped = double.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1);
        _transparency = (byte)(255 - (int)((clamped * 255) + 0.5));
        Mode = mode;
    }

    private Compositor(in Compositor compositor, bool knocksOut)
    {
        _transparency = compositor._transparency;
        Mode = compositor.Mode;
        _knocksOut = knocksOut;
    }

    /// <summary>Gets the blend mode.</summary>
    public BlendMode Mode { get; }

    /// <summary>Gets the constant alpha, 0 to 255.</summary>
    public int Alpha => 255 - _transparency;

    /// <summary>
    /// Gets a value indicating whether every paint through this compositor leaves the backdrop as it is: alpha 0, outside a knockout
    /// group (inside one, a paint of alpha 0 still replaces what earlier paints left under its shape, ISO 32000-2 §11.4.6).
    /// </summary>
    public bool PaintsNothing => _transparency == 255 && !_knocksOut;

    /// <summary>Returns this compositor for paints into a knockout group, or not.</summary>
    public Compositor WithKnockout(bool knocksOut) => new(this, knocksOut);

    /// <summary>Gets a value indicating whether the blend is plain source-over (Normal).</summary>
    public bool IsNormal => Mode == BlendMode.Normal;

    /// <summary>Returns <paramref name="coverage"/> (0 to 255) scaled by the constant alpha.</summary>
    public int Scale(int coverage) => _transparency == 0 ? coverage : Divide255(coverage * (255 - _transparency));

    /// <summary>Composites <paramref name="source"/> over <paramref name="destination"/> at <paramref name="coverage"/> (0 to 255, before the constant alpha).</summary>
    /// <param name="source">The source pixel, premultiplied BGRA.</param>
    /// <param name="destination">The backdrop pixel, premultiplied BGRA.</param>
    /// <param name="coverage">The shape of the paint at the pixel.</param>
    /// <returns>The result pixel.</returns>
    public uint Composite(uint source, uint destination, int coverage)
    {
        int scaled = Scale(coverage);
        return Mode == BlendMode.Normal ? Over(source, destination, scaled) : Blend(source, destination, scaled, Mode);
    }

    /// <summary>Composites one opaque colour over a run of pixels at one coverage (0 to 255, before the constant alpha).</summary>
    /// <param name="line">The row.</param>
    /// <param name="x">The first pixel of the run.</param>
    /// <param name="count">The pixels in the run.</param>
    /// <param name="coverage">The shape of the paint over the run.</param>
    /// <param name="color">The colour, opaque premultiplied BGRA.</param>
    public void Fill(Span<uint> line, int x, int count, int coverage, uint color)
    {
        int scaled = Scale(coverage);
        if (scaled <= 0)
        {
            return;
        }

        Span<uint> span = line.Slice(x, count);
        if (Mode != BlendMode.Normal)
        {
            foreach (ref uint pixel in span)
            {
                pixel = Blend(color, pixel, scaled, Mode);
            }

            return;
        }

        if (scaled >= 255)
        {
            span.Fill(color);
            return;
        }

        // Premultiplied source-over of an opaque colour with source alpha = coverage: d = s × c + d × (1 − c), per channel, rounded.
        int inverse = 255 - scaled;
        Span<byte> bytes = MemoryMarshal.AsBytes(span);
        int s0 = (int)(color & 0xFF) * scaled;
        int s1 = (int)((color >> 8) & 0xFF) * scaled;
        int s2 = (int)((color >> 16) & 0xFF) * scaled;
        int s3 = (int)(color >> 24) * scaled;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)Divide255(s0 + (bytes[i] * inverse));
            bytes[i + 1] = (byte)Divide255(s1 + (bytes[i + 1] * inverse));
            bytes[i + 2] = (byte)Divide255(s2 + (bytes[i + 2] * inverse));
            bytes[i + 3] = (byte)Divide255(s3 + (bytes[i + 3] * inverse));
        }
    }

    /// <summary>Premultiplied source-over with the source scaled by <paramref name="coverage"/>: d = s·c + d·(1 − a·c).</summary>
    public static uint Over(uint source, uint destination, int coverage)
    {
        uint alpha = source >> 24;
        if (coverage == 255 && alpha == 255)
        {
            return source;
        }

        if (coverage <= 0)
        {
            return destination;
        }

        int keep = 255 - Divide255((int)alpha * coverage);
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int s = (int)((source >> shift) & 0xFF);
            int d = (int)((destination >> shift) & 0xFF);
            result |= (uint)Math.Min(Divide255((s * coverage) + (d * keep)), 255) << shift;
        }

        return result;
    }

    /// <summary>The union of two shapes or alphas, 0 to 255: b + s − b × s (ISO 32000-2 §11.3.7.3).</summary>
    public static int Union(int backdrop, int source) => backdrop + source - Divide255(backdrop * source);

    /// <summary>
    /// The knockout of one paint in a knockout group (ISO 32000-2 §11.4.6, §11.4.8 with b = 0), premultiplied: given what the group
    /// held, the paint composited over the initial backdrop at shape <paramref name="shape"/>, and that backdrop, the result is
    /// (1 − fs) × previous + composited − (1 − fs) × backdrop.
    /// </summary>
    /// <param name="previous">What the group held at the pixel.</param>
    /// <param name="composited">The paint composited over <paramref name="backdrop"/> with its shape, alpha and blend mode.</param>
    /// <param name="backdrop">The group's initial backdrop at the pixel.</param>
    /// <param name="shape">The paint's shape fs, 0 to 255.</param>
    /// <returns>The pixel.</returns>
    public static uint KnockOut(uint previous, uint composited, uint backdrop, int shape)
    {
        if (shape >= 255)
        {
            return composited;
        }

        if (shape <= 0 || previous == backdrop)
        {
            return shape <= 0 ? previous : composited;
        }

        int keep = 255 - shape;
        int alpha = Math.Clamp((int)(composited >> 24) + Scaled((int)(previous >> 24) - (int)(backdrop >> 24), keep), 0, 255);
        uint result = (uint)alpha << 24;
        for (int shift = 0; shift < 24; shift += 8)
        {
            int c = (int)((composited >> shift) & 0xFF) + Scaled((int)((previous >> shift) & 0xFF) - (int)((backdrop >> shift) & 0xFF), keep);
            result |= (uint)Math.Clamp(c, 0, alpha) << shift;
        }

        return result;
    }

    /// <summary>
    /// Mixes two premultiplied pixels: <paramref name="from"/> + (<paramref name="to"/> − <paramref name="from"/>) × amount / 255. A
    /// non-isolated group in the Normal blend mode composites onto the backdrop it started from this way (ISO 32000-2 §11.4.4: the
    /// group composited with constant alpha q onto its backdrop B is (1 − q) × B + q × the group's complete result).
    /// </summary>
    public static uint Mix(uint from, uint to, int amount)
    {
        if (amount >= 255 || from == to)
        {
            return to;
        }

        if (amount <= 0)
        {
            return from;
        }

        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int f = (int)((from >> shift) & 0xFF);
            int t = (int)((to >> shift) & 0xFF);
            result |= (uint)Math.Clamp(f + Scaled(t - f, amount), 0, 255) << shift;
        }

        return result;
    }

    /// <summary>
    /// Removes the backdrop from a non-isolated group's complete result (ISO 32000-2 §11.4.4, Result): with the group alpha αg,
    /// αg × C = αn × Cn − (1 − αg) × α0 × C0, premultiplied, so the group's own colour and alpha can composite as one object.
    /// </summary>
    /// <param name="complete">The group's complete result αn Cn at the pixel.</param>
    /// <param name="backdrop">The group's initial backdrop α0 C0.</param>
    /// <param name="groupAlpha">αg, 0 to 255.</param>
    /// <returns>The group's colour and alpha, premultiplied.</returns>
    public static uint RemoveBackdrop(uint complete, uint backdrop, int groupAlpha)
    {
        if (groupAlpha <= 0)
        {
            return 0;
        }

        int keep = 255 - groupAlpha;
        uint result = (uint)groupAlpha << 24;
        for (int shift = 0; shift < 24; shift += 8)
        {
            int c = (int)((complete >> shift) & 0xFF) - Divide255((int)((backdrop >> shift) & 0xFF) * keep);
            result |= (uint)Math.Clamp(c, 0, groupAlpha) << shift;
        }

        return result;
    }

    /// <summary>Rounds <paramref name="difference"/> × <paramref name="amount"/> / 255 for a difference of either sign.</summary>
    private static int Scaled(int difference, int amount) =>
        difference >= 0 ? Divide255(difference * amount) : -Divide255(-difference * amount);

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 2 × 255 × 255.</summary>
    public static int Divide255(int value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }

    /// <summary>The general compositing formula of §11.3.6 with a blend function other than Normal.</summary>
    private static uint Blend(uint source, uint destination, int coverage, BlendMode mode)
    {
        int sourceByte = (int)(source >> 24);
        if (coverage <= 0 || sourceByte == 0)
        {
            return destination;
        }

        float sa = sourceByte * Unit;
        float alphaS = sa * coverage * Unit;
        int backdropByte = (int)(destination >> 24);
        float alphaB = backdropByte * Unit;

        // Unpremultiplied colours, red first; B(Cb, Cs) needs them, the formula weighs them by the alphas again.
        float sr = Math.Min(((source >> 16) & 0xFF) * Unit / sa, 1);
        float sg = Math.Min(((source >> 8) & 0xFF) * Unit / sa, 1);
        float sb = Math.Min((source & 0xFF) * Unit / sa, 1);
        float br = 0;
        float bg = 0;
        float bb = 0;
        if (backdropByte > 0)
        {
            br = Math.Min(((destination >> 16) & 0xFF) * Unit / alphaB, 1);
            bg = Math.Min(((destination >> 8) & 0xFF) * Unit / alphaB, 1);
            bb = Math.Min((destination & 0xFF) * Unit / alphaB, 1);
        }

        float rr;
        float rg;
        float rb;
        if (mode >= BlendMode.Hue)
        {
            (rr, rg, rb) = NonSeparable(mode, br, bg, bb, sr, sg, sb);
        }
        else
        {
            rr = Separable(mode, br, sr);
            rg = Separable(mode, bg, sg);
            rb = Separable(mode, bb, sb);
        }

        float keep = 1 - alphaS;
        float own = alphaS * (1 - alphaB);
        float mixed = alphaS * alphaB;
        float alphaR = alphaS + alphaB - (alphaS * alphaB);
        int a = ToByte(alphaR);
        int r = Math.Min(ToByte((keep * alphaB * br) + (own * sr) + (mixed * rr)), a);
        int g = Math.Min(ToByte((keep * alphaB * bg) + (own * sg) + (mixed * rg)), a);
        int b = Math.Min(ToByte((keep * alphaB * bb) + (own * sb) + (mixed * rb)), a);
        return (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static int ToByte(float value) => value <= 0 ? 0 : value >= 1 ? 255 : (int)((value * 255) + 0.5f);

    /// <summary>The separable blend functions of Table 134, on one additive component.</summary>
    private static float Separable(BlendMode mode, float cb, float cs) => mode switch
    {
        BlendMode.Multiply => cb * cs,
        BlendMode.Screen => cb + cs - (cb * cs),
        BlendMode.Overlay => HardLight(cs, cb),
        BlendMode.Darken => Math.Min(cb, cs),
        BlendMode.Lighten => Math.Max(cb, cs),
        BlendMode.ColorDodge => cb == 0 ? 0 : cb >= 1 - cs ? 1 : cb / (1 - cs),
        BlendMode.ColorBurn => cb == 1 ? 1 : 1 - cb >= cs ? 0 : 1 - ((1 - cb) / cs),
        BlendMode.HardLight => HardLight(cb, cs),
        BlendMode.SoftLight => SoftLight(cb, cs),
        BlendMode.Difference => Math.Abs(cb - cs),
        BlendMode.Exclusion => cb + cs - (2 * cb * cs),
        _ => cs,
    };

    private static float HardLight(float cb, float cs) =>
        cs <= 0.5f ? cb * 2 * cs : Screen(cb, (2 * cs) - 1);

    private static float Screen(float cb, float cs) => cb + cs - (cb * cs);

    private static float SoftLight(float cb, float cs)
    {
        if (cs <= 0.5f)
        {
            return cb - ((1 - (2 * cs)) * cb * (1 - cb));
        }

        float d = cb <= 0.25f ? ((((16 * cb) - 12) * cb) + 4) * cb : MathF.Sqrt(cb);
        return cb + (((2 * cs) - 1) * (d - cb));
    }

    /// <summary>The non-separable blend functions of Table 135, on RGB.</summary>
    private static (float R, float G, float B) NonSeparable(BlendMode mode, float br, float bg, float bb, float sr, float sg, float sb) => mode switch
    {
        BlendMode.Hue => SetLum(SetSat(sr, sg, sb, Sat(br, bg, bb)), Lum(br, bg, bb)),
        BlendMode.Saturation => SetLum(SetSat(br, bg, bb, Sat(sr, sg, sb)), Lum(br, bg, bb)),
        BlendMode.Color => SetLum((sr, sg, sb), Lum(br, bg, bb)),
        _ => SetLum((br, bg, bb), Lum(sr, sg, sb)),
    };

    private static float Lum(float r, float g, float b) => (0.3f * r) + (0.59f * g) + (0.11f * b);

    private static float Sat(float r, float g, float b) => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    private static (float R, float G, float B) SetLum((float R, float G, float B) c, float l)
    {
        float d = l - Lum(c.R, c.G, c.B);
        return ClipColor(c.R + d, c.G + d, c.B + d);
    }

    private static (float R, float G, float B) ClipColor(float r, float g, float b)
    {
        float l = Lum(r, g, b);
        float n = Math.Min(r, Math.Min(g, b));
        float x = Math.Max(r, Math.Max(g, b));
        if (n < 0)
        {
            float scale = l / (l - n);
            r = l + ((r - l) * scale);
            g = l + ((g - l) * scale);
            b = l + ((b - l) * scale);
        }

        if (x > 1)
        {
            float scale = (1 - l) / (x - l);
            r = l + ((r - l) * scale);
            g = l + ((g - l) * scale);
            b = l + ((b - l) * scale);
        }

        return (r, g, b);
    }

    /// <summary>SetSat: the largest component becomes <paramref name="s"/>, the smallest 0, the middle one in proportion.</summary>
    private static (float R, float G, float B) SetSat(float r, float g, float b, float s)
    {
        Span<float> c = [r, g, b];
        int max = r >= g ? (r >= b ? 0 : 2) : (g >= b ? 1 : 2);
        int min = r < g ? (r < b ? 0 : 2) : (g < b ? 1 : 2);
        int mid = 3 - max - min;
        if (c[max] > c[min])
        {
            c[mid] = (c[mid] - c[min]) * s / (c[max] - c[min]);
            c[max] = s;
        }
        else
        {
            c[mid] = 0;
            c[max] = 0;
        }

        c[min] = 0;
        return (c[0], c[1], c[2]);
    }
}
