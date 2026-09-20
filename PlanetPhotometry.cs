using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Distant planets on the same brightness scale as the stars.
///
/// The game sizes a far-off body's sprite with GetApparentSizeScale, which is
/// 10*(R/R_earth)^k clamped to 5..30 for a planet: a visibility aid that grows with RADIUS
/// rather than with brightness. Neptune, large but very far, takes the full 30x - roughly
/// 900x in flux from area alone - while Venus, which should outshine everything in the sky,
/// takes 9.5x. The ordering is inverted from physics, and it only became visible once the
/// star field started carrying real magnitudes.
///
/// So the scale is replaced by one computed from the body itself. Nothing here is hardcoded
/// per planet: the absolute magnitude comes from the body's own radius and albedo, which
/// means a modded system's bodies are handled on the same footing as Sol's.
/// </summary>
internal static class PlanetPhotometry
{
    // These must match the constants in StarShaders, or a planet and a star of the same
    // magnitude will not match. RSCheck compares them against the GLSL.
    public const double MagRef = 5.0;         // the magnitude that peaks at 1.0 on screen
    public const double PsfCore = 1.0;        // point spread function core, pixels
    public const double Brightness = 3.1;
    public const double DisplayLevels = 255.0;
    public const double MinGlowPx = 1.0;

    private const double Au = 1.495978707e11;

    /// <summary>
    /// Apparent V magnitude of a sunlit body.
    ///
    /// The absolute magnitude of a solar system body follows from its size and albedo,
    /// H = 5 log10(1329 / (D_km * sqrt(p))), the standard relation behind every published
    /// H - it returns -6.88 for Neptune against the IAU's -6.87. Distance then contributes
    /// 5 log10(r * delta) in AU, and the phase angle is handled by the IAU H-G system with
    /// G = 0.15, which is the usual default for a body whose slope parameter is unmeasured.
    /// </summary>
    public static double ApparentMagnitude(double radiusM, double albedo,
                                           double sunDistM, double obsDistM, double cosPhase)
    {
        double diameterKm = 2.0 * radiusM / 1000.0;
        double h = 5.0 * Math.Log10(1329.0 / (diameterKm * Math.Sqrt(Math.Max(albedo, 1e-4))));
        double distance = 5.0 * Math.Log10((sunDistM / Au) * (obsDistM / Au));

        double alpha = Math.Acos(Math.Clamp(cosPhase, -1.0, 1.0));
        double halfTan = Math.Tan(Math.Min(alpha, Math.PI * 0.999) * 0.5);
        double phi1 = Math.Exp(-3.33 * Math.Pow(halfTan, 0.63));
        double phi2 = Math.Exp(-1.87 * Math.Pow(halfTan, 1.22));
        double phase = -2.5 * Math.Log10(Math.Max(0.85 * phi1 + 0.15 * phi2, 1e-12));

        return h + distance + phase;
    }

    /// <summary>
    /// Radius in pixels at which the profile drops below one display level - the same
    /// relation the star vertex shader uses, so a planet and a star of equal magnitude come
    /// out the same size and the same brightness.
    /// </summary>
    public static double GlowRadiusPx(double magnitude)
    {
        double flux = Math.Pow(10.0, -0.4 * (magnitude - MagRef));
        double peak = flux * Brightness / (Math.PI * PsfCore * PsfCore);
        return Math.Max(MinGlowPx, PsfCore * Math.Sqrt(Math.Max(Math.Sqrt(peak * DisplayLevels) - 1.0, 0.0)));
    }

    /// <summary>The blend the caller applies after us: full sprite below 1 px across, gone by 4,
    /// where the body starts being drawn as an actual sphere instead.</summary>
    public static double SpriteMul(double pixelDiameter)
    {
        double t = Math.Clamp((pixelDiameter - 1.0) / 3.0, 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    // ---------------------------------------------------------------- reflection glue

    /// <summary>
    /// How much smaller our sprite must be for the same number. Stock's vertex shader puts the
    /// quad's half-extent at scalePixel/4 pixels; ours puts it at scalePixel, because scalePixel
    /// means a glow radius to us. Anything we do not compute a magnitude for still has to come
    /// back in OUR units, or it is drawn four times too large and, since our shader derives
    /// brightness from the radius, far too bright with it.
    /// </summary>
    public const double StockScaleToGlow = 0.25;

    private static MethodInfo? _getPositionEgo, _getCamera, _diameterPixels;
    private static PropertyInfo? _mainViewport;
    private static bool _logged, _loggedFallback;

    /// <summary>
    /// Postfix on StaticCelestialDistanceRendering.GetApparentSizeScale, replacing the game's
    /// radius-based visibility scale with one computed from the body's actual brightness.
    ///
    /// A postfix rather than a prefix so that whatever we cannot measure - a vehicle's glint, a
    /// comet, a body with no scattering data - still arrives here as the game's own value, and
    /// only needs converting into our shader's units rather than reconstructing from nothing.
    /// </summary>
    public static void ApparentSizeScalePostfix(object celestial, ref double __result)
    {
        try
        {
            double? mag = MagnitudeOf(celestial, out double pixelDiameter);
            if (mag != null)
            {
                // The caller computes scalePixel = base * ours * spriteMul, where base is the
                // true diameter in pixels, floored at 1 for anything orbiting a star. Divide
                // out the base so what survives is our glow radius, but LEAVE spriteMul: that
                // is the cross-fade to the lit sphere the game starts drawing at 2 px, and
                // keeping it is what stops a point source being painted over a resolved disc.
                __result = GlowRadiusPx(mag.Value) / Math.Max(pixelDiameter, 1.0);
                if (!_logged)
                {
                    ShaderShadow.Log($"planets on the photometric scale (first: V {mag.Value:F2})");
                    _logged = true;
                }
                return;
            }
        }
        catch (Exception ex)
        {
            if (!_loggedFallback)
            {
                ShaderShadow.Log("WARN: could not measure a body, using the game's own sizing for it: "
                                 + ex.Message);
                _loggedFallback = true;
            }
        }
        __result *= StockScaleToGlow;
    }

    /// <summary>
    /// The members we need off one concrete body type. Cached per type, because the game's
    /// celestials are a hierarchy and a member found on one is not valid on another.
    /// </summary>
    private sealed record Accessors(PropertyInfo? BodyTemplate, PropertyInfo? MeanRadius,
                                    MethodInfo? GetPositionEcl);

    private static readonly Dictionary<Type, Accessors> _accessors = new();

    /// <summary>
    /// A property by name, taking the most derived declaration. Plain GetProperty throws on
    /// these types: BodyTemplate is declared more than once down the hierarchy with different
    /// types, which reflection reports as an ambiguous match rather than resolving.
    /// </summary>
    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>Magnitude of a body, or null when it is not one we can measure.</summary>
    private static double? MagnitudeOf(object celestial, out double pixelDiameter)
    {
        pixelDiameter = 0.0;
        Type t = celestial.GetType();

        if (!_accessors.TryGetValue(t, out Accessors? acc))
            _accessors[t] = acc = new Accessors(
                FindProperty(t, "BodyTemplate"),
                FindProperty(t, "MeanRadius"),
                AccessTools.Method(t, "GetPositionEcl", Type.EmptyTypes));

        object? template = acc.BodyTemplate?.GetValue(celestial);
        if (template == null) return null;                   // vehicles, comets: not our business

        if (acc.MeanRadius?.GetValue(celestial) is not double radius || double.IsNaN(radius) || radius <= 0)
            return null;

        double albedo = Albedo(template);

        object? eclObj = acc.GetPositionEcl?.Invoke(celestial, null);
        if (eclObj == null) return null;

        _mainViewport ??= AccessTools.Property(AccessTools.TypeByName("KSA.Program")!, "MainViewport");
        object? viewport = _mainViewport?.GetValue(null);
        if (viewport == null) return null;
        _getCamera ??= AccessTools.Method(viewport.GetType(), "GetCamera");
        object? camera = _getCamera?.Invoke(viewport, null);
        if (camera == null) return null;

        _getPositionEgo ??= AccessTools.Method(camera.GetType(), "GetPositionEgo",
                                               new[] { AccessTools.TypeByName("KSA.IOrbiter")! });
        object? egoObj = _getPositionEgo?.Invoke(camera, new[] { celestial });
        if (egoObj == null) return null;

        (double ex, double ey, double ez) = Vec(eclObj);     // body relative to its star
        (double gx, double gy, double gz) = Vec(egoObj);     // body relative to the camera
        double sunDist = Math.Sqrt(ex * ex + ey * ey + ez * ez);
        double obsDist = Math.Sqrt(gx * gx + gy * gy + gz * gz);
        if (sunDist <= 0 || obsDist <= 0) return null;

        // Phase angle at the body, between its star and the camera.
        double cosPhase = (ex * gx + ey * gy + ez * gz) / (sunDist * obsDist);

        _diameterPixels ??= AccessTools.Method(camera.GetType(), "GetObjectDiameterPixels");
        if (_diameterPixels?.Invoke(camera, new object[] { 2.0 * radius, obsDist }) is double px)
            pixelDiameter = px;

        return ApparentMagnitude(radius, albedo, sunDist, obsDist, cosPhase);
    }

    private static readonly Dictionary<Type, FieldInfo?> _scatteringFields = new();
    private static readonly Dictionary<Type, FieldInfo?> _albedoFields = new();
    private static readonly Dictionary<Type, MemberInfo?> _albedoValues = new();

    /// <summary>
    /// The body's geometric albedo from its own scattering data, falling back to the same 0.5
    /// the game uses when a body has none. This is why nothing here is per-planet: a modded
    /// world's albedo and radius give its magnitude exactly as Neptune's do.
    /// </summary>
    private static double Albedo(object template)
    {
        Type tt = template.GetType();
        if (!_scatteringFields.TryGetValue(tt, out FieldInfo? sf))
            _scatteringFields[tt] = sf = AccessTools.Field(tt, "ScatteringReference");
        object? scattering = sf?.GetValue(template);
        if (scattering == null) return 0.5;

        Type st = scattering.GetType();
        if (!_albedoFields.TryGetValue(st, out FieldInfo? af))
            _albedoFields[st] = af = AccessTools.Field(st, "MeanAlbedo");
        object? albedoRef = af?.GetValue(scattering);
        if (albedoRef == null) return 0.5;

        return ReadFloat(albedoRef) ?? 0.5;
    }

    /// <summary>
    /// The number inside one of the engine's FloatReference wrappers.
    ///
    /// Its Value is a FIELD, not a property. Worth being explicit about: looking only for a
    /// property finds nothing and throws nothing, so every body quietly takes the 0.5 fallback,
    /// which is over a magnitude out on somewhere as dark as Mars.
    /// </summary>
    public static double? ReadFloat(object reference)
    {
        Type at = reference.GetType();
        if (!_albedoValues.TryGetValue(at, out MemberInfo? vm))
            _albedoValues[at] = vm = (MemberInfo?)AccessTools.Field(at, "Value") ?? FindProperty(at, "Value");

        object? value = vm switch
        {
            FieldInfo f => f.GetValue(reference),
            PropertyInfo p => p.GetValue(reference),
            _ => null
        };
        return value switch
        {
            float f => f,
            double d => d,
            _ => null
        };
    }

    /// <summary>Components of one of the engine's double3 values, whatever it calls them.</summary>
    private static (double, double, double) Vec(object v)
    {
        Type t = v.GetType();
        double Read(string upper, string lower)
        {
            object? o = (t.GetField(upper) ?? t.GetField(lower))?.GetValue(v)
                        ?? (t.GetProperty(upper) ?? t.GetProperty(lower))?.GetValue(v);
            return o is double d ? d : o is float f ? f : throw new InvalidOperationException(
                $"cannot read component {upper} of {t.Name}");
        }
        return (Read("X", "x"), Read("Y", "y"), Read("Z", "z"));
    }
}
