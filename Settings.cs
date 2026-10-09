using System.Globalization;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The player's settings, changed in the game through ModMenu (Menu.cs) and kept beside the game's own settings, in
/// RealStars/settings.toml under its documents folder (which Borea gives each instance its own of). Plain TOML, safe to
/// edit while the game is closed. A missing or unreadable file is the defaults.
/// </summary>
internal static class Settings
{
    /// <summary>
    /// How long the quick part of the eye's adaptation to a new light takes, in seconds. The eye adapts in two
    /// stages (Fairchild and Reniff 1995, Adaptation): a fast one, a little over half of the change, and a slow one
    /// that takes thirty times as long. This is the fast one, as far as it is seen to go: 95% of its own way, three
    /// of its time constants. 3 s is the measured course, the most this goes; the whole of that is 94% done at 90 s.
    ///
    /// The setting was the whole course's time, to 94%, which read as 40 s for a change that was over in a second
    /// or two to look at (user, 2026-10-08). So it is the part that is seen, and what was 40 is the default.
    /// </summary>
    internal const float DefaultAdaptationFastSeconds = 40f / WholeOverFast;
    internal const float MinAdaptationFastSeconds = 0.02f, MaxAdaptationFastSeconds = 3f;

    /// <summary>The whole course, to 94%, over the quick part's time: Adaptation's 90 s over three of its fast
    /// stage's one-second time constants.</summary>
    internal const float WholeOverFast = 30f;

    internal static float AdaptationFastSeconds { get; private set; } = DefaultAdaptationFastSeconds;

    /// <summary>The whole course's time, to 94%, which is what Adaptation eases by.</summary>
    internal static float AdaptationSeconds => AdaptationFastSeconds * WholeOverFast;

    /// <summary>
    /// The player's switches, each on unless the file says otherwise (user, 2026-10-08).
    /// ColourAdaptation: the eye's adaptation to the light it is in (Adaptation); off, the frame is shown as an eye
    /// adapted to the display's white has it, every light in its raw colour.
    /// Twinkle: the stars' and planets' scintillation through an atmosphere (Scintillation).
    /// Glare: the starburst and the soft glow round the Sun, the bright planets and the brightest stars (Starburst);
    /// it follows the game's Lens Flare setting as well, and either one off is no glare.
    /// </summary>
    internal static bool ColourAdaptation { get; private set; } = true;
    internal static bool Twinkle { get; private set; } = true;
    internal static bool Glare { get; private set; } = true;

    private const string ColourAdaptationKey = "colour_adaptation", TwinkleKey = "twinkle", GlareKey = "glare";

    private const string AdaptationKey = "adaptation_fast_seconds";
    /// <summary>The setting as it was kept before: the whole course's time. Still read, where the new one is absent.</summary>
    private const string WholeCourseKey = "chromatic_adaptation_seconds";
    private const long SettleMs = 500;                   // written this long after the last change, not on every frame of a drag

    private static string? _path;
    private static bool _loaded, _dirty;
    private static long _changedAt;

    /// <summary>The file, under the game's documents folder (KSA.Constants.DocumentsFolderPath).</summary>
    internal static string? FilePath
    {
        get
        {
            if (_path != null) return _path;
            try
            {
                Type? constants = AccessTools.TypeByName("KSA.Constants");
                if (AccessTools.Property(constants, "DocumentsFolderPath")?.GetValue(null) is string documents)
                    _path = Path.Combine(documents, "RealStars", "settings.toml");
            }
            catch (Exception ex)
            {
                ShaderShadow.Log("WARN: the game's documents folder was not found; settings are not kept: " + ex.Message);
            }
            return _path;
        }
    }

    /// <summary>Reads the file once; the defaults stand for anything it does not say.</summary>
    internal static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        string? path = FilePath;
        if (path == null || !File.Exists(path))
        {
            ShaderShadow.Log(FormattableString.Invariant($"settings: the defaults ({Summary()})"));
            return;
        }
        try
        {
            string[] lines = File.ReadAllLines(path);
            AdaptationFastSeconds = Read(lines);
            ColourAdaptation = ReadSwitch(lines, ColourAdaptationKey, true);
            Twinkle = ReadSwitch(lines, TwinkleKey, true);
            Glare = ReadSwitch(lines, GlareKey, true);
            ShaderShadow.Log(FormattableString.Invariant($"settings from {path}: {Summary()}"));
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the settings file could not be read, the defaults stand: " + ex.Message);
        }
    }

    /// <summary>A line's key and value, or null for a comment, a blank or anything else.</summary>
    internal static (string Key, string Value)? Parse(string line)
    {
        string s = line.Trim();
        if (s.Length == 0 || s[0] == '#') return null;
        int eq = s.IndexOf('=');
        if (eq <= 0) return null;
        string value = s[(eq + 1)..];
        int hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash];
        return (s[..eq].Trim(), value.Trim());
    }

    /// <summary>The quick part's time as a settings file has it: its own key, or failing that the old one, a
    /// thirtieth of the whole course's time; the default if neither is there.</summary>
    internal static float Read(IEnumerable<string> lines)
    {
        float? fast = null, whole = null;
        foreach (string line in lines)
        {
            if (Parse(line) is not (string key, string value)
                || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                continue;
            if (key == AdaptationKey) fast = seconds;
            else if (key == WholeCourseKey) whole = seconds;
        }
        return fast is float f ? Clamp(f) : whole is float w ? Clamp(w / WholeOverFast) : DefaultAdaptationFastSeconds;
    }

    /// <summary>A switch as a settings file has it; <paramref name="otherwise"/> if it does not say.</summary>
    internal static bool ReadSwitch(IEnumerable<string> lines, string key, bool otherwise)
    {
        bool value = otherwise;
        foreach (string line in lines)
            if (Parse(line) is (string k, string v) && k == key && bool.TryParse(v, out bool on))
                value = on;
        return value;
    }

    private static string Summary() => FormattableString.Invariant(
        $"the eye's adaptation {(ColourAdaptation ? "on" : "off")}, {AdaptationFastSeconds:0.##} s for its quick part, {AdaptationSeconds:0.#} s in all; twinkle {(Twinkle ? "on" : "off")}; glare {(Glare ? "on" : "off")}");

    internal static void SetColourAdaptation(bool on)
    {
        Load();
        if (on == ColourAdaptation) return;
        ColourAdaptation = on;
        Touched();
    }

    internal static void SetTwinkle(bool on)
    {
        Load();
        if (on == Twinkle) return;
        Twinkle = on;
        Touched();
    }

    internal static void SetGlare(bool on)
    {
        Load();
        if (on == Glare) return;
        Glare = on;
        Touched();
    }

    private static void Touched()
    {
        _dirty = true;
        _changedAt = Environment.TickCount64;
    }

    internal static float Clamp(float seconds) =>
        float.IsFinite(seconds) ? Math.Clamp(seconds, MinAdaptationFastSeconds, MaxAdaptationFastSeconds) : DefaultAdaptationFastSeconds;

    internal static void SetAdaptationFastSeconds(float seconds)
    {
        Load();
        float value = Clamp(seconds);
        if (value == AdaptationFastSeconds) return;
        AdaptationFastSeconds = value;
        _dirty = true;
        _changedAt = Environment.TickCount64;
    }

    /// <summary>Writes the file once a change has settled. Called every frame; does nothing most of them.</summary>
    internal static void SaveIfSettled()
    {
        if (!_dirty || Environment.TickCount64 - _changedAt < SettleMs) return;
        _dirty = false;
        string? path = FilePath;
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Text());
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the settings file could not be written: " + ex.Message);
        }
    }

    internal static string Text() => FormattableString.Invariant($"""
        # Real Stars settings, written by its page in the game's Mods menu (ModMenu). Safe to edit with the game closed.

        # How long the quick part of the eye's adaptation to a new light takes, in seconds: {MinAdaptationFastSeconds} to {MaxAdaptationFastSeconds}.
        # The eye adapts in two stages: this one, a little over half of the change, and a slow one that finishes the
        # rest over {WholeOverFast:0} times as long. {MaxAdaptationFastSeconds:0} is the measured human course (Fairchild and Reniff 1995); {DefaultAdaptationFastSeconds:0.##} is the default.
        {AdaptationKey} = {AdaptationFastSeconds:0.###}

        # The eye's adaptation to the colour of the light it is in: true or false. False shows every light in its raw colour.
        {ColourAdaptationKey} = {(ColourAdaptation ? "true" : "false")}

        # Stars and planets twinkling through an atmosphere: true or false.
        {TwinkleKey} = {(Twinkle ? "true" : "false")}

        # The starburst and soft glow round the Sun, the bright planets and the brightest stars: true or false.
        # It follows the game's Lens Flare setting too.
        {GlareKey} = {(Glare ? "true" : "false")}

        """);
}
