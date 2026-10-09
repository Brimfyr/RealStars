using System.Globalization;

namespace RealStars;

/// <summary>
/// What an eye makes of a faint light's colour.
///
/// The catalogue gives every star the colour of its light (star_colours.py), and that light is what reaches the
/// eye. But a person looking at the night sky sees most stars as white: only the brightest few dozen show colour,
/// and orange and red show where blue does not. Colour vision needs light on the cones, and a star near the
/// limit of sight gives them too little: its colour fades into what the eye takes for white. A monitor cannot do
/// this by itself, because it shows every star at daylight brightness, where the cones see all of its colour; so
/// it is done here, from how bright each source really is.
///
/// Two fades, by the source's V magnitude, on the two opponent signals the cones feed:
///
///   red-green    full from V 0, gone by V 5. The faintest stars with a colour on record are orange and red at
///                V 3.3: Edasich, in pre-telescopic records, and gamma Hydri, seen red by one of the authors of
///                Neuhauser et al. (2022, MNRAS 516, 693). Direct vision, which puts a star on the cones, reaches
///                to about V 5.
///   blue         full from V -2, gone by V 2.5. Small lights at the brightness of a V +1.3 to +2.0 star cannot
///                be told blue from green (Hill 1947, at 1 and 2 mile-candles); blue is named more as a small
///                light brightens (Holmes 1949, Documenta Ophthalmologica 3, 240); Sirius, at -1.5, keeps a tint.
///   yellow       as red-green: "yellow and white are distinct colours" in small lights where blue is lost
///                (Holmes 1949), so an orange star stays orange as it fades, rather than going pink.
///
/// The colours are taken against the white the eye is adapted to (Adaptation), in the same cone space, so a
/// faint star becomes whatever the frame shows as white, under the Sun or a red dwarf alike.
///
/// What is NOT done here is the eye's blindness to blue in very small spots, small-field tritanopia: the centre
/// of the fovea, where a fixated star lands, has no blue-sensitive cones over 20-25 arcmin (Wald 1967). The
/// player's own eye does that, to a star sprite a few arcminutes across on the screen just as to a star; doing it
/// here as well would do it twice.
/// </summary>
internal static class EyeColour
{
    // The fades' ends, in V magnitude: the shader's numbers, written once here and read on both sides.
    internal const string RedGreenFullGlsl = "0.0", RedGreenGoneGlsl = "5.0";
    internal const string BlueFullGlsl = "-2.0", BlueGoneGlsl = "2.5";

    internal static readonly double RedGreenFull = Parse(RedGreenFullGlsl), RedGreenGone = Parse(RedGreenGoneGlsl);
    internal static readonly double BlueFull = Parse(BlueFullGlsl), BlueGone = Parse(BlueGoneGlsl);

    private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    // Linear sRGB to CAT02 cones and back, as Adaptation has them.
    private static readonly double[] ToCone =
        { 0.3904725, 0.5499044, 0.0089016, 0.0709259, 0.9631074, 0.0013581, 0.0231427, 0.1280122, 0.9360519 };
    private static readonly double[] FromCone =
        { 2.8583111, -1.6287080, -0.0248187, -0.2104348, 1.1584149, 0.0003205, -0.0418895, -0.1181543, 1.0688866 };
    private static readonly double[] Luma = { 0.2126729, 0.7151522, 0.0721750 };

    /// <summary>How much of a red-green, a blue and a yellow signal an eye sees in a light this bright.</summary>
    internal static (double RedGreen, double Blue, double Yellow) Keep(double magnitude)
    {
        double rg = Math.Clamp((RedGreenGone - magnitude) / (RedGreenGone - RedGreenFull), 0.0, 1.0);
        double blue = Math.Clamp((BlueGone - magnitude) / (BlueGone - BlueFull), 0.0, 1.0);
        return (rg, double.IsNaN(blue) ? 0.0 : blue, rg);
    }

    /// <summary>
    /// The colour an eye adapted to a white sees in a light: raw linear sRGB at the input's luminance. The white is
    /// given as its cone signals over D65's (L/M and S/M, with M at 1): (1, 1) is an eye adapted to D65.
    /// </summary>
    internal static float[] Seen(float[] rgb, double magnitude, double eyeL, double eyeS)
    {
        double[] w = Cone(1.0, 1.0, 1.0);
        double[] eye = { w[0] * eyeL, w[1], w[2] * eyeS };
        double[] c = Cone(rgb[0], rgb[1], rgb[2]);
        double qL = c[0] / eye[0], qM = c[1] / eye[1], qS = c[2] / eye[2];
        double a = 0.5 * (qL + qM);
        if (!(a > 0.0))
            return (float[])rgb.Clone();
        double rg = (qL - qM) / a, by = qS / a - 1.0;
        (double keepRg, double keepBlue, double keepYellow) = Keep(magnitude);
        double keepBy = by > 0.0 ? keepBlue : keepYellow;
        double[] seen = { a * (1.0 + 0.5 * rg * keepRg) * eye[0], a * (1.0 - 0.5 * rg * keepRg) * eye[1],
                          a * (1.0 + by * keepBy) * eye[2] };
        var back = new double[3];
        for (int i = 0; i < 3; i++)
            back[i] = Math.Max(FromCone[3 * i] * seen[0] + FromCone[3 * i + 1] * seen[1] + FromCone[3 * i + 2] * seen[2], 0.0);
        double y0 = Luma[0] * rgb[0] + Luma[1] * rgb[1] + Luma[2] * rgb[2];
        double y1 = Math.Max(Luma[0] * back[0] + Luma[1] * back[1] + Luma[2] * back[2], 1e-12);
        return new[] { (float)(back[0] * y0 / y1), (float)(back[1] * y0 / y1), (float)(back[2] * y0 / y1) };
    }

    private static double[] Cone(double r, double g, double b) =>
        new[] { ToCone[0] * r + ToCone[1] * g + ToCone[2] * b, ToCone[3] * r + ToCone[4] * g + ToCone[5] * b,
                ToCone[6] * r + ToCone[7] * g + ToCone[8] * b };

    // ---- the camera word ------------------------------------------------------------------------
    // The camera block's one spare int carries the starburst strength, three flags and, since this, the eye's white:
    //   bits 0-7    strength, 0-4 in steps of 1/63.75
    //   bits 8-9    flags (Starburst: the Sun sphere drawn in this view, the stars switched off)
    //   bit 10      the eye's white is given
    //   bit 11      a third flag: the second half of the game stars' slots is this frame's (GameStars.SecondSet)
    //   bits 12-21  log2 of the white's L/M over D65's, -2 to 2 in steps of 1/256
    //   bits 22-31  log2 of its S/M over D65's, -2 to 2 in steps of 1/256
    // Zero, the engine's own value, reads as no burst, no flags and an eye adapted to D65.

    internal const float MaxStrength = 4.0f;

    internal static int CameraWord(float strength, int flags, double eyeL, double eyeS)
    {
        uint s = (uint)Math.Clamp(Math.Round(Math.Clamp(strength, 0f, MaxStrength) * 63.75), 0.0, 255.0);
        uint l = (uint)Math.Clamp(Math.Round((Math.Log2(Math.Max(eyeL, 1e-6)) + 2.0) * 256.0), 0.0, 1023.0);
        uint sm = (uint)Math.Clamp(Math.Round((Math.Log2(Math.Max(eyeS, 1e-6)) + 2.0) * 256.0), 0.0, 1023.0);
        uint third = (flags & 4) != 0 ? 0x800u : 0u;
        return unchecked((int)(s | ((uint)(flags & 3) << 8) | (1u << 10) | third | (l << 12) | (sm << 22)));
    }

    /// <summary>The word read back as the shader reads it.</summary>
    internal static (float Strength, int Flags, double EyeL, double EyeS) ReadWord(int word)
    {
        uint w = unchecked((uint)word);
        float strength = (w & 0xFFu) / 63.75f;
        int flags = (int)((w >> 8) & 3u) | ((w & 0x800u) != 0u ? 4 : 0);
        if ((w & 0x400u) == 0u)
            return (strength, flags, 1.0, 1.0);
        return (strength, flags, Math.Pow(2.0, ((w >> 12) & 0x3FFu) / 256.0 - 2.0), Math.Pow(2.0, (w >> 22) / 256.0 - 2.0));
    }

    /// <summary>The camera word's readers and the eye's fade, for every shader that includes the tuning.</summary>
    internal const string Glsl = $$"""
        // The camera block's spare int (EyeColour.cs): the starburst strength, two flags, and the eye's white.
        uint rsCameraBits()
        {
            return uint(global.camera.pad0);
        }

        // The game's own lens flare setting, carried into these shaders - which cannot see the flare buffer.
        // Starburst.cs writes it: the toggle and the intensity together, and zero when either of them says no.
        float rsBurstStrength()
        {
            return float(rsCameraBits() & 0xFFu) / 63.75;
        }

        // Whether this view draws the engine's Sun sphere: only the main one does.
        bool rsSphereHere()
        {
            return (rsCameraBits() & 0x100u) != 0u;
        }

        // Whether the stars are switched off in the settings - see StarsOff.cs.
        bool rsStarsHidden()
        {
            return (rsCameraBits() & 0x200u) != 0u;
        }

        // Which half of the game's stars is this frame's to draw - see GameStars.SecondSet.
        bool rsGameStarsSecond()
        {
            return (rsCameraBits() & 0x800u) != 0u;
        }

        // The white the eye is adapted to, as its cone signals over D65's (CAT02, M at 1); D65's own when the
        // word carries none.
        vec3 rsEyeWhite()
        {
            uint w = rsCameraBits();
            if ((w & 0x400u) == 0u) return vec3(1.0);
            return vec3(exp2(float((w >> 12) & 0x3FFu) / 256.0 - 2.0), 1.0, exp2(float(w >> 22) / 256.0 - 2.0));
        }

        // What an eye makes of a light this faint (EyeColour.cs): its colour against the eye's white, the
        // red-green signal kept from V {{RedGreenFullGlsl}} and gone by V {{RedGreenGoneGlsl}}, yellow the same, blue kept from V {{BlueFullGlsl}} and gone by
        // V {{BlueGoneGlsl}}. At the input's luminance, which the magnitude alone sets.
        vec3 rsSeenColour(vec3 colour, float mag)
        {
            const mat3 toCone = {{Adaptation.ToConeGlsl}};
            const mat3 fromCone = {{Adaptation.FromConeGlsl}};
            vec3 eye = (toCone * vec3(1.0)) * rsEyeWhite();
            vec3 q = (toCone * colour) / eye;
            float a = 0.5 * (q.x + q.y);
            if (!(a > 0.0)) return colour;
            float rg = (q.x - q.y) / a;
            float by = q.z / a - 1.0;
            float keepRg = clamp(({{RedGreenGoneGlsl}} - mag) / ({{RedGreenGoneGlsl}} - ({{RedGreenFullGlsl}})), 0.0, 1.0);
            float keepBlue = clamp(({{BlueGoneGlsl}} - mag) / ({{BlueGoneGlsl}} - ({{BlueFullGlsl}})), 0.0, 1.0);
            float keepBy = by > 0.0 ? keepBlue : keepRg;
            vec3 seen = a * vec3(1.0 + 0.5 * rg * keepRg, 1.0 - 0.5 * rg * keepRg, 1.0 + by * keepBy) * eye;
            vec3 back = max(fromCone * seen, vec3(0.0));
            return back * (dot(colour, rsLuma) / max(dot(back, rsLuma), 1e-6));
        }
        """;
}
