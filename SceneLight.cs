using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The light the eye is in, estimated from what the camera sees, for Adaptation. A grid of view rays is traced
/// against the system's bodies and vessels. Each thing in view counts as the light that lights it, not as its own
/// colour: a sunlit surface as the star's light, so a planet filling the view still reads as sunlight and keeps its
/// colour; a vessel as the sunlight and planetshine on it; the star in view as itself; open space as starlight.
/// Each light counts by the luminance it puts into the view. With nothing sunlit in view (an eclipse, a night side,
/// deep space) the star drops out and the eye has only dim light to adapt to.
///
/// Inside an atmosphere the daytime sky counts as the sunlight reaching the camera, the direct beam after its path
/// through the air: what reddens a sunset or the light under Titan's haze. The sky's own tint, and the vessel's
/// lamps and plumes, are not counted yet.
///
/// The star's light counts at its real strength where it lands. The game lights every body as the Sun lights the
/// Earth, at any distance and from any star; an eye adapts to the light that is really there, and adapts less the
/// dimmer it is (Adaptation's luminance factor). So the star's light on each surface, and at the camera, is scaled
/// to the star's own brightness at that distance: Pluto's sunlight is a 1,500th of the Earth's, the daylight on
/// Proxima b a 40th. Its colour is the game's.
/// </summary>
internal static class SceneLight
{
    internal const int Columns = 24, Rows = 14;
    internal const double LuxPerUnit = 128000.0 / 9.0;    // the game's sunlight at 1 AU, 9, is the Sun's 128,000 lux
    internal const double SunLux = 128000.0;              // the Sun's light at 1 AU
    private const double MetresPerAu = 1.495978707e11;
    internal const double StarlightCandela = 2e-4;         // the dark sky's luminance, stars and Milky Way together
    internal const double VesselAlbedo = 0.5;
    internal static readonly double[] Starlight = { 1.0, 0.86, 0.72 };   // warm white, roughly a 5000 K blackbody

    internal enum Kind { Body, Vessel, Star }

    /// <summary>A thing in view, where the camera sees it. Law: how a body sends out the light it reflects, measured
    /// (BodyColours.Photometry); none is a matte surface.</summary>
    internal readonly record struct Sphere(double X, double Y, double Z, double Radius, double Albedo, Kind Kind,
                                           BodyColours.Photometry? Law = null);

    /// <summary>The view: unit axes in the ego frame (camera at the origin), tan of half the vertical angle, width/height.</summary>
    internal readonly record struct View(double[] Forward, double[] Right, double[] Up, double TanHalfV, double Aspect);

    /// <summary>The lights. Sun: the star's light at the camera, after any air (raw linear sRGB, the game's units);
    /// SunRaw: before it; SunEgo: where the star is; Planetshine: the irradiance from the nearby body at the camera.
    /// Nearby: that body's index in the sphere list, or -1; InAir: the camera is inside its atmosphere.
    /// StarFainter: how much fainter than the Sun the star is, in V magnitudes (GameStars.LightingFainter), which with
    /// distance sets its real light; NaN takes the game's light as it is, at any distance.</summary>
    internal readonly record struct Lights(double[] Sun, double[] SunRaw, double[] SunEgo, double[] Planetshine,
                                            int Nearby, bool InAir, double StarFainter = double.NaN);

    /// <summary>The star's real light over the game's, at this distance from it.</summary>
    internal static double RealOverGame(Lights lights, double metres)
    {
        double gameLux = Luminance(lights.SunRaw) * LuxPerUnit;
        if (double.IsNaN(lights.StarFainter) || !(gameLux > 0.0) || !(metres > 0.0))
            return 1.0;
        double au = metres / MetresPerAu;
        return SunLux * Math.Pow(10.0, -0.4 * lights.StarFainter) / (au * au) / gameLux;
    }

    /// <summary>The adapting light: its colour (linear sRGB at unit luminance), a white surface's luminance under
    /// it (cd/m2), and the share of the view's light that is the star's.</summary>
    internal readonly record struct Estimate(double[] Colour, double Candela, double StarShare);

    internal static double Luminance(double[] c) => 0.2126729 * c[0] + 0.7151522 * c[1] + 0.0721750 * c[2];

    /// <summary>The estimate for a view, from plain data (the game's own is gathered by Measure).</summary>
    internal static Estimate Evaluate(View view, IReadOnlyList<Sphere> spheres, Lights lights)
    {
        var sum = new double[3];
        double total = 0.0, candelaSum = 0.0, starSum = 0.0;

        void Add(double luminance, double[] light, double candela, bool star)
        {
            double y = Luminance(light);
            if (!(luminance > 0.0) || !(y > 0.0))
                return;
            for (int c = 0; c < 3; c++)
                sum[c] += luminance * light[c] / y;
            total += luminance;
            candelaSum += luminance * candela;
            if (star)
                starSum += luminance;
        }

        // At the camera, the star's light and the planetshine are as real as the star's distance makes them.
        double atCamera = RealOverGame(lights, Norm(lights.SunEgo));
        double sunLuma = Luminance(lights.Sun) * atCamera, sunCandela = sunLuma * LuxPerUnit / Math.PI;
        double shineLuma = Luminance(lights.Planetshine) * atCamera, shineCandela = shineLuma * LuxPerUnit / Math.PI;
        double starCandelaUnits = StarlightCandela * Math.PI / LuxPerUnit;   // starlight's luminance, in the game's units
        double[] sunDir = Normalised(lights.SunEgo);
        double tanH = view.TanHalfV * view.Aspect;

        // The sky's brightness over a white surface's, by day inside an atmosphere: the share of the sunlight the
        // air has taken out of the direct beam, at the Sun's height above the camera's horizon.
        double skyShare = 0.0;
        if (lights.InAir && lights.Nearby >= 0)
        {
            Sphere home = spheres[lights.Nearby];
            double[] up = Normalised(new[] { -home.X, -home.Y, -home.Z });
            double elevation = up[0] * sunDir[0] + up[1] * sunDir[1] + up[2] * sunDir[2];
            double rawLuma = Luminance(lights.SunRaw);
            skyShare = rawLuma > 0.0 ? Math.Max(0.0, 1.0 - sunLuma / rawLuma) * Math.Max(elevation, 0.0) : 0.0;
        }

        for (int j = 0; j < Rows; j++)
        for (int i = 0; i < Columns; i++)
        {
            double x = ((i + 0.5) / Columns * 2.0 - 1.0) * tanH, y = ((j + 0.5) / Rows * 2.0 - 1.0) * view.TanHalfV;
            double[] d = Normalised(new[]
            {
                view.Forward[0] + x * view.Right[0] + y * view.Up[0],
                view.Forward[1] + x * view.Right[1] + y * view.Up[1],
                view.Forward[2] + x * view.Right[2] + y * view.Up[2],
            });
            int hit = Nearest(d, spheres, out double t);
            double share = 1.0 / (Columns * Rows);
            if (hit < 0)
            {
                if (skyShare > 0.0)
                    Add(share * skyShare * sunLuma / Math.PI, lights.Sun, sunCandela, true);
                else
                    Add(share * starCandelaUnits, Starlight, StarlightCandela, false);
                continue;
            }
            Sphere s = spheres[hit];
            if (s.Kind == Kind.Star)
                continue;                                   // the star's disc is added whole, below
            if (s.Kind == Kind.Vessel)
            {
                Add(share * s.Albedo * 0.5 * sunLuma / Math.PI, lights.Sun, sunCandela, true);
                Add(share * s.Albedo * 0.5 * shineLuma / Math.PI, lights.Planetshine, shineCandela, false);
                continue;
            }
            double px = d[0] * t, py = d[1] * t, pz = d[2] * t;
            double nx = (px - s.X) / s.Radius, ny = (py - s.Y) / s.Radius, nz = (pz - s.Z) / s.Radius;
            double[] fromHit = { lights.SunEgo[0] - px, lights.SunEgo[1] - py, lights.SunEgo[2] - pz };
            double[] toSun = Normalised(fromHit);
            double mu = nx * toSun[0] + ny * toSun[1] + nz * toSun[2];
            if (mu <= 0.0)
            {
                Add(share * starCandelaUnits, Starlight, StarlightCandela, false);   // a night side
                continue;
            }
            bool underAir = lights.InAir && hit == lights.Nearby;
            double[] light = underAir ? lights.Sun : lights.SunRaw;
            double luma = Luminance(light) * RealOverGame(lights, Norm(fromHit));
            double law = s.Law?.Law(mu, -(d[0] * toSun[0] + d[1] * toSun[1] + d[2] * toSun[2])) ?? 1.0;
            Add(share * s.Albedo * mu * law * luma / Math.PI, light, luma * LuxPerUnit / Math.PI, true);
        }

        // The star's disc, if it is in view and nothing is in front of it: its light over the view's solid angle.
        double sunDistance = Norm(lights.SunEgo);
        double ahead = Dot(sunDir, view.Forward);
        if (ahead > 0.0 && sunDistance > 0.0)
        {
            double sx = Dot(sunDir, view.Right) / ahead, sy = Dot(sunDir, view.Up) / ahead;
            int blocker = Nearest(sunDir, spheres, out double tb);
            bool clear = blocker < 0 || spheres[blocker].Kind == Kind.Star || tb >= sunDistance * 0.999;
            if (Math.Abs(sx) <= tanH && Math.Abs(sy) <= view.TanHalfV && clear)
            {
                double halfH = Math.Atan(tanH), halfV = Math.Atan(view.TanHalfV);
                double solidAngle = 4.0 * Math.Asin(Math.Sin(halfH) * Math.Sin(halfV));
                Add(sunLuma / solidAngle, lights.Sun, sunCandela, true);
            }
        }

        if (!(total > 0.0))
            return new Estimate((double[])Starlight.Clone(), StarlightCandela, 0.0);
        var colour = new[] { sum[0] / total, sum[1] / total, sum[2] / total };
        return new Estimate(colour, candelaSum / total, starSum / total);
    }

    /// <summary>The nearest sphere a ray from the camera meets, and how far along; -1 if none. From inside a
    /// sphere (a camera within a vessel's bounds) the ray meets its far side.</summary>
    internal static int Nearest(double[] d, IReadOnlyList<Sphere> spheres, out double t)
    {
        int best = -1;
        t = double.PositiveInfinity;
        for (int k = 0; k < spheres.Count; k++)
        {
            Sphere s = spheres[k];
            double b = d[0] * s.X + d[1] * s.Y + d[2] * s.Z;
            double c = s.X * s.X + s.Y * s.Y + s.Z * s.Z - s.Radius * s.Radius;
            double disc = b * b - c;
            if (disc < 0.0)
                continue;
            double root = Math.Sqrt(disc);
            double hit = c > 0.0 ? b - root : b + root;
            if (hit > 0.0 && hit < t)
            {
                t = hit;
                best = k;
            }
        }
        return best;
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));

    private static double[] Normalised(double[] a)
    {
        double n = Norm(a);
        return n > 0.0 ? new[] { a[0] / n, a[1] / n, a[2] / n } : new[] { 0.0, 0.0, 1.0 };
    }

    // ------------------------------------------------------------------ the game's view and lights
    private static PropertyInfo? _system, _mainViewport;
    private static MemberInfo? _all;
    private static FieldInfo? _lighting, _sunColour, _occlusion, _planetColour, _sunPosition, _planetPosition,
                              _planetRadius, _atmosphereHeight, _aspect;
    private static readonly FieldInfo?[] _f4 = new FieldInfo?[3];
    private static MethodInfo? _forward, _right, _up, _fov, _positionEgo;
    private static PropertyInfo? _nearbyCelestial;
    private static Type? _star, _vessel, _atmospheric;
    private static readonly Dictionary<Type, (MethodInfo? Camera, PropertyInfo? Slot, PropertyInfo? Mode)> _viewports = new();
    private static readonly Dictionary<Type, (PropertyInfo? Radius, PropertyInfo? Template)> _bodies = new();

    // A body's size, albedo and kind do not change, so they are read once; a vessel's size is read every time
    // (it changes as it stages). The dense system has thousands of bodies, looked at ten times a second.
    private sealed class Known
    {
        public double Radius, Albedo;
        public BodyColours.Photometry? Law;
        public Kind Kind;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Known> _known = new();

    /// <summary>Finds the game's view, bodies and lighting; false leaves the eye adapted to the star's light alone.</summary>
    internal static bool Resolve()
    {
        Type? universe = AccessTools.TypeByName("KSA.Universe"), program = AccessTools.TypeByName("KSA.Program");
        Type? camera = AccessTools.TypeByName("KSA.Camera");
        _system = universe == null ? null : AccessTools.Property(universe, "CurrentSystem");
        Type? system = _system?.PropertyType;
        _all = system == null ? null : (MemberInfo?)AccessTools.Property(system, "All") ?? AccessTools.Field(system, "All");
        _mainViewport = program == null ? null : AccessTools.Property(program, "MainViewport");
        _lighting = program == null ? null : AccessTools.Field(program, "_lightingData");
        Type? ubo = _lighting?.FieldType.GetElementType();
        _sunColour = ubo == null ? null : AccessTools.Field(ubo, "SunColor");
        _occlusion = ubo == null ? null : AccessTools.Field(ubo, "OcclusionColor");
        _planetColour = ubo == null ? null : AccessTools.Field(ubo, "PlanetColor");
        _sunPosition = ubo == null ? null : AccessTools.Field(ubo, "SunPositionRadius");
        _planetPosition = ubo == null ? null : AccessTools.Field(ubo, "PlanetPosition");
        _planetRadius = ubo == null ? null : AccessTools.Field(ubo, "PlanetRadius");
        _atmosphereHeight = ubo == null ? null : AccessTools.Field(ubo, "AtmosphereHeight");
        Type? f4 = _sunColour?.FieldType;
        for (int i = 0; i < 3; i++)
            _f4[i] = f4 == null ? null : AccessTools.Field(f4, "XYZ"[i].ToString());
        _forward = camera == null ? null : AccessTools.Method(camera, "GetForwardEcl");
        _right = camera == null ? null : AccessTools.Method(camera, "GetRightEcl");
        _up = camera == null ? null : AccessTools.Method(camera, "GetUpEcl");
        _fov = camera == null ? null : AccessTools.Method(camera, "GetFieldOfView");
        _aspect = camera == null ? null : AccessTools.Field(camera, "AspectRatio");
        _positionEgo = camera?.GetMethods().FirstOrDefault(m => m.Name == "GetPositionEgo" && m.GetParameters().Length == 1
                                                                && m.GetParameters()[0].ParameterType.Name == "IPosition");
        _nearbyCelestial = camera == null ? null : AccessTools.Property(camera, "NearbyCelestial");
        _star = AccessTools.TypeByName("KSA.StellarBody");
        _atmospheric = AccessTools.TypeByName("KSA.AtmosphericBody");
        _vessel = AccessTools.TypeByName("KSA.Vehicle");
        return _system != null && _all != null && _mainViewport != null && _lighting != null && _lighting.IsStatic
            && _sunColour != null && _occlusion?.FieldType == f4 && _planetColour?.FieldType == f4
            && _sunPosition?.FieldType == f4 && _planetPosition?.FieldType == f4 && _planetRadius != null
            && _atmosphereHeight != null && _f4.All(f => f?.FieldType == typeof(float)) && _forward != null
            && _right != null && _up != null && _fov != null && _aspect != null && _positionEgo != null && _star != null
            && _nearbyCelestial != null;
    }

    /// <summary>True if this is the main view, the one the eye is in.</summary>
    internal static bool IsMain(object viewport) => ReferenceEquals(_mainViewport?.GetValue(null), viewport);

    /// <summary>True if the view is the map: not the eye in space, so the eye's state is held.</summary>
    internal static bool IsMap(object viewport)
    {
        var (_, _, mode) = Viewport(viewport.GetType());
        return mode?.GetValue(viewport)?.ToString() == "Map";
    }

    private static (MethodInfo? Camera, PropertyInfo? Slot, PropertyInfo? Mode) Viewport(Type vt)
    {
        if (!_viewports.TryGetValue(vt, out var v))
            _viewports[vt] = v = (AccessTools.Method(vt, "GetCamera"), AccessTools.Property(vt, "ShaderSlot"),
                                  AccessTools.Property(vt, "Mode"));
        return v;
    }

    /// <summary>The estimate for the game's main view, or null if something it needs is missing this frame.</summary>
    internal static Estimate? Measure(object viewport)
    {
        var (getCamera, slotProp, _) = Viewport(viewport.GetType());
        object? camera = getCamera?.Invoke(viewport, null);
        if (camera == null || slotProp?.GetValue(viewport) is not int slot
            || _lighting!.GetValue(null) is not Array lighting || slot < 0 || slot >= lighting.Length)
            return null;
        object box = lighting.GetValue(slot)!;
        double[] Vec4(FieldInfo f)
        {
            object v = f.GetValue(box)!;
            return new double[] { (float)_f4[0]!.GetValue(v)!, (float)_f4[1]!.GetValue(v)!, (float)_f4[2]!.GetValue(v)! };
        }
        double[] sunRaw = Vec4(_sunColour!), occlusion = Vec4(_occlusion!), sunEgo = Vec4(_sunPosition!);
        double[] planetshine = Vec4(_planetColour!), planet = Vec4(_planetPosition!);
        double planetRadius = (float)_planetRadius!.GetValue(box)!, air = (float)_atmosphereHeight!.GetValue(box)!;
        double[] sun = { sunRaw[0] * occlusion[0], sunRaw[1] * occlusion[1], sunRaw[2] * occlusion[2] };

        // Planetshine as it really is at the camera: BodyColours writes it over the shaders' fade, if they have one.
        double over = BodyColours.FadeWrittenOver(Norm(planet));
        for (int c = 0; c < 3; c++)
            planetshine[c] *= over;

        var spheres = new List<Sphere>();
        int nearby = -1;
        object? nearbyBody = _nearbyCelestial!.GetValue(camera);
        double[] forward = Vec(_forward!.Invoke(camera, null)!), right = Vec(_right!.Invoke(camera, null)!);
        double[] up = Vec(_up!.Invoke(camera, null)!);
        double tanHalfV = Math.Tan(0.5 * (float)_fov!.Invoke(camera, null)!);
        double aspect = (float)_aspect!.GetValue(camera)!;
        double spacing = 2.0 * tanHalfV / Rows;
        object? system = _system!.GetValue(null);
        object? bodies = system == null ? null : _all is PropertyInfo ap ? ap.GetValue(system) : ((FieldInfo)_all!).GetValue(system);
        if (bodies is IEnumerable all)
            foreach (object body in all)
            {
                if (body == null)
                    continue;
                Type bt = body.GetType();
                if (!_bodies.TryGetValue(bt, out var members))
                    _bodies[bt] = members = (PlanetPhotometry.FindPropertyPublic(bt, "MeanRadius"),
                                             PlanetPhotometry.FindPropertyPublic(bt, "BodyTemplate"));
                if (!_known.TryGetValue(body, out Known? known))
                {
                    known = new Known
                    {
                        Kind = _star!.IsInstanceOfType(body) ? Kind.Star
                             : _vessel != null && _vessel.IsInstanceOfType(body) ? Kind.Vessel : Kind.Body,
                        Radius = members.Radius?.GetValue(body) is double r0 ? r0 : double.NaN,
                    };
                    if (known.Kind == Kind.Vessel)
                        known.Albedo = VesselAlbedo;
                    else if (members.Template?.GetValue(body) is object t0)
                        (known.Albedo, known.Law) = BodyColours.SceneSurface(t0);
                    else
                        known.Albedo = 0.3;
                    _known.AddOrUpdate(body, known);
                }
                double radius = known.Kind == Kind.Vessel && members.Radius?.GetValue(body) is double rv ? rv : known.Radius;
                if (!(radius > 0.0))
                    continue;
                double[] p = Vec(_positionEgo!.Invoke(camera, new[] { body })!);
                double distance = Norm(p);
                // Too small to matter to the eye (under a quarter of a ray's spacing), or wholly behind the camera.
                if (!ReferenceEquals(body, nearbyBody) && distance > radius
                    && (radius / distance < 0.25 * spacing || Dot(p, forward) < -radius))
                    continue;
                if (ReferenceEquals(body, nearbyBody))
                    nearby = spheres.Count;
                spheres.Add(new Sphere(p[0], p[1], p[2], radius, known.Albedo, known.Kind, known.Law));
            }
        // The lighting uniform's planet fields are only current for a body with an atmosphere.
        bool inAir = nearby >= 0 && _atmospheric != null && _atmospheric.IsInstanceOfType(nearbyBody) && air > 0.0
                     && Norm(new[] { spheres[nearby].X, spheres[nearby].Y, spheres[nearby].Z }) < spheres[nearby].Radius + air;
        return Evaluate(new View(forward, right, up, tanHalfV, aspect), spheres,
                        new Lights(sun, sunRaw, sunEgo, planetshine, nearby, inAir, GameStars.LightingFainter));
    }

    private static double[] Vec(object v)
    {
        var (x, y, z) = PlanetPhotometry.VecPublic(v);
        return new[] { x, y, z };
    }
}
