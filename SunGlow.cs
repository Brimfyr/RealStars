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
/// Feeding that pass better numbers does not work, which a first attempt proved: sunbloom.frag
/// scales the flare's own colour by the radius it is given, so a smaller radius does not draw a
/// smaller sun, it draws a dimmer one, until by Saturn there is nothing left.
///
/// So the Sun is not drawn by that pass at all once it is a point. It sits in our catalogue
/// like any other star, at the origin with the Sun's absolute magnitude, and the star shader
/// draws it with the same profile as everything else in the sky - brightening correctly as you
/// approach, shrinking as you leave. This class only manages the handover: the engine's sprite
/// fades out as the disc falls below a couple of pixels, ours fades in, and the last spare int
/// of the lighting uniform carries the crossfade to the shader.
/// </summary>
internal static class SunGlow
{
    /// <summary>Apparent magnitude of the Sun at 1 AU. The rest is distance and size.</summary>
    public const double SolarMagnitudeAt1Au = -26.74;
    public const double SolarRadiusM = 6.957e8;
    private const double Au = 1.495978707e11;

    /// <summary>
    /// The handover, in pixels of the Sun's disc RADIUS. The engine's sprite holds above the
    /// first, ours holds below the second, and they cross over between.
    ///
    /// These have to be read in AU to mean anything, because the disc radius is about
    /// 6.5e11 / distance: a window that sounds tight in pixels can be an astronomical unit
    /// wide. Two attempts got this wrong in the same way, each time leaving a stretch where the
    /// sphere had shrunk away and ours had not arrived, so the engine's hundred-pixel halo was
    /// the only thing drawing the Sun - the soft gradient blob.
    ///
    ///     2 px -> 1 px      2.2 to 4.3 AU    Ceres to nearly Jupiter
    ///     3 px -> 2.5 px    1.45 to 1.74 AU  just past Mars
    ///     4.5 px -> 3.5 px  0.97 to 1.24 AU  just past Earth, where the halo takes over
    ///
    /// The last is the one that matters: the halo starts dominating the look a little after
    /// Earth, while the sphere is still being drawn, so ours has to be in by then. The two
    /// overlapping for a fraction of an AU is fine - a small disc with glare around it is what
    /// the Sun looks like there.
    ///
    /// The shader mirrors both numbers; the checks convert them back into AU and fail if they
    /// drift, because in pixels this mistake is invisible.
    /// </summary>
    public const double HandoverStartPx = 4.5;     // engine alone above this
    public const double HandoverEndPx = 3.5;       // ours alone below it

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

    private static FieldInfo? _flareArray, _sunRadiusField, _glowRadiusField, _lightingArray, _lpPad1;
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

            double discPx = 0.5 * diameterPx;

            // The handover. While the Sun is a resolved disc the engine draws it, and does it
            // well; once it is a point the star shader does, from the catalogue, with the same
            // profile as every other star. This crossfades between them over the pixel where
            // it stops being one and starts being the other.
            float asPoint = (float)Smoothstep(HandoverStartPx, HandoverEndPx, discPx);

            if (!_shaderSlots.TryGetValue(vt, out PropertyInfo? slotProp))
                _shaderSlots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot || slot < 0) return;

            // Fade the engine's sprite out rather than resizing it. Feeding it a smaller radius
            // does not make a smaller sun: sunbloom.frag scales the flare's own colour by that
            // radius, so it dims and disappears instead, which is what a first attempt did.
            _flareArray ??= AccessTools.Field(__instance.GetType(), "_sunflareData");
            if (_flareArray?.GetValue(_flareArray.IsStatic ? null : __instance) is Array flare
                && slot < flare.Length)
            {
                object box = flare.GetValue(slot)!;
                _sunRadiusField ??= AccessTools.Field(box.GetType(), "ScreenspaceSunRadius");
                _glowRadiusField ??= AccessTools.Field(box.GetType(), "ScreenspaceGlowRadius");
                if (_sunRadiusField == null || _glowRadiusField == null)
                {
                    _consecutiveFailures = GiveUpAfter;
                    ShaderShadow.Log("WARN: sun flare fields not found; the Sun keeps its stock sprite");
                    return;
                }
                if (asPoint > 0.0f)
                {
                    _sunRadiusField.SetValue(box, (float)_sunRadiusField.GetValue(box)! * (1.0f - asPoint));
                    _glowRadiusField.SetValue(box, (float)_glowRadiusField.GetValue(box)! * (1.0f - asPoint));
                    flare.SetValue(box, slot);
                }
            }

            // And tell the star shader where we are in that handover, through the last spare
            // int of the lighting uniform.
            _lightingArray ??= AccessTools.Field(__instance.GetType(), "_lightingData");
            if (_lightingArray?.GetValue(null) is Array lighting && slot < lighting.Length)
            {
                object lbox = lighting.GetValue(slot)!;
                _lpPad1 ??= AccessTools.Field(lbox.GetType(), "lpPad1");
                _lpPad1?.SetValue(lbox, BitConverter.SingleToInt32Bits((float)discPx));
                lighting.SetValue(lbox, slot);
            }
            _consecutiveFailures = 0;

            if (!_logged)
            {
                ShaderShadow.Log($"sun handover live: V {Magnitude(distance, radius):F1} at "
                                 + $"{distance / Au:F2} AU, disc {discPx:F2} px, "
                                 + $"{asPoint * 100:F0}% drawn as a star");
                _logged = true;
            }
        }
        catch (Exception ex)
        {
            if (_consecutiveFailures++ == 0)
                ShaderShadow.Log("WARN: sun glow hit an error (retrying): " + ex.Message);
        }
    }

    /// <summary>The shader's smoothstep, so both ends of the handover agree.</summary>
    public static double Smoothstep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
