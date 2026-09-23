using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The air around the body the camera is at, published for the sprite shaders and the Sun's
/// burst, so a source behind a limb's haze fades as the Sun itself does.
///
/// The engine dims what it draws through an atmosphere with the atmosphere's VISUAL block:
/// Rayleigh and Mie extinction coefficients, each with its own scale height. The same numbers
/// drive the rendered sky, and they are what makes Titan yellow. So they are published as they
/// are, and the shader works out the slant optical depth from them per source - the engine's
/// own Chapman approximation, the one its CPU transmittance uses for distant glints, with its
/// constant corrected so that a grazing ray reads the true limb column.
///
/// Two earlier versions got this wrong, in instructive ways. The first took altitudes as
/// fractions of the visual HEIGHT, which says how far up the sky is drawn and nothing about how
/// far up it is opaque; Mars has a visual atmosphere and next to no extinction. The second
/// worked opacity out from surface density and scale height with a Rayleigh cross section,
/// which is exact for clear gas and blind to aerosols - and Titan's haze is aerosols. The burst
/// followed the Sun down through the clear upper air and then outlived it through the yellow.
/// </summary>
internal static class LimbAir
{
    /// <summary>Rec. 709 luminance: one number from three channels, weighted the way an eye is.</summary>
    private const double LumR = 0.2126, LumG = 0.7152, LumB = 0.0722;

    /// <summary>What was measured for a body: vertical optical depth and scale height, for each part.</summary>
    public readonly record struct Air(double RayleighDepth, double RayleighHeightM,
                                      double MieDepth, double MieHeightM);

    private static FieldInfo? _celestialArray, _pad0, _pad1, _pad2;
    private static readonly Dictionary<Type, PropertyInfo?> _templates = new();
    private static readonly Dictionary<(Type, string), MemberInfo?> _members = new();
    private static readonly Dictionary<object, Air?> _measured = new(ReferenceEqualityComparer.Instance);
    private static bool _logged, _warned;

    /// <summary>
    /// Reads the Visual block. The coefficients arrive in metres: the engine divides the XML's
    /// per-kilometre figures by a thousand as it loads them. Mie is scattering, and extinction is
    /// scattering times the absorption multiplier - which is what the engine uses for it too.
    /// </summary>
    public static Air? Measure(object body)
    {
        if (_measured.TryGetValue(body, out Air? known)) return known;

        Air? air = null;
        object? visual = Walk(Walk(Template(body), "AtmosphereReference"), "Visual");
        object? rayleigh = Walk(visual, "RayleighScattering");
        object? mie = Walk(visual, "MieScattering");
        if (rayleigh != null || mie != null)
        {
            (double rBeta, double rHeight) = Part(rayleigh, 1.0);
            double multiplier = mie == null ? 1.0 : PlanetPhotometry.ReadFloat(Walk(mie, "AbsorptionMultiplier")) ?? 1.0;
            (double mBeta, double mHeight) = Part(mie, multiplier);
            if (rBeta * rHeight > 0.0 || mBeta * mHeight > 0.0)
                air = new Air(rBeta * rHeight, rHeight, mBeta * mHeight, mHeight);
        }

        _measured[body] = air;
        if (!_logged && air is Air a)
        {
            ShaderShadow.Log($"{body.GetType().Name}'s air: Rayleigh depth {a.RayleighDepth:G3} over "
                             + $"{a.RayleighHeightM / 1000:F1} km, Mie {a.MieDepth:G3} over {a.MieHeightM / 1000:F1} km");
            _logged = true;
        }
        return air;
    }

    /// <summary>Luminance-weighted extinction per metre, and the scale height in metres.</summary>
    private static (double, double) Part(object? scattering, double multiplier)
    {
        object? coefficients = Walk(scattering, "Coefficients");
        if (coefficients == null) return (0.0, 0.0);
        double r = Walk(coefficients, "R") is double rv ? rv : 0.0;
        double g = Walk(coefficients, "G") is double gv ? gv : 0.0;
        double b = Walk(coefficients, "B") is double bv ? bv : 0.0;
        double height = PlanetPhotometry.ReadFloat(Walk(scattering, "ScaleHeight")) ?? 0.0;
        return ((LumR * r + LumG * g + LumB * b) * multiplier, height);
    }

    /// <summary>
    /// Writes the body's air into the celestial block's first two spare words, every frame, as
    /// half-floats: vertical optical depth and scale height in kilometres, Rayleigh then Mie.
    /// Zero when there is no air, so nothing stale is ever left behind - the trap the engine's
    /// own atmosphere height falls into. The third word is cleared for the Sun's vessel share.
    /// </summary>
    public static void Publish(object program, object? nearby, int slot)
    {
        try
        {
            Air air = nearby == null ? default : Measure(nearby) ?? default;

            _celestialArray ??= AccessTools.Field(program.GetType(), "_celestialData");
            if (_celestialArray?.GetValue(null) is not Array celestial || slot >= celestial.Length) return;
            object box = celestial.GetValue(slot)!;
            _pad0 ??= AccessTools.Field(box.GetType(), "pad0");
            _pad1 ??= AccessTools.Field(box.GetType(), "pad1");
            _pad2 ??= AccessTools.Field(box.GetType(), "pad2");
            if (_pad0 == null || _pad1 == null) return;
            _pad0.SetValue(box, Halves(air.RayleighDepth, air.RayleighHeightM / 1000.0));
            _pad1.SetValue(box, Halves(air.MieDepth, air.MieHeightM / 1000.0));
            // The Sun's share hidden by vessels, which SunGlow fills in later this same frame.
            // Cleared here so a frame which never gets that far cannot leave last frame's behind.
            _pad2?.SetValue(box, 0);
            celestial.SetValue(box, slot);
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                ShaderShadow.Log("WARN: the air around the body could not be read: " + ex.Message);
                _warned = true;
            }
        }
    }

    /// <summary>Two numbers as the half-floats unpackHalf2x16 reads: the first in the low bits.</summary>
    public static int Halves(double low, double high)
    {
        uint lo = BitConverter.HalfToUInt16Bits((Half)low);
        uint hi = BitConverter.HalfToUInt16Bits((Half)high);
        return unchecked((int)(lo | (hi << 16)));
    }

    private static object? Template(object body)
    {
        Type t = body.GetType();
        if (!_templates.TryGetValue(t, out PropertyInfo? p))
            _templates[t] = p = PlanetPhotometry.FindPropertyPublic(t, "BodyTemplate");
        return p?.GetValue(body);
    }

    /// <summary>
    /// A field or property by name, cached per concrete type and name - the lesson of this
    /// mod's history, where a member found on one type was reused on another and read nothing.
    /// </summary>
    private static object? Walk(object? from, string name)
    {
        if (from == null) return null;
        var key = (from.GetType(), name);
        if (!_members.TryGetValue(key, out MemberInfo? member))
            _members[key] = member = (MemberInfo?)AccessTools.Field(key.Item1, name)
                                     ?? AccessTools.Property(key.Item1, name);
        return member switch
        {
            FieldInfo f => f.GetValue(from),
            PropertyInfo p => p.GetValue(from),
            _ => null,
        };
    }
}
