using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// How much of a source the vessels near the camera are covering.
///
/// A sprite is a billboard standing where its source is, so the depth test only hides the part
/// of it a vessel actually overlaps: with the Sun behind a ship, the core went and every ray
/// reaching past the hull stayed, radiating out of nothing. The celestial bodies are handled in
/// the shader, because the celestial block carries them; vessels are nowhere a sprite shader can
/// reach, and a bounding sphere would be a poor stand-in for a ship made of struts and panels.
///
/// So the question is asked here instead, with the engine's own geometry: the same
/// Part.RayCastEgo the game uses to pick a part under the mouse, cast from the camera toward
/// the source. A point source takes one ray. A resolved one takes a small spread across its
/// disc, so the Sun fades as a hull slides over it rather than going at the instant its centre
/// is crossed - the same falloff as the disc being covered.
///
/// The vessel list is built once a frame per camera, from the system's own list of bodies, and
/// shared by the Sun and every planet asked about in that frame. A vessel's bounding sphere is
/// tested before any of its parts, so the raycasts only run for the rare ray that actually
/// passes near a hull.
/// </summary>
internal static class VesselOcclusion
{
    /// <summary>A vessel smaller than this, seen from where it is, cannot visibly hide anything.</summary>
    private const double MinAngularRadius = 1e-5;       // about two arcseconds

    /// <summary>
    /// Where the disc is sampled, as (fraction of its angular radius, number around). The centre
    /// counts once. Thirteen rays in all: enough that a hull crossing the Sun takes it down in
    /// steps no eye will separate at the size the Sun is drawn.
    /// </summary>
    private static readonly (double Fraction, int Count)[] Rings = { (0.45, 4), (0.85, 8) };

    private sealed record Candidate(object Matrix, IList Parts, double X, double Y, double Z,
                                    double Radius);

    private static readonly List<Candidate> _candidates = new();
    private static readonly object _lock = new();
    private static object? _cachedCamera;
    private static ulong _cachedFrame = ulong.MaxValue;

    private static FieldInfo? _frameNumber, _allList, _partList;
    private static PropertyInfo? _currentSystem, _all, _meanRadius, _partTree;
    private static MethodInfo? _getPositionEgo, _getMatrix, _rayCast;
    private static ConstructorInfo? _rayCtor, _vecCtor;
    private static Type? _vehicleType;
    private static object? _origin;
    private static bool _broken, _logged;

    /// <summary>
    /// The fraction of a source hidden by vessels, from 0 (nothing in the way) to 1.
    /// </summary>
    /// <param name="camera">The camera the question is asked from.</param>
    /// <param name="dx">Unit direction from the camera to the source, in ego space.</param>
    /// <param name="sourceDistM">How far the source is: nothing behind it can hide it.</param>
    /// <param name="angularRadius">Its angular radius; zero for a point.</param>
    public static double Covered(object camera, double dx, double dy, double dz,
                                 double sourceDistM, double angularRadius)
    {
        if (_broken) return 0.0;
        lock (_lock)
        {
            try
            {
                Refresh(camera);
                if (_candidates.Count == 0) return 0.0;

                // Two directions square to the source, to lay the samples out across its disc.
                (double hx, double hy, double hz) = Math.Abs(dz) < 0.9 ? (0.0, 0.0, 1.0) : (1.0, 0.0, 0.0);
                (double ux, double uy, double uz) = Normalize(dy * hz - dz * hy, dz * hx - dx * hz, dx * hy - dy * hx);
                (double vx, double vy, double vz) = (dy * uz - dz * uy, dz * ux - dx * uz, dx * uy - dy * ux);

                int rays = 0, hits = 0;
                void Cast(double sx, double sy, double sz)
                {
                    rays++;
                    if (Blocked(Normalize(sx, sy, sz), sourceDistM)) hits++;
                }

                Cast(dx, dy, dz);
                if (angularRadius > 0.0)
                {
                    foreach ((double fraction, int count) in Rings)
                    {
                        double t = Math.Tan(angularRadius * fraction);
                        for (int i = 0; i < count; i++)
                        {
                            double a = 2.0 * Math.PI * (i + 0.5 * fraction) / count;
                            double cu = Math.Cos(a) * t, cv = Math.Sin(a) * t;
                            Cast(dx + cu * ux + cv * vx, dy + cu * uy + cv * vy, dz + cu * uz + cv * vz);
                        }
                    }
                }
                return (double)hits / rays;
            }
            catch (Exception ex)
            {
                _broken = true;
                ShaderShadow.Log("WARN: vessels cannot hide a light source: " + ex.Message);
                return 0.0;
            }
        }
    }

    /// <summary>Does anything with parts stand on this ray, in front of the camera and short of the source?</summary>
    private static bool Blocked((double X, double Y, double Z) dir, double sourceDistM)
    {
        object? ray = null;
        foreach (Candidate c in _candidates)
        {
            // The bounding sphere first, which costs nothing: where the ray passes closest to
            // the vessel's centre, and whether that is inside its radius.
            double along = c.X * dir.X + c.Y * dir.Y + c.Z * dir.Z;
            double centre2 = c.X * c.X + c.Y * c.Y + c.Z * c.Z;
            bool inside = centre2 < c.Radius * c.Radius;
            if (!inside && (along <= 0.0 || along - c.Radius >= sourceDistM)) continue;
            if (centre2 - along * along > c.Radius * c.Radius) continue;

            // Then its parts, with the engine's own test.
            ray ??= _rayCtor!.Invoke(new[] { _origin!, _vecCtor!.Invoke(new object[] { dir.X, dir.Y, dir.Z }) });
            foreach (object? part in c.Parts)
            {
                if (part == null) continue;
                object?[] args = { c.Matrix, ray, null, null, null, null, null, null, null, null };
                if (_rayCast!.Invoke(part, args) is not true) continue;
                // In front of the camera and short of the source. A part the camera is inside -
                // a cockpit, looking out - does not count: the Sun through its window is the Sun.
                if (args[2] is double near && near > 0.0 && near < sourceDistM) return true;
            }
        }
        return false;
    }

    /// <summary>The vessels worth asking about, once a frame for each camera.</summary>
    private static void Refresh(object camera)
    {
        Resolve(camera);
        ulong frame = _frameNumber!.GetValue(null) is ulong f ? f : 0UL;
        if (ReferenceEquals(camera, _cachedCamera) && frame == _cachedFrame) return;
        _cachedCamera = camera;
        _cachedFrame = frame;
        _candidates.Clear();

        object? system = _currentSystem!.GetValue(null);
        object? all = system == null ? null : _all!.GetValue(system);
        _allList ??= all == null ? null : AccessTools.Field(all.GetType(), "_all");
        if (_allList?.GetValue(all) is not IList bodies) return;

        foreach (object? body in bodies)
        {
            if (body == null || !_vehicleType!.IsInstanceOfType(body)) continue;
            if (_meanRadius!.GetValue(body) is not double radius || radius <= 0.0) continue;
            object? ego = _getPositionEgo!.Invoke(camera, new[] { body });
            if (ego == null) continue;
            (double x, double y, double z) = PlanetPhotometry.VecPublic(ego);
            double dist = Math.Sqrt(x * x + y * y + z * z);
            if (dist > radius && radius / dist < MinAngularRadius) continue;

            object? tree = _partTree!.GetValue(body);
            _partList ??= tree == null ? null : AccessTools.Field(tree.GetType(), "_parts");
            if (_partList?.GetValue(tree) is not IList parts || parts.Count == 0) continue;
            object? matrix = _getMatrix!.Invoke(body, new[] { camera });
            if (matrix == null) continue;

            _candidates.Add(new Candidate(matrix, parts, x, y, z, radius));
        }

        if (!_logged && _candidates.Count > 0)
        {
            ShaderShadow.Log($"vessels can hide light sources ({_candidates.Count} near enough to matter)");
            _logged = true;
        }
    }

    /// <summary>The members, once. Anything missing turns the feature off rather than guessing.</summary>
    private static void Resolve(object camera)
    {
        if (_rayCast != null) return;
        Type? program = AccessTools.TypeByName("KSA.Program");
        Type? universe = AccessTools.TypeByName("KSA.Universe");
        Type? orbiter = AccessTools.TypeByName("KSA.IOrbiter");
        Type? part = AccessTools.TypeByName("KSA.Part");
        _vehicleType = AccessTools.TypeByName("KSA.Vehicle");
        if (program == null || universe == null || orbiter == null || part == null || _vehicleType == null)
            throw new MissingMemberException("a KSA type this needs is gone");

        _frameNumber = AccessTools.Field(program, "FrameNumber");
        _currentSystem = AccessTools.Property(universe, "CurrentSystem");
        _all = _currentSystem == null ? null : AccessTools.Property(_currentSystem.PropertyType, "All");
        _meanRadius = PlanetPhotometry.FindPropertyPublic(_vehicleType, "MeanRadius");
        _partTree = PlanetPhotometry.FindPropertyPublic(_vehicleType, "Parts");
        _getPositionEgo = AccessTools.Method(camera.GetType(), "GetPositionEgo", new[] { orbiter });
        _getMatrix = AccessTools.Method(_vehicleType, "GetMatrixAsmb2Ego", new[] { camera.GetType() });
        MethodInfo? rayCast = part.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                  .FirstOrDefault(m => m.Name == "RayCastEgo" && m.GetParameters().Length == 10);
        if (_frameNumber == null || _all == null || _meanRadius == null || _partTree == null
            || _getPositionEgo == null || _getMatrix == null || rayCast == null)
            throw new MissingMemberException("a KSA member this needs is gone");

        Type vec = _getPositionEgo.ReturnType;
        Type rayType = rayCast.GetParameters()[1].ParameterType;
        _vecCtor = vec.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
        _rayCtor = rayType.GetConstructor(new[] { vec, vec });
        if (_vecCtor == null || _rayCtor == null)
            throw new MissingMemberException("the engine's ray or vector can no longer be built");
        _origin = _vecCtor.Invoke(new object[] { 0.0, 0.0, 0.0 });
        _rayCast = rayCast;
    }

    private static FieldInfo? _celestialArray, _pad2;

    /// <summary>
    /// The Sun's covered fraction, for the star shader, in the celestial block's last spare
    /// word. Written every frame the Sun is measured, and cleared by <see cref="LimbAir"/> at
    /// the top of every frame, so a frame that never measures it never leaves one behind.
    /// Zero means nothing in the way, which is also what the engine writes there - so a mod
    /// that has stopped working leaves the Sun visible rather than hidden.
    /// </summary>
    public static void PublishSun(object program, int slot, double covered)
    {
        _celestialArray ??= AccessTools.Field(program.GetType(), "_celestialData");
        if (_celestialArray?.GetValue(null) is not Array celestial || slot >= celestial.Length) return;
        object box = celestial.GetValue(slot)!;
        _pad2 ??= AccessTools.Field(box.GetType(), "pad2");
        if (_pad2 == null) return;
        _pad2.SetValue(box, BitConverter.SingleToInt32Bits((float)Math.Clamp(covered, 0.0, 1.0)));
        celestial.SetValue(box, slot);
    }

    private static (double, double, double) Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return l > 0.0 ? (x / l, y / l, z / l) : (0.0, 0.0, 1.0);
    }
}
