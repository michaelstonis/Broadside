using System.Security.Cryptography;
using Broadside.Numerics;

namespace Broadside.Tests.Numerics;

/// <summary>
/// The platform-independent elementary functions behind the bit-identical software rasterizer (issue #233): close to the platform
/// library everywhere, IEEE 754 at the special values, exact where callers rely on exactness, and the same bits on every runner
/// (<see cref="Results_are_the_same_bits_on_every_platform"/> runs on each CI operating system).
/// </summary>
public sealed class DeterministicMathTests
{
    private const int Samples = 20_000;

    private const string Fingerprint = "F7571C6FD4DE0A38D18330E3C45BE0E61FCDBADC311B9580E2D2FF68652F4A28";

    [Fact]
    public void Pow_stays_within_a_few_units_in_the_last_place_of_the_platform_library()
    {
        var inputs = new Inputs(1);
        for (int i = 0; i < Samples; i++)
        {
            double x = inputs.Next(0, 4);
            double y = inputs.Next(-8, 8);
            AssertClose(Math.Pow(x, y), DeterministicMath.Pow(x, y), MaxPowUlps(x, y), $"Pow({x:R}, {y:R})");
        }

        foreach (double gamma in new[] { 1 / 2.4, 2.2, 1.8, 2.4, 0.45, 3.0 })
        {
            for (int sample = 0; sample <= 65535; sample += 7)
            {
                double x = sample / 65535.0;
                AssertClose(Math.Pow(x, gamma), DeterministicMath.Pow(x, gamma), MaxPowUlps(x, gamma), $"Pow({x:R}, {gamma:R})");
            }
        }
    }

    [Fact]
    public void Pow_stays_close_to_the_platform_library_at_the_edges_of_the_double_range()
    {
        // Subnormal bases, results that overflow or underflow (through the subnormals), exponents near ±1024·ln 2 / ln x, and bases
        // next to 1, where the logarithm is tiny.
        var inputs = new Inputs(4);
        for (int i = 0; i < Samples; i++)
        {
            double subnormal = Math.ScaleB(inputs.Next(1, 2), (int)inputs.Next(-1074, -1023));
            double y = inputs.Next(-2, 2) + 0.25;
            AssertClose(Math.Pow(subnormal, y), DeterministicMath.Pow(subnormal, y), MaxPowUlps(subnormal, y), $"Pow({subnormal:R}, {y:R})");

            double x = inputs.Next(0.001, 1000);
            double edge = (inputs.Next(-1080, 1030) / Math.Log2(x)) + 0.125;
            AssertClose(Math.Pow(x, edge), DeterministicMath.Pow(x, edge), MaxPowUlps(x, edge), $"Pow({x:R}, {edge:R})");

            double nearOne = 1 + Math.ScaleB(inputs.Next(-1, 1), -(int)inputs.Next(1, 52));
            double z = inputs.Next(-1000, 1000) + 0.5;
            AssertClose(Math.Pow(nearOne, z), DeterministicMath.Pow(nearOne, z), MaxPowUlps(nearOne, z), $"Pow({nearOne:R}, {z:R})");
        }
    }

    [Fact]
    public void Sine_and_cosine_in_degrees_alone_are_the_bits_of_both_together()
    {
        var inputs = new Inputs(5);
        double[] edges = [0, -0.0, 45, -45, 90, 135, 180, 270, 359.99999999999994, 360, -360, 1e300, -1e17, 9007199254740993.0 * 90];
        foreach (double degrees in edges.Concat(Enumerable.Range(0, Samples).Select(_ => inputs.Next(-1000, 1000))))
        {
            (double sin, double cos) = DeterministicMath.SinCosDegrees(degrees);
            Assert.Equal(BitConverter.DoubleToInt64Bits(sin), BitConverter.DoubleToInt64Bits(DeterministicMath.SinDegrees(degrees)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(cos), BitConverter.DoubleToInt64Bits(DeterministicMath.CosDegrees(degrees)));
        }
    }

    [Fact]
    public void Log_and_exp_stay_within_two_units_in_the_last_place_of_the_platform_library()
    {
        var inputs = new Inputs(2);
        for (int i = 0; i < Samples; i++)
        {
            double x = Math.ScaleB(inputs.Next(1, 2), (int)inputs.Next(-1070, 1020));
            AssertClose(Math.Log(x), DeterministicMath.Log(x), 2, $"Log({x:R})");
            AssertClose(Math.Log10(x), DeterministicMath.Log10(x), 2, $"Log10({x:R})");
            double e = inputs.Next(-740, 709);
            AssertClose(Math.Exp(e), DeterministicMath.Exp(e), 2, $"Exp({e:R})");
        }
    }

    [Fact]
    public void Sine_cosine_arc_tangent_arc_cosine_and_cube_root_stay_within_two_units_in_the_last_place_of_the_platform_library()
    {
        var inputs = new Inputs(3);
        for (int i = 0; i < Samples; i++)
        {
            double angle = inputs.Next(-20, 20);
            (double sin, double cos) = DeterministicMath.SinCos(angle);
            AssertCloseAbsolute(Math.Sin(angle), sin, $"Sin({angle:R})");
            AssertCloseAbsolute(Math.Cos(angle), cos, $"Cos({angle:R})");
            double degrees = inputs.Next(-1000, 1000);
            (double sinDegrees, double cosDegrees) = DeterministicMath.SinCosDegrees(degrees);
            AssertCloseAbsolute(double.SinPi(degrees % 360 / 180), sinDegrees, $"SinDegrees({degrees:R})", 1e-15);
            AssertCloseAbsolute(double.CosPi(degrees % 360 / 180), cosDegrees, $"CosDegrees({degrees:R})", 1e-15);

            double y = Math.ScaleB(inputs.Next(-2, 2), (int)inputs.Next(-30, 30));
            double x = Math.ScaleB(inputs.Next(-2, 2), (int)inputs.Next(-30, 30));
            AssertClose(Math.Atan2(y, x), DeterministicMath.Atan2(y, x), 2, $"Atan2({y:R}, {x:R})");
            double c = inputs.Next(-1, 1);
            AssertClose(Math.Acos(c), DeterministicMath.Acos(c), 2, $"Acos({c:R})");
            double nearOne = 1 - Math.ScaleB(inputs.Next(0, 1), -(int)inputs.Next(1, 50));
            AssertClose(Math.Acos(nearOne), DeterministicMath.Acos(nearOne), 2, $"Acos({nearOne:R})");
            double cube = Math.ScaleB(inputs.Next(-2, 2), (int)inputs.Next(-1070, 1020));
            // The platform cbrt is the wrong oracle: glibc and Windows are up to 3 ulps from the correctly rounded root. Check exactly
            // that the true root lies strictly between the neighbors of the result, which is within 1 ulp.
            AssertCbrtWithinOneUlp(cube, DeterministicMath.Cbrt(cube));
        }
    }

    [Theory]
    [InlineData(2.0, 0.0, 1.0)]
    [InlineData(double.NaN, 0.0, 1.0)]
    [InlineData(1.0, double.NaN, 1.0)]
    [InlineData(2.0, double.NaN, double.NaN)]
    [InlineData(0.0, -1.0, double.PositiveInfinity)]
    [InlineData(-0.0, -1.0, double.NegativeInfinity)]
    [InlineData(-0.0, -2.0, double.PositiveInfinity)]
    [InlineData(-0.0, 3.0, -0.0)]
    [InlineData(-0.0, 2.5, 0.0)]
    [InlineData(-1.0, double.PositiveInfinity, 1.0)]
    [InlineData(0.5, double.PositiveInfinity, 0.0)]
    [InlineData(0.5, double.NegativeInfinity, double.PositiveInfinity)]
    [InlineData(2.0, double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(2.0, double.NegativeInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, -1.0, 0.0)]
    [InlineData(double.PositiveInfinity, 0.5, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 3.0, double.NegativeInfinity)]
    [InlineData(double.NegativeInfinity, -3.0, -0.0)]
    [InlineData(double.NegativeInfinity, 2.0, double.PositiveInfinity)]
    [InlineData(-2.0, 0.5, double.NaN)]
    [InlineData(-2.0, 3.0, -8.0)]
    [InlineData(-2.0, 63.0, -9223372036854775808.0)]
    [InlineData(2.0, 10.0, 1024.0)]
    [InlineData(10.0, -2.0, 0.01)]
    [InlineData(0.25, 0.5, 0.5)]
    [InlineData(0.3, 1.0, 0.3)]
    [InlineData(0.3, 2.0, 0.09)]
    [InlineData(1e300, 2.0, double.PositiveInfinity)]
    [InlineData(1e-300, 5.5, 0.0)]
    public void Pow_follows_IEEE_754_at_the_special_values_and_is_exact_where_callers_rely_on_it(double x, double y, double expected) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(DeterministicMath.Pow(x, y)));

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(-0.0, 2.0, -0.0)]
    [InlineData(0.0, -0.0, Math.PI)]
    [InlineData(-0.0, -1.0, -Math.PI)]
    [InlineData(1.0, 0.0, Math.PI / 2)]
    [InlineData(-1.0, -0.0, -Math.PI / 2)]
    [InlineData(1.0, double.PositiveInfinity, 0.0)]
    [InlineData(1.0, double.NegativeInfinity, Math.PI)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, Math.PI / 4)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity, -3 * Math.PI / 4)]
    [InlineData(double.NegativeInfinity, 5.0, -Math.PI / 2)]
    [InlineData(1.0, 1.0, Math.PI / 4)]
    [InlineData(1.0, -1.0, 3 * Math.PI / 4)]
    public void Atan2_follows_IEEE_754_at_the_special_values(double y, double x, double expected) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(DeterministicMath.Atan2(y, x)));

    [Fact]
    public void Exact_values_stay_exact()
    {
        Assert.Equal(0.0, DeterministicMath.Log(1));
        Assert.Equal(double.NegativeInfinity, DeterministicMath.Log(0));
        Assert.True(double.IsNaN(DeterministicMath.Log(-1)));
        Assert.Equal(1.0, DeterministicMath.Exp(0));
        for (int k = 0; k <= 22; k++)
        {
            Assert.Equal(k, DeterministicMath.Log10(Math.Pow(10, k)));
        }

        Assert.Equal(0.0, DeterministicMath.Acos(1));
        Assert.Equal(Math.PI, DeterministicMath.Acos(-1));
        Assert.True(double.IsNaN(DeterministicMath.Acos(1.0000000000000002)));
        foreach (double root in new[] { 1.0, 2.0, 3.0, 0.5, 10.0, 1e-100 })
        {
            Assert.Equal(root, DeterministicMath.Cbrt(root * root * root));
            Assert.Equal(-root, DeterministicMath.Cbrt(-root * root * root));
        }

        Assert.Equal((0.0, 1.0), DeterministicMath.SinCos(0));
        Assert.Equal((1.0, 0.0), DeterministicMath.SinCosDegrees(90));
        Assert.Equal((0.0, -1.0), DeterministicMath.SinCosDegrees(180));
        Assert.Equal((-1.0, 0.0), DeterministicMath.SinCosDegrees(-90));
        Assert.Equal((0.0, 1.0), DeterministicMath.SinCosDegrees(720));
        Assert.Equal(0.5, DeterministicMath.SinCosDegrees(30).Sin, 15);
        Assert.False(double.IsNegative(DeterministicMath.SinCosDegrees(180).Sin));
        Assert.True(double.IsNaN(DeterministicMath.SinCos(double.PositiveInfinity).Sin));
    }

    [Fact]
    public void Results_are_the_same_bits_on_every_platform()
    {
        // A fingerprint of every function over fixed inputs, taken on macOS arm64 and checked on every CI runner. A change here is a
        // change to every render that uses the function: update it only with the implementation, and say so in the pull request.
        var inputs = new Inputs(233);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bytes = new byte[8];
        void Add(double value)
        {
            BitConverter.TryWriteBytes(bytes, BitConverter.DoubleToInt64Bits(value));
            hash.AppendData(bytes);
        }

        for (int i = 0; i < Samples; i++)
        {
            double x = inputs.Next(0, 4);
            double y = inputs.Next(-8, 8);
            Add(DeterministicMath.Pow(x, y));
            Add(DeterministicMath.Log(x));
            Add(DeterministicMath.Log10(x));
            Add(DeterministicMath.Exp(y * 40));
            (double sin, double cos) = DeterministicMath.SinCos(y);
            Add(sin);
            Add(cos);
            (sin, cos) = DeterministicMath.SinCosDegrees(y * 100);
            Add(sin);
            Add(cos);
            Add(DeterministicMath.SinDegrees(x * 1000));
            Add(DeterministicMath.CosDegrees(x * 1000));
            Add(DeterministicMath.Pow(x, y * 0.1));
            Add(DeterministicMath.Atan2(y, x - 2));
            Add(DeterministicMath.Acos(y / 8));
            Add(DeterministicMath.Cbrt(y));
        }

        string fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        Assert.True(fingerprint == Fingerprint, $"The fingerprint is {fingerprint}, not {Fingerprint}.");
    }

    private static long MaxPowUlps(double x, double y)
    {
        // exp(y·ln x) amplifies the logarithm's rounding by |y·ln x|.
        double amplification = Math.Abs(y * Math.Log(x));
        return 3 + (long)Math.Ceiling(amplification * 2);
    }

    private static void AssertClose(double expected, double actual, long maxUlps, string call)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual) || double.IsInfinity(expected) || expected == 0)
        {
            Assert.True(expected.Equals(actual) || (expected == 0 && Math.Abs(actual) < 1e-300), $"{call}: expected {expected:R}, got {actual:R}");
            return;
        }

        long distance = Math.Abs(BitConverter.DoubleToInt64Bits(expected) - BitConverter.DoubleToInt64Bits(actual));
        Assert.True(distance <= maxUlps, $"{call}: expected {expected:R}, got {actual:R} ({distance} units in the last place apart)");
    }

    private static void AssertCbrtWithinOneUlp(double x, double root)
    {
        if (x == 0 || double.IsNaN(x) || double.IsInfinity(x))
        {
            Assert.True(x.Equals(root), $"Cbrt({x:R}): expected {x:R}, got {root:R}");
            return;
        }

        Assert.True(Math.Sign(root) == Math.Sign(x), $"Cbrt({x:R}): got {root:R}, of the wrong sign");
        double a = Math.Abs(x);
        double r = Math.Abs(root);
        double below = Math.BitDecrement(r);
        double above = Math.BitIncrement(r);
        Assert.True(
            CompareCube(below, a) < 0 && CompareCube(above, a) > 0,
            $"Cbrt({x:R}): got {root:R}, more than one unit in the last place from the true root");
    }

    /// <summary>Compares <paramref name="r"/>³ with <paramref name="a"/> exactly (both finite and positive).</summary>
    private static int CompareCube(double r, double a)
    {
        (System.Numerics.BigInteger rm, int re) = Exact(r);
        (System.Numerics.BigInteger am, int ae) = Exact(a);
        System.Numerics.BigInteger cube = rm * rm * rm;
        int ce = 3 * re;
        int shift = ce - ae;
        return shift >= 0 ? (cube << shift).CompareTo(am) : cube.CompareTo(am << -shift);
    }

    /// <summary>Splits a finite positive double into an integer mantissa and a power-of-two exponent.</summary>
    private static (System.Numerics.BigInteger Mantissa, int Exponent) Exact(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        return exponent == 0 ? (fraction, -1074) : (fraction | (1L << 52), exponent - 1075);
    }

    private static void AssertCloseAbsolute(double expected, double actual, string call, double tolerance = 4e-16) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{call}: expected {expected:R}, got {actual:R}");

    /// <summary>A xorshift generator: the same inputs on every platform, independent of <see cref="Random"/>'s implementation.</summary>
    private sealed class Inputs(ulong seed)
    {
        private ulong _state = seed * 0x9E3779B97F4A7C15UL;

        public double Next(double low, double high)
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return low + ((high - low) * ((_state >> 11) / 9007199254740992.0));
        }
    }
}
