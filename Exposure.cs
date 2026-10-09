using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Carries the game's exposure setting to the shaders that draw light sources.
///
/// The setting is GameSettings.Current.Graphics.TonemapExposure, 1.25 unless the player moves it. It multiplies
/// the finished frame ahead of the tone curve, and nothing else in the game reads it. Every level in these shaders
/// that means "white on the screen" was chosen at 1.25: a core's ceiling, the Sun's level, the glare's ceiling and
/// the floor it has to clear. Left there, a lower exposure showed each of them as gray. At 0.1 the burnt-out part
/// of the Sun's glare was a flat gray star, a twelfth of white, with the Sun's disc gray inside it, and no smaller
/// than at 1.25 (user, 2026-10-08, of a Martian sunset: "gray spikes around the sun", "like it's not lowering its
/// intensity"). So those levels follow the setting: a source far past white stays white until the exposure has
/// really come down to it, and its glare draws in as the light it is made of falls under what shows.
///
/// The value goes into the first spare word of the engine's session constants (UboGlobalConstants.pad0, which
/// Global.glsl has as globalConstants.gtPad0), as a float's bits. Every pass binds that block beside the global
/// one, the engine writes it once and never reads the spare, and its memory stays mapped, so one write reaches
/// every frame in flight, which suits a setting. Zero, the engine's own value, reads as the default exposure: a
/// mod that has stopped working leaves the shaders as they were.
/// </summary>
internal static class Exposure
{
    /// <summary>The game's default, which every level in the shaders was chosen at.</summary>
    internal const float Default = 1.25f;

    /// <summary>How far the levels follow the setting, either way. The frame is half floats, which hold the
    /// Sun's level a thousand times over and no more.</summary>
    internal const double MinScale = 1e-3, MaxScale = 1e3;

    /// <summary>Where Global.glsl reads the spare word: after the ten handles.</summary>
    internal const int SpareOffset = 40, ConstantsSize = 48;

    private static FieldInfo? _memory, _mapPtr, _graphics, _exposure;
    private static MethodInfo? _toAddress;
    private static PropertyInfo? _currentSettings;
    private static bool _resolved, _usable;
    private static float _logged = float.NaN;

    /// <summary>White now over white at the default exposure, as the shaders have it (rsWhiteScale): what the
    /// CPU's copies of their levels are multiplied by. One until the setting has reached the shaders.</summary>
    internal static double WhiteScale { get; private set; } = 1.0;

    /// <summary>The shaders' rsWhiteScale for an exposure.</summary>
    internal static double ScaleFor(float exposure) =>
        exposure > 0f ? Math.Clamp(Default / (double)exposure, MinScale, MaxScale) : 1.0;

    /// <summary>Called every frame, from the postfix on Program.UpdateShaderData.</summary>
    public static void Publish()
    {
        if (_resolved && !_usable) return;
        try
        {
            if (!_resolved)
            {
                _resolved = true;
                Resolve();
                _usable = true;
            }

            object? settings = _currentSettings!.GetValue(null);
            object? graphics = settings == null ? null : _graphics!.GetValue(settings);
            if (graphics == null || _exposure!.GetValue(graphics) is not float exposure) return;

            // Read afresh each frame: the engine maps the block again whenever it rebuilds its bindings.
            object memory = _memory!.GetValue(null)!;
            nint address = (nint)_toAddress!.Invoke(null, new[] { _mapPtr!.GetValue(memory) })!;
            if (address == 0) return;                               // not mapped yet
            Marshal.WriteInt32(address, SpareOffset, BitConverter.SingleToInt32Bits(exposure));
            WhiteScale = ScaleFor(exposure);

            if (exposure != _logged)
            {
                // Once, and again only when the player has let go of the slider at somewhere new.
                if (float.IsNaN(_logged) || Settled(exposure))
                {
                    ShaderShadow.Log(FormattableString.Invariant(
                        $"the game's exposure is {exposure:0.####}: white is {WhiteScale:0.###} times the default's in the star shaders"));
                    _logged = exposure;
                }
            }
        }
        catch (Exception ex)
        {
            _usable = false;
            WhiteScale = 1.0;
            ShaderShadow.Log("WARN: the game's exposure could not be passed to the star shaders, which keep the default's levels: "
                             + ex.GetBaseException().Message);
        }
    }

    private static float _last = float.NaN;
    private static int _same;

    /// <summary>True once the setting has held one value for a second or so of frames.</summary>
    private static bool Settled(float exposure)
    {
        _same = exposure == _last ? _same + 1 : 0;
        _last = exposure;
        return _same >= 60;
    }

    /// <summary>The members, once. Anything missing or laid out differently turns this off rather than guessing.</summary>
    private static void Resolve()
    {
        Type bindings = AccessTools.TypeByName("KSA.GlobalShaderBindings")
                        ?? throw new MissingMemberException("GlobalShaderBindings not found");
        Type constants = AccessTools.TypeByName("KSA.UboGlobalConstants")
                         ?? throw new MissingMemberException("UboGlobalConstants not found");
        _memory = AccessTools.Field(bindings, "_constantsMemory")
                  ?? throw new MissingMemberException("GlobalShaderBindings._constantsMemory not found");
        _mapPtr = AccessTools.Field(_memory.FieldType, "MapPtr")
                  ?? throw new MissingMemberException("MappedMemory.MapPtr not found");
        _toAddress = AddressOf(_mapPtr.FieldType)
                     ?? throw new MissingMemberException("the mapped pointer no longer converts to an address");
        (int size, int spare) = Layout(constants);
        if (size != ConstantsSize || spare != SpareOffset)
            throw new InvalidOperationException($"the session constants are laid out differently ({size} bytes, the spare at {spare})");

        Type gameSettings = AccessTools.TypeByName("KSA.GameSettings")
                            ?? throw new MissingMemberException("GameSettings not found");
        _currentSettings = AccessTools.Property(gameSettings, "Current")
                           ?? throw new MissingMemberException("GameSettings.Current not found");
        _graphics = AccessTools.Field(_currentSettings.PropertyType, "Graphics")
                    ?? throw new MissingMemberException("GameSettings.Graphics not found");
        _exposure = AccessTools.Field(_graphics.FieldType, "TonemapExposure")
                    ?? throw new MissingMemberException("Graphics.TonemapExposure not found");
        if (_exposure.FieldType != typeof(float))
            throw new InvalidOperationException("Graphics.TonemapExposure is no longer a float");
    }

    /// <summary>The engine's pointer wrapper to an address: its explicit conversion to nint.</summary>
    internal static MethodInfo? AddressOf(Type pointer) =>
        pointer.GetMethods(BindingFlags.Public | BindingFlags.Static)
               .FirstOrDefault(m => m.Name == "op_Explicit" && m.ReturnType == typeof(nint)
                                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == pointer);

    /// <summary>The session constants' size, and where their first spare word sits (-1 if it is gone).</summary>
    internal static (int Size, int Spare) Layout(Type constants)
    {
        FieldInfo? spare = constants.GetField("pad0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (Marshal.SizeOf(constants), spare == null ? -1 : (int)Marshal.OffsetOf(constants, "pad0"));
    }
}
