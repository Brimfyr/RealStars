using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The eye's adaptation to the light it is in. Every light is its raw colour, what a star's temperature
/// gives against the display's white (D65), the stars' catalogue colours and each star's &lt;Sunlight&gt;
/// alike (see SunLight). An eye adapts toward that white only partly: the closer the light is to daylight
/// and the brighter it is, the more. Zhu et al. (2026, Opt. Express 34(2):1340) measured it for exactly our
/// case: people adapted to a real lit scene, then set the white of that scene on a display in the dark. The
/// white they chose is W = D x D65 + (1 - D) x the light, in CAT02 cone space, with D = f_L(luminance) x a
/// part by colour temperature (their Table 2 on the blackbody locus).
///
/// The light is the scene's: what lights what the camera sees (SceneLight), not a star it cannot see. The eye's
/// state follows it as Fairchild and Reniff (1995) measured, a fast stage and a slow one, and holds in the map.
/// Each frame the light's cone signals are scaled to W's before the tone curve (composite.frag, through three
/// spare floats of the tone curve's push constants): a sunlit white shows as W, and everything else, stars,
/// glows and all, moves with it. Under the Sun that is all but white; under a red dwarf, warm.
/// </summary>
internal static class Adaptation
{
    // Linear sRGB to CAT02 cone space (XYZ, D65, then CAT02), and back: the same numbers as the shader's.
    private static readonly float[] ToCone =
        { 0.3904725f, 0.5499044f, 0.0089016f, 0.0709259f, 0.9631074f, 0.0013581f, 0.0231427f, 0.1280122f, 0.9360519f };
    internal const string ToConeGlsl =
        "mat3(vec3(0.3904725, 0.0709259, 0.0231427), vec3(0.5499044, 0.9631074, 0.1280122), vec3(0.0089016, 0.0013581, 0.9360519))";
    internal const string FromConeGlsl =
        "mat3(vec3(2.8583111, -0.2104348, -0.0418895), vec3(-1.6287080, 1.1584149, -0.1181543), vec3(-0.0248187, 0.0003205, 1.0688866))";

    // Linear sRGB to CIE XYZ (D65).
    private static readonly double[] XyzFromRgb =
        { 0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041 };

    // Zhu et al. 2026, Table 2: D on the blackbody locus (Duv 0) at 1000 cd/m2, over their luminance factor there
    // (0.9842), by colour temperature in mired. 6500 K is their reference, adapted to completely.
    private static readonly (double Mired, double D)[] OnLocus =
    {
        (100.0, 0.7722), (125.0, 0.7824), (1e6 / 6500.0, 1.0), (200.0, 0.7824), (250.0, 0.4471), (1e6 / 3000.0, 0.2439),
    };

    /// <summary>Where the per-frame scale goes: CompositeData's tone-curve block, padding of segments 0 and 1.</summary>
    internal static readonly int[] PaddingOffsets = { 24, 28, 56 };

    // Fairchild and Reniff (1995): about 60% of the adaptation in the first 5 s, 90% after a minute. A fast stage
    // (time constant ~1 s) carrying 55%, and a slow one with a half-life of 30 s.
    internal const double FastShare = 0.55, FastSeconds = 1.0, SlowSeconds = 30.0 / 0.6931471805599453;
    private const double EstimateEvery = 0.1;             // seconds between looks at the scene

    private static PropertyInfo? _sunlight, _program;
    private static FieldInfo? _push;
    private static FieldInfo? _x, _y, _z;
    private static (float R, float G, float B) _lastLight = (-1f, -1f, -1f);
    private static float[] _scale = { 1f, 1f, 1f };

    private static bool _scene;                            // SceneLight resolved: adapt to the scene, not the star
    private static double[]? _target, _fast, _slow;        // the eye's white, target and stages (cone space)
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastTime, _lastLook = double.NegativeInfinity, _lastLog = double.NegativeInfinity;
    private static double _loggedKelvin = double.NaN, _loggedD = double.NaN;

    /// <summary>The star's raw light as the game has it this frame, which lights the bodies (BodyColours); null
    /// before the first frame, or without the adaptation.</summary>
    internal static (float R, float G, float B)? Light => _lastLight.R < 0f ? null : _lastLight;

    /// <summary>Adapt to the scene's light (SceneLight) rather than the star's alone.</summary>
    internal static void UseScene(bool on) => _scene = on;

    internal static bool Resolve()
    {
        Type? universe = AccessTools.TypeByName("KSA.Universe");
        Type? program = AccessTools.TypeByName("KSA.Program");
        _sunlight = universe == null ? null : AccessTools.Property(universe, "SunlightColor");
        _program = program == null ? null : AccessTools.Property(program, "Instance");
        _push = program == null ? null : AccessTools.Field(program, "_compositePushConstantData");
        Type? f4 = _sunlight?.PropertyType;
        _x = f4 == null ? null : AccessTools.Field(f4, "X");
        _y = f4 == null ? null : AccessTools.Field(f4, "Y");
        _z = f4 == null ? null : AccessTools.Field(f4, "Z");
        return _sunlight != null && _program != null && _push != null && _x != null && _y != null && _z != null
            && _push.FieldType.IsValueType && Marshal.SizeOf(_push.FieldType) == 128;
    }

    /// <summary>Correlated colour temperature of a linear sRGB colour (McCamy 1992).</summary>
    internal static double Cct(double r, double g, double b)
    {
        double X = XyzFromRgb[0] * r + XyzFromRgb[1] * g + XyzFromRgb[2] * b;
        double Y = XyzFromRgb[3] * r + XyzFromRgb[4] * g + XyzFromRgb[5] * b;
        double Z = XyzFromRgb[6] * r + XyzFromRgb[7] * g + XyzFromRgb[8] * b;
        double sum = X + Y + Z;
        double x = X / sum, y = Y / sum;
        double n = (x - 0.3320) / (0.1858 - y);
        return 449.0 * n * n * n + 3525.0 * n * n + 6823.3 * n + 5520.33;
    }

    /// <summary>The degree of adaptation to a light near the blackbody locus, by colour temperature.</summary>
    internal static double DegreeOfAdaptation(double kelvin)
    {
        double mired = 1e6 / Math.Clamp(kelvin, 1000.0, 50000.0);
        if (mired <= OnLocus[0].Mired)
            return OnLocus[0].D;                                   // bluer than 10000 K: as at 10000 K
        for (int i = 1; i < OnLocus.Length; i++)
            if (mired <= OnLocus[i].Mired)
            {
                var (m0, d0) = OnLocus[i - 1];
                var (m1, d1) = OnLocus[i];
                return d0 + (d1 - d0) * (mired - m0) / (m1 - m0);
            }
        // redder than 3000 K: on along the 4000 -> 3000 K slope, to a floor
        var (ma, da) = OnLocus[^2];
        var (mb, db) = OnLocus[^1];
        return Math.Max(0.05, db + (db - da) / (mb - ma) * (mired - mb));
    }

    /// <summary>Per-cone scale from a light to the white an eye adapted to it settles on (equal luminance).</summary>
    internal static float[] ScaleFor(double r, double g, double b)
    {
        double y = XyzFromRgb[3] * r + XyzFromRgb[4] * g + XyzFromRgb[5] * b;
        if (!(y > 1e-9))
            return new[] { 1f, 1f, 1f };
        double d = DegreeOfAdaptation(Cct(r, g, b));
        var scale = new float[3];
        for (int i = 0; i < 3; i++)
        {
            double light = ToCone[3 * i] * r + ToCone[3 * i + 1] * g + ToCone[3 * i + 2] * b;
            double white = (ToCone[3 * i] + ToCone[3 * i + 1] + ToCone[3 * i + 2]) * y;   // D65 at the light's luminance
            scale[i] = (float)((d * white + (1.0 - d) * light) / light);
        }
        return scale;
    }

    /// <summary>Zhu et al.'s luminance factor: how much of the adaptation happens at an adapting luminance (cd/m2).
    /// 0.29 in the dark, 0.98 at 1000 cd/m2, all of it in sunlight.</summary>
    internal static double LuminanceFactor(double candela) => 1.0 - Math.Exp(-0.0038 * Math.Max(candela, 0.0) - 0.3471);

    private static double[] Cone(double r, double g, double b) =>
        new[] { ToCone[0] * r + ToCone[1] * g + ToCone[2] * b, ToCone[3] * r + ToCone[4] * g + ToCone[5] * b,
                ToCone[6] * r + ToCone[7] * g + ToCone[8] * b };

    /// <summary>The eye's white once adapted to a light (colour at unit luminance, a white's luminance under it):
    /// the cone signals that make a white surface under the light show as W = D x D65 + (1 - D) x the light.</summary>
    internal static double[] AdaptedWhite(double[] colour, double candela, out double d, out double kelvin)
    {
        kelvin = Cct(colour[0], colour[1], colour[2]);
        d = LuminanceFactor(candela) * DegreeOfAdaptation(kelvin);
        double[] light = Cone(colour[0], colour[1], colour[2]), white = Cone(1.0, 1.0, 1.0);
        var eye = new double[3];
        for (int i = 0; i < 3; i++)
            eye[i] = light[i] * white[i] / (d * white[i] + (1.0 - d) * light[i]);
        return eye;
    }

    /// <summary>The per-cone scale for an eye with this white: a surface shows relative to it, on a D65 display.</summary>
    internal static float[] ScaleForWhite(double[] eye)
    {
        double[] white = Cone(1.0, 1.0, 1.0);
        return new[] { (float)(white[0] / eye[0]), (float)(white[1] / eye[1]), (float)(white[2] / eye[2]) };
    }

    /// <summary>One step of the eye's two stages toward its target over dt seconds; the white it gives.</summary>
    internal static double[] Ease(double[] target, double[] fast, double[] slow, double dt)
    {
        double f = 1.0 - Math.Exp(-dt / FastSeconds), s = 1.0 - Math.Exp(-dt / SlowSeconds);
        var eye = new double[3];
        for (int i = 0; i < 3; i++)
        {
            fast[i] += (target[i] - fast[i]) * f;
            slow[i] += (target[i] - slow[i]) * s;
            eye[i] = FastShare * fast[i] + (1.0 - FastShare) * slow[i];
        }
        return eye;
    }

    /// <summary>Postfix on Program.UpdateShaderData: the eye's state, eased toward the light in view, into the tone
    /// curve's push constants. The game rewrites that block when its settings change, so every frame.</summary>
    public static void UpdateShaderDataPostfix(object viewport)
    {
        try
        {
            object? light = _sunlight!.GetValue(null);
            object? program = _program!.GetValue(null);
            if (light == null || program == null)
                return;
            _lastLight = ((float)_x!.GetValue(light)!, (float)_y!.GetValue(light)!, (float)_z!.GetValue(light)!);
            if (_scene)
            {
                if (SceneLight.IsMain(viewport))
                    Look(viewport);
            }
            else
                _scale = ScaleFor(_lastLight.R, _lastLight.G, _lastLight.B);      // no scene to read: the star's light
            object push = _push!.GetValue(program)!;
            WriteScale(push, _scale);
            _push.SetValue(program, push);
        }
        catch (Exception ex)
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (now - _lastLog > 5.0)
            {
                _lastLog = now;
                ShaderShadow.Log("WARN: the eye's adaptation was not updated: " + ex.Message);
            }
        }
    }

    /// <summary>Looks at the scene (ten times a second) and moves the eye toward it; in the map, holds.</summary>
    private static void Look(object viewport)
    {
        double now = Clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - _lastTime, 0.0, 1.0);
        _lastTime = now;
        if (SceneLight.IsMap(viewport))
            return;
        if (now - _lastLook >= EstimateEvery && SceneLight.Measure(viewport) is { } seen)
        {
            _lastLook = now;
            _target = AdaptedWhite(seen.Colour, seen.Candela, out double d, out double kelvin);
            bool moved = double.IsNaN(_loggedD) || Math.Abs(d - _loggedD) > 0.05 || Math.Abs(kelvin - _loggedKelvin) > 150.0;
            if (moved && now - _lastLog > 5.0)
            {
                _lastLog = now;
                _loggedD = d;
                _loggedKelvin = kelvin;
                ShaderShadow.Log(FormattableString.Invariant(
                    $"the eye adapting to {kelvin:0} K at {seen.Candela:0.####} cd/m2, the star {100.0 * seen.StarShare:0}% of the light in view: D {d:0.00}"));
            }
        }
        if (_target == null)
            return;
        if (_fast == null || _slow == null)
        {
            _fast = (double[])_target.Clone();            // the first look: the eye starts adapted, with no state before it
            _slow = (double[])_target.Clone();
        }
        _scale = ScaleForWhite(Ease(_target, _fast, _slow, dt));
    }

    /// <summary>Writes the scale into a boxed CompositeData, in place.</summary>
    internal static void WriteScale(object boxedCompositeData, float[] scale)
    {
        GCHandle handle = GCHandle.Alloc(boxedCompositeData, GCHandleType.Pinned);
        try
        {
            IntPtr p = handle.AddrOfPinnedObject();
            for (int i = 0; i < 3; i++)
                Marshal.WriteInt32(p, PaddingOffsets[i], BitConverter.SingleToInt32Bits(scale[i]));
        }
        finally
        {
            handle.Free();
        }
    }
}
