using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// A body's &lt;Color&gt; is its colour in white light: its albedo per channel with the Sun as white, as its maps
/// are. The engine sends it out as the body's light just as it is for the distant sprite; with every light raw
/// (SunLight) that would be the body in white light, not in its star's, so here it is lit by the star's light
/// first, as its surface is.
///
/// The Sun's own bodies also take their measured colours (BodyColours.Generated.cs, from make_body_colours.py)
/// where the game's are drawn: Mars (1, 0.27, 0), a deep blue Neptune, a pure red Amalthea. Only the game's own
/// bodies, by Id; a mod's bodies keep their authors' colours. Orbit lines and sphere-of-influence shells keep the
/// game's colours: the engine takes those from the template once, when it builds a body, and the template is left
/// as it is.
///
/// Planetshine on vessels is the real irradiance from the body where the camera is: its star's light reflected by
/// the body, summed over the part of its sunlit side in view. The engine's own is a fixed share of the body's colour,
/// about 2% of the sunlight in low orbit where Earth gives 26%, only from a body with an atmosphere, and gone by
/// 15,000 km. Here every body gives it, at any distance it is worth seeing. The Sun's bodies reflect as measured,
/// colour, albedo and phase curve (BodyAlbedos.Generated.cs, from make_body_albedos.py); any other body in the colour
/// of its map, with the albedo of the surface the game renders, as a matte (Lambert) surface.
/// </summary>
internal static partial class BodyColours
{
    private const string GameMod = "Core";
    private const int GiveUpAfter = 120;

    private sealed class Entry
    {
        public readonly object Reference;     // our ColorRgbReference, read in place of the template's
        public readonly float[] Hue;          // the body's colour in white light, at unit luminance
        public readonly float Luminance;      // the game's colour's, kept: the sprite shader takes only the hue
        public bool Lit;
        public (float R, float G, float B)? Light;
        public float[]? Shown;                // as last written, after the eye (EyeColour)

        public Entry(object reference, float[] hue, float luminance)
        {
            Reference = reference;
            Hue = hue;
            Luminance = luminance;
        }
    }

    private static readonly ConditionalWeakTable<object, Entry> Entries = new();
    private static readonly ConditionalWeakTable<object, Entry>.CreateValueCallback NewEntry = Create;

    private static FieldInfo? _colour, _r, _g, _b;
    private static Type? _reference;
    private static MethodInfo? _load;
    private static PropertyInfo? _id, _mod, _modId;
    private static int _failures;

    /// <summary>True once the distant sprites read their colours from here.</summary>
    internal static bool SpritesPatched { get; private set; }

    /// <summary>Finds a template's colour and what rebuilds one; false leaves every body's colour as the game has it.</summary>
    internal static bool Resolve(Type modType)
    {
        Type? template = AccessTools.TypeByName("KSA.CelestialTemplate");
        _colour = template == null ? null : AccessTools.Field(template, "ColorRgb");
        _reference = _colour?.FieldType;
        if (template == null || _reference == null)
            return false;
        _r = AccessTools.Field(_reference, "R");
        _g = AccessTools.Field(_reference, "G");
        _b = AccessTools.Field(_reference, "B");
        _load = AccessTools.Method(_reference, "OnDataLoad", new[] { modType });
        _id = AccessTools.Property(template, "Id");
        _mod = AccessTools.Property(template, "Mod");
        _modId = _mod == null ? null : AccessTools.Property(_mod.PropertyType, "Id");
        return _r != null && _g != null && _b != null && _load != null && _id != null && _mod != null && _modId != null
            && _reference.GetConstructor(Type.EmptyTypes) != null;
    }

    internal static float Luminance(float r, float g, float b) => 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;

    /// <summary>A colour in white light, lit by a light: by the light's hue, per channel, at the colour's own
    /// luminance. No light (no adaptation) is white light.</summary>
    internal static float[] LitBy(float[] hue, float luminance, (float R, float G, float B)? light)
    {
        float r = hue[0], g = hue[1], b = hue[2];
        if (light is { } l && Luminance(l.R, l.G, l.B) is var y && y > 1e-6f)
        {
            r *= l.R / y;
            g *= l.G / y;
            b *= l.B / y;
        }
        float k = luminance / Math.Max(Luminance(r, g, b), 1e-9f);
        return new[] { r * k, g * k, b * k };
    }

    /// <summary>The measured colour for a template from the game's own content, or null.</summary>
    internal static float[]? MeasuredFor(object template)
    {
        object? mod = _mod!.GetValue(template);
        return mod != null && _modId!.GetValue(mod) as string == GameMod
            && _id!.GetValue(template) is string id && Measured.TryGetValue(id, out float[]? colour) ? colour : null;
    }

    private static Entry Create(object template)
    {
        object stock = _colour!.GetValue(template)!;
        float r = (float)_r!.GetValue(stock)!, g = (float)_g!.GetValue(stock)!, b = (float)_b!.GetValue(stock)!;
        float y = Luminance(r, g, b);
        float[] hue = MeasuredFor(template) ?? (y > 1e-9f ? new[] { r / y, g / y, b / y } : new[] { 1f, 1f, 1f });
        return new Entry(Activator.CreateInstance(_reference!)!, hue, y);
    }

    /// <summary>What the distant sprites read in place of a template's ColorRgb: its colour, lit by the star, as an
    /// eye sees it at the brightness the sprite was just given (EyeColour): a faint moon's colour fades to white.</summary>
    public static object? LitColour(object? template)
    {
        if (template == null)
            return null;
        if (_failures >= GiveUpAfter)
            return _colour!.GetValue(template);
        try
        {
            Entry e = Entries.GetValue(template, NewEntry);
            var light = Adaptation.Light;
            float[] c = LitBy(e.Hue, e.Luminance, light);
            if (ReferenceEquals(template, PlanetPhotometry.LastTemplate) && !double.IsNaN(PlanetPhotometry.LastMagnitude))
            {
                (double eyeL, double eyeS) = Adaptation.EyeRatios;
                c = EyeColour.Seen(c, PlanetPhotometry.LastMagnitude, eyeL, eyeS);
            }
            if (!e.Lit || e.Shown == null || !Same(e.Shown, c))
            {
                _r!.SetValue(e.Reference, c[0]);
                _g!.SetValue(e.Reference, c[1]);
                _b!.SetValue(e.Reference, c[2]);
                _load!.Invoke(e.Reference, new object?[] { null });
                e.Light = light;
                e.Shown = c;
                e.Lit = true;
            }
            return e.Reference;
        }
        catch (Exception ex)
        {
            if (++_failures == GiveUpAfter)
                ShaderShadow.Log("WARN: bodies keep the game's colours from here on: " + ex.Message);
            return _colour!.GetValue(template);
        }
    }

    /// <summary>Whether two colours differ by less than shows: a part in a thousand.</summary>
    private static bool Same(float[] a, float[] b) =>
        Math.Abs(a[0] - b[0]) <= 1e-3f * Math.Max(Math.Abs(b[0]), 1e-3f) && Math.Abs(a[1] - b[1]) <= 1e-3f * Math.Max(Math.Abs(b[1]), 1e-3f)
        && Math.Abs(a[2] - b[2]) <= 1e-3f * Math.Max(Math.Abs(b[2]), 1e-3f);

    /// <summary>Transpiler on StaticCelestialDistanceRendering.UpdateRenderData: the sprite's colour comes from
    /// LitColour instead of the template's ColorRgb. One read is expected there; otherwise nothing changes.</summary>
    public static IEnumerable<CodeInstruction> SpriteTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        int[] reads = code.Select((c, i) => IsColourRead(c) ? i : -1).Where(i => i >= 0).ToArray();
        if (reads.Length != 1)
        {
            ShaderShadow.Log($"WARN: {reads.Length} reads of a body's colour in the distant sprites, 1 expected; "
                             + "they keep the game's colours");
            return code;
        }
        int at = reads[0];
        code[at] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BodyColours), nameof(LitColour)))
        {
            labels = code[at].labels,
            blocks = code[at].blocks,
        };
        code.Insert(at + 1, new CodeInstruction(OpCodes.Castclass, _reference));
        SpritesPatched = true;
        return code;
    }

    internal static bool IsColourRead(CodeInstruction c) =>
        c.opcode == OpCodes.Ldfld && c.operand is FieldInfo f && _colour != null
        && f.Name == _colour.Name && f.DeclaringType == _colour.DeclaringType;


    // ------------------------------------------------------------------ planetshine
    // The vessel shaders take planetshine as a light at the planet's centre. Here it is set to the real irradiance
    // where the camera is: the star's light as the planet's surface reflects it, over the part of its sunlit side in
    // view, which dims with distance by itself. Their own fade to nothing by 0.0001 AU from the centre is taken out of
    // them (ShaderShadow, StarShaders.VesselShaders); where it cannot be, the value is written over it.
    private const double MetresPerAu = 149597870700.0;
    private const double EngineFadeAu = 0.0001;
    private const double MinFade = 0.05;
    private const int BetaSteps = 48, PhiSteps = 48;     // steps across the disc resolve a crescent lit from behind (Titan to 4%)

    private sealed class Shine
    {
        public readonly float[] Hue;          // colour in white light, at unit luminance
        public readonly float Albedo;         // the share of the sunlight it reflects
        public readonly Photometry? Law;      // how it sends that light out; none is a matte surface
        public readonly string Source;
        public bool Logged;

        public Shine(float[] hue, float albedo, Photometry? law, string source)
        {
            Hue = hue;
            Albedo = albedo;
            Law = law;
            Source = source;
        }
    }

    /// <summary>
    /// A body's measured reflectance in visible light (BodyAlbedos.Generated.cs): the share of the light on it that it
    /// reflects in all (its spherical albedo), its brightness seen full (geometric albedo) and phase integral, and a
    /// surface law that gives them. The law, rho = (Albedo / pi) k(alpha) / N(mu0), reflects the share Albedo of the
    /// light on every patch at any Sun height, as a matte surface does, and sends it out by phase angle as k says, so
    /// that from afar the body follows its measured phase curve. make_body_albedos.py solves k for each body.
    /// </summary>
    internal sealed class Photometry
    {
        public readonly float Albedo, Geometric, PhaseIntegral;
        public readonly string Source;
        private readonly float[] _logK, _n;
        private readonly double _perK, _perN;

        /// <param name="k">k(alpha), evenly from 0 to 180 degrees of phase.</param>
        /// <param name="n">N(mu0), evenly from mu0 = 0 to 1.</param>
        public Photometry(float albedo, float geometric, float phaseIntegral, string source, float[] k, float[] n)
        {
            Albedo = albedo;
            Geometric = geometric;
            PhaseIntegral = phaseIntegral;
            Source = source;
            _logK = k.Select(v => (float)Math.Log(v)).ToArray();
            _n = n;
            _perK = (k.Length - 1) / Math.PI;
            _perN = n.Length - 1;
        }

        /// <summary>How much brighter than a matte surface of the same albedo a patch is, with the Sun at mu0 and seen at
        /// phase angle alpha: k(alpha) / N(mu0), k read between its steps in log, as it was fitted.</summary>
        public double Law(double mu0, double cosAlpha)
        {
            double a = Math.Acos(Math.Clamp(cosAlpha, -1.0, 1.0)) * _perK;
            int i = Math.Min((int)a, _logK.Length - 2);
            double m = Math.Clamp(mu0, 0.0, 1.0) * _perN;
            int j = Math.Min((int)m, _n.Length - 2);
            return Math.Exp(_logK[i] + (a - i) * (_logK[i + 1] - _logK[i])) / (_n[j] + (m - j) * (_n[j + 1] - _n[j]));
        }
    }

    private static readonly ConditionalWeakTable<object, Shine> Shines = new();
    private static readonly ConditionalWeakTable<object, Shine>.CreateValueCallback NewShine = CreateShine;
    private static readonly double[] CosPhi =
        Enumerable.Range(0, PhiSteps).Select(j => Math.Cos((j + 0.5) * 2.0 * Math.PI / PhiSteps)).ToArray();
    private static readonly double[] SinPhi =
        Enumerable.Range(0, PhiSteps).Select(j => Math.Sin((j + 0.5) * 2.0 * Math.PI / PhiSteps)).ToArray();

    private static FieldInfo? _lighting, _planetColour, _planetPosition, _planetRadius, _sunPosition, _sunColour,
                              _atmosphereHeight, _f4W;
    private static readonly FieldInfo?[] _f4 = new FieldInfo?[3];
    private static FieldInfo? _diffuse, _scattering, _meanAlbedo;
    private static PropertyInfo? _modPath;
    private static Type? _atmospheric, _staticCelestial;
    private static readonly Dictionary<Type, (MethodInfo? Camera, PropertyInfo? Radius)> _airless = new();
    private static MethodInfo? _positionEgo;
    private static readonly Dictionary<Type, PropertyInfo?> _slots = new(), _templates = new();
    private static int _shineFailures;

    /// <summary>Finds the lighting uniform's planetshine, planet and Sun; false leaves planetshine as the game sets it.
    /// A body's map and mean albedo, for bodies with no measurements, are optional.</summary>
    internal static bool ResolvePlanetshine(Type program)
    {
        _atmospheric = AccessTools.TypeByName("KSA.AtmosphericBody");
        _lighting = AccessTools.Field(program, "_lightingData");
        Type? ubo = _lighting?.FieldType.GetElementType();
        _planetColour = ubo == null ? null : AccessTools.Field(ubo, "PlanetColor");
        _planetPosition = ubo == null ? null : AccessTools.Field(ubo, "PlanetPosition");
        _planetRadius = ubo == null ? null : AccessTools.Field(ubo, "PlanetRadius");
        _sunPosition = ubo == null ? null : AccessTools.Field(ubo, "SunPositionRadius");
        _sunColour = ubo == null ? null : AccessTools.Field(ubo, "SunColor");
        _atmosphereHeight = ubo == null ? null : AccessTools.Field(ubo, "AtmosphereHeight");
        Type? f4 = _planetColour?.FieldType;
        for (int i = 0; i < 3; i++)
            _f4[i] = f4 == null ? null : AccessTools.Field(f4, "XYZ"[i].ToString());
        _f4W = f4 == null ? null : AccessTools.Field(f4, "W");
        _staticCelestial = AccessTools.TypeByName("KSA.StaticCelestial");
        Type? camera = AccessTools.TypeByName("KSA.Camera");
        _positionEgo = camera?.GetMethods().FirstOrDefault(m => m.Name == "GetPositionEgo" && m.GetParameters().Length == 1
                                                            && m.GetParameters()[0].ParameterType.Name == "IPosition");

        Type? astronomical = AccessTools.TypeByName("KSA.AstronomicalTemplate");
        _diffuse = astronomical == null ? null : AccessTools.Field(astronomical, "DiffuseReference");
        _modPath = _diffuse == null ? null : AccessTools.Property(_diffuse.FieldType, "ModPath");
        _scattering = astronomical == null ? null : AccessTools.Field(astronomical, "ScatteringReference");
        _meanAlbedo = _scattering == null ? null : AccessTools.Field(_scattering.FieldType, "MeanAlbedo");

        return _atmospheric != null && _lighting != null && _lighting.IsStatic && _planetColour != null
            && _planetPosition?.FieldType == f4 && _sunPosition?.FieldType == f4 && _sunColour?.FieldType == f4
            && _planetRadius?.FieldType == typeof(float) && _f4.All(f => f?.FieldType == typeof(float));
    }

    /// <summary>Planetshine at an observer, per unit albedo and sunlight, on a plate facing the planet's centre: the
    /// planet's surface law summed over the directions in which the observer sees it. 1 at the surface under an
    /// overhead Sun whatever the law; far off (p / A) Phi(alpha) (R/r)^2 with a measured one, 2/3 (R/r)^2 at full phase
    /// for a matte (Lambert) planet, the law without one; 0 with only the night side in view.</summary>
    internal static double PlanetshineFactor(double ox, double oy, double oz, double sx, double sy, double sz,
                                             double radius, Photometry? law = null)
    {
        double r = Math.Sqrt(ox * ox + oy * oy + oz * oz), sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (!(radius > 0.0) || !(r > 0.0) || !(sl > 0.0))
            return 0.0;
        sx /= sl;
        sy /= sl;
        sz /= sl;
        double nx = -ox / r, ny = -oy / r, nz = -oz / r;       // toward the centre
        r = Math.Max(r, radius * (1.0 + 1e-6));
        bool xish = Math.Abs(nx) < 0.9;
        double ux = xish ? 0.0 : nz, uy = xish ? -nz : 0.0, uz = xish ? ny : -nx;   // n x (the x or y axis)
        double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ul;
        uy /= ul;
        uz /= ul;
        double vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
        // Steps in beta = betaMax sin(theta), even in theta: close together toward the limb, where the light falls
        // to nothing as a square root and even steps would overshoot.
        double betaMax = Math.Asin(radius / r), sum = 0.0;
        for (int i = 0; i < BetaSteps; i++)
        {
            double theta = (i + 0.5) * 0.5 * Math.PI / BetaSteps;
            double beta = betaMax * Math.Sin(theta), sb = Math.Sin(beta), cb = Math.Cos(beta);
            double t = r * cb - Math.Sqrt(Math.Max(radius * radius - r * r * sb * sb, 0.0));
            double ring = 0.0;
            for (int j = 0; j < PhiSteps; j++)
            {
                double dx = cb * nx + sb * (CosPhi[j] * ux + SinPhi[j] * vx);
                double dy = cb * ny + sb * (CosPhi[j] * uy + SinPhi[j] * vy);
                double dz = cb * nz + sb * (CosPhi[j] * uz + SinPhi[j] * vz);
                double mu = ((ox + t * dx) * sx + (oy + t * dy) * sy + (oz + t * dz) * sz) / radius;
                if (mu > 0.0)
                    ring += law == null ? mu : mu * law.Law(mu, -(dx * sx + dy * sy + dz * sz));
            }
            sum += ring * cb * sb * betaMax * Math.Cos(theta);
        }
        return sum * (0.5 * Math.PI / BetaSteps) * (2.0 * Math.PI / PhiSteps) / Math.PI;
    }

    /// <summary>The vessel shaders' fade, smoothstep(0.0001, 0.0, distance in AU).</summary>
    internal static double EngineFade(double metres)
    {
        double t = Math.Clamp(1.0 - metres / (EngineFadeAu * MetresPerAu), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>What a body's planetshine is made of. Its colour: measured for the Sun's bodies, otherwise the average of
    /// its map, last its own colour. How it reflects: measured for the Sun's bodies that have been (albedo and phase
    /// curve), otherwise the albedo of the surface the game renders, as a matte surface.</summary>
    private static Shine CreateShine(object template)
    {
        float[]? hue = MeasuredFor(template);
        string colourFrom = "measured colour";
        if (hue == null)
        {
            float[]? map = MapAverage(template);
            hue = map != null ? AtUnitLuminance(map) : StockHue(template);
            colourFrom = map != null ? "its map's colour" : "its own colour";
        }
        if (MeasuredPhotometryFor(template) is Photometry m)
            return new Shine(hue, m.Albedo, m, colourFrom + ", measured albedo and phase curve (" + m.Source + ")");
        return new Shine(hue, (float)SurfaceAlbedo(MeanAlbedo(template) ?? DefaultHapkeAlbedo), null,
                         colourFrom + ", the albedo of the surface the game renders");
    }

    /// <summary>The measured reflectance for a template from the game's own content, or null.</summary>
    internal static Photometry? MeasuredPhotometryFor(object template) =>
        IsGameBody(template) && _id?.GetValue(template) is string id
        && MeasuredPhotometry.TryGetValue(id, out Photometry? m) ? m : null;

    private static bool IsGameBody(object template) =>
        _mod?.GetValue(template) is object mod && _modId?.GetValue(mod) as string == GameMod;

    /// <summary>How a body reflects, for estimating the light in view (SceneLight): measured for the Sun's bodies that
    /// have been, otherwise the albedo of its surface as a matte one. No map is read.</summary>
    internal static (double Albedo, Photometry? Law) SceneSurface(object template)
    {
        if (MeasuredPhotometryFor(template) is Photometry m)
            return (m.Albedo, m);
        return (SurfaceAlbedo((_scattering != null ? MeanAlbedo(template) : null) ?? DefaultHapkeAlbedo), null);
    }

    /// <summary>The Hapke single-scattering albedo the game renders a surface with when the body gives none.</summary>
    private const float DefaultHapkeAlbedo = 0.5f;

    /// <summary>How much light a surface the game renders with Hapke single-scattering albedo w reflects in all: the
    /// spherical albedo, 2 x the integral of r_h(mu) mu dmu with r_h = (1 - g) / (1 + 2 g mu), g = sqrt(1 - w).
    /// The game's own MeanAlbedo is that w, not a planet's albedo: the Moon's 0.32357 reflects 0.088 in all (0.076
    /// measured, in visible light).</summary>
    internal static double SurfaceAlbedo(double w)
    {
        double g = Math.Sqrt(1.0 - Math.Clamp(w, 0.0, 1.0));
        if (g < 1e-4)
            return 1.0 - 7.0 / 3.0 * g;                  // w near 1: the series, where the ratio loses digits
        return (1.0 - g) / g * (1.0 - Math.Log(1.0 + 2.0 * g) / (2.0 * g));
    }

    private static float[] AtUnitLuminance(float[] c)
    {
        float y = Luminance(c[0], c[1], c[2]);
        return y > 1e-9f ? new[] { c[0] / y, c[1] / y, c[2] / y } : new[] { 1f, 1f, 1f };
    }

    private static float[] StockHue(object template)
    {
        object stock = _colour!.GetValue(template)!;
        return AtUnitLuminance(new[] { (float)_r!.GetValue(stock)!, (float)_g!.GetValue(stock)!, (float)_b!.GetValue(stock)! });
    }

    private static float? MeanAlbedo(object template)
    {
        object? scattering = _scattering?.GetValue(template);
        object? albedo = scattering == null ? null : _meanAlbedo?.GetValue(scattering);
        return albedo == null ? null : (float?)PlanetPhotometry.ReadFloat(albedo);
    }

    /// <summary>The average colour of the body's surface map, or null if it has none that can be read.</summary>
    internal static float[]? MapAverage(object template)
    {
        if (_diffuse?.GetValue(template) is not object texture || _modPath == null)
            return null;
        try
        {
            return _modPath.GetValue(texture) is string path && path.Length > 0
                ? MapColour.Average(Path.GetFullPath(path)) : null;
        }
        catch (Exception)
        {
            return null;                               // no mod behind the reference: nothing to read
        }
    }

    private static float[] Xyz(object f4) =>
        new[] { (float)_f4[0]!.GetValue(f4)!, (float)_f4[1]!.GetValue(f4)!, (float)_f4[2]!.GetValue(f4)! };

    /// <summary>What planetshine is written over to make up for the vessel shaders' fade at this distance: nothing
    /// once the fade is gone from them (ShaderShadow.PlanetshineUnfaded).</summary>
    internal static double FadeWrittenOver(double metres)
    {
        if (ShaderShadow.PlanetshineUnfaded)
            return 1.0;
        double fade = EngineFade(metres);
        return fade > 0.0 ? Math.Max(fade, MinFade) : 1.0;
    }

    /// <summary>Postfix on Program.UpdatePlanetShaderData: the planetshine the engine has just set, a fixed share of
    /// the body's colour, replaced with the real irradiance at the camera. The engine gives planetshine only to a
    /// body with an atmosphere, and near an airless one leaves the lighting uniform's planet as the last it was:
    /// that body is written in as the engine writes its own (with no air), and lights vessels the same way.</summary>
    public static void UpdatePlanetShaderDataPostfix(object? nearbyCelestial, object viewport)
    {
        if (nearbyCelestial == null || _atmospheric == null || _shineFailures >= GiveUpAfter)
            return;
        bool air = _atmospheric.IsInstanceOfType(nearbyCelestial);
        if (!air && (_staticCelestial == null || !_staticCelestial.IsInstanceOfType(nearbyCelestial)))
            return;
        try
        {
            if (_lighting!.GetValue(null) is not Array lighting)
                return;
            Type vt = viewport.GetType();
            if (!_slots.TryGetValue(vt, out PropertyInfo? slotProp))
                _slots[vt] = slotProp = AccessTools.Property(vt, "ShaderSlot");
            if (slotProp?.GetValue(viewport) is not int slot || slot < 0 || slot >= lighting.Length)
                return;
            Type ct = nearbyCelestial.GetType();
            if (!_templates.TryGetValue(ct, out PropertyInfo? templateProp))
                _templates[ct] = templateProp = PlanetPhotometry.FindPropertyPublic(ct, "BodyTemplate");
            if (templateProp?.GetValue(nearbyCelestial) is not object template)
                return;

            object box = lighting.GetValue(slot)!;
            if (!air && !WriteAirless(box, nearbyCelestial, viewport))
                return;
            float[] planet = Xyz(_planetPosition!.GetValue(box)!), sun = Xyz(_sunPosition!.GetValue(box)!);
            float[] light = Xyz(_sunColour!.GetValue(box)!);
            double radius = (float)_planetRadius!.GetValue(box)!;
            double ox = -planet[0], oy = -planet[1], oz = -planet[2];
            double distance = Math.Sqrt(ox * ox + oy * oy + oz * oz);
            Shine s = Shines.GetValue(template, NewShine);
            double g = PlanetshineFactor(ox, oy, oz, sun[0] - planet[0], sun[1] - planet[1], sun[2] - planet[2], radius,
                                         s.Law);
            double k = s.Albedo * g / FadeWrittenOver(distance);

            object shine = _planetColour!.GetValue(box)!;
            for (int i = 0; i < 3; i++)
                _f4[i]!.SetValue(shine, (float)(light[i] * s.Hue[i] * k));
            _planetColour.SetValue(box, shine);
            lighting.SetValue(box, slot);
            _shineFailures = 0;

            if (!s.Logged && g > 0.0)
            {
                s.Logged = true;
                string reflects = s.Law == null ? FormattableString.Invariant($"albedo {s.Albedo:0.###}")
                    : FormattableString.Invariant(
                        $"albedo {s.Albedo:0.###} ({s.Law.Geometric:0.###} seen full, phase integral {s.Law.PhaseIntegral:0.###})");
                ShaderShadow.Log(FormattableString.Invariant(
                    $"planetshine from {_id!.GetValue(template)}: {reflects} and colour ({s.Hue[0]:0.###}, {s.Hue[1]:0.###}, {s.Hue[2]:0.###}) ({s.Source}); {(distance - radius) / 1000.0:0} km up, {100.0 * s.Albedo * g:0.#}% of the sunlight"));
            }
        }
        catch (Exception ex)
        {
            if (++_shineFailures == GiveUpAfter)
                ShaderShadow.Log("WARN: planetshine is left as the game sets it from here on: " + ex.Message);
        }
    }

    /// <summary>Writes an airless body into the lighting uniform's planet, where the camera sees it, with no air; false
    /// if its place or size cannot be read.</summary>
    private static bool WriteAirless(object box, object body, object viewport)
    {
        Type vt = viewport.GetType(), bt = body.GetType();
        if (!_airless.TryGetValue(vt, out var v))
            _airless[vt] = v = (AccessTools.Method(vt, "GetCamera"), null);
        if (!_airless.TryGetValue(bt, out var b))
            _airless[bt] = b = (null, PlanetPhotometry.FindPropertyPublic(bt, "MeanRadius"));
        object? camera = v.Camera?.Invoke(viewport, null);
        if (camera == null || _positionEgo == null || b.Radius?.GetValue(body) is not double radius || !(radius > 0.0)
            || _atmosphereHeight == null || _f4W == null)
            return false;
        var (x, y, z) = PlanetPhotometry.VecPublic(_positionEgo.Invoke(camera, new[] { body })!);
        WritePlanet(box, x, y, z, radius);
        return true;
    }

    /// <summary>The lighting uniform's planet (a boxed UboLightingData), set to a body with no air at (x, y, z) from
    /// the camera, as the engine writes a body with air.</summary>
    internal static void WritePlanet(object box, double x, double y, double z, double radius)
    {
        object position = _planetPosition!.GetValue(box)!;
        _f4[0]!.SetValue(position, (float)x);
        _f4[1]!.SetValue(position, (float)y);
        _f4[2]!.SetValue(position, (float)z);
        _f4W!.SetValue(position, 1f);
        _planetPosition.SetValue(box, position);
        _planetRadius!.SetValue(box, (float)radius);
        _atmosphereHeight!.SetValue(box, 0f);
    }
}
