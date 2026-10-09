using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Carries the game's lens flare setting to the star and planet shaders, which cannot see it.
///
/// The setting is GameSettings.Current.Graphics.LensFlare and .LensFlareIntensity. Our
/// sprites are drawn from the global bindings and never see the flare buffer the engine copies
/// it into, so the decision is written into the camera block's one spare word -
/// UboCameraData._padding2, which the engine writes as zero and no shipped shader reads.
///
/// The setting itself, and not the engine's ShowFlare. ShowFlare folds in whether this viewport
/// is showing the map, and turns the flare off there; that suits a lens flare, but a starburst
/// is the eye's, and the Sun seen in the map is still the Sun. The intensity rides along in the
/// same number, so the slider moves the rays as well; zero for either means no burst at all,
/// and with it no quad grown to hold one.
///
/// The word (EyeColour.CameraWord) carries the strength, two flags and the eye's white. The flags:
/// whether this view draws the engine's Sun sphere, which only the main view does, so our sprite
/// knows whether to give way to it; and whether the stars are switched off, so the star shader
/// keeps only the Sun (see StarsOff). Both read as no when this has never run, the safe way
/// round: the sprite stays, and the stars show. The eye's white is what a faint star's colour
/// fades into (EyeColour).
/// </summary>
internal static class Starburst
{
    /// <summary>Above this the slider is doing something other than taste, so it is capped.</summary>
    private const float MaxStrength = EyeColour.MaxStrength;

    /// <summary>The flags in the word. Star.vert and Star.frag read the same bits.</summary>
    internal const int SphereHere = 1, StarsHidden = 2;

    /// <summary>A third flag, for the star shader alone: the second half of the game stars' slots is this
    /// frame's (GameStars.SecondSet).</summary>
    internal const int SecondStars = 4;

    /// <summary>True while the camera word is being written each frame: only then can a frame be told which half
    /// of the game stars' slots is its own.</summary>
    internal static bool WordLive => _resolved && _usable;

    private static FieldInfo? _cameraArray, _pad, _graphics, _lensFlare, _intensity, _stars;
    private static PropertyInfo? _viewType;
    private static PropertyInfo? _currentSettings;
    private static FieldInfo? _lightingArray, _atmosphereHeight, _sunPositionRadius;
    private static FieldInfo? _bodyParent, _bodyArray, _bodyCount;
    private static bool _bodiesBroken, _bodiesLogged;
    private static Type? _atmosphericBody;
    private static readonly Dictionary<Type, PropertyInfo?> _shaderSlots = new();
    private static readonly Dictionary<Type, MethodInfo?> _cameras = new();
    private static readonly Dictionary<Type, PropertyInfo?> _nearby = new();
    private static bool _resolved, _usable, _logged, _airLogged;

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

            _cameraArray ??= AccessTools.Field(program.GetType(), "_cameraData");
            if (_cameraArray?.GetValue(null) is not Array cameras || slot >= cameras.Length) return;
            object cbox = cameras.GetValue(slot)!;
            _pad ??= AccessTools.Field(cbox.GetType(), "_padding2");

            _currentSettings ??= AccessTools.Property(AccessTools.TypeByName("KSA.GameSettings"), "Current");
            object? settings = _currentSettings?.GetValue(null);
            _graphics ??= settings == null ? null : AccessTools.Field(settings.GetType(), "Graphics");
            object? graphics = settings == null ? null : _graphics?.GetValue(settings);
            _lensFlare ??= graphics == null ? null : AccessTools.Field(graphics.GetType(), "LensFlare");
            _intensity ??= graphics == null ? null : AccessTools.Field(graphics.GetType(), "LensFlareIntensity");
            _stars ??= graphics == null ? null : AccessTools.Field(graphics.GetType(), "Stars");
            // The interface's own property, which reaches an explicit implementation too.
            _viewType ??= AccessTools.TypeByName("KSA.IViewport")?.GetProperty("Type");

            _resolved = true;
            _usable = _lensFlare != null && _intensity != null && _pad != null;
            if (!_usable)
            {
                ShaderShadow.Log("WARN: the lens flare setting could not be read; no starbursts");
                return;
            }

            // The game's setting and the mod's own switch (Settings.Glare): either one off is no glare.
            Settings.Load();
            bool on = _lensFlare!.GetValue(graphics) is bool b && b && Settings.Glare;
            float slider = _intensity!.GetValue(graphics) is float f && f > 0.0f ? f : 1.0f;
            float strength = on ? Math.Min(slider, MaxStrength) : 0.0f;

            // RenderGame draws the Sun sphere for the main view; the other views and the editor
            // draw none, and there our sprite is the Sun at every distance.
            bool main = _viewType?.GetValue(viewport)?.ToString() == "Main";
            // Inside the sphere's own mesh the engine draws nothing of it, unless SunSphere got its
            // pipeline to: there the sprite is the star again, as in a view with no sphere.
            if (main && !SunSphere.DrawnFromInside && InsideSunMesh(program, slot)) main = false;
            bool starsOff = _stars?.GetValue(graphics) is bool shown && !shown;
            int flags = (main ? SphereHere : 0) | (starsOff ? StarsHidden : 0) | (GameStars.SecondSet ? SecondStars : 0);
            (double eyeL, double eyeS) = Adaptation.EyeRatios;
            int word = EyeColour.CameraWord(strength, flags, eyeL, eyeS);
            _pad!.SetValue(cbox, word);
            cameras.SetValue(cbox, slot);

            ClearStaleAtmosphere(program, viewport, slot);

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

    /// <summary>
    /// Makes global.celestial.bodies mean what it says.
    ///
    /// UpdateShaderData fills that list with the planet within a quarter of an AU of the camera and its moons,
    /// each where it stands from the camera, and with no such planet it leaves the list as it was: the last
    /// planet's, where it stood from the camera then. Jump from a low orbit to a star with no planets and the
    /// planet comes along, as far below the camera as it was. Our shaders hide every source behind a body in the
    /// list, so there was a black disc across the sky with no stars in it, and no star lighting the scene either
    /// (user, 2026-10-08, at Alpha Centauri A, straight from a planet of Barnard's Star); and the engine's own
    /// shaders take its shadow from it. With no system at all the engine empties the list. So does this, on the
    /// frames the engine found no planet: the count alone, which is all any reader goes by.
    /// </summary>
    public static void ClearStaleBodies(object program, object viewport)
    {
        if (_bodiesBroken) return;
        try
        {
            Type vt = viewport.GetType();
            if (!_shaderSlots.TryGetValue(vt, out PropertyInfo? slotProp))
                _shaderSlots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot || slot < 0) return;

            _bodyParent ??= AccessTools.Field(program.GetType(), "_uboCelestialParent")
                            ?? throw new MissingMemberException("Program._uboCelestialParent not found");
            _bodyArray ??= AccessTools.Field(program.GetType(), "_celestialData")
                           ?? throw new MissingMemberException("Program._celestialData not found");
            if (_bodyParent.GetValue(program) != null) return;          // filled this frame: leave it be
            if (_bodyArray.GetValue(null) is not Array bodies || slot >= bodies.Length) return;
            object box = bodies.GetValue(slot)!;
            _bodyCount ??= AccessTools.Field(box.GetType(), "BodyCount")
                           ?? throw new MissingMemberException("UboCelestialData.BodyCount not found");
            if (_bodyCount.GetValue(box) is not int count || count == 0) return;

            _bodyCount.SetValue(box, 0);
            bodies.SetValue(box, slot);
            if (!_bodiesLogged)
            {
                ShaderShadow.Log("emptying the stale body list far from any planet (the engine leaves the last planet's behind)");
                _bodiesLogged = true;
            }
        }
        catch (Exception ex)
        {
            _bodiesBroken = true;
            ShaderShadow.Log("WARN: the stale body list far from any planet is left as the engine has it: "
                             + ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Whether this view's camera is within the mesh the engine draws the star's sphere on: the
    /// lighting block has the star's place from the camera and its radius.
    /// </summary>
    private static bool InsideSunMesh(object program, int slot)
    {
        _lightingArray ??= AccessTools.Field(program.GetType(), "_lightingData");
        if (_lightingArray?.GetValue(null) is not Array lighting || slot >= lighting.Length) return false;
        object box = lighting.GetValue(slot)!;
        _sunPositionRadius ??= AccessTools.Field(box.GetType(), "SunPositionRadius");
        if (_sunPositionRadius?.GetValue(box) is not object sun) return false;
        Type f4 = sun.GetType();
        double Part(string name) => f4.GetField(name)?.GetValue(sun) is float f ? f : double.NaN;
        double x = Part("X"), y = Part("Y"), z = Part("Z"), radius = Part("W");
        return radius > 0.0 && Math.Sqrt(x * x + y * y + z * z) < SunSphere.MeshRadii * radius;
    }

    /// <summary>
    /// Makes global.lighting.atmosphereHeight mean what it says.
    ///
    /// UpdatePlanetShaderData writes the planet block ONLY when the nearby celestial is an
    /// AtmosphericBody, so approach an airless moon and the height, radius and position are
    /// whatever the last atmosphere left behind. Every shader that reads it - the engine's
    /// own included, which treats height &lt;= 0 as "no air" - is being told there is an
    /// atmosphere where there is none. Ours reads it to decide whether a source has gone
    /// behind a limb's haze, which is a question it would then answer about the wrong body.
    ///
    /// Zeroing it on exactly the frames the engine would not have written it cannot race
    /// that write, and leaves the value alone whenever it is real.
    /// </summary>
    private static void ClearStaleAtmosphere(object program, object viewport, int slot)
    {
        Type vt = viewport.GetType();
        if (!_cameras.TryGetValue(vt, out MethodInfo? getCamera))
            _cameras[vt] = getCamera = AccessTools.Method(vt, "GetCamera");
        object? camera = getCamera?.Invoke(viewport, null);
        if (camera == null) return;

        Type ct = camera.GetType();
        if (!_nearby.TryGetValue(ct, out PropertyInfo? nearbyProp))
            _nearby[ct] = nearbyProp = AccessTools.Property(ct, "NearbyCelestial");
        object? nearby = nearbyProp?.GetValue(camera);

        _atmosphericBody ??= AccessTools.TypeByName("KSA.AtmosphericBody");
        if (_atmosphericBody == null) return;

        // How high this body's air stops letting starlight through, for the limb test. It goes
        // out every frame, and zero when there is no air, so nothing stale is left behind.
        bool hasAir = nearby != null && _atmosphericBody.IsInstanceOfType(nearby);
        LimbAir.Publish(program, hasAir ? nearby : null, slot);

        if (hasAir) return;                                     // real air; leave its height be

        _lightingArray ??= AccessTools.Field(program.GetType(), "_lightingData");
        if (_lightingArray?.GetValue(null) is not Array lighting || slot >= lighting.Length) return;
        object box = lighting.GetValue(slot)!;
        _atmosphereHeight ??= AccessTools.Field(box.GetType(), "AtmosphereHeight");
        if (_atmosphereHeight == null) return;
        if (_atmosphereHeight.GetValue(box) is not float height || height == 0.0f) return;

        _atmosphereHeight.SetValue(box, 0.0f);
        lighting.SetValue(box, slot);

        if (!_airLogged)
        {
            ShaderShadow.Log("clearing the stale atmosphere height near airless bodies "
                             + "(the engine leaves the last one's behind)");
            _airLogged = true;
        }
    }
}
