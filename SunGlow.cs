using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The Sun, once it is too far away to be drawn as a sphere.
///
/// The engine hands its flare pass two numbers, and both are floored:
///
///     sunRadius  = 0.5 * diameterInPixels, but never below 4
///     glowRadius = max(sunRadius * 50, 100)
///
/// So past a couple of astronomical units the Sun stops shrinking: it holds an eight-pixel
/// disc and a hundred-pixel halo however far you go, which is why it looks wrong from Jupiter
/// and worse from Pluto. The halo's profile, exp(-smoothstep(0,1,x)*8), is also why its edge
/// reads as a soft gradient rather than a star.
///
/// Both numbers are replaced here. The disc gets its true size, which is allowed to fall below
/// a pixel and vanish, and the glow gets the radius our own star shader would give a point
/// source of the Sun's apparent magnitude - the same law, the same cap, so the Sun sits on the
/// same scale as every star around it. From Jupiter that is about 33 px against a floor of 100,
/// and from Pluto 21.
/// </summary>
internal static class SunGlow
{
    /// <summary>Apparent magnitude of the Sun at 1 AU. The rest is distance and size.</summary>
    public const double SolarMagnitudeAt1Au = -26.74;
    public const double SolarRadiusM = 6.957e8;
    private const double Au = 1.495978707e11;

    /// <summary>
    /// What a star of this radius would look like from this distance, in magnitudes.
    ///
    /// Scaled from the Sun by surface area, which assumes solar surface brightness: exact for
    /// Sol, and the right direction for a modded star, since the template carries a radius but
    /// no luminosity or temperature to do better with.
    /// </summary>
    public static double Magnitude(double distanceM, double radiusM)
    {
        double au = Math.Max(distanceM, 1.0) / Au;
        double sizeRatio = Math.Max(radiusM, 1.0) / SolarRadiusM;
        return SolarMagnitudeAt1Au + 5.0 * Math.Log10(au) - 5.0 * Math.Log10(sizeRatio);
    }

    private static FieldInfo? _flareArray, _sunRadiusField, _glowRadiusField;
    private static PropertyInfo? _worldSun;
    private static MethodInfo? _sunPositionEcl, _cameraPositionEcl, _diameterPixels;
    private static readonly Dictionary<Type, PropertyInfo?> _shaderSlots = new();
    private static readonly Dictionary<Type, MethodInfo?> _cameras = new();
    private static int _consecutiveFailures;
    private static bool _logged;
    private const int GiveUpAfter = 120;

    /// <summary>
    /// Postfix on Program.UpdateShaderData: overwrite the two flare sizes after the engine has
    /// set them. A postfix rather than a prefix because everything else it fills - position,
    /// depth, colour, occlusion - is wanted exactly as it is.
    /// </summary>
    public static void UpdateShaderDataPostfix(object __instance, object viewport)
    {
        if (_consecutiveFailures >= GiveUpAfter) return;
        try
        {
            _worldSun ??= AccessTools.Property(AccessTools.TypeByName("KSA.Universe")!, "WorldSun");
            object? sun = _worldSun?.GetValue(null);
            if (sun == null) return;

            if (PlanetPhotometry.FindPropertyPublic(sun.GetType(), "MeanRadius")?.GetValue(sun)
                is not double radius || radius <= 0.0) return;

            Type vt = viewport.GetType();
            if (!_cameras.TryGetValue(vt, out MethodInfo? getCamera))
                _cameras[vt] = getCamera = AccessTools.Method(vt, "GetCamera");
            object? camera = getCamera?.Invoke(viewport, null);
            if (camera == null) return;

            // The star sits at the system's origin, near enough, but take its position anyway:
            // a modded system need not agree.
            _sunPositionEcl ??= AccessTools.Method(sun.GetType(), "GetPositionEcl", Type.EmptyTypes);
            _cameraPositionEcl ??= AccessTools.PropertyGetter(camera.GetType(), "PositionEcl");
            object? sunEcl = _sunPositionEcl?.Invoke(sun, null);
            object? camEcl = _cameraPositionEcl?.Invoke(camera, null);
            if (sunEcl == null || camEcl == null) return;

            (double sx, double sy, double sz) = PlanetPhotometry.VecPublic(sunEcl);
            (double cx, double cy, double cz) = PlanetPhotometry.VecPublic(camEcl);
            double dx = cx - sx, dy = cy - sy, dz = cz - sz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= 0.0) return;

            _diameterPixels ??= AccessTools.Method(camera.GetType(), "GetObjectDiameterPixels");
            if (_diameterPixels?.Invoke(camera, new object[] { 2.0 * radius, distance }) is not double diameterPx)
                return;

            double discPx = 0.5 * diameterPx;                       // no floor: let it vanish
            double glowPx = PlanetPhotometry.GlowRadiusPx(Magnitude(distance, radius));

            _flareArray ??= AccessTools.Field(__instance.GetType(), "_sunflareData");
            if (_flareArray?.GetValue(_flareArray.IsStatic ? null : __instance) is not Array flare) return;

            if (!_shaderSlots.TryGetValue(vt, out PropertyInfo? slotProp))
                _shaderSlots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot || slot < 0 || slot >= flare.Length) return;

            object box = flare.GetValue(slot)!;
            _sunRadiusField ??= AccessTools.Field(box.GetType(), "ScreenspaceSunRadius");
            _glowRadiusField ??= AccessTools.Field(box.GetType(), "ScreenspaceGlowRadius");
            if (_sunRadiusField == null || _glowRadiusField == null)
            {
                _consecutiveFailures = GiveUpAfter;
                ShaderShadow.Log("WARN: sun flare fields not found; the Sun keeps its stock sprite");
                return;
            }
            _sunRadiusField.SetValue(box, (float)discPx);
            _glowRadiusField.SetValue(box, (float)glowPx);
            flare.SetValue(box, slot);
            _consecutiveFailures = 0;

            if (!_logged)
            {
                ShaderShadow.Log($"sun on the star scale: V {Magnitude(distance, radius):F1} at "
                                 + $"{distance / Au:F2} AU, disc {discPx:F1} px, glow {glowPx:F0} px "
                                 + "(stock floors these at 4 and 100)");
                _logged = true;
            }
        }
        catch (Exception ex)
        {
            if (_consecutiveFailures++ == 0)
                ShaderShadow.Log("WARN: sun glow hit an error (retrying): " + ex.Message);
        }
    }
}
