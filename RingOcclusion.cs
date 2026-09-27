using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// How much of the Sun a planet's rings hide from the camera, for the Sun's burst.
///
/// The rings are drawn by compute passes that dim whatever lies behind them. Our stars, planets
/// and the Sun's own sprite are drawn before those passes, so the rings dim them pixel by pixel
/// as they should. The Sun's burst is drawn after everything, over the finished image, and the
/// pass that draws it cannot read the ring texture: the engine binds it only for the passes that
/// draw rings, air and planets. So the rings are read here, from the same texture, and the burst
/// is dimmed by what they let through.
///
/// A ring texture is a single row whose alpha is the ring's opacity seen straight through it,
/// from the inner radius to the outer. A slanted ray goes through more of it: the engine takes
/// the column density -ln(1 - alpha) over the cosine of the angle, held at a half or more
/// (GetAnalyticRingTransmittanceShadow), and so does this. The Sun is sampled across its disc,
/// the pattern VesselOcclusion uses, so the burst fades as the disc crosses a ring's edge.
/// </summary>
internal static class RingOcclusion
{
    /// <summary>Where the disc is sampled, as (fraction of its angular radius, number around),
    /// plus the centre: thirteen rays, as for vessels.</summary>
    private static readonly (double Fraction, int Count)[] DiscSamples = { (0.5, 4), (0.92, 8) };

    private sealed record Ringed(object Celestial, object Rings, double Inner, double Outer,
                                 float[] Alpha, MethodInfo PositionEcl);

    private static readonly List<Ringed> _ringed = new();
    private static object? _system;
    private static PropertyInfo? _currentSystem, _all;
    private static FieldInfo? _allList;
    private static bool _broken;

    /// <summary>
    /// Optical depth of the rings between the camera and the Sun: -ln of what they let through,
    /// averaged over the Sun's disc. Zero when nothing is in the way. Positions are the camera's
    /// and the Sun's, in the ecliptic frame, in metres.
    /// </summary>
    public static double Depth(double cx, double cy, double cz, double sx, double sy, double sz,
                               double sunAngularRadius)
    {
        if (_broken) return 0.0;
        try
        {
            Refresh();
            if (_ringed.Count == 0) return 0.0;

            double dx = sx - cx, dy = sy - cy, dz = sz - cz;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= 0.0) return 0.0;
            dx /= distance; dy /= distance; dz /= distance;

            // Each ring system's plane and centre, relative to the camera, once for all the rays.
            var planes = new List<(double Px, double Py, double Pz, double Nx, double Ny, double Nz, Ringed R)>();
            foreach (Ringed r in _ringed)
            {
                if (r.PositionEcl.Invoke(r.Celestial, null) is not object ecl) continue;
                if (PlanetPhotometry.RingNormal(r.Rings, r.Celestial) is not (double nx, double ny, double nz)) continue;
                (double px, double py, double pz) = PlanetPhotometry.VecPublic(ecl);
                planes.Add((cx - px, cy - py, cz - pz, nx, ny, nz, r));
            }
            if (planes.Count == 0) return 0.0;

            // Two directions across the disc.
            (double ux, double uy, double uz) = Math.Abs(dz) < 0.9 ? Cross(dx, dy, dz, 0, 0, 1) : Cross(dx, dy, dz, 1, 0, 0);
            (ux, uy, uz) = Normalize(ux, uy, uz);
            (double vx, double vy, double vz) = Cross(dx, dy, dz, ux, uy, uz);

            double sum = 0.0;
            int rays = 0;
            void Ray(double ox, double oy, double oz)
            {
                (double rx, double ry, double rz) = Normalize(dx + ox, dy + oy, dz + oz);
                double through = 1.0;
                foreach (var p in planes)
                    through *= Transmittance(p.Px, p.Py, p.Pz, rx, ry, rz, p.Nx, p.Ny, p.Nz,
                                             p.R.Inner, p.R.Outer, p.R.Alpha, distance);
                sum += through;
                rays++;
            }
            Ray(0.0, 0.0, 0.0);
            foreach ((double fraction, int count) in DiscSamples)
            {
                for (int k = 0; k < count; k++)
                {
                    double angle = 2.0 * Math.PI * (k + 0.5 * fraction) / count;
                    double off = fraction * sunAngularRadius;
                    double a = Math.Cos(angle) * off, b = Math.Sin(angle) * off;
                    Ray(ux * a + vx * b, uy * a + vy * b, uz * a + vz * b);
                }
            }
            return -Math.Log(Math.Max(sum / rays, 1e-6));
        }
        catch (Exception ex)
        {
            _broken = true;
            ShaderShadow.Log("WARN: rings could not be read for the Sun's burst: " + ex.Message);
            return 0.0;
        }
    }

    /// <summary>
    /// What one ring system lets through along a ray from the camera. The camera is at
    /// (px, py, pz) from the planet's centre, the ray's direction and the ring's normal are unit
    /// vectors, and the radii are in metres. 1 where the ray misses the rings, or meets their
    /// plane behind the camera or past the source at maxDistance.
    /// </summary>
    public static double Transmittance(double px, double py, double pz, double dx, double dy, double dz,
                                       double nx, double ny, double nz, double inner, double outer,
                                       float[] alpha, double maxDistance)
    {
        double cos = dx * nx + dy * ny + dz * nz;
        if (Math.Abs(cos) < 1e-9) return 1.0;                   // along the plane, never through it
        double t = -(px * nx + py * ny + pz * nz) / cos;
        if (t <= 0.0 || t >= maxDistance) return 1.0;
        double hx = px + dx * t, hy = py + dy * t, hz = pz + dz * t;
        double r = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        if (r < inner || r > outer) return 1.0;
        double column = -Math.Log(Math.Max(1.0 - Sample(alpha, (r - inner) / (outer - inner)), 1e-5));
        return Math.Exp(-column / Math.Max(Math.Abs(cos), 0.5));
    }

    /// <summary>The row at u in [0, 1], filtered linearly between texel centres as the GPU does,
    /// clamped at the ends.</summary>
    private static double Sample(float[] row, double u)
    {
        double x = u * row.Length - 0.5;
        int i = (int)Math.Floor(x);
        double f = x - i;
        int a = Math.Clamp(i, 0, row.Length - 1), b = Math.Clamp(i + 1, 0, row.Length - 1);
        return row[a] * (1.0 - f) + row[b] * f;
    }

    /// <summary>The ringed bodies of the system in view, found again when the system changes.</summary>
    private static void Refresh()
    {
        _currentSystem ??= AccessTools.Property(AccessTools.TypeByName("KSA.Universe"), "CurrentSystem");
        object? system = _currentSystem?.GetValue(null);
        if (ReferenceEquals(system, _system)) return;
        _system = system;
        _ringed.Clear();
        if (system == null) return;

        _all ??= AccessTools.Property(system.GetType(), "All");
        object? all = _all?.GetValue(system);
        _allList ??= all == null ? null : AccessTools.Field(all.GetType(), "_all");
        if (_allList?.GetValue(all) is not IList bodies) return;

        foreach (object? body in bodies)
        {
            if (body == null) continue;
            object? template = PlanetPhotometry.FindPropertyPublic(body.GetType(), "BodyTemplate")?.GetValue(body);
            object? rings = template == null ? null : PlanetPhotometry.RingsOf(template);
            if (rings == null) continue;
            double inner = PlanetPhotometry.DistancePublic(rings, "InnerRadius");
            double outer = PlanetPhotometry.DistancePublic(rings, "OuterRadius");
            MethodInfo? ecl = AccessTools.Method(body.GetType(), "GetPositionEcl", Type.EmptyTypes);
            float[]? alpha = RingTexture.Of(rings)?.Alpha;
            if (outer <= inner || ecl == null || alpha == null) continue;
            _ringed.Add(new Ringed(body, rings, inner, outer, alpha, ecl));
            ShaderShadow.Log($"rings read for the Sun's burst: {inner / 1000:F0}-{outer / 1000:F0} km, "
                             + $"{alpha.Length} samples");
        }
    }

    private static (double, double, double) Cross(double ax, double ay, double az, double bx, double by, double bz)
        => (ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx);

    private static (double, double, double) Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return l > 0.0 ? (x / l, y / l, z / l) : (0.0, 0.0, 1.0);
    }
}
