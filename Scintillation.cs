using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Feeds the star shader the two numbers it needs to twinkle, because it cannot work them
/// out for itself.
///
/// Scintillation is not one effect but three, and each scales differently:
///
///   amplitude  The scintillation index goes as the turbulence strength, which goes as the
///              square of density fluctuations - so it scales with AIR DENSITY, nearly
///              linearly, and spans four orders of magnitude across the solar system. Earth
///              shimmers, Mars is 60 times thinner and barely moves, Pluto is vacuum.
///   threshold  A source wider than the intensity pattern's angular scale averages over many
///              cells and holds steady. That scale is sqrt(lambda / h) for turbulence at
///              height h, which tracks the scale height: 1.5 arcsec on Earth, 1.0 on Titan.
///              It is the reason planets do not twinkle and stars do.
///   colour     Refraction is wavelength dependent, so a star low down smears into a tiny
///              spectrum. Once that smear passes the threshold above, red and blue sample
///              different turbulence and flicker independently: the star flashes colours.
///              The smear scales with density too, so thin air neither twinkles nor flashes.
///
/// Only the Earth calibration points are constants here. Everything else comes from the
/// body's own sea-level density and scale height, so Titan and a modded world are handled on
/// the same terms.
///
/// The injection exists because the engine's UpdatePlanetShaderData only refreshes its planet
/// fields for an AtmosphericBody: approach an airless moon and they keep the last atmosphere's
/// values, which would have stars twinkling over the Moon. Running as a postfix on that same
/// method means we write zero whenever the body is not one with air.
/// </summary>
internal static class Scintillation
{
    /// <summary>Naked-eye scintillation index at the zenith, at Earth's sea level.</summary>
    public const double SigmaEarthZenith = 0.25;
    public const double EarthSeaLevelDensity = 1.225;
    /// <summary>Turbulent layer height as a multiple of the scale height: Earth's is ~10 km.</summary>
    public const double TurbulentLayerScale = 1.2;
    public const double Wavelength = 550e-9;

    /// <summary>
    /// Scintillation index at the zenith for an observer this far above the surface, and the
    /// angular scale below which a source twinkles at all, in radians. Zero and zero when
    /// there is no air to speak of.
    /// </summary>
    public static (double Sigma, double ThetaC) ForObserver(double seaLevelDensity,
                                                            double scaleHeightM,
                                                            double altitudeM)
    {
        if (seaLevelDensity <= 0.0 || scaleHeightM <= 0.0) return (0.0, 0.0);

        double density = seaLevelDensity * Math.Exp(-Math.Max(altitudeM, 0.0) / scaleHeightM);
        double sigma = Math.Min(SigmaEarthZenith * density / EarthSeaLevelDensity, 1.0);
        if (sigma < 1e-4) return (0.0, 0.0);                 // vacuum for all practical purposes

        double thetaC = Math.Sqrt(Wavelength / (TurbulentLayerScale * scaleHeightM));
        return (sigma, thetaC);
    }

    // ------------------------------------------------------------------ reflection glue
    private static FieldInfo? _lightingArray;
    private static PropertyInfo? _meanRadius, _bodyTemplateProp;
    private static FieldInfo? _atmosphereField, _physicalField, _densityField, _scaleHeightField;
    private static readonly Dictionary<Type, MethodInfo?> _positionEgos = new();
    private static FieldInfo? _pad0, _pad1;
    private static bool _logged;

    // Per TYPE, because the game has more than one kind of viewport and more than one kind of
    // camera: a member resolved from a part thumbnail's viewport throws when handed the game's.
    // Caching one of each was what froze scintillation at whatever the first frame saw.
    private static readonly Dictionary<Type, PropertyInfo?> _shaderSlots = new();
    private static readonly Dictionary<Type, MethodInfo?> _cameras = new();

    // A bad frame should cost that frame, not the feature. Only a persistent fault disables it.
    private static int _consecutiveFailures;
    private const int GiveUpAfter = 120;

    /// <summary>
    /// Postfix on PlanetRenderer.UpdatePlanetShaderData: work out the observer's air and leave
    /// it in the two spare floats of the lighting uniform, which nothing else uses.
    /// </summary>
    public static void UpdatePlanetShaderDataPostfix(object __instance, object? nearbyCelestial, object viewport)
    {
        if (_consecutiveFailures >= GiveUpAfter) return;
        try
        {
            double sigma = 0.0, thetaC = 0.0;
            if (nearbyCelestial != null)
                (sigma, thetaC) = Measure(nearbyCelestial, viewport);

            // Static, and declared on Program itself rather than on a renderer.
            _lightingArray ??= AccessTools.Field(__instance.GetType(), "_lightingData");
            if (_lightingArray?.GetValue(null) is not Array lighting) return;

            Type vt = viewport.GetType();
            if (!_shaderSlots.TryGetValue(vt, out PropertyInfo? slotProp))
                _shaderSlots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot
                || slot < 0 || slot >= lighting.Length) return;

            // A struct in an array: take a copy, set the private pads, put it back.
            object box = lighting.GetValue(slot)!;
            _pad0 ??= AccessTools.Field(box.GetType(), "csPad0");
            _pad1 ??= AccessTools.Field(box.GetType(), "csPad1");
            if (_pad0 == null || _pad1 == null) { _consecutiveFailures = GiveUpAfter; return; }
            _pad0.SetValue(box, (float)sigma);
            _pad1.SetValue(box, (float)thetaC);
            lighting.SetValue(box, slot);
            _consecutiveFailures = 0;

            if (!_logged && sigma > 0.0)
            {
                ShaderShadow.Log($"scintillation live: sigma {sigma:F3} at the zenith, "
                                 + $"sources under {thetaC * 206265.0:F2} arcsec twinkle");
                _logged = true;
            }
        }
        catch (Exception ex)
        {
            // One bad frame must not cost the feature: giving up permanently is what froze the
            // last good value into the uniform, so the sky kept twinkling on the Moon.
            if (_consecutiveFailures++ == 0)
                ShaderShadow.Log("WARN: scintillation hit an error (retrying): " + ex.Message);
            else if (_consecutiveFailures == GiveUpAfter)
                ShaderShadow.Log("WARN: scintillation failing persistently; stars will hold steady");
        }
    }

    private static bool _diagnosed;

    /// <summary>
    /// Says once, on the first body we look at, what we found and what came of it. A silent
    /// zero here is indistinguishable from working correctly in space, which is exactly how a
    /// density reader that always returned nothing went unnoticed until someone looked up.
    /// </summary>
    private static (double, double) Diagnose(string what, double sigma, double thetaC)
    {
        if (!_diagnosed)
        {
            ShaderShadow.Log($"scintillation: {what} -> sigma {sigma:F4}"
                             + (thetaC > 0 ? $", sources under {thetaC * 206265.0:F2} arcsec twinkle" : ""));
            _diagnosed = true;
        }
        return (sigma, thetaC);
    }

    /// <summary>The observer's air, from the body they are near.</summary>
    private static (double, double) Measure(object celestial, object viewport)
    {
        string name = celestial.GetType().Name;

        _bodyTemplateProp ??= PlanetPhotometry.FindPropertyPublic(celestial.GetType(), "BodyTemplate");
        object? template = _bodyTemplateProp?.GetValue(celestial);
        if (template == null) return Diagnose($"{name} has no body template", 0.0, 0.0);

        _atmosphereField ??= AccessTools.Field(template.GetType(), "AtmosphereReference");
        object? atmosphere = _atmosphereField?.GetValue(template);
        if (atmosphere == null) return Diagnose($"{name} is airless", 0.0, 0.0);

        _physicalField ??= AccessTools.Field(atmosphere.GetType(), "Physical");
        object? physical = _physicalField?.GetValue(atmosphere);
        if (physical == null) return Diagnose($"{name} has no physical atmosphere", 0.0, 0.0);

        _densityField ??= AccessTools.Field(physical.GetType(), "SeaLevelDensity");
        _scaleHeightField ??= AccessTools.Field(physical.GetType(), "ScaleHeight");
        double density = Reference(_densityField?.GetValue(physical));
        double scaleHeight = Reference(_scaleHeightField?.GetValue(physical));
        if (density <= 0.0 || scaleHeight <= 0.0)
            return Diagnose($"{name} read as density {density:G3} kg/m3, scale height {scaleHeight:G3} m",
                            0.0, 0.0);

        _meanRadius ??= PlanetPhotometry.FindPropertyPublic(celestial.GetType(), "MeanRadius");
        if (_meanRadius?.GetValue(celestial) is not double radius)
            return Diagnose($"{name} has no readable radius", 0.0, 0.0);

        Type vt = viewport.GetType();
        if (!_cameras.TryGetValue(vt, out MethodInfo? getCamera))
            _cameras[vt] = getCamera = AccessTools.Method(vt, "GetCamera");
        object? camera = getCamera?.Invoke(viewport, null);
        if (camera == null) return Diagnose("no camera on the viewport", 0.0, 0.0);
        // Per camera type too: a thumbnail viewport's camera need not be the game's class.
        Type ct = camera.GetType();
        if (!_positionEgos.TryGetValue(ct, out MethodInfo? positionEgo))
            _positionEgos[ct] = positionEgo = AccessTools.Method(ct, "GetPositionEgo",
                                                new[] { AccessTools.TypeByName("KSA.IOrbiter")! });
        object? ego = positionEgo?.Invoke(camera, new[] { celestial });
        if (ego == null) return Diagnose($"no camera position relative to {name}", 0.0, 0.0);

        (double x, double y, double z) = PlanetPhotometry.VecPublic(ego);
        double altitude = Math.Sqrt(x * x + y * y + z * z) - radius;

        (double sigma, double thetaC) = ForObserver(density, scaleHeight, altitude);
        return Diagnose($"{name}, {density:G3} kg/m3 at the surface, scale height {scaleHeight / 1000:F1} km, "
                        + $"camera {altitude / 1000:F0} km up", sigma, thetaC);
    }

    /// <summary>
    /// A DensityReference or DistanceReference, as a plain number in SI units. InMeters() first
    /// where it exists, because a distance says nothing about its own units otherwise.
    /// </summary>
    public static double Reference(object? reference)
    {
        if (reference == null) return 0.0;

        MethodInfo? inMeters = AccessTools.Method(reference.GetType(), "InMeters");
        if (inMeters?.Invoke(reference, null) is double m) return m;

        return PlanetPhotometry.ReadFloat(reference) ?? 0.0;
    }
}
