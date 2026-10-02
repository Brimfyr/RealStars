using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// A star's &lt;Sunlight&gt; is its raw colour: what its temperature gives against the display's white,
/// the value any colour table has, at whatever brightness the author wants. The catalogue stores every
/// star's colour the same way, and Adaptation turns both into what an eye in that light sees.
///
/// The stock Sol's (9, 9, 9) predates this: it is the Sun already seen as white. It is read here as the
/// Sun's raw colour at the same luminance, so Sol lights, and is adapted to, like any other star.
/// </summary>
internal static class SunLight
{
    /// <summary>The Sun's raw colour: its 5778 K blackbody (B-V 0.65, as make_star_binary.py colours
    /// every star) against the display's D65 white, brightest channel 1. The catalogue stores it as
    /// (255, 224, 210).</summary>
    internal static readonly float[] SunRaw = { 1.0f, 0.879805f, 0.822874f };

    private const string StockSunId = "Sol";
    private const float StockSunLight = 9f;

    // A template's data can be loaded again (a mod reload): its light is restated once.
    private static readonly ConditionalWeakTable<object, object> Restated = new();

    private static FieldInfo? _light, _r, _g, _b, _indexed;
    private static PropertyInfo? _id;
    private static MethodInfo? _lightLoad;
    private static object? _noIndexedColor;

    /// <summary>The stock Sol's light as the Sun's raw colour, at the luminance (Rec. 709) of (9, 9, 9).</summary>
    internal static float[] StockSunRaw()
    {
        float luminance = 0.2126729f * SunRaw[0] + 0.7151522f * SunRaw[1] + 0.0721750f * SunRaw[2];
        float k = StockSunLight / luminance;
        return new[] { k * SunRaw[0], k * SunRaw[1], k * SunRaw[2] };
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
    /// Sun's raw colour and rebuilt. Every other star's light is already raw and is left alone.</summary>
    public static void OnDataLoadPostfix(object __instance, object mod)
    {
        try
        {
            object? light = _light!.GetValue(__instance);
            if (light == null || Restated.TryGetValue(light, out _))
                return;
            string id = _id!.GetValue(__instance) as string ?? "";
            float r = (float)_r!.GetValue(light)!, g = (float)_g!.GetValue(light)!, b = (float)_b!.GetValue(light)!;
            if (id != StockSunId || r != StockSunLight || g != StockSunLight || b != StockSunLight)
                return;
            Restated.Add(light, light);

            float[] raw = StockSunRaw();
            _r.SetValue(light, raw[0]);
            _g.SetValue(light, raw[1]);
            _b.SetValue(light, raw[2]);
            _indexed!.SetValue(light, _noIndexedColor);   // so the rebuild keeps these, not a named colour's
            _lightLoad!.Invoke(light, new[] { mod });
            ShaderShadow.Log(FormattableString.Invariant(
                $"light of {id}: stock (9, 9, 9) read as the Sun's raw colour ({raw[0]:0.###}, {raw[1]:0.###}, {raw[2]:0.###})"));
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the Sun's light was left as the game reads it: " + ex.Message);
        }
    }
}
