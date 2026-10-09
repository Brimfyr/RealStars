using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Stars behind a vessel.
///
/// A star's sprite is drawn before the world, so a hull drawn over it hides what of it the hull covers and no
/// more: with the star itself behind a ship, its glare went on radiating from behind the hull (user, 2026-10-08,
/// of Alpha Centauri past a vessel at Proxima). The Sun and the planets are asked about every frame
/// (<see cref="VesselOcclusion"/>): the Sun's share goes to the shaders, and a planet's instance is written
/// afresh each frame anyway. A star's instance is written once. So a star a vessel hides is taken out of the
/// buffer here, by its magnitude byte, and put back when it comes out: a star is a point, and goes out at once,
/// as it does behind a limb.
///
/// Every one of the game's own stars is asked about, and of the catalogue the few bright enough from where the
/// camera is to throw a glare well past their own cores: a ray each, every frame, is what a planet costs
/// already, and a ray is tried against every part of a vessel the camera is close to. None at all with no vessel
/// in range.
/// </summary>
internal static partial class GameStars
{
    /// <summary>The faintest catalogue star asked about at the default exposure, as it looks from the camera.
    /// Its glare is seven pixels long round a core of under three; a fainter star's is hardly past its core.</summary>
    internal const double CoverMagnitude = 1.0;

    /// <summary>A glare is as long at a higher exposure for a star this many magnitudes fainter, to the decade:
    /// its level goes as the peak to the 0.62 (rsGlareKappa) and the floor it clears as white does.</summary>
    internal const double CoverPerDecade = 2.5 / 0.62;

    /// <summary>And never fainter than this, the naked eye's limit; nor more of them than this, the brightest.</summary>
    internal const double CoverFaintest = 6.5;
    internal const int CoverMost = 24;

    /// <summary>The list is made again once the camera is this far, in parsecs, from where it was made. A star
    /// three parsecs off changes by four hundredths of a magnitude over it.</summary>
    private const double CoverListPc = 0.05;

    private static int[] _coverList = Array.Empty<int>();       // catalogue instances bright enough, from where the list was made
    private static double _coverX = double.NaN, _coverY, _coverZ, _coverCut;   // the camera's place then (parsecs), and the cut
    private static readonly HashSet<int> _covered = new();      // catalogue instances a vessel hides now
    private static readonly Dictionary<Type, MethodInfo?> _viewCameras = new();
    private static MethodInfo? _cameraEcl;
    private static bool _coverBroken;

    /// <summary>The faintest star asked about, by how far white is from the default's (Exposure.WhiteScale).</summary>
    internal static double CoverCut(double whiteScale) =>
        Math.Clamp(CoverMagnitude - CoverPerDecade * Math.Log10(Math.Max(whiteScale, 1e-6)), -30.0, CoverFaintest);

    /// <summary>
    /// The catalogue's instances that look at least as bright as <paramref name="cut"/> from a place (parsecs
    /// from the Sun), by the magnitudes they were loaded with: the brightest <paramref name="most"/> of them,
    /// brightest first, without those in <paramref name="skip"/>.
    /// </summary>
    internal static int[] BrightFrom(Array instances, int first, uint[] packed, ICollection<int> skip,
                                     double px, double py, double pz, double cut, int most)
    {
        Span<StarBuffer.Sprite> all = StarBuffer.Sprites(instances);
        var list = new List<(double Seen, int Index)>();
        for (int n = 0; n < packed.Length; n++)
        {
            uint magnitude = packed[n] & 0xFF;
            if (magnitude == 0) continue;
            ref StarBuffer.Sprite star = ref all[first + n];
            double dx = star.X - px, dy = star.Y - py, dz = star.Z - pz;
            double pc = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(pc > 0.0)) continue;
            double seen = MagFaint - (magnitude - 1) / BytesPerMag + 5.0 * Math.Log10(pc / 10.0);
            if (seen <= cut && !skip.Contains(first + n))
                list.Add((seen, first + n));
        }
        list.Sort();
        return list.Take(most).Select(star => star.Index).ToArray();
    }

    /// <summary>Takes a catalogue star out of the buffer, or puts it back as it was loaded. True if that changed it.</summary>
    internal static bool SetCovered(Array instances, int index, uint loaded, bool hidden)
    {
        ref StarBuffer.Sprite star = ref StarBuffer.Sprites(instances)[index];
        uint packed = hidden ? loaded & 0xFFFFFF00u : loaded;
        if (star.Packed == packed) return false;
        star.Packed = packed;
        return true;
    }

    /// <summary>
    /// Asks about every star worth asking about, from this view's camera: the hidden go out of the buffer, and
    /// those that have come out go back. True if anything changed, and the buffer should go up at once.
    /// </summary>
    private static bool Cover(object viewport, Star? light)
    {
        if (_coverBroken || _instances == null || _uploadArgs == null || !ReferenceEquals(viewport, _uploadArgs[0]))
            return false;
        try
        {
            Type vt = viewport.GetType();
            if (!_viewCameras.TryGetValue(vt, out MethodInfo? getCamera))
                _viewCameras[vt] = getCamera = AccessTools.Method(vt, "GetCamera");
            object? camera = getCamera?.Invoke(viewport, null);
            if (camera == null) return false;
            if (!VesselOcclusion.Any(camera))
                return Uncover();

            _cameraEcl ??= AccessTools.PropertyGetter(camera.GetType(), "PositionEcl");
            object? ecl = _cameraEcl?.Invoke(camera, null);
            if (ecl == null) return false;
            (double cx, double cy, double cz) = PlanetPhotometry.VecPublic(ecl);

            bool changed = false;
            foreach (Star s in _stars)
            {
                // The star lighting the scene is the Sun's business: its share covered goes to the shaders.
                bool hidden = false;
                if (!ReferenceEquals(s, light))
                {
                    double dx = s.X - cx, dy = s.Y - cy, dz = s.Z - cz;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    hidden = dist > 0.0 && VesselOcclusion.Covered(camera, dx / dist, dy / dist, dz / dist, dist, 0.0) >= 0.5;
                }
                changed |= hidden != s.Covered;
                s.Covered = hidden;
            }

            double px = cx / ParsecM, py = cy / ParsecM, pz = cz / ParsecM;
            double cut = CoverCut(Exposure.WhiteScale);
            double ox = px - _coverX, oy = py - _coverY, oz = pz - _coverZ;
            if (double.IsNaN(_coverX) || ox * ox + oy * oy + oz * oz > CoverListPc * CoverListPc || Math.Abs(cut - _coverCut) > 0.25)
            {
                // Whatever the old list had hidden comes back first; the new one hides its own below.
                changed |= Uncover(catalogueOnly: true);
                var skip = new HashSet<int>(_hidden) { _sun };
                _coverList = BrightFrom(_instances, _catFirst, _catPacked!, skip, px, py, pz, cut, CoverMost);
                (_coverX, _coverY, _coverZ, _coverCut) = (px, py, pz, cut);
            }

            Span<StarBuffer.Sprite> all = StarBuffer.Sprites(_instances);
            foreach (int i in _coverList)
            {
                double dx = all[i].X - px, dy = all[i].Y - py, dz = all[i].Z - pz;
                double pc = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                bool hidden = pc > 0.0 && VesselOcclusion.Covered(camera, dx / pc, dy / pc, dz / pc, pc * ParsecM, 0.0) >= 0.5;
                if (!SetCovered(_instances, i, _catPacked![i - _catFirst], hidden)) continue;
                if (hidden) _covered.Add(i); else _covered.Remove(i);
                changed = true;
            }
            return changed;
        }
        catch (Exception ex)
        {
            _coverBroken = true;
            ShaderShadow.Log("WARN: a star behind a vessel keeps its glare: " + ex.GetBaseException().Message);
            return Uncover();
        }
    }

    /// <summary>Every star a vessel had hidden, back as it was. True if any was.</summary>
    private static bool Uncover(bool catalogueOnly = false)
    {
        bool changed = false;
        if (!catalogueOnly)
            foreach (Star s in _stars)
            {
                changed |= s.Covered;
                s.Covered = false;
            }
        if (_instances != null && _catPacked != null)
            foreach (int i in _covered)
                changed |= SetCovered(_instances, i, _catPacked[i - _catFirst], false);
        _covered.Clear();
        return changed;
    }

    /// <summary>A new system, or the catalogue handed back: nothing is hidden, and the list is made afresh.</summary>
    private static void ForgetCover()
    {
        Uncover();
        _coverList = Array.Empty<int>();
        _coverX = double.NaN;
    }
}
