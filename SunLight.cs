using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// A star's &lt;Sunlight&gt; is its raw colour: the colour of its light against the display's white, at
/// whatever brightness the author wants. The catalogue stores every star's colour the same way, from real
/// spectra (star_colours.py), and Adaptation turns both into what an eye in that light sees.
///
/// The stock Sol's (9, 9, 9) predates this: it is the Sun already seen as white. It is read here as the
/// Sun's raw colour at the same luminance, so Sol lights, and is adapted to, like any other star.
///
/// The stock interstellar stars' lights are paler than their stars: Proxima's (9, 5.8, 3.4) is the colour of a
/// 4,000 K blackbody, much less saturated than Proxima's own. Each is read as its star's colour in the catalogue,
/// at the luminance the game gave it, the same way. The colours are generated with the catalogue
/// (SunLight.Generated.cs), so a star's light and the star in the sky are one colour.
/// </summary>
internal static partial class SunLight
{
    private const string StockSunId = "Sol";
    private const float StockSunLight = 9f;

    // A template's data can be loaded again (a mod reload): its light is restated once.
    private static readonly ConditionalWeakTable<object, object> Restated = new();

    private static FieldInfo? _light, _r, _g, _b, _indexed;
    private static PropertyInfo? _id;
    private static MethodInfo? _lightLoad;
    private static object? _noIndexedColor;

    /// <summary>The stock Sol's light as the Sun's raw colour, at the luminance (Rec. 709) of (9, 9, 9).</summary>
    internal static float[] StockSunRaw() => AtLuminance(SunRaw, StockSunLight);

    /// <summary>A stock interstellar star's light as its star's colour in the catalogue, at the luminance of the
    /// light the game gives it; null for any other star, or a light that is not the stock one, so a mod's own
    /// choice stands.</summary>
    internal static (float[] Raw, string Source)? StockStarRaw(string id, float r, float g, float b)
    {
        if (!StockStars.TryGetValue(id, out var stock) || r != stock.R || g != stock.G || b != stock.B)
            return null;
        return (AtLuminance(stock.Raw, Luminance(r, g, b)), stock.Source);
    }

    private static float Luminance(float r, float g, float b) => 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;

    private static float[] AtLuminance(float[] colour, float luminance)
    {
        float k = luminance / Luminance(colour[0], colour[1], colour[2]);
        return new[] { k * colour[0], k * colour[1], k * colour[2] };
    }

    /// <summary>Finds what the postfix touches; false leaves every star's light as the game reads it.</summary>
    internal static bool Resolve(Type stellarTemplate, Type modType)
    {
        _light = AccessTools.Field(stellarTemplate, "LightColorRgb");
        Type? reference = _light?.FieldType;
        if (reference == null)
            return false;
        _r = AccessTools.Field(reference, "R");
        _g = AccessTools.Field(reference, "G");
        _b = AccessTools.Field(reference, "B");
        _indexed = AccessTools.Field(reference, "IndexedColor");
        _lightLoad = AccessTools.Method(reference, "OnDataLoad", new[] { modType });
        _id = AccessTools.Property(stellarTemplate, "Id");
        _noIndexedColor = _indexed != null && _indexed.FieldType.IsEnum
            && Enum.IsDefined(_indexed.FieldType, "Invalid") ? Enum.Parse(_indexed.FieldType, "Invalid") : null;
        return _r != null && _g != null && _b != null && _lightLoad != null && _id != null && _noIndexedColor != null;
    }

    /// <summary>Postfix on StellarBodyTemplate.OnDataLoad: the stock Sol's white light, restated as the
    /// Sun's raw colour, and the stock interstellar stars' lights as their stars' colours, then rebuilt. Every
    /// other star's light is already raw and is left alone.</summary>
    public static void OnDataLoadPostfix(object __instance, object mod)
    {
        try
        {
            object? light = _light!.GetValue(__instance);
            if (light == null || Restated.TryGetValue(light, out _))
                return;
            string id = _id!.GetValue(__instance) as string ?? "";
            float r = (float)_r!.GetValue(light)!, g = (float)_g!.GetValue(light)!, b = (float)_b!.GetValue(light)!;
            float[] raw;
            string why;
            if (id == StockSunId && r == StockSunLight && g == StockSunLight && b == StockSunLight)
            {
                raw = StockSunRaw();
                why = "the Sun's raw colour";
            }
            else if (StockStarRaw(id, r, g, b) is { } star)
            {
                raw = star.Raw;
                why = "its star's colour in the catalogue (" + star.Source + ")";
            }
            else
                return;
            Restated.Add(light, light);

            _r.SetValue(light, raw[0]);
            _g.SetValue(light, raw[1]);
            _b.SetValue(light, raw[2]);
            _indexed!.SetValue(light, _noIndexedColor);   // so the rebuild keeps these, not a named colour's
            _lightLoad!.Invoke(light, new[] { mod });
            ShaderShadow.Log(FormattableString.Invariant(
                $"light of {id}: stock ({r:0.###}, {g:0.###}, {b:0.###}) read as {why}: ({raw[0]:0.###}, {raw[1]:0.###}, {raw[2]:0.###})"));
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: a star's light was left as the game reads it: " + ex.Message);
        }
    }
}
