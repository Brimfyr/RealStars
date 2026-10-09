using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The free camera, held off the star it follows.
///
/// The game keeps a free camera that follows a star outside the mesh the star's sphere is drawn on, 5.5 radii out
/// (FlyController.ClampCamera). It works the place out from the star's centre and then sets it as a place in the
/// system, which is the same thing only for a star at the system's origin. Round any other star, coming inside that
/// distance put the camera 5.5 of the star's radii from the ORIGIN: closing on Proxima in the game's own interstellar
/// system dropped it inside Sol (user, 2026-10-07: "using free cam to get really close to Proxima teleports the
/// camera back to Sol"). This is the game's, not this mod's or Real Atmospheres'.
///
/// The prefix does the same clamp about the star, at the game's own distance, and leaves every other case to the game.
/// </summary>
internal static class StarCamera
{
    private static FieldInfo? _camera;
    private static MemberInfo? _following;
    private static PropertyInfo? _positionCce;
    private static MethodInfo? _meshRadius;
    private static Type? _stellarBody;
    private static bool _resolved, _usable, _logged;

    /// <summary>FlyController.ClampCamera, or null if it moved.</summary>
    internal static MethodInfo? Target() =>
        AccessTools.TypeByName("KSA.FlyController") is Type fly ? AccessTools.Method(fly, "ClampCamera", Type.EmptyTypes) : null;

    private static bool Resolve(object controller)
    {
        if (_resolved) return _usable;
        _resolved = true;
        _camera = AccessTools.Field(controller.GetType(), "Camera");
        Type? camera = _camera?.FieldType;
        _following = camera == null ? null
            : (MemberInfo?)AccessTools.Property(camera, "Following") ?? AccessTools.Field(camera, "Following");
        _positionCce = camera == null ? null : AccessTools.Property(camera, "PositionCce");
        _stellarBody = AccessTools.TypeByName("KSA.StellarBody");
        Type? renderer = AccessTools.TypeByName("KSA.Rendering.Sun.SunRenderer");
        _meshRadius = renderer == null ? null : AccessTools.Method(renderer, "MeshRadiusOf");
        _usable = _camera != null && _following != null && _stellarBody != null && _meshRadius != null
               && _positionCce != null && _positionCce.CanRead && _positionCce.CanWrite;
        if (!_usable)
            ShaderShadow.Log("WARN: the free camera's clamp could not be read; near a star other than the system's first it is the game's");
        return _usable;
    }

    /// <summary>Prefix on FlyController.ClampCamera. False (the game's clamp skipped) only when the camera follows a
    /// star and was clamped about it here.</summary>
    public static bool ClampCameraPrefix(object __instance)
    {
        try
        {
            if (!Resolve(__instance)) return true;
            object? camera = _camera!.GetValue(__instance);
            object? following = camera == null ? null : _following switch
            {
                PropertyInfo p => p.GetValue(camera),
                FieldInfo f => f.GetValue(camera),
                _ => null,
            };
            if (following == null || !_stellarBody!.IsInstanceOfType(following)) return true;
            if (_meshRadius!.Invoke(null, new[] { following }) is not double mesh) return true;
            if (!HoldOff(camera!, _positionCce!, mesh + 2.0)) return true;
            if (!_logged)
            {
                ShaderShadow.Log("the free camera is held off the star it follows, about the star (the game holds it off the system's origin)");
                _logged = true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Holds a camera at least `distance` from the body it follows: its place from that body, the game's
    /// PositionCce, is drawn out along itself. True if the place could be read, moved or not.</summary>
    internal static bool HoldOff(object camera, PropertyInfo positionCce, double distance)
    {
        object? place = positionCce.GetValue(camera);
        if (place == null) return false;
        Type vec = place.GetType();
        FieldInfo? fx = vec.GetField("X"), fy = vec.GetField("Y"), fz = vec.GetField("Z");
        if (fx?.GetValue(place) is not double x || fy?.GetValue(place) is not double y || fz?.GetValue(place) is not double z)
            return false;
        double from = Math.Sqrt(x * x + y * y + z * z);
        if (!(from > 0.0) || from >= distance) return true;      // at the centre there is no way out to choose
        double scale = distance / from;
        fx.SetValue(place, x * scale);
        fy.SetValue(place, y * scale);
        fz.SetValue(place, z * scale);
        positionCce.SetValue(camera, place);
        return true;
    }
}
