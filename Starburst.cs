using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Carries the game's lens flare setting to the star and planet shaders, which cannot see it.
///
/// The setting reaches the engine's own flare as SunFlareData.ShowFlare and .FlareIntensity,
/// in a buffer bound only to the bloom passes. Our sprites are drawn from the global bindings
/// and never see that buffer, so the decision is copied into the camera block's one spare word
/// - UboCameraData._padding2, which the engine writes as zero and no shipped shader reads.
///
/// ShowFlare is taken rather than the setting itself because the engine has already folded in
/// everything else it depends on: the graphics toggle, and whether this viewport is a map,
/// where a starburst would be nonsense. The intensity rides along in the same number, so the
/// slider moves the rays as well; zero for either one means no burst at all, and with it no
/// quad grown to hold one.
/// </summary>
internal static class Starburst
{
    /// <summary>Above this the slider is doing something other than taste, so it is capped.</summary>
    private const float MaxStrength = 4.0f;

    private static FieldInfo? _flareArray, _showFlare, _intensity, _cameraArray, _pad;
    private static readonly Dictionary<Type, PropertyInfo?> _shaderSlots = new();
    private static bool _resolved, _usable, _logged;

    /// <summary>
    /// Called from the same postfix on Program.UpdateShaderData that switches the engine's sun
    /// sprite off, before anything that can return early: the shaders read this every frame,
    /// and a frame that skipped it would keep whatever was last written.
    /// </summary>
    public static void Publish(object program, object viewport)
    {
        if (_resolved && !_usable) return;
        try
        {
            Type vt = viewport.GetType();
            if (!_shaderSlots.TryGetValue(vt, out PropertyInfo? slotProp))
                _shaderSlots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot || slot < 0) return;

            _flareArray ??= AccessTools.Field(program.GetType(), "_sunflareData");
            _cameraArray ??= AccessTools.Field(program.GetType(), "_cameraData");
            if (_flareArray?.GetValue(null) is not Array flare || slot >= flare.Length) return;
            if (_cameraArray?.GetValue(null) is not Array cameras || slot >= cameras.Length) return;

            object fbox = flare.GetValue(slot)!;
            _showFlare ??= AccessTools.Field(fbox.GetType(), "ShowFlare");
            _intensity ??= AccessTools.Field(fbox.GetType(), "FlareIntensity");

            object cbox = cameras.GetValue(slot)!;
            _pad ??= AccessTools.Field(cbox.GetType(), "_padding2");

            _resolved = true;
            _usable = _showFlare != null && _intensity != null && _pad != null;
            if (!_usable)
            {
                ShaderShadow.Log("WARN: the lens flare setting could not be read; no starbursts");
                return;
            }

            bool on = _showFlare!.GetValue(fbox) is bool b && b;
            float slider = _intensity!.GetValue(fbox) is float f && f > 0.0f ? f : 1.0f;
            float strength = on ? Math.Min(slider, MaxStrength) : 0.0f;

            _pad!.SetValue(cbox, BitConverter.SingleToInt32Bits(strength));
            cameras.SetValue(cbox, slot);

            if (!_logged)
            {
                ShaderShadow.Log($"starbursts {(on ? $"on at x{strength:0.##}" : "off")} "
                                 + "(following the game's lens flare setting)");
                _logged = true;
            }
        }
        catch (Exception ex)
        {
            _resolved = true;
            _usable = false;
            ShaderShadow.Log("WARN: the lens flare setting could not be read; no starbursts: " + ex.Message);
        }
    }
}
