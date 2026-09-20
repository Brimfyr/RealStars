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
    private static MethodInfo? _getPositionEcl, _getPositionEgo, _getCamera, _diameterPixels;
    private static PropertyInfo? _meanRadius, _bodyTemplate, _mainViewport;
    private static FieldInfo? _scatteringRef, _meanAlbedo;
    private static PropertyInfo? _albedoValue;
    private static bool _failed, _logged;

    /// <summary>
    /// Prefix on StaticCelestialDistanceRendering.GetApparentSizeScale. Returns the scale that
    /// leaves the caller holding our glow radius, or defers to the original when the body is
    /// not a sunlit sphere (a vehicle, say, whose glint the game handles its own way) or when
    /// anything needed is missing.
    /// </summary>
    public static bool ApparentSizeScalePrefix(object celestial, ref double __result)
    {
        if (_failed) return true;
        try
        {
            double? mag = MagnitudeOf(celestial, out double pixelDiameter);
            if (mag == null) return true;

            // The caller computes scalePixel = base * ours * spriteMul, where base is the true
            // diameter in pixels, floored at 1 for anything orbiting a star. Undo that, so what
            // survives is exactly the radius our shader wants.
            double sprite = SpriteMul(pixelDiameter);
            if (sprite <= 1e-3) return true;                 // becoming a sphere; leave it be
            double base_ = Math.Max(pixelDiameter, 1.0);

            __result = GlowRadiusPx(mag.Value) / (base_ * sprite);
            if (!_logged)
            {
                ShaderShadow.Log($"planets on the photometric scale (first: V {mag.Value:F2})");
                _logged = true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _failed = true;
            ShaderShadow.Log("WARN: planet photometry disabled after an error; "
                             + "the game's own sizing stands. " + ex.Message);
            return true;
        }
    }

    /// <summary>Magnitude of a body, or null when it is not one we can measure.</summary>
    private static double? MagnitudeOf(object celestial, out double pixelDiameter)
    {
        pixelDiameter = 0.0;
        Type t = celestial.GetType();

        _bodyTemplate ??= t.GetProperty("BodyTemplate");
        object? template = _bodyTemplate?.GetValue(celestial);
        if (template == null) return null;                   // vehicles, comets: not our business

        _meanRadius ??= AccessTools.Property(t, "MeanRadius");
        if (_meanRadius?.GetValue(celestial) is not double radius || double.IsNaN(radius) || radius <= 0)
            return null;

        // Albedo from the body's own scattering data, with the game's own fallback.
        double albedo = 0.5;
        _scatteringRef ??= AccessTools.Field(template.GetType(), "ScatteringReference");
        object? scattering = _scatteringRef?.GetValue(template);
        if (scattering != null)
        {
            _meanAlbedo ??= AccessTools.Field(scattering.GetType(), "MeanAlbedo");
            object? albedoRef = _meanAlbedo?.GetValue(scattering);
            if (albedoRef != null)
            {
                _albedoValue ??= albedoRef.GetType().GetProperty("Value");
                object? v = _albedoValue?.GetValue(albedoRef);
                if (v is float f) albedo = f;
                else if (v is double d) albedo = d;
            }
        }

        _getPositionEcl ??= AccessTools.Method(t, "GetPositionEcl", Type.EmptyTypes);
        object? eclObj = _getPositionEcl?.Invoke(celestial, null);
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
