using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Keeps the Sun when the stars are switched off in the settings.
///
/// The Sun is a star in our catalogue, drawn by the star pass at every distance. The engine skips
/// that whole pass when GameSettings.Graphics.Stars is off, and our Sun went with it, where the
/// stock game's did not: its Sun was drawn by the bloom pass, which we turned off. So on those
/// frames the star pass is drawn anyway, straight after the distant planets, which every view
/// that shows the sky draws whatever the setting. Star.vert leaves out every star but the Sun
/// (Starburst writes the flag), and the Milky Way, which the engine draws with the stars, stays
/// off.
/// </summary>
internal static class StarsOff
{
    private static MethodInfo? _showStars, _render;
    private static FieldInfo? _technique;
    private static bool _broken, _logged;

    /// <summary>
    /// Postfix on StaticCelestialDistanceRendering.Run(commandBuffer, viewport, frameIndex). The
    /// arguments go straight through to the star technique's Render, which takes the same three.
    /// </summary>
    public static void RunPostfix(object[] __args)
    {
        if (_broken) return;
        try
        {
            _showStars ??= AccessTools.Method(AccessTools.TypeByName("KSA.GameSettings"), "ShowStars");
            if (_showStars?.Invoke(null, null) is not bool shown || shown) return;   // drawn as usual

            _technique ??= AccessTools.Field(AccessTools.TypeByName("KSA.Program"), "_starStarTechnique");
            object? technique = _technique?.GetValue(null);
            if (technique == null) return;
            _render ??= technique.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .FirstOrDefault(m => m.Name == "Render" && m.GetParameters().Length == 3);
            if (_render == null)
            {
                _broken = true;
                ShaderShadow.Log("WARN: the star pass's Render was not found; the Sun goes when the stars are off");
                return;
            }
            _render.Invoke(technique, new[] { __args[0], __args[1], __args[2] });

            if (!_logged)
            {
                ShaderShadow.Log("the stars are off in the settings: the Sun is drawn on its own");
                _logged = true;
            }
        }
        catch (Exception ex)
        {
            _broken = true;
            ShaderShadow.Log("WARN: the Sun could not be drawn with the stars off: " + ex.GetBaseException().Message);
        }
    }
}
