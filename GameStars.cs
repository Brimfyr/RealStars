using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The game's own stars, drawn as the Sun is.
///
/// KSA 2026.10 put more than one star in a system: Sol's interstellar system carries Alpha Centauri A, B
/// and Proxima, Barnard's Star and Tau Ceti at their real places. The engine lights everything by the
/// star nearest the camera (Universe.WorldSun), draws that one's sphere close up, and draws every other
/// star as a dot sized by its radius. Our catalogue already had those stars in the sky at their measured
/// places, so each was drawn twice, and the star lighting the scene was drawn by our Sun's code, at the
/// Sun's brightness and wherever the Sun is.
///
/// So the star shader draws every star the game has, as it always drew the Sun:
///
///   the lighting star   in the catalogue's Sun, the one instance at the origin. The shader places it
///                       where the engine's lighting block says it is, relative to the camera, which keeps
///                       its precision anywhere, at the magnitude SunGlow writes beside its disc's size;
///                       the merge pass draws its glare, and the engine's sphere takes over close up.
///   the other stars     in room kept after the catalogue, each at its place relative to the lighting
///                       star. Relative, because a place in parsecs from the Sun keeps a float's 7 digits:
///                       3.7 million km of slop at Alpha Centauri, minutes of arc as seen from a planet of B.
///                       Each is a point at its own magnitude, like any star.
///
/// Brightness is the catalogue's wherever it has the star: a game star is matched to the catalogue star at
/// its place and of its brightness, which is then not drawn itself. A star the catalogue does not have is
/// estimated from its mass, radius and light (make_star_estimates.py). Every star is drawn in the colour of
/// its own light, which is raw by this mod's convention (SunLight.cs restates the stock ones as their stars'
/// colours in the catalogue), so the glare of a star and the light on its planets are one colour.
///
/// Fail-soft: without our catalogue, or on any error, the catalogue is left whole and the engine's dots
/// come back; the lighting star's magnitude still reaches SunGlow and the planets.
/// </summary>
internal static partial class GameStars
{
    /// <summary>Room kept after the catalogue for the game's stars other than the lighting one; the engine's
    /// buffer is made bigger by as much (<see cref="GetStarCountPostfix"/>).</summary>
    public const int Slots = 64;

    /// <summary>A game star's place relative to the lighting star, in parsecs, is written times a power of two,
    /// which a float carries exactly in its exponent. Anything over 2^40 in some coordinate is one of those: the
    /// catalogue's largest coordinate is 20 kpc. There are two scales, one for each half of the slots (see
    /// <see cref="SecondSet"/>): 2^72 and 2^116. A place between <see cref="MinOffsetPc"/> and
    /// <see cref="MaxOffsetPc"/> from the lighting star has its largest coordinate between 2^41 and 2^81 at the
    /// first and between 2^85 and 2^125 at the second, so the shader tells the halves apart at 2^83. Its
    /// rsGameStarMark, rsGameStarSplit, rsGameStarScale and rsGameStarScaleSecond must agree.</summary>
    public const int ScaleExponent = 72, ScaleExponentSecond = 116;
    public const float Mark = 1.099511627776e12f, Split = 9.671406556917033e24f;

    /// <summary>No star is drawn nearer the lighting star than this (30,000 km: no star sits that near another's
    /// centre), nor further than this, in parsecs.</summary>
    internal const double MinOffsetPc = 1e-9, MaxOffsetPc = 512.0;

    /// <summary>
    /// Which half of the slots is live.
    ///
    /// The slots hold places relative to the lighting star, and the star buffer is one piece of memory the GPU
    /// reads as it draws, shared by every frame it has not finished. So when the lighting star changed, the frame
    /// before, drawn with the star it was lit by, could find the slots already rewritten about the new one: every
    /// one of the game's stars in the wrong place for that frame, and the new star's close companion on top of
    /// the old lighting star. Crossing from Proxima's side to Alpha Centauri's, some 6,500 AU out, that put Alpha
    /// Centauri B, eight thousand times the brighter of the two from there, three pixels from Proxima for one frame
    /// (user, 2026-10-09: "It's just the star that flashes, for one frame before going back to its normal
    /// brightness. I was able to get it to happen every time at a certain zoom step by zooming in and out").
    ///
    /// So a change of lighting star is written into the other half, at that half's own scale, and the half that
    /// was live is left as it was. Each frame's camera block says which half is its own (Starburst, the camera
    /// word), and the star shader draws only that one: a frame goes on drawing the stars about the star that lit
    /// it, whenever the GPU gets to it.
    /// </summary>
    internal static bool SecondSet { get; private set; }

    /// <summary>A place relative to the lighting star, in parsecs, as a slot of this half holds it.</summary>
    internal static (float X, float Y, float Z) SlotPlace(double x, double y, double z, bool second)
    {
        int e = second ? ScaleExponentSecond : ScaleExponent;
        return ((float)Math.ScaleB(x, e), (float)Math.ScaleB(y, e), (float)Math.ScaleB(z, e));
    }

    private const double ParsecM = 3.0856775814913673e16;
    private const double SolarMassKg = 1.98841e30;        // the game's own (KSA.Constants.SOLAR_MASS)
    private const double SolarRadiusM = 6.957e8;          // IAU nominal, as the dwarf table's radii are
    private const double SolarTeff = 5772.0;
    private const double SolarMbol = 4.74;

    /// <summary>The catalogue's byte, as make_star_binary.py writes it and the shader reads it.</summary>
    private const double MagFaint = 16.5, BytesPerMag = 10.0;

    /// <summary>
    /// A game star is the catalogue's star when it sits within this much of it, in parsecs, plus a share of its
    /// distance from the Sun for a mod's placement from a different catalogue; and when the brightness its own
    /// data give is within this many magnitudes of the catalogue's. The game's Proxima is 0.054 pc from the
    /// real one (its orbit is the game's own), and Barnard's Star's own data put it 0.9 mag out.
    /// </summary>
    private const double MatchPc = 0.1, MatchShare = 0.03, MatchMag = 2.0;

    /// <summary>A star that has moved by this share of its distance from the lighting star is written again:
    /// two arcseconds at most, seen from anywhere near the lighting star.</summary>
    private const double MoveShare = 1e-5;
    private const long MinUploadMs = 100;

    /// <summary>The star lighting the scene this frame: where it is (ecliptic, metres), its radius, and its V
    /// absolute magnitude. The Sun's at the origin until a system has one.</summary>
    internal readonly record struct Light(double X, double Y, double Z, double Radius, double AbsMag);

    /// <summary>The Sun's magnitude as this mod draws it: the catalogue's byte for it, else the dwarf sequence's.</summary>
    internal static double SunMag { get; private set; } = 4.80;

    private static Light SolLight => new(0, 0, 0, 6.96342e8, SunMag);

    internal static Light Lighting { get; private set; } = new(0, 0, 0, 6.96342e8, 4.80);

    /// <summary>How much fainter than the Sun the lighting star is, in magnitudes: what a planet it lights loses.</summary>
    internal static double LightingFainter => Lighting.AbsMag - SunMag;

    /// <summary>True while the game's stars are ours to draw, and the engine's dots for them are left out.</summary>
    public static bool DrawsStars => _instances != null;

    private sealed class Star
    {
        public readonly object Body;
        public readonly string Id;
        public readonly double Radius;
        public byte Magnitude;
        public uint Colour;               // R << 24 | G << 16 | B << 8, as the catalogue packs it (ColourBits)
        public int Catalogue = -1;
        public double Estimate;
        public string Source = "";
        public double X, Y, Z;            // this frame, ecliptic metres
        public double Wx = double.NaN, Wy, Wz;   // as last written, parsecs from the lighting star; NaN: never
        public uint WrittenPacked;
        public bool Covered;              // behind a vessel, from the main camera: not drawn (GameStars.Cover.cs)

        public Star(object body, string id, double radius)
        {
            Body = body;
            Id = id;
            Radius = radius;
        }

        public uint Packed => Colour | Magnitude;
        public double AbsMag => MagFaint - (Magnitude - 1) / BytesPerMag;
    }

    // ---- the engine's star buffer -------------------------------------------------------------
    private static Array? _instances;
    private static int _sun = -1, _slotFirst, _slotCount, _catFirst;
    private static float[]? _catStart;
    private static uint[]? _catPacked;
    private static object? _technique;
    private static MethodInfo? _upload;
    private static object[]? _uploadArgs;
    private static FieldInfo? _maxInstances;

    // ---- the game ----------------------------------------------------------------------------
    private static PropertyInfo? _worldSun, _currentSystem;
    private static FieldInfo? _starsField;
    private static readonly Dictionary<Type, MethodInfo?> _positions = new();
    private static object? _system, _lightingBody;
    private static List<Star> _stars = new();
    private static readonly List<int> _hidden = new();
    private static long _uploadedAt;
    private static bool _resolved, _loggedLight;
    private static int _failures;
    private const int GiveUpAfter = 120;

    /// <summary>Postfix on ModLibrary.GetStarCount, which sizes the engine's star buffer: room for ours too.</summary>
    public static void GetStarCountPostfix(ref int __result) => __result += Slots;

    /// <summary>How many slots there is room for after what is loaded so far.</summary>
    public static int Room(object technique, object viewport)
    {
        try
        {
            int slot = (int)AccessTools.Property(viewport.GetType(), "ShaderSlot")!.GetValue(viewport)!;
            var counts = (int[])AccessTools.Property(technique.GetType(), "InstanceCount")!.GetValue(technique)!;
            _maxInstances ??= AccessTools.Field(technique.GetType(), "_maxInstances");
            return _maxInstances?.GetValue(technique) is int max ? Math.Clamp(max - counts[slot], 0, Slots) : 0;
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: no room found for the game's stars: " + ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Called by the loader once our catalogue and <paramref name="slots"/> hidden instances after it are in the
    /// technique, last in the viewport's instances. Fail-soft: anything unexpected leaves the engine drawing its
    /// own stars' dots.
    /// </summary>
    public static void Attach(object technique, object viewport, float[] start, uint[] packed, int slots)
    {
        try
        {
            int slot = (int)AccessTools.Property(viewport.GetType(), "ShaderSlot")!.GetValue(viewport)!;
            var perViewport = (Array)AccessTools.Field(technique.GetType(), "Instances")!.GetValue(technique)!;
            var instances = (Array)perViewport.GetValue(slot)!;
            var counts = (int[])AccessTools.Property(technique.GetType(), "InstanceCount")!.GetValue(technique)!;
            int first = counts[slot] - slots - packed.Length;
            if (slots <= 0)
                throw new InvalidOperationException("no room was kept after the catalogue");
            // Drawn by us only where the engine's dots for them can be left out, or every star shows twice.
            if (!DotsPatched)
                throw new InvalidOperationException("the engine's own dots for them could not be left out");
            if (!StarBuffer.FitsSprite(instances.GetType().GetElementType()!))
                throw new InvalidOperationException("the star instances are not laid out as a float3 and a uint");
            if (first < 0 || !StarBuffer.ReadsBack(instances, first, start, packed))
                throw new InvalidOperationException("the catalogue is not where it was added");
            int sun = packed.Length - 1;
            if (start[3 * sun] != 0f || start[3 * sun + 1] != 0f || start[3 * sun + 2] != 0f)
                throw new InvalidOperationException("the catalogue's last star is not the Sun at the origin");
            _upload = AccessTools.Method(technique.GetType(), "UpdateInstanceBuffer",
                                         new[] { AccessTools.TypeByName("KSA.IViewport")!, typeof(int) })
                      ?? throw new InvalidOperationException("UpdateInstanceBuffer(IViewport, int) not found");

            _technique = technique;
            _uploadArgs = new object[] { viewport, 0 };
            _catFirst = first;
            _catStart = start;
            _catPacked = packed;
            _sun = first + sun;
            _slotFirst = first + packed.Length;
            _slotCount = slots;
            SunMag = MagFaint - ((packed[sun] & 0xFF) - 1) / BytesPerMag;
            _instances = instances;
            ShaderShadow.Log($"the game's stars: {slots} kept after the catalogue, drawn as the Sun is");
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the game's stars stay the engine's dots: " + ex.Message);
        }
    }

    /// <summary>Postfix on Program.UpdateShaderData, ahead of SunGlow's, which reads <see cref="Lighting"/>.</summary>
    [HarmonyPriority(Priority.High)]
    public static void UpdateShaderDataPostfix(object viewport)
    {
        if (_failures >= GiveUpAfter) return;
        object? sun;
        Star? light = null;
        try
        {
            if (!_resolved)
            {
                Type universe = AccessTools.TypeByName("KSA.Universe")
                                ?? throw new InvalidOperationException("KSA.Universe not found");
                _worldSun = AccessTools.Property(universe, "WorldSun");
                _currentSystem = AccessTools.Property(universe, "CurrentSystem");
                _starsField = AccessTools.Field(universe, "_stars");
                if (_worldSun == null || _currentSystem == null || _starsField == null)
                    throw new InvalidOperationException("Universe.WorldSun, CurrentSystem or _stars not found");
                _resolved = true;
            }

            object? system = _currentSystem!.GetValue(null);
            if (!ReferenceEquals(system, _system))
                Rematch(system);

            sun = _worldSun!.GetValue(null);
            foreach (Star s in _stars)
            {
                (s.X, s.Y, s.Z) = PositionOf(s.Body);
                if (ReferenceEquals(s.Body, sun))
                    light = s;
            }
            Lighting = light == null ? SolLight : new Light(light.X, light.Y, light.Z, light.Radius, light.AbsMag);
            if (light != null && !_loggedLight)
            {
                ShaderShadow.Log(FormattableString.Invariant(
                    $"{light.Id} lights the scene: V absolute magnitude {light.AbsMag:F2} ({light.Source})"));
                _loggedLight = true;
            }
            _failures = 0;
        }
        catch (Exception ex)
        {
            // Without knowing which star lights the scene, the Sun's numbers stand in for it, and the game's
            // stars cannot be placed: the engine's dots come back.
            Lighting = SolLight;
            if (_failures++ == 0)
                Stand("WARN: the star lighting the scene could not be read: " + ex.GetBaseException().Message);
            return;
        }

        if (_instances == null) return;
        try
        {
            Write(light, !ReferenceEquals(sun, _lightingBody), Cover(viewport, light));
            _lightingBody = sun;
        }
        catch (Exception ex)
        {
            Stand("WARN: the game's stars went back to the engine's dots: " + ex.GetBaseException().Message);
        }
    }

    /// <summary>The engine draws its own stars again and our catalogue is whole: after an error, and for good.</summary>
    private static void Stand(string why)
    {
        ShaderShadow.Log(why + (_instances != null ? "; the engine draws its own stars' dots again" : ""));
        if (_instances == null) return;
        try
        {
            Span<StarBuffer.Sprite> all = StarBuffer.Sprites(_instances);
            foreach (int i in _hidden)
                all[i].Packed = _catPacked![i - _catFirst];
            all[_sun].Packed = _catPacked![_sun - _catFirst];
            ForgetCover();
            for (int k = 0; k < _slotCount; k++)
                all[_slotFirst + k].Packed = 0u;
            SecondSet = false;
            _upload!.Invoke(_technique, _uploadArgs);
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: and the catalogue could not be put back: " + ex.GetBaseException().Message);
        }
        _hidden.Clear();
        _instances = null;
    }

    // ---- which star is which ------------------------------------------------------------------

    /// <summary>A new system: its stars, what each looks like, and which catalogue stars they are.</summary>
    private static void Rematch(object? system)
    {
        _system = system;
        _lightingBody = null;
        _loggedLight = false;
        ForgetCover();
        _stars = new List<Star>();
        if (_instances != null)
        {
            Span<StarBuffer.Sprite> all = StarBuffer.Sprites(_instances);
            foreach (int i in _hidden)
                all[i].Packed = _catPacked![i - _catFirst];
        }
        _hidden.Clear();
        if (system == null || _starsField!.GetValue(null) is not Array bodies)
            return;

        foreach (object? body in bodies)
        {
            if (body == null) continue;
            Type t = body.GetType();
            string id = PlanetPhotometry.FindPropertyPublic(t, "Id")?.GetValue(body) as string ?? t.Name;
            double radius = PlanetPhotometry.FindPropertyPublic(t, "MeanRadius")?.GetValue(body) is double r ? r : 0.0;
            double mass = PlanetPhotometry.FindPropertyPublic(t, "Mass")?.GetValue(body) is double m ? m : 0.0;
            (double lr, double lg, double lb) = LightOf(body);
            var star = new Star(body, id, radius);
            (star.Estimate, string how) = Estimate(mass / SolarMassKg, radius / SolarRadiusM, lr, lg, lb);
            star.Magnitude = MagnitudeByte(star.Estimate);
            star.Colour = ColourBytes(lr, lg, lb);
            star.Source = how;
            (star.X, star.Y, star.Z) = PositionOf(body);
            _stars.Add(star);
        }
        if (_catStart != null)
        {
            int[] found = Match(_stars.Select(t => (t.X / ParsecM, t.Y / ParsecM, t.Z / ParsecM, t.Estimate)).ToList(),
                                _catStart, _catPacked!);
            for (int n = 0; n < found.Length; n++)
            {
                if (found[n] < 0) continue;
                Star t = _stars[n];
                uint p = _catPacked![found[n]];
                int k = 3 * found[n];
                double dx = _catStart[k] - t.X / ParsecM, dy = _catStart[k + 1] - t.Y / ParsecM,
                       dz = _catStart[k + 2] - t.Z / ParsecM;
                t.Catalogue = found[n];
                t.Magnitude = (byte)(p & 0xFF);
                double pc = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                t.Source = FormattableString.Invariant(
                    $"measured: the catalogue's star {pc:F4} pc from the game's place (its own data say {t.Estimate:F1})");
            }
        }

        if (_instances != null)
        {
            Span<StarBuffer.Sprite> all = StarBuffer.Sprites(_instances);
            foreach (Star s in _stars)
            {
                int i = s.Catalogue < 0 ? -1 : _catFirst + s.Catalogue;
                if (i < 0 || i == _sun) continue;    // the Sun's own instance is the lighting star's
                all[i].Packed &= 0xFFFFFF00u;         // magnitude byte 0: not drawn
                _hidden.Add(i);
            }
            foreach (Star s in _stars)
                s.Wx = double.NaN;                    // write every star afresh
        }
        foreach (Star s in _stars)
            ShaderShadow.Log(FormattableString.Invariant(
                $"star {s.Id}: V absolute magnitude {s.AbsMag:F1}, {s.Source}"));
    }

    /// <summary>
    /// Which catalogue star each game star is, or -1: the one at its place (parsecs, ecliptic, from the Sun) and
    /// of its brightness (the V absolute magnitude its own data give), best pairs first, so that two stars side
    /// by side (Alpha Centauri A and B) each find their own and no catalogue star is two of the game's.
    /// </summary>
    internal static int[] Match(IReadOnlyList<(double X, double Y, double Z, double AbsMag)> stars, float[] start, uint[] packed)
    {
        var pairs = new List<(double Score, int Star, int Index)>();
        for (int n = 0; n < stars.Count; n++)
        {
            (double x, double y, double z, double estimate) = stars[n];
            double reach = MatchPc + MatchShare * Math.Sqrt(x * x + y * y + z * z);
            for (int i = 0, k = 0; i < packed.Length; i++, k += 3)
            {
                double dx = start[k] - x, dy = start[k + 1] - y, dz = start[k + 2] - z;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > reach * reach || (packed[i] & 0xFF) == 0) continue;
                double off = MagFaint - ((packed[i] & 0xFF) - 1) / BytesPerMag - estimate;
                if (Math.Abs(off) > MatchMag) continue;
                pairs.Add((d2 / (reach * reach) + off * off / (MatchMag * MatchMag), n, i));
            }
        }
        int[] found = Enumerable.Repeat(-1, stars.Count).ToArray();
        var taken = new HashSet<int>();
        foreach (var p in pairs.OrderBy(p => p.Score))
            if (found[p.Star] < 0 && taken.Add(p.Index))
                found[p.Star] = p.Index;
        return found;
    }

    // ---- writing the instances -----------------------------------------------------------------

    /// <summary>The lighting star into the Sun's instance, the others into the live half of the room after the
    /// catalogue, each at its place relative to the lighting star; the buffer goes up when anything has moved
    /// enough. A new lighting star takes the other half: see <see cref="SecondSet"/>.</summary>
    private static void Write(Star? light, bool lightChanged, bool coverChanged)
    {
        Span<StarBuffer.Sprite> all = StarBuffer.Sprites(_instances!);
        bool urgent = lightChanged || coverChanged, moved = false;
        // Two halves while there is room for them and each frame can be told which is its own; else the one
        // set, rewritten in place, as it was before.
        bool halves = _slotCount >= 2 && Starburst.WordLive;
        if (lightChanged)
        {
            if (halves) SecondSet = !SecondSet;
            foreach (Star s in _stars)
                s.Wx = double.NaN;                       // every place is relative to the new one, and slots shift
        }
        if (!halves && _slotCount < 2) SecondSet = false;
        int room = _slotCount >= 2 ? _slotCount / 2 : _slotCount;
        int firstSlot = _slotFirst + (SecondSet ? room : 0);

        uint lightPacked = light?.Packed ?? _catPacked![_sun - _catFirst];
        if (all[_sun].Packed != lightPacked)
        {
            all[_sun].Packed = lightPacked;
            urgent = true;
        }

        double lx = light?.X ?? 0.0, ly = light?.Y ?? 0.0, lz = light?.Z ?? 0.0;
        int k = 0;
        foreach (Star s in _stars)
        {
            if (ReferenceEquals(s, light)) continue;
            if (k == room) break;                        // more stars than room: the rest go undrawn
            ref StarBuffer.Sprite slot = ref all[firstSlot + k++];
            double x = (s.X - lx) / ParsecM, y = (s.Y - ly) / ParsecM, z = (s.Z - lz) / ParsecM;
            double d = Math.Sqrt(x * x + y * y + z * z);
            // Nowhere to draw a star that is where the light is: no star sits within 30,000 km of another's centre.
            // Nor one too far for a slot to hold apart from the other half's. And none behind a vessel.
            uint packed = d < MinOffsetPc || d >= MaxOffsetPc || s.Covered ? 0u : s.Packed;
            double ox = x - s.Wx, oy = y - s.Wy, oz = z - s.Wz;
            bool far = double.IsNaN(s.Wx) || ox * ox + oy * oy + oz * oz > MoveShare * MoveShare * d * d;
            if (!far && slot.Packed == packed && s.WrittenPacked == packed) continue;
            (slot.X, slot.Y, slot.Z) = SlotPlace(x, y, z, SecondSet);
            slot.Packed = packed;
            (s.Wx, s.Wy, s.Wz, s.WrittenPacked) = (x, y, z, packed);
            moved = true;
        }
        // The rest of the live half. The other half is left as it is: a frame still being drawn may be its.
        for (; k < room; k++)
        {
            ref StarBuffer.Sprite slot = ref all[firstSlot + k];
            if (slot.Packed == 0u) continue;
            slot.Packed = 0u;
            urgent = true;
        }

        long now = Environment.TickCount64;
        if (urgent || (moved && now - _uploadedAt >= MinUploadMs))
        {
            // Written while earlier frames may still be drawing from the buffer: a frame that catches it half
            // done shows a star a step behind, once.
            _upload!.Invoke(_technique, _uploadArgs);
            _uploadedAt = now;
        }
    }

    // ---- the engine's dots ---------------------------------------------------------------------

    /// <summary>
    /// Transpiler on StaticCelestialDistanceRendering.UpdateRenderData: its call to AppendStars, which adds a
    /// dot for every star but the lighting one, goes ahead only while the game's stars are not ours to draw.
    /// Its three arguments are already on the stack when the choice is made, so the other way pops them.
    /// </summary>
    public static IEnumerable<CodeInstruction> AppendStarsTranspiler(IEnumerable<CodeInstruction> instructions,
                                                                    ILGenerator generator)
    {
        List<CodeInstruction> code = instructions.ToList();
        Type? distance = AccessTools.TypeByName("KSA.StaticCelestialDistanceRendering");
        MethodInfo? append = distance == null ? null : AccessTools.Method(distance, "AppendStars");
        int[] calls = code.Select((c, i) => append != null && c.Calls(append) ? i : -1).Where(i => i >= 0).ToArray();
        if (calls.Length != 1 || append!.GetParameters().Length != 3 || calls[0] + 1 >= code.Count)
        {
            ShaderShadow.Log($"WARN: {calls.Length} calls to AppendStars where 1 was expected; "
                             + "the engine's dots for its stars stay beside ours");
            return code;
        }
        int at = calls[0];
        Label ours = generator.DefineLabel(), after = generator.DefineLabel();
        code[at + 1].labels.Add(after);
        var choice = new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(GameStars), nameof(DrawsStars)));
        choice.labels.AddRange(code[at].labels);         // a jump to the call now reaches the choice first
        code[at].labels.Clear();
        code.InsertRange(at + 1, new[]
        {
            new CodeInstruction(OpCodes.Br, after),
            new CodeInstruction(OpCodes.Pop) { labels = { ours } },
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Pop),
        });
        code.InsertRange(at, new[] { choice, new CodeInstruction(OpCodes.Brtrue, ours) });
        DotsPatched = true;
        return code;
    }

    public static bool DotsPatched { get; private set; }

    // ---- what a star looks like, from what the game knows of it -----------------------------------

    /// <summary>
    /// A star's V absolute magnitude from its own data (make_star_estimates.py): a dwarf's of its mass when its
    /// radius is a dwarf's, otherwise from its size and the temperature of its light.
    /// </summary>
    internal static (double AbsMag, string How) Estimate(double massSuns, double radiusSuns, double r, double g, double b)
    {
        if (massSuns >= DwarfMass[0] && massSuns <= DwarfMass[^1] && radiusSuns > 0.0)
        {
            double lm = Math.Log(massSuns);
            double dwarfRadius = Math.Exp(Interpolate(lm, DwarfMass, DwarfRadius, logX: true, logY: true));
            double share = radiusSuns / dwarfRadius;
            if (share > 0.5 && share < 2.0)
                return (Interpolate(lm, DwarfMass, DwarfMv, logX: true, logY: false),
                        FormattableString.Invariant($"estimated as a dwarf of its mass, {massSuns:0.###} Suns"));
        }
        double kelvin = Kelvin(r, g, b);
        double size = Math.Max(radiusSuns, 1e-6);
        double luminosity = size * size * Math.Pow(kelvin / SolarTeff, 4.0);
        double bc = Interpolate(kelvin, DwarfTeff, DwarfBCv, logX: false, logY: false);
        return (SolarMbol - 2.5 * Math.Log10(luminosity) - bc, FormattableString.Invariant(
            $"estimated from its radius, {radiusSuns:0.###} Suns, and its light's {kelvin:F0} K"));
    }

    /// <summary>The blackbody temperature whose colour is closest to this one's, ratios compared in log.</summary>
    internal static double Kelvin(double r, double g, double b)
    {
        double peak = Math.Max(r, Math.Max(g, b));
        if (!(peak > 0.0)) return SolarTeff;
        double cr = Math.Log(Math.Max(r / peak, 1e-3)), cg = Math.Log(Math.Max(g / peak, 1e-3)),
               cb = Math.Log(Math.Max(b / peak, 1e-3));
        int best = 0;
        double bestErr = double.PositiveInfinity;
        for (int i = 0; i < LocusKelvin.Length; i++)
        {
            double er = cr - Math.Log(Math.Max(LocusRed[i], 1e-3f)), eg = cg - Math.Log(Math.Max(LocusGreen[i], 1e-3f)),
                   eb = cb - Math.Log(Math.Max(LocusBlue[i], 1e-3f));
            double err = er * er + eg * eg + eb * eb;
            if (err < bestErr)
            {
                bestErr = err;
                best = i;
            }
        }
        return LocusKelvin[best];
    }

    private static double Interpolate(double x, float[] xs, float[] ys, bool logX, bool logY)
    {
        double X(int i) => logX ? Math.Log(xs[i]) : xs[i];
        double Y(int i) => logY ? Math.Log(ys[i]) : ys[i];
        if (x <= X(0)) return Y(0);
        for (int i = 1; i < xs.Length; i++)
        {
            if (x > X(i)) continue;
            double span = X(i) - X(i - 1);
            return span > 0.0 ? Y(i - 1) + (Y(i) - Y(i - 1)) * (x - X(i - 1)) / span : Y(i);
        }
        return Y(xs.Length - 1);
    }

    internal static byte MagnitudeByte(double absMag) =>
        (byte)Math.Clamp(Math.Round((MagFaint - absMag) * BytesPerMag) + 1.0, 1.0, 255.0);

    /// <summary>
    /// A star's colour in the top three bytes of its instance word, red highest, as the engine packs one
    /// (InstancedStarTechnique.AddInstance) and as Shared.glsl's unpackRGBA reads it back: r from bits 24-31,
    /// g from 16-23, b from 8-15. The low byte is the magnitude's.
    ///
    /// Red and blue were the other way round here from the day the catalogue went 3D (2026-09-21) until
    /// 2026-10-07: every star was drawn with its red and blue exchanged, the cool ones blue and the hot ones
    /// orange, and so was the star lighting the scene. That one is burnt out, so it showed as white, until its
    /// disc was held to its hue: then a red dwarf's disc was blue inside its orange glare (user, 2026-10-07).
    /// </summary>
    internal static uint ColourBits(byte r, byte g, byte b) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8);

    /// <summary>A light's colour as the catalogue packs one: brightest channel 255, white for no light at all.</summary>
    private static uint ColourBytes(double r, double g, double b)
    {
        double peak = Math.Max(r, Math.Max(g, b));
        if (!(peak > 0.0)) (r, g, b, peak) = (1.0, 1.0, 1.0, 1.0);
        byte Byte(double c) => (byte)Math.Clamp(Math.Round(Math.Max(c, 0.0) / peak * 255.0), 0.0, 255.0);
        return ColourBits(Byte(r), Byte(g), Byte(b));
    }

    // ---- reading the game ----------------------------------------------------------------------

    private static (double, double, double) PositionOf(object body)
    {
        Type t = body.GetType();
        if (!_positions.TryGetValue(t, out MethodInfo? m))
            _positions[t] = m = AccessTools.Method(t, "GetPositionEcl", Type.EmptyTypes);
        object? v = m?.Invoke(body, null) ?? throw new InvalidOperationException($"{t.Name}.GetPositionEcl not found");
        return PlanetPhotometry.VecPublic(v);
    }

    private static FieldInfo? _lightField, _lr, _lg, _lb;

    /// <summary>The colour of a star's light, its &lt;Sunlight&gt; as loaded.</summary>
    private static (double, double, double) LightOf(object body)
    {
        object? template = PlanetPhotometry.FindPropertyPublic(body.GetType(), "BodyTemplate")?.GetValue(body);
        if (template == null) return (1.0, 1.0, 1.0);
        _lightField ??= AccessTools.Field(template.GetType(), "LightColorRgb");
        object? light = _lightField?.GetValue(template);
        if (light == null) return (1.0, 1.0, 1.0);
        _lr ??= AccessTools.Field(light.GetType(), "R");
        _lg ??= AccessTools.Field(light.GetType(), "G");
        _lb ??= AccessTools.Field(light.GetType(), "B");
        return _lr?.GetValue(light) is float r && _lg?.GetValue(light) is float g && _lb?.GetValue(light) is float b
            ? (r, g, b) : (1.0, 1.0, 1.0);
    }
}
