using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Distant planets on the same brightness scale as the stars.
///
/// The game sizes a far-off body's sprite with GetApparentSizeScale, which is
/// 10*(R/R_earth)^k clamped to 5..30 for a planet: a visibility aid that grows with RADIUS
/// rather than with brightness. Neptune, large but very far, takes the full 30x - roughly
/// 900x in flux from area alone - while Venus, which should outshine everything in the sky,
/// takes 9.5x. The ordering is inverted from physics, and it only became visible once the
/// star field started carrying real magnitudes.
///
/// So the scale is replaced by one computed from the body itself. Nothing here is hardcoded
/// per planet: the absolute magnitude comes from the body's own radius and albedo, which
/// means a modded system's bodies are handled on the same footing as Sol's.
/// </summary>
internal static class PlanetPhotometry
{
    // These must match the constants in StarShaders, or a planet and a star of the same
    // magnitude will not match. RSCheck compares them against the GLSL.
    public const double MagRef = 5.0;         // the magnitude that peaks at 1.0 on screen
    public const double PsfCore = 1.0;        // point spread function core, pixels
    /// <summary>
    /// How fast the profile falls away, and so how large a bright source looks. The radius
    /// that clears one display level grows as flux^(1/2beta), so at beta = 2 a 12 magnitude
    /// range spread sizes over 13x and a bright moon's glint came out wider than the planet
    /// it orbits. Real seeing-limited profiles sit between about 2.5 and 4.5, and this is the
    /// compact end of that, holding the same range to 4.2x. Steeper means smaller but harder
    /// edged; <see cref="PsfCore"/> is the knob that shrinks without changing the shape.
    /// </summary>
    public const double PsfBeta = 4.5;
    /// <summary>Carries the (beta - 1) normalisation, so beta changes width and not brightness.</summary>
    public const double Brightness = 0.886;
    public const double DisplayLevels = 255.0;
    public const double MinGlowPx = 1.0;

    /// <summary>
    /// Largest glow a point source may draw, in pixels of radius.
    ///
    /// The radius where the profile crosses one display level grows as the fourth root of
    /// flux, which is well behaved across a real sky: Sirius is 18 px and Venus at its very
    /// best is 40. A planet seen from a few million kilometres is another matter - Earth from
    /// 10 Mkm is magnitude -9.7, and the same rule asks for 117 px, which is how a distant
    /// planet ended up as a blob a fifth of the screen across. Real glare does not grow without
    /// bound either, and a body this bright is about to be drawn as a sphere anyway.
    ///
    /// 40 px is chosen to leave the entire real sky alone: nothing fainter than magnitude -5,
    /// which is brighter than Venus ever gets, reaches it.
    /// </summary>
    public const double MaxGlowPx = 40.0;

    private const double Au = 1.495978707e11;

    // ---- phase -----------------------------------------------------------------------
    // The IAU H-G system is an ASTEROID model, fitted below about 100 degrees of phase, and
    // past that it extrapolates into nonsense: at 170 degrees it asks for 17 magnitudes of
    // dimming where Mercury, the best-measured airless body, loses 9.3 and Venus loses 4.0.
    // Bodies were vanishing at conjunction as a result.
    //
    // So H-G keeps the range it was fitted for, and a Lambert sphere - the geometric floor,
    // no scattering tricks - stops it collapsing beyond. The floor is fitted to Mercury's
    // published phase polynomial over 100-170 degrees, landing within 0.6 mag of it.
    public const double AirlessLambertFloor = 0.198;

    // An atmosphere behaves differently: forward scattering through cloud and haze keeps a
    // thin crescent brilliant, which is why Venus is at its best as a crescent rather than
    // full. A Lambert base plus a Henyey-Greenstein forward lobe, both fitted to Venus's
    // published polynomial over its whole valid range, holds to 0.3 mag - against errors of
    // up to 12 magnitudes from H-G. The same scattering Real Atmospheres uses for haze.
    public const double AtmosphereForwardG = 0.384;
    public const double AtmosphereForwardWeight = 0.1529;

    // ---- rings -----------------------------------------------------------------------
    // Ring brightness follows from the ring geometry the body itself carries, so a modded
    // ringed world works the same way. This constant is the one thing that cannot be derived:
    // an effective albedo that also carries the rings' opposition surge, calibrated so that
    // Saturn's swing between edge-on and wide open matches the ~1.0 magnitude observed.
    public const double RingAlbedo = 0.97;

    /// <summary>
    /// Apparent V magnitude of a sunlit body.
    ///
    /// The absolute magnitude of a solar system body follows from its size and albedo,
    /// H = 5 log10(1329 / (D_km * sqrt(p))), the standard relation behind every published
    /// H - it returns -6.88 for Neptune against the IAU's -6.87. Distance then contributes
    /// 5 log10(r * delta) in AU, and the phase angle is handled by the IAU H-G system with
    /// G = 0.15, which is the usual default for a body whose slope parameter is unmeasured.
    /// </summary>
    public static double ApparentMagnitude(double radiusM, double albedo,
                                           double sunDistM, double obsDistM, double cosPhase,
                                           bool hasAtmosphere = false, double ringFluxRatio = 0.0,
                                           double litFraction = 1.0)
    {
        double diameterKm = 2.0 * radiusM / 1000.0;
        double h = 5.0 * Math.Log10(1329.0 / (diameterKm * Math.Sqrt(Math.Max(albedo, 1e-4))));
        double distance = 5.0 * Math.Log10((sunDistM / Au) * (obsDistM / Au));

        double alpha = Math.Acos(Math.Clamp(cosPhase, -1.0, 1.0));
        double flux = PhaseFunction(alpha, hasAtmosphere)
                    * (1.0 + Math.Max(ringFluxRatio, 0.0))
                    * Math.Clamp(litFraction, 0.0, 1.0);

        // Floored rather than allowed to reach zero: a totally eclipsed moon still catches
        // light bent through its planet's atmosphere, and an infinity here would poison the
        // glow radius downstream.
        return h + distance - 2.5 * Math.Log10(Math.Max(flux, 1e-9));
    }

    /// <summary>
    /// Fraction of the light an observer would see at opposition, for this phase angle.
    /// Airless bodies follow H-G with a Lambert floor; atmospheres get the forward lobe.
    /// </summary>
    public static double PhaseFunction(double alphaRad, bool hasAtmosphere)
    {
        double a = Math.Clamp(alphaRad, 0.0, Math.PI);
        double lambert = Math.Max((Math.Sin(a) + (Math.PI - a) * Math.Cos(a)) / Math.PI, 1e-12);

        if (hasAtmosphere)
            return lambert + AtmosphereForwardWeight * Henyey(Math.PI - a, AtmosphereForwardG);

        double halfTan = Math.Tan(Math.Min(a, Math.PI * 0.999) * 0.5);
        double hg = 0.85 * Math.Exp(-3.33 * Math.Pow(halfTan, 0.63))
                  + 0.15 * Math.Exp(-1.87 * Math.Pow(halfTan, 1.22));
        return Math.Max(hg, AirlessLambertFloor * lambert);
    }

    /// <summary>Henyey-Greenstein, the standard one-parameter scattering lobe.</summary>
    private static double Henyey(double thetaRad, double g)
    {
        double d = 1.0 + g * g - 2.0 * g * Math.Cos(thetaRad);
        return (1.0 - g * g) / (4.0 * Math.PI * Math.Pow(Math.Max(d, 1e-9), 1.5));
    }

    /// <summary>
    /// Light the rings add, as a fraction of what the globe reflects.
    ///
    /// A flat annulus intercepts starlight in proportion to how open it is to the star, and
    /// sends it on in proportion to how open it is to the observer, so the sine enters twice:
    /// edge-on rings contribute nothing, which is exactly what Saturn does every fifteen years.
    /// </summary>
    public static double RingFluxRatio(double bodyRadiusM, double bodyAlbedo,
                                       double innerM, double outerM,
                                       double sinToStar, double sinToObserver)
    {
        if (outerM <= innerM || bodyRadiusM <= 0.0) return 0.0;
        double annulus = outerM * outerM - innerM * innerM;
        double disc = bodyRadiusM * bodyRadiusM;
        return (RingAlbedo / Math.Max(bodyAlbedo, 1e-3)) * (annulus / disc)
             * Math.Abs(sinToStar) * Math.Abs(sinToObserver);
    }

    /// <summary>
    /// How much of the star a body can still see, given its parent might be in the way: 1 in
    /// the open, 0 deep in the umbra, and a smooth ramp through the penumbra. All positions
    /// are relative - the body to its parent, the parent to the star.
    /// </summary>
    public static double LitFraction(double bx, double by, double bz,
                                     double px, double py, double pz,
                                     double parentRadiusM, double starRadiusM)
    {
        double parentDist = Math.Sqrt(px * px + py * py + pz * pz);
        if (parentDist <= 0.0 || parentRadiusM <= 0.0) return 1.0;

        // The shadow points straight away from the star.
        double sx = px / parentDist, sy = py / parentDist, sz = pz / parentDist;
        double depth = bx * sx + by * sy + bz * sz;
        if (depth <= 0.0) return 1.0;                    // sunward of its parent

        double ox = bx - depth * sx, oy = by - depth * sy, oz = bz - depth * sz;
        double offset = Math.Sqrt(ox * ox + oy * oy + oz * oz);

        // The umbra narrows with depth and the penumbra widens, both at the rate the star's
        // size sets. Past the umbra's tip its radius goes negative, which is an annular
        // eclipse: never total, and the test below handles it without a special case.
        double umbra = parentRadiusM - depth * (starRadiusM - parentRadiusM) / parentDist;
        double penumbra = parentRadiusM + depth * (starRadiusM + parentRadiusM) / parentDist;

        if (offset >= penumbra) return 1.0;
        if (offset <= umbra) return 0.0;
        double t = (offset - umbra) / Math.Max(penumbra - umbra, 1e-6);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// Radius in pixels at which the profile drops below one display level - the same
    /// relation the star vertex shader uses, so a planet and a star of equal magnitude come
    /// out the same size and the same brightness.
    /// </summary>
    public static double GlowRadiusPx(double magnitude)
    {
        double flux = Math.Pow(10.0, -0.4 * (magnitude - MagRef));
        double peak = flux * Brightness * (PsfBeta - 1.0) / (Math.PI * PsfCore * PsfCore);
        double glow = PsfCore * Math.Sqrt(
            Math.Max(Math.Pow(peak * DisplayLevels, 1.0 / PsfBeta) - 1.0, 0.0));
        return Math.Clamp(glow, MinGlowPx, MaxGlowPx);
    }

    /// <summary>The blend the caller applies after us: full sprite below 1 px across, gone by 4,
    /// where the body starts being drawn as an actual sphere instead.</summary>
    public static double SpriteMul(double pixelDiameter)
    {
        double t = Math.Clamp((pixelDiameter - 1.0) / 3.0, 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    // ---------------------------------------------------------------- reflection glue

    /// <summary>
    /// How much smaller our sprite must be for the same number. Stock's vertex shader puts the
    /// quad's half-extent at scalePixel/4 pixels; ours puts it at scalePixel, because scalePixel
    /// means a glow radius to us. Anything we do not compute a magnitude for still has to come
    /// back in OUR units, or it is drawn four times too large and, since our shader derives
    /// brightness from the radius, far too bright with it.
    /// </summary>
    public const double StockScaleToGlow = 0.25;

    private static MethodInfo? _getPositionEgo, _getCamera, _diameterPixels;
    private static PropertyInfo? _mainViewport;
    private static bool _logged, _loggedFallback;

    /// <summary>
    /// Postfix on StaticCelestialDistanceRendering.GetApparentSizeScale, replacing the game's
    /// radius-based visibility scale with one computed from the body's actual brightness.
    ///
    /// A postfix rather than a prefix so that whatever we cannot measure - a vehicle's glint, a
    /// comet, a body with no scattering data - still arrives here as the game's own value, and
    /// only needs converting into our shader's units rather than reconstructing from nothing.
    /// </summary>
    public static void ApparentSizeScalePostfix(object celestial, ref double __result)
    {
        try
        {
            double? mag = MagnitudeOf(celestial, out double pixelDiameter);
            if (mag != null)
            {
                // The caller computes scalePixel = base * ours * spriteMul, where base is the
                // true diameter in pixels, floored at 1 for anything orbiting a star. Divide
                // out the base so what survives is our glow radius, but LEAVE spriteMul: that
                // is the cross-fade to the lit sphere the game starts drawing at 2 px, and
                // keeping it is what stops a point source being painted over a resolved disc.
                __result = GlowRadiusPx(mag.Value) / Math.Max(pixelDiameter, 1.0);
                if (!_logged)
                {
                    ShaderShadow.Log($"planets on the photometric scale (first: V {mag.Value:F2})");
                    _logged = true;
                }
                return;
            }
        }
        catch (Exception ex)
        {
            if (!_loggedFallback)
            {
                ShaderShadow.Log("WARN: could not measure a body, using the game's own sizing for it: "
                                 + ex.Message);
                _loggedFallback = true;
            }
        }
        __result *= StockScaleToGlow;
    }

    /// <summary>
    /// The members we need off one concrete body type. Cached per type, because the game's
    /// celestials are a hierarchy and a member found on one is not valid on another.
    /// </summary>
    private sealed record Accessors(PropertyInfo? BodyTemplate, PropertyInfo? MeanRadius,
                                    MethodInfo? GetPositionEcl);

    private static readonly Dictionary<Type, Accessors> _accessors = new();

    /// <summary>
    /// A property by name, taking the most derived declaration. Plain GetProperty throws on
    /// these types: BodyTemplate is declared more than once down the hierarchy with different
    /// types, which reflection reports as an ambiguous match rather than resolving.
    /// </summary>
    /// <summary>Shared with <see cref="Scintillation"/>, which walks the same hierarchies.</summary>
    public static PropertyInfo? FindPropertyPublic(Type type, string name) => FindProperty(type, name);

    /// <summary>Shared with <see cref="Scintillation"/>: the engine's double3 by component.</summary>
    public static (double, double, double) VecPublic(object v) => Vec(v);

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>Magnitude of a body, or null when it is not one we can measure.</summary>
    private static double? MagnitudeOf(object celestial, out double pixelDiameter)
    {
        pixelDiameter = 0.0;
        Type t = celestial.GetType();

        if (!_accessors.TryGetValue(t, out Accessors? acc))
            _accessors[t] = acc = new Accessors(
                FindProperty(t, "BodyTemplate"),
                FindProperty(t, "MeanRadius"),
                AccessTools.Method(t, "GetPositionEcl", Type.EmptyTypes));

        object? template = acc.BodyTemplate?.GetValue(celestial);
        if (template == null) return null;                   // vehicles, comets: not our business

        if (acc.MeanRadius?.GetValue(celestial) is not double radius || double.IsNaN(radius) || radius <= 0)
            return null;

        double albedo = Albedo(template);

        object? eclObj = acc.GetPositionEcl?.Invoke(celestial, null);
        if (eclObj == null) return null;

        _mainViewport ??= AccessTools.Property(AccessTools.TypeByName("KSA.Program")!, "MainViewport");
        object? viewport = _mainViewport?.GetValue(null);
        if (viewport == null) return null;
        _getCamera ??= AccessTools.Method(viewport.GetType(), "GetCamera");
        object? camera = _getCamera?.Invoke(viewport, null);
        if (camera == null) return null;

        _getPositionEgo ??= AccessTools.Method(camera.GetType(), "GetPositionEgo",
                                               new[] { AccessTools.TypeByName("KSA.IOrbiter")! });
        object? egoObj = _getPositionEgo?.Invoke(camera, new[] { celestial });
        if (egoObj == null) return null;

        (double ex, double ey, double ez) = Vec(eclObj);     // body relative to its star
        (double gx, double gy, double gz) = Vec(egoObj);     // body relative to the camera
        double sunDist = Math.Sqrt(ex * ex + ey * ey + ez * ez);
        double obsDist = Math.Sqrt(gx * gx + gy * gy + gz * gz);
        if (sunDist <= 0 || obsDist <= 0) return null;

        // Phase angle at the body, between its star and the camera.
        double cosPhase = (ex * gx + ey * gy + ez * gz) / (sunDist * obsDist);

        _diameterPixels ??= AccessTools.Method(camera.GetType(), "GetObjectDiameterPixels");
        if (_diameterPixels?.Invoke(camera, new object[] { 2.0 * radius, obsDist }) is double px)
            pixelDiameter = px;

        return ApparentMagnitude(radius, albedo, sunDist, obsDist, cosPhase,
                                 HasAtmosphere(celestial),
                                 Rings(celestial, template, radius, albedo, ex, ey, ez, gx, gy, gz),
                                 Lit(celestial, ex, ey, ez));
    }

    private static Type? _atmosphericType;
    private static bool _atmosphericLookedUp;

    /// <summary>Does the engine consider this body to have an atmosphere? It already knows.</summary>
    private static bool HasAtmosphere(object celestial)
    {
        if (!_atmosphericLookedUp)
        {
            _atmosphericType = AccessTools.TypeByName("KSA.AtmosphericBody");
            _atmosphericLookedUp = true;
        }
        return _atmosphericType?.IsInstanceOfType(celestial) ?? false;
    }

    private static readonly Dictionary<Type, FieldInfo?> _ringsFields = new();
    private static MethodInfo? _ringNormal;
    private static bool _ringNormalLookedUp;

    /// <summary>
    /// The ring contribution, from the body's own ring geometry. Returns 0 for a body with no
    /// rings, which is almost all of them.
    /// </summary>
    private static double Rings(object celestial, object template, double radius, double albedo,
                                double ex, double ey, double ez, double gx, double gy, double gz)
    {
        Type tt = template.GetType();
        if (!_ringsFields.TryGetValue(tt, out FieldInfo? rf))
            _ringsFields[tt] = rf = AccessTools.Field(tt, "RingsReference");
        object? rings = rf?.GetValue(template);
        if (rings == null) return 0.0;

        double inner = Distance(rings, "InnerRadius"), outer = Distance(rings, "OuterRadius");
        if (outer <= inner) return 0.0;

        if (!_ringNormalLookedUp)
        {
            // Lives off in KSA.Rendering.Rings.Rendering, not beside the bodies, and a rename
            // of that namespace should cost us rings rather than throw, so the simple name is
            // a fallback.
            Type? rr = AccessTools.TypeByName("KSA.Rendering.Rings.Rendering.PlanetaryRingsRenderer")
                    ?? AccessTools.AllTypes().FirstOrDefault(t => t.Name == "PlanetaryRingsRenderer");
            _ringNormal = rr == null ? null : AccessTools.Method(rr, "ComputeRingNormal");
            _ringNormalLookedUp = true;
            if (_ringNormal == null)
                ShaderShadow.Log("WARN: ring geometry not found; ringed planets lose their rings' light");
        }
        object? normalObj = _ringNormal?.Invoke(null, new[] { rings, celestial });
        if (normalObj == null) return 0.0;
        (double nx, double ny, double nz) = Vec(normalObj);
        double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nl <= 0.0) return 0.0;
        nx /= nl; ny /= nl; nz /= nl;

        // How open the rings are to the star, and to the camera. The body sits at ex,ey,ez
        // from its star and at gx,gy,gz from the camera, so those are the two directions.
        double el = Math.Sqrt(ex * ex + ey * ey + ez * ez);
        double gl = Math.Sqrt(gx * gx + gy * gy + gz * gz);
        if (el <= 0.0 || gl <= 0.0) return 0.0;
        double sinStar = Math.Abs((nx * ex + ny * ey + nz * ez) / el);
        double sinObs = Math.Abs((nx * gx + ny * gy + nz * gz) / gl);

        return RingFluxRatio(radius, albedo, inner, outer, sinStar, sinObs);
    }

    private static readonly Dictionary<string, FieldInfo?> _distanceFields = new();
    private static readonly Dictionary<Type, MethodInfo?> _inMeters = new();

    /// <summary>A DistanceReference field, in metres.</summary>
    private static double Distance(object owner, string field)
    {
        Type t = owner.GetType();
        string key = t.FullName + "." + field;
        if (!_distanceFields.TryGetValue(key, out FieldInfo? f))
            _distanceFields[key] = f = AccessTools.Field(t, field);

        object? d = f?.GetValue(owner);
        if (d == null) return 0.0;

        Type dt = d.GetType();
        if (!_inMeters.TryGetValue(dt, out MethodInfo? m))
            _inMeters[dt] = m = AccessTools.Method(dt, "InMeters");
        return m?.Invoke(d, null) is double v ? v : 0.0;
    }

    private static PropertyInfo? _parentProp;
    private static double _starRadius;

    /// <summary>
    /// Whether the body is in its parent's shadow. Only moons can be: a planet's parent is the
    /// star itself, and a body cannot be eclipsed by what lights it.
    /// </summary>
    private static double Lit(object celestial, double ex, double ey, double ez)
    {
        _parentProp ??= FindProperty(celestial.GetType(), "Parent");
        object? parent = _parentProp?.GetValue(celestial);
        if (parent == null) return 1.0;

        // The parent must itself orbit something, or it is the star.
        PropertyInfo? grandParent = FindProperty(parent.GetType(), "Parent");
        if (grandParent?.GetValue(parent) == null) return 1.0;

        PropertyInfo? radiusProp = FindProperty(parent.GetType(), "MeanRadius");
        if (radiusProp?.GetValue(parent) is not double parentRadius || parentRadius <= 0.0) return 1.0;

        MethodInfo? eclMethod = AccessTools.Method(parent.GetType(), "GetPositionEcl", Type.EmptyTypes);
        object? parentEcl = eclMethod?.Invoke(parent, null);
        if (parentEcl == null) return 1.0;
        (double px, double py, double pz) = Vec(parentEcl);

        if (_starRadius <= 0.0) _starRadius = StarRadius(parent);
        if (_starRadius <= 0.0) return 1.0;

        return LitFraction(ex - px, ey - py, ez - pz, px, py, pz, parentRadius, _starRadius);
    }

    /// <summary>Radius of whatever sits at the root of the parent chain: the star.</summary>
    private static double StarRadius(object body)
    {
        for (int i = 0; i < 8 && body != null; i++)
        {
            PropertyInfo? p = FindProperty(body.GetType(), "Parent");
            object? next = p?.GetValue(body);
            if (next == null)
                return FindProperty(body.GetType(), "MeanRadius")?.GetValue(body) is double r ? r : 0.0;
            body = next;
        }
        return 0.0;
    }

    private static readonly Dictionary<Type, FieldInfo?> _scatteringFields = new();
    private static readonly Dictionary<Type, FieldInfo?> _albedoFields = new();
    private static readonly Dictionary<Type, MemberInfo?> _albedoValues = new();

    /// <summary>
    /// The body's geometric albedo from its own scattering data, falling back to the same 0.5
    /// the game uses when a body has none. This is why nothing here is per-planet: a modded
    /// world's albedo and radius give its magnitude exactly as Neptune's do.
    /// </summary>
    private static double Albedo(object template)
    {
        Type tt = template.GetType();
        if (!_scatteringFields.TryGetValue(tt, out FieldInfo? sf))
            _scatteringFields[tt] = sf = AccessTools.Field(tt, "ScatteringReference");
        object? scattering = sf?.GetValue(template);
        if (scattering == null) return 0.5;

        Type st = scattering.GetType();
        if (!_albedoFields.TryGetValue(st, out FieldInfo? af))
            _albedoFields[st] = af = AccessTools.Field(st, "MeanAlbedo");
        object? albedoRef = af?.GetValue(scattering);
        if (albedoRef == null) return 0.5;

        return ReadFloat(albedoRef) ?? 0.5;
    }

    /// <summary>
    /// The number inside one of the engine's reference wrappers, whichever kind it is.
    ///
    /// They do not agree with each other, which has cost two bugs now. FloatReference keeps a
    /// public Value FIELD - not a property, so a property-only lookup silently finds nothing.
    /// DensityReference has no Value at all: unit fields like KgPerM3, a private _value, and an
    /// implicit conversion to double. A reader that assumed otherwise returned nothing, the
    /// caller fell back to zero, and stars stopped twinkling everywhere.
    ///
    /// So: the implicit conversion first, since every one of them defines it and it is the
    /// engine's own way of unwrapping these, then the named field, then the private one.
    /// </summary>
    public static double? ReadFloat(object reference)
    {
        Type at = reference.GetType();
        if (!_albedoValues.TryGetValue(at, out MemberInfo? vm))
        {
            _albedoValues[at] = vm =
                (MemberInfo?)at.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "op_Implicit"
                                      && (m.ReturnType == typeof(double) || m.ReturnType == typeof(float))
                                      && m.GetParameters().Length == 1
                                      && m.GetParameters()[0].ParameterType == at)
                ?? (MemberInfo?)AccessTools.Field(at, "Value")
                ?? (MemberInfo?)FindProperty(at, "Value")
                ?? AccessTools.Field(at, "_value");
        }

        object? value = vm switch
        {
            MethodInfo m => m.Invoke(null, new[] { reference }),
            FieldInfo f => f.GetValue(reference),
            PropertyInfo p => p.GetValue(reference),
            _ => null
        };
        return value switch
        {
            float f => f,
            double d => d,
            _ => null
        };
    }

    /// <summary>Components of one of the engine's double3 values, whatever it calls them.</summary>
    private static (double, double, double) Vec(object v)
    {
        Type t = v.GetType();
        double Read(string upper, string lower)
        {
            object? o = (t.GetField(upper) ?? t.GetField(lower))?.GetValue(v)
                        ?? (t.GetProperty(upper) ?? t.GetProperty(lower))?.GetValue(v);
            return o is double d ? d : o is float f ? f : throw new InvalidOperationException(
                $"cannot read component {upper} of {t.Name}");
        }
        return (Read("X", "x"), Read("Y", "y"), Read("Z", "z"));
    }
}
