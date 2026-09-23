using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// How high above a body its air stops letting starlight through, published for the sprite
/// shaders so they can drop a source that has gone behind a limb rather than behind rock.
///
/// The first attempt took the altitudes as fractions of the atmosphere's VISUAL height, the
/// one number the engine already publishes. Titan came out right and Earth, Mars and Venus
/// came out far too early, which is the giveaway: a rendered atmosphere shell is sized to look
/// right, and how far up it is opaque has nothing to do with how far up it is drawn. Mars has a
/// visual atmosphere and almost no extinction at all.
///
/// So it is worked out instead, from the two numbers that decide it and that the scintillation
/// already reads: the density at the surface and the scale height. A ray grazing at height h
/// crosses a column of n(h) * sqrt(2 pi R H) - the standard limb enhancement, a few hundredfold
/// for a planet - and n falls as exp(-h/H), so the optical depth does too and the altitudes
/// follow by logarithm.
///
/// The cross section is Rayleigh, calibrated so Earth reads the 8.9 a grazing ray sees at sea
/// level at 550 nm. That makes it exact for clear air and an underestimate for anything whose
/// opacity is in its aerosols; Titan lands close anyway, because its density and scale height
/// are both large. See [[ksa-titan-disr-haze]] if the haze ever needs its own term.
/// </summary>
internal static class LimbAir
{
    // Earth at sea level, 550 nm: vertical optical depth 0.13 over an 8.5 km scale height.
    private const double EarthDensity = 1.225;          // kg/m3
    private const double EarthScaleHeight = 8500.0;     // m
    private const double EarthRadius = 6371000.0;       // m
    private const double EarthLimbDepth = 8.93;         // what a grazing ray sees at the surface

    /// <summary>Opacity at which a source is gone, and the one at which it is back.</summary>
    private const double OpaqueDepth = 4.0;
    private const double ClearDepth = 0.1;

    private static FieldInfo? _celestialArray, _pad0, _pad1, _pad2;
    private static readonly Dictionary<Type, PropertyInfo?> _meanRadius = new();
    private static bool _logged;

    /// <summary>
    /// The altitudes, in metres above the surface, at which a grazing ray goes opaque and comes
    /// clear. Both zero when the body has no air worth the name, which is how the shader knows
    /// to leave the limb to the geometry.
    /// </summary>
    public static (double Opaque, double Clear) Altitudes(double density, double scaleHeight,
                                                          double meanRadius)
    {
        if (density <= 0.0 || scaleHeight <= 0.0 || meanRadius <= 0.0) return (0.0, 0.0);

        double kappa = EarthLimbDepth
                     / (EarthDensity * Math.Sqrt(2.0 * Math.PI * EarthRadius * EarthScaleHeight));
        double atSurface = kappa * density * Math.Sqrt(2.0 * Math.PI * meanRadius * scaleHeight);
        if (atSurface <= ClearDepth) return (0.0, 0.0);      // Mars: the limb is a limb, no more

        double opaque = Math.Max(scaleHeight * Math.Log(atSurface / OpaqueDepth), 0.0);
        double clear = Math.Max(scaleHeight * Math.Log(atSurface / ClearDepth), opaque + 1.0);
        return (opaque, clear);
    }

    /// <summary>
    /// Writes them into the celestial block's spare words, every frame, for the body the camera
    /// is at. Zero when there is none, so nothing stale is ever left behind - which is the same
    /// trap the engine's own atmosphere height falls into.
    /// </summary>
    public static void Publish(object program, object nearby, int slot, object viewport)
    {
        try
        {
            double opaque = 0.0, clear = 0.0;
            if (nearby != null)
            {
                (double density, double scaleHeight) = Scintillation.MeasureAir(nearby, viewport);
                Type nt = nearby.GetType();
                if (!_meanRadius.TryGetValue(nt, out PropertyInfo? radiusProp))
                    _meanRadius[nt] = radiusProp = PlanetPhotometry.FindPropertyPublic(nt, "MeanRadius");
                if (radiusProp?.GetValue(nearby) is double radius)
                    (opaque, clear) = Altitudes(density, scaleHeight, radius);

                if (!_logged && clear > 0.0)
                {
                    ShaderShadow.Log($"{nt.Name}'s limb: opaque to {opaque / 1000.0:F0} km, "
                                     + $"clear above {clear / 1000.0:F0} km");
                    _logged = true;
                }
            }

            _celestialArray ??= AccessTools.Field(program.GetType(), "_celestialData");
            if (_celestialArray?.GetValue(null) is not Array celestial || slot >= celestial.Length) return;
            object box = celestial.GetValue(slot)!;
            _pad0 ??= AccessTools.Field(box.GetType(), "pad0");
            _pad1 ??= AccessTools.Field(box.GetType(), "pad1");
            _pad2 ??= AccessTools.Field(box.GetType(), "pad2");
            if (_pad0 == null || _pad1 == null) return;
            _pad0.SetValue(box, BitConverter.SingleToInt32Bits((float)opaque));
            _pad1.SetValue(box, BitConverter.SingleToInt32Bits((float)clear));
            // The third word is the Sun's share hidden by vessels, which SunGlow fills in later
            // this same frame. Cleared here so that a frame which never gets that far cannot
            // leave last frame's value behind.
            _pad2?.SetValue(box, 0);
            celestial.SetValue(box, slot);
        }
        catch (Exception ex)
        {
            if (!_logged)
            {
                ShaderShadow.Log("WARN: the limb's air could not be measured: " + ex.Message);
                _logged = true;
            }
        }
    }
}
