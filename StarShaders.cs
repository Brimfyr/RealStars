namespace RealStars;

/// <summary>
/// The replacement star shaders, written over the copies in our shader tree.
///
/// These are full replacements rather than anchored edits, because what changes is the
/// meaning of the instance data rather than a line here or there: stock treats the packed
/// byte as a sprite SIZE, and everything follows from that. <see cref="ShaderShadow"/>
/// fingerprints the stock files first and leaves them alone if a game update rewrites
/// them, so an update degrades to the stock sky rather than to a broken one.
/// </summary>
internal static class StarShaders
{
    /// <summary>Distinctive stock text. If it is gone, the shader we designed against is gone too.</summary>
    public const string VertFingerprint = "outMaxTheta = glowScale * sqrt(irradiation);";
    public const string FragFingerprint = "float psf_glow(float offset)";
    public const string PlanetVertFingerprint = "vertPosition.x *= instanceData.scalePixel / global.camera.screenWidth;";
    public const string PlanetFragFingerprint = "float simpleBrightness = maxBrightness * (inScalePixel / smallStarThreshold);";

    /// <summary>Every shader we replace, with the stock text that proves it is the one we mean.</summary>
    public static readonly (string Name, string Fingerprint, string Replacement)[] All =
    {
        ("Star.vert", VertFingerprint, Vert),
        ("Star.frag", FragFingerprint, Frag),
        ("StaticCelestialDistance.vert", PlanetVertFingerprint, PlanetVert),
        ("StaticCelestialDistance.frag", PlanetFragFingerprint, PlanetFrag),
    };

    /// <summary>
    /// Files served from our tree rather than the game's. Anything we edit belongs here, unless
    /// it reaches the compiler as an #include of something that is already on the list: shaderc
    /// resolves an #include relative to the file that asked for it, so Sun.frag is here to carry
    /// the raymarch we edit, not because we change Sun.frag itself.
    /// </summary>
    public static readonly string[] Redirected =
        { "Sun/Sun.frag", "MilkyWay.frag", "PostProcess/sunbloom.frag", "PostProcess/sunbloom_merge.comp" };

    /// <summary>
    /// Shaders we change a line of rather than replace. The Sun's surface is a raymarch of
    /// several hundred lines that do their job, and taking custody of all of it to fix a few
    /// numbers would mean maintaining it forever. Each anchor is a whole line, so an update
    /// that touches one leaves the file stock and says so in the log.
    /// </summary>
    public static readonly (string Name, string Find, string Replace)[] Edits =
    {
        // The sphere and its corona are coloured by a blackbody ramp over 1400-2700 K, which is
        // a flame: embers through to orange. The Sun's photosphere is 5772 K and reads as
        // near-white, its granulation varying a couple of hundred kelvin either side, so the
        // range moves onto the star it is meant to be. Everything else about the palette - the
        // wavelengths, the exposure curve - is left as it was.
        ("RayMarching/RayMarchingTest.glsl",
         "    float T = 1400. + 1300.*i; // Temperature range (in Kelvin).",
         "    float T = 5200. + 1200.*i; // Real Stars: the solar photosphere, not a fire."),

        // The marched surface sits at half the mesh radius, and the mesh is 5.5 solar radii,
        // so the Sun was being drawn 2.75 times its own size. Nothing in the game contradicts
        // it - the sphere is only ever on screen inside 0.41 AU, where there is nothing to
        // measure it against - but our sprite is drawn from the real angular size, so at the
        // handover the disc jumped. It is the star's radius, which is the number the lighting
        // already carries.
        ("RayMarching/RayMarchingTest.glsl",
         "float sunRadius = meshRadius / 2.;",
         "float sunRadius = global.lighting.sunRadius * scaleDownFactor;  // Real Stars: life size"),

        // And the corona is not one.
        //
        // The march adds 1/200 to its density on every step whether or not it hit anything,
        // and alpha is derived from that density. Fifty-six steps carry it past the point
        // where alpha saturates, so the star reads as opaque to 2.8 radii and then fades to
        // the mesh edge at 5.5 - a halo made out of the loop counting itself. The real corona
        // is about a millionth of the photosphere's surface brightness and is why totality is
        // the only time anyone sees it.
        //
        // A sphere's edge is geometry, so that is where it comes from here: the ray's closest
        // approach to the centre against the radius, softened by one pixel's worth of it so
        // the limb does not stair-step. Beyond it there is nothing to draw, and the glare
        // around the Sun - which is in the eye and the lens, not the sky - is already ours.
        ("RayMarching/RayMarchingTest.glsl",
           "    float alpha = 1.;\n"
         + "\n"
         + "    // Lower alpha outside of the sphere for a glow\n"
         + "    if (total_density < .5)\n"
         + "    {\n"
         + "        float remappedDensity = remap(total_density, .05, .4);\n"
         + "        alpha = 6. * falloffFunc(1. - remappedDensity, 1.);\n"
         + "        alpha = clamp(alpha, 0., 1.);\n"
         + "    }",
           "    // Real Stars: the edge of the star, from the star's shape.\n"
         + "    vec3 toCentre = centerPos - ray_origin;\n"
         + "    float impact = length(cross(toCentre, ray_direction));\n"
         + "    float edge = max(fwidth(impact), 1e-6);\n"
         + "    float alpha = 1. - smoothstep(sunRadius - edge, sunRadius + edge, impact);\n"
         + "\n"
         + "    // The engine draws this mesh below 88 solar radii and not above it, in one\n"
         + "    // step. Our sprite draws the Sun underneath at every distance, at the same\n"
         + "    // angular size and now the same level, so the two carry the same disc - but the\n"
         + "    // sphere has a limb and a darkened edge where the sprite has a halo, and trading\n"
         + "    // one for the other between two frames is the jump that was left. It fades in\n"
         + "    // across the top half of the range instead, and has reached nothing by the time\n"
         + "    // the engine drops it. Both distances are read off the radius the lighting\n"
         + "    // carries, the same number the engine reads them from, so they cannot drift.\n"
         + "    alpha *= 1. - smoothstep(44. * sunRadius, 88. * sunRadius, length(centerPos));\n"
         + "\n"
         + "    // Limb darkening: a sight line near the edge is slanted, so it reaches optical\n"
         + "    // depth one higher in the photosphere, where the gas is cooler. The classical\n"
         + "    // linear law, u = 0.6, which is about right in the visible. Without it a disc\n"
         + "    // this evenly lit reads as a cut-out rather than a sphere.\n"
         + "    float mu = sqrt(max(1. - impact * impact / (sunRadius * sunRadius), 0.));\n"
         + "    total_color *= 1. - 0.6 * (1. - mu);"),

        // The sphere came out of the raymarch at 1.3 while our sprite's core is clamped at
        // MaxOutput, and the Sun's own profile is orders past that clamp at every distance the
        // sphere is on screen - so the sprite was some eighteen times the brighter of the two.
        // That is the step across the handover, and inside it the saturated part of our halo
        // stood out past the sphere's edge as a brighter ring, which is the disc reading larger
        // than the sphere, and the bloom appearing to double. One shared number removes all of
        // it: the two write the same value, so which of them is on top stops mattering.
        // The value is not constant - it gives brightness back as the disc fills the frame,
        // standing in for the exposure adaptation the engine has not got - but both sides read
        // it from the same expression, so it moves for both of them together.
        ("Sun/Sun.frag",
           "    // Apply bloom\n"
         + "    col.rgb *= 1.3;",
           "    // Real Stars: the level our own sprite draws the Sun at, so they can be traded.\n"
         + "    col.rgb *= " + SunLevelGlsl + ";"),

        // The last thing drawing a Sun that is not ours.
        //
        // The bloom pass fills a disc of TWO solar radii solid white whenever the pixel's ray
        // hits it - insideSun() raytraces it - with no relation to the screenspace radius the
        // rest of the pass is sized by, and so no relation to anything we zeroed. That is the
        // white disc sitting inside the sphere. It also steps twice on the way in: at 0.78 of
        // showSurfDist the disc widens by a quarter and its brightness falls to a third, which
        // is the jump around Mercury that survived the sprite going away.
        //
        // The Sun's disc and its glare are our sprite's now, at the real angular size and the
        // real magnitude, so the pass has nothing left to contribute. Only the sun's own term
        // goes: the occlusion output below is untouched, so the lens flare still knows whether
        // the Sun is in view, and the spokes and ghosts in sunbloom_blur.comp are left alone.
        // The Sun's starburst is drawn here, over the finished image, rather than with the
        // stars. It is scatter inside whoever is looking, so it lies over everything they are
        // looking at - and drawn with the stars it went UNDER the first thing in front of the
        // Sun, a hull or a ridge or a limb taking every ray that crossed it. This pass runs
        // after the world, writes the final HDR image, skips what is under the UI, and does not
        // include the atmosphere functions, so Real Atmospheres keeps its own.
        ("PostProcess/sunbloom_merge.comp",
         "layout (set = 1, binding = 2) uniform sampler2D bloomColor;",
         "layout (set = 1, binding = 2) uniform sampler2D bloomColor;\n" + MergeSunBurst),
        ("PostProcess/sunbloom_merge.comp",
         "    vec4 finalColor = screenCol + vec4(bloom.rgb, 0.0);",
         "    vec4 finalColor = screenCol + vec4(bloom.rgb, 0.0);\n"
         + "    // Real Stars: the Sun's starburst, over everything - see rsSunBurst.\n"
         + "    finalColor.rgb += rsSunBurst(vec2(screenCoords) + vec2(0.5), dim);"),

        ("PostProcess/sunbloom.frag",
         "    float sunPower = innerSun * innerSunScalar + outerSun * sunData.outerSunColorScalar;",
         "    float sunPower = 0.;  // Real Stars: the Sun is drawn as the star it is"),

        // The Milky Way sits about 50 degrees out of place, and always has. Its rotation is
        // built by GalacticPlane.BuildRotation from EquatorialDirection(ra, dec), which is
        // (cos(dec)cos(ra), sin(dec), cos(dec)sin(ra)) - equatorial, Y up. The world it is then
        // applied to is ecliptic, Z up: the planets orbit in its XY plane and the shipped star
        // binary is in it too. Measured against the real sky the galactic centre lands 55.3
        // degrees from where it belongs and the north galactic pole 49.1.
        //
        // Nothing here changes the matrix. The view direction is converted out of the world's
        // frame and into the one the matrix was built for, first: equatorial by the obliquity,
        // then Y-up by swapping the last two components. That is a conversion of the input
        // frame, so it holds whichever way round the engine's matrix convention runs. With it,
        // the centre lands 0.07 degrees from Sagittarius A*, which is the width of the IAU's
        // own definition.
        ("MilkyWay.frag",
         "    sampleDir   = (global.camera.galacticPlane * vec4(sampleDir, 0.0)).xyz;",
         "    // Real Stars: into the equatorial Y-up frame the galactic rotation was built in.\n"
         + "    const float rsCosObliquity = 0.91748206;   // 23.4392911 degrees\n"
         + "    const float rsSinObliquity = 0.39777716;\n"
         + "    sampleDir   = vec3(sampleDir.x,\n"
         + "                       rsSinObliquity * sampleDir.y + rsCosObliquity * sampleDir.z,\n"
         + "                       rsCosObliquity * sampleDir.y - rsSinObliquity * sampleDir.z);\n"
         + "    sampleDir   = (global.camera.galacticPlane * vec4(sampleDir, 0.0)).xyz;"),
    };

    // ---------------------------------------------------------------------------------
    // Why any of this changes:
    //
    // Stock carries a star's brightness in the SIZE of its sprite: Star.vert sets the quad
    // radius to scale/255 in clip units, so Sirius is a ~52 px octagon at 1080p and a faint
    // star is 3.7 px. The fragment shader then evaluates a point spread function whose angle
    // comes from the UV offset scaled by resolution, while its cutoff angle comes from the
    // quad radius. The two are unrelated: for a bright star the profile stays above the 1.5
    // clamp across the whole quad, which is why bright stars read as flat white discs.
    //
    // Here the byte is a MAGNITUDE, written by make_star_binary.py, and flux follows from
    // Pogson's law. The profile is a normalised Moffat with beta = 2, whose 1/r^2 wings are
    // what both diffraction and atmospheric seeing leave behind, and the quad is sized to
    // exactly contain it: a star's apparent size becomes a consequence of its brightness
    // rather than the way it is stored.
    //
    // This means the shaders and the catalogue are a matched pair. Running these against the
    // stock binary would read its size bytes as magnitudes and light the sky wrongly, which
    // is why ModMain only patches the shaders' meaning of the data alongside its own file.
    // ---------------------------------------------------------------------------------

    /// <summary>Shared tuning. Both stages need the same numbers, so they are written once.</summary>
    /// <remarks>
    /// rsMagFaint and rsBytesPerMag MUST match make_star_binary.py, which writes the byte.
    /// </remarks>
    /// <summary>
    /// The brightest value any of our sprites writes, and now the value the Sun's sphere is
    /// drawn at too. A bright star's core genuinely does saturate a fixed exposure, so this is
    /// a limit rather than a look; it keeps one star out of the range where the engine's bloom
    /// would smear it across the frame. The Sun's own profile is far past it at every distance
    /// the sphere is on screen, so the sphere matching it is what makes the two interchangeable.
    /// Turn it down if the Sun blooms too hard - it binds on nothing else in the catalogue.
    /// </summary>
    public const string MaxOutput = "24.0";

    /// <summary>
    /// Standing in for auto exposure, which the engine does not have yet. A fixed exposure
    /// chosen to suit the Sun as a distant point leaves it blazing once it fills the frame, so
    /// the disc gives brightness back as it grows: below the knee nothing changes, above it the
    /// level falls inversely with the disc's share of the screen, and it stops at the floor.
    ///
    /// The knee is the disc RADIUS as a fraction of the screen's HEIGHT. At 0.04 it starts
    /// around 0.1 AU, well inside the 0.41 AU where the sphere appears, so the handover is
    /// untouched. The floor is what the engine drew the sphere at before any of this, which is
    /// a sensible place to stop. The power is what makes it bite: true adaptation works on the
    /// light collected, which goes as the AREA, so 2.0 is the physical end of that range and 1.0
    /// the gentlest useful one. At 2.0 the level is back on the floor once the disc is a sixth
    /// of the screen high, which is still inside a tenth of an AU.
    /// </summary>
    public const string SunExposureKnee = "0.04";
    public const string SunExposureFloor = "1.3";
    public const string SunExposurePower = "2.0";

    /// <summary>
    /// The Sun's disc radius as a fraction of the screen's height. Resolution cancels: pixels
    /// per radian is half the height times projection[1][1], so dividing by the height leaves
    /// half the vertical focal length times the angular radius. Field of view does not cancel,
    /// which is the point - zooming in fills the frame the same way approaching does, and costs
    /// the same brightness.
    /// </summary>
    private const string SunFractionGlsl =
        "(0.5 * abs(global.camera.projection[1][1]) * global.lighting.sunRadius"
        + " / max(length(global.lighting.sunPosition.xyz), 1.0))";

    /// <summary>
    /// The level the Sun's disc is drawn at. Written once and read by both the sprite and the
    /// sphere, because the moment they disagree the handover becomes visible again.
    /// </summary>
    public const string SunLevelGlsl =
        "clamp(" + MaxOutput + " * pow(" + SunExposureKnee + " / max(" + SunFractionGlsl + ", 1e-6), "
        + SunExposurePower + "), " + SunExposureFloor + ", " + MaxOutput + ")";

    /// <summary>
    /// The Sun's starburst as the merge pass draws it, over the finished image. The tuning comes
    /// with it, so the rays are the same rays the sprites draw - the same eye - and the
    /// occlusion is the finished depth buffer, which is the best answer there is to what is in
    /// front of the Sun.
    /// </summary>
    private const string MergeSunBurst = $$"""

        // ---- Real Stars ----
        {{Tuning}}
        const float rsMaxOutput = {{MaxOutput}};
        const float rsSunAbsMag = 4.83;            // what the catalogue carries for the Sun

        // Where the Sun is, and what of it is left to see, from the finished frame.
        //
        // Being drawn after the world means the depth buffer is complete, and that answers what
        // is in front of the Sun better than anything worked out beforehand: hulls, ridges,
        // moons, any of it, to the pixel. The Sun as it LOOKS - its disc and the white ring the
        // profile saturates past the limb - is sampled at thirteen points, and the share with
        // something drawn nearer than the Sun is the share hidden. The Sun's own sphere writes
        // no depth, so it cannot hide itself. Air writes none either, so the haze keeps its term.
        //
        // What is left sets the rays' BRIGHTNESS, linearly: the scatter that makes them is a
        // fixed share of the light that gets in, so a sliver of Sun throws a sliver of burst.
        // Their reach was the only thing that fell before, and it falls by magnitude - ninety
        // five percent covered is still seven magnitudes past the burst line - which is why the
        // rays outlived the Sun behind a hull and through Titan's haze.
        vec3 rsSunBurst(vec2 pixel, ivec2 dim)
        {
            if (rsBurstStrength() <= 0.0) return vec3(0.0);

            vec3 sunEgo = global.lighting.sunPosition.xyz;
            float dist = length(sunEgo);
            if (dist <= 0.0) return vec3(0.0);
            vec4 clip = global.camera.viewProjection * vec4(sunEgo, 1.0);
            if (clip.w <= 0.0) return vec3(0.0);                   // behind the camera
            vec2 ndc = clip.xy / clip.w;
            vec2 sunPx = (ndc * 0.5 + 0.5) * vec2(dim);

            // The whole Sun, before anything is in the way, and how far its rays could reach.
            float mag = rsSunAbsMag + 5.0 * log(dist / (10.0 * rsParsecMetres)) * 0.4342944819;
            float peak = pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef))
                       * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float reach = rsBurstReachPx(peak);
            vec2 offset = pixel - sunPx;
            float skirt = reach + 8.0;                              // and the blur beside a ray
            if (reach <= 0.0 || dot(offset, offset) > skirt * skirt) return vec3(0.0);

            // Off the frame, nothing entered.
            vec2 pastEdge = 0.5 * vec2(dim) * (abs(ndc) - vec2(1.0));
            float framed = 1.0 - smoothstep(-rsBurstEdgePx, rsBurstEdgePx, max(pastEdge.x, pastEdge.y));
            if (framed <= 0.0) return vec3(0.0);

            // What is in front of it, from the depth buffer, across the Sun as it looks.
            float discPx = max(intBitsToFloat(global.lighting.lpPad1), 0.0);
            float whitePx = rsPsfCore * sqrt(max(pow(max(peak / rsMaxOutput, 1.0),
                                                     1.0 / rsPsfBeta) - 1.0, 0.0));
            float looksPx = discPx + whitePx;
            float sunDepth = clip.z / clip.w;
            int samples = 0, hidden = 0;
            for (int ring = 0; ring < 3; ring++)
            {
                int count = ring == 0 ? 1 : (ring == 1 ? 4 : 8);
                float radius = ring == 0 ? 0.0 : (ring == 1 ? 0.5 : 0.92);
                for (int i = 0; i < count; i++)
                {
                    float a = 6.28318531 * (float(i) + 0.5 * radius) / float(count);
                    ivec2 at = ivec2(sunPx + radius * looksPx * vec2(cos(a), sin(a)));
                    if (any(lessThan(at, ivec2(0))) || any(greaterThanEqual(at, dim))) continue;
                    samples++;
                    // Reversed depth: nearer is larger, and the Sun is almost at zero.
                    if (texelFetch(samplerDepth, at, 0).r > sunDepth) hidden++;
                }
            }
            float seen = samples > 0 ? 1.0 - float(hidden) / float(samples) : 1.0;
            seen *= rsAirVisibility(sunEgo / dist, dist);
            if (seen <= 0.0) return vec3(0.0);

            vec3 light = global.lighting.sunColor.rgb;
            vec3 tint = light / max(max(light.r, light.g), max(light.b, 1e-6));
            return tint * rsBurst(offset, rsBurstReachPx(peak * seen) * framed) * seen;
        }
        """;

    private const string Tuning = """
        // ---- Real Stars tuning ----
        // Our catalogue stores ABSOLUTE magnitude linearly in the byte: byte 1 is the faint
        // limit and each step is a tenth of a magnitude. make_star_binary.py writes it, along
        // with the star's position in parsecs, and the apparent magnitude follows from the
        // distance to wherever the observer is standing.
        const float rsMagFaint = 16.5;
        const float rsBytesPerMag = 10.0;
        const float rsParsecMetres = 3.0856775814913673e16;
        const float rsStarShell = 19.5;        // the radius the sky is drawn on, as stock does
        // The magnitude that peaks at 1.0 on screen. Lower makes the whole sky brighter.
        // At 5.0 a naked-eye star of magnitude 6 sits at 0.4, faint but present.
        const float rsMagRef = 5.0;
        // Core width of the point spread function in pixels. About a pixel is right for an
        // unresolved source: wider reads as a soft lens, narrower aliases into a hard dot.
        const float rsPsfCore = 1.0;
        // How fast the profile falls away, and so how large a bright source looks. Intensity
        // goes as (1 + (r/core)^2)^-beta, and the radius that clears one display level grows
        // as flux^(1/2beta): at beta = 2 that is flux^0.25, which spread a 12 magnitude range
        // over 13x in size and made a bright moon's glint wider than the planet it orbits.
        // Real seeing-limited profiles sit between about 2.5 and 4.5, and this is the compact
        // end of that, holding the same range to 4.2x. It trades halo for tightness: steeper
        // is smaller but harder edged, so this is the knob to move for size against softness.
        // For a uniform change that keeps the shape, use rsPsfCore instead - though below a
        // pixel its core samples unevenly as a star drifts, and faint stars start to shimmer.
        const float rsPsfBeta = 4.5;
        // Overall scale, so a star at the reference magnitude peaks near 1.0. It carries the
        // (beta - 1) normalisation, so changing beta alone alters width and not brightness.
        const float rsBrightness = 0.886;
        // One display level out of 8-bit, the level below which a wing cannot show.
        const float rsDisplayLevels = 255.0;
        // Largest glow a point source may draw, in pixels of radius. The radius grows as the
        // fourth root of flux, which behaves across a real sky - Sirius is 18 px, Venus at its
        // best 40 - but a planet seen from a few million kilometres reaches magnitude -10 and
        // upwards, where the same rule asks for hundreds of pixels. Nothing fainter than
        // magnitude -5 reaches this, so the sky proper is untouched.
        const float rsMaxGlowPx = 40.0;
        const float rsPi = 3.14159265;

        // Highlight rolloff. The size law is calibrated across a real sky, whose brightest star
        // is Sirius at -1.5, and it extrapolates badly far past that: the Sun from Pluto is
        // -18.8, which the raw law renders as 23 px of saturated white before the halo starts,
        // and it reads as a giant star rather than a brilliant point. A camera would handle
        // this with exposure, which this engine does not have, so the excess is compressed
        // instead - a static rolloff where auto exposure would otherwise sit.
        //
        // Nothing in the catalogue is brighter than the knee except the Sun, so the sky itself
        // is untouched. The slope keeps it responsive: the Sun still grows as you approach and
        // shrinks as you leave, just over a range a screen can show.
        const float rsGlareKnee = -6.0;
        const float rsGlareSlope = 0.35;


        float rsCompressMagnitude(float mag)
        {
            return mag < rsGlareKnee ? rsGlareKnee + (mag - rsGlareKnee) * rsGlareSlope : mag;
        }

        // Byte back to flux, through the distance the observer actually is from the star.
        // Pogson: five magnitudes is a factor of a hundred, and an absolute magnitude is what
        // a star would show at ten parsecs.
        float rsFlux(float packedScale, float distancePc)
        {
            float absMag = rsMagFaint - (packedScale * 255.0 - 1.0) / rsBytesPerMag;
            float mag = absMag + 5.0 * log(distancePc / 10.0) * 0.4342944819;
            return pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef));
        }

        // ---- scintillation ----
        // Turbulence rearranges a wavefront on its way down, and a point source' brightness
        // wanders as a result. Three scalings, all consequences rather than choices:
        //   amplitude  sigma^2 goes as (sec z)^(11/6), so sigma goes as airmass^0.92 and
        //              saturates near 1. The zenith value arrives in csPad0 from the C# side,
        //              already carrying the body's air density and the observer's altitude.
        //   speed      the pattern drifts past on the high-altitude wind. It is milliseconds
        //              in truth; what an eye or a frame resolves is the low end, and it slows
        //              towards the horizon as the path lengthens.
        //   colour     refraction is wavelength dependent, so a low star smears into a small
        //              spectrum. Past the angular scale in csPad1 the colours cross different
        //              turbulence and flicker apart, which is a star flashing red and blue.
        const float rsScintFreqHz = 34.0;      // at the zenith; slower low down
        const float rsDispersionArcsec = 0.54; // 400-700nm separation per tan(z), Earth sea level
        const float rsArcsecPerRad = 206265.0;
        // Two knobs of taste, applied on top of the physics. The theory is calibrated for a
        // dark-adapted eye staring at one star; on a screen the full amount reads as a strobe,
        // and the colour separation especially so. Both scale a physically derived result
        // rather than replacing it, so the relative behaviour - deeper and slower and more
        // colourful towards the horizon, gone in space - is untouched.
        const float rsScintScale = 0.55;
        const float rsChromaScale = 0.30;

        float rsHash(vec3 p)
        {
            return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        // Value noise in time, smooth, one seed per star.
        float rsFlicker(float t, float seed)
        {
            float i = floor(t), f = fract(t);
            f = f * f * (3.0 - 2.0 * f);
            return mix(rsHash(vec3(i, seed, 0.0)), rsHash(vec3(i + 1.0, seed, 0.0)), f) * 2.0 - 1.0;
        }

        // Three octaves, scaled to about unit variance so sigma means what it says. Two was
        // enough to move a star but not to look like air: the fundamental dominated, so every
        // excursion went most of the way and it read as blinking rather than shimmering. The
        // extra octave fills in the small, partial fluctuations between the big ones.
        float rsNoise(float t, float seed)
        {
            return (rsFlicker(t, seed)
                  + 0.55 * rsFlicker(t * 2.17 + 13.0, seed)
                  + 0.30 * rsFlicker(t * 4.61 + 71.0, seed)) * 1.55;
        }

        // Per-channel intensity multipliers. Log-normal, which is what weak scintillation
        // actually follows, and the -sigma^2/2 keeps the MEAN brightness unchanged: a star
        // twinkles without getting brighter or fainter on average.
        vec3 rsScintillation(vec3 starDir, float sourceRadians, float time)
        {
            float sigmaZenith = global.lighting.csPad0;
            float thetaC = global.lighting.csPad1;
            if (sigmaZenith <= 0.0 || thetaC <= 0.0) return vec3(1.0);

            // Straight up, from the camera's place above the nearby body.
            vec3 up = -normalize(global.lighting.planetPosition.xyz);
            float cosZ = dot(up, starDir);
            if (cosZ <= 0.0) return vec3(1.0);                  // below the horizon

            float zDeg = degrees(acos(clamp(cosZ, -1.0, 1.0)));
            float airmass = 1.0 / (cosZ + 0.50572 * pow(max(96.07995 - zDeg, 0.1), -1.6364));

            // A source wider than the pattern's own scale averages its own twinkle away.
            float sizeRatio = sourceRadians / thetaC;
            float suppression = pow(1.0 + sizeRatio * sizeRatio, -7.0 / 12.0);

            float sigma = min(sigmaZenith * pow(airmass, 0.92), 1.0) * suppression * rsScintScale;
            if (sigma < 1e-3) return vec3(1.0);

            float seed = rsHash(starDir * 811.7);

            // Simulation seconds, arriving as float bits in a spare int, so the air moves with
            // the universe: still when paused, quicker under time warp. The `time` argument is
            // only the fallback for when there is no simulation clock to read.
            float simTime = intBitsToFloat(global.lighting.lpPad0);
            float frequency = rsScintFreqHz / sqrt(airmass);
            float t = (simTime != 0.0 ? simTime : time) * frequency;

            // Warp far enough and the flicker outruns the frame. A frame spanning many
            // fluctuations averages them, exactly as a long exposure is steadier than the eye,
            // so time warp settles into stillness rather than dissolving into static.
            float perFrame = frequency * max(global.camera.simSpeed, 0.0)
                           * max(global.camera.deltaTime, 1e-5);
            if (perFrame > 1.0) sigma /= sqrt(perFrame);

            // Colour separation, as a fraction of the correlation scale: nil overhead, total
            // by ten degrees up, which is exactly when a bright star starts flashing colours.
            float dispersion = rsDispersionArcsec * (sigmaZenith / 0.25) * tan(radians(min(zDeg, 89.0)));
            float decorrelate = clamp(dispersion / (thetaC * rsArcsecPerRad), 0.0, 1.0) * rsChromaScale;

            vec3 n = vec3(rsNoise(t + decorrelate * 0.7, seed),
                          rsNoise(t, seed),
                          rsNoise(t - decorrelate * 0.7, seed));
            return exp(sigma * n - 0.5 * sigma * sigma);
        }

        // ---- the observer's starburst ----
        // What a bright point does to an EYE rather than to a camera. A lens stopped down by
        // N straight blades gives N spikes, or 2N when N is odd: evenly spaced, all one
        // length, all one brightness. That is the look the stock burst has, and this replaces
        // it.
        //
        // An eye has no blades. Its lens is grown from fibres that meet along suture lines - a
        // Y at the front, an inverted Y at the back, branching further with age - and those,
        // with the tear film and the edge of the iris, leave many fine rays of unequal length
        // and brightness at irregular angles. They differ between people, and between one
        // person's two eyes.
        //
        // So the pattern is a constant rather than something hashed per source: it belongs to
        // whoever is looking, not to what is being looked at. Every light in the frame shows
        // the same one, fixed in the screen, and only its size changes - with brightness, the
        // way the halo already does. eye_rays.py generated the table and says where it is from.
        //
        // A ray is a line rather than a fan: its width is constant in pixels, which is why a
        // spike reads as a needle, and it fades from its base to its tip rather than falling
        // away as a point source's halo does. There are many of them and each is faint, which
        // is the difference between an eye's burst and the four hard spikes of a lens.
        const int rsRayCount = 84;
        // xy: direction, so the shader needs no trig. z: reach, as a fraction of the
        // burst's length. w: amplitude, as a fraction of the brightest ray. eye_rays.py
        // generated these and says where they come from.
        const vec4 rsRays[rsRayCount] = vec4[rsRayCount](
            vec4( 0.99980,  0.02023, 0.815, 0.574),
            vec4( 0.99595,  0.08987, 0.721, 0.684),
            vec4( 0.98977,  0.14267, 0.455, 0.735),
            vec4( 0.97348,  0.22876, 0.580, 0.399),
            vec4( 0.95404,  0.29969, 0.256, 0.634),
            vec4( 0.92526,  0.37932, 0.947, 0.731),
            vec4( 0.89673,  0.44257, 0.300, 0.772),
            vec4( 0.86662,  0.49897, 0.373, 0.720),
            vec4( 0.82437,  0.56606, 0.330, 0.981),
            vec4( 0.78751,  0.61630, 0.270, 0.312),
            vec4( 0.72385,  0.68996, 0.830, 0.981),
            vec4( 0.68971,  0.72408, 0.891, 0.541),
            vec4( 0.61283,  0.79022, 0.756, 0.472),
            vec4( 0.56880,  0.82247, 0.752, 0.195),
            vec4( 0.50408,  0.86366, 0.707, 0.431),
            vec4( 0.43636,  0.89977, 0.749, 0.832),
            vec4( 0.38198,  0.92417, 0.963, 0.717),
            vec4( 0.28271,  0.95921, 0.851, 0.277),
            vec4( 0.20352,  0.97907, 0.589, 0.245),
            vec4( 0.15559,  0.98782, 0.723, 0.674),
            vec4( 0.05445,  0.99852, 0.859, 0.686),
            vec4(-0.01277,  0.99992, 0.463, 0.568),
            vec4(-0.08499,  0.99638, 0.787, 0.335),
            vec4(-0.13155,  0.99131, 0.419, 0.401),
            vec4(-0.24009,  0.97075, 0.643, 0.407),
            vec4(-0.28177,  0.95948, 0.485, 0.421),
            vec4(-0.37603,  0.92661, 1.000, 0.995),
            vec4(-0.41822,  0.90835, 0.248, 0.726),
            vec4(-0.50299,  0.86429, 0.423, 0.888),
            vec4(-0.57424,  0.81869, 0.571, 0.365),
            vec4(-0.61990,  0.78468, 0.633, 0.216),
            vec4(-0.69066,  0.72318, 0.927, 0.440),
            vec4(-0.72290,  0.69096, 0.379, 0.710),
            vec4(-0.79355,  0.60850, 0.895, 0.951),
            vec4(-0.83487,  0.55044, 0.399, 0.706),
            vec4(-0.87314,  0.48747, 0.548, 0.629),
            vec4(-0.90643,  0.42235, 0.273, 0.327),
            vec4(-0.93123,  0.36444, 0.518, 0.475),
            vec4(-0.95266,  0.30402, 0.988, 0.789),
            vec4(-0.97669,  0.21467, 0.861, 0.441),
            vec4(-0.98783,  0.15553, 0.871, 0.459),
            vec4(-0.99752,  0.07041, 0.510, 0.756),
            vec4(-1.00000, -0.00093, 0.712, 0.460),
            vec4(-0.99571, -0.09255, 0.366, 0.523),
            vec4(-0.98994, -0.14146, 0.607, 0.695),
            vec4(-0.97289, -0.23128, 0.618, 0.379),
            vec4(-0.95883, -0.28399, 0.224, 0.324),
            vec4(-0.93802, -0.34658, 0.816, 0.875),
            vec4(-0.89205, -0.45193, 0.949, 0.299),
            vec4(-0.87526, -0.48364, 0.388, 0.486),
            vec4(-0.82797, -0.56078, 0.749, 0.307),
            vec4(-0.78164, -0.62373, 0.454, 0.548),
            vec4(-0.73105, -0.68232, 0.827, 0.970),
            vec4(-0.69137, -0.72250, 0.731, 0.757),
            vec4(-0.62082, -0.78395, 0.996, 0.749),
            vec4(-0.57374, -0.81904, 0.289, 0.501),
            vec4(-0.51116, -0.85949, 0.855, 0.721),
            vec4(-0.41661, -0.90909, 0.896, 0.384),
            vec4(-0.38396, -0.92335, 0.643, 0.306),
            vec4(-0.28898, -0.95733, 0.353, 0.797),
            vec4(-0.23680, -0.97156, 0.738, 0.939),
            vec4(-0.15946, -0.98720, 0.740, 0.364),
            vec4(-0.08979, -0.99596, 0.934, 0.741),
            vec4(-0.00454, -0.99999, 0.782, 0.185),
            vec4( 0.09285, -0.99568, 0.455, 0.234),
            vec4( 0.13676, -0.99060, 0.311, 0.757),
            vec4( 0.20475, -0.97881, 0.838, 0.742),
            vec4( 0.30707, -0.95169, 0.828, 0.716),
            vec4( 0.34922, -0.93704, 0.887, 0.290),
            vec4( 0.42625, -0.90461, 0.573, 0.911),
            vec4( 0.51194, -0.85902, 0.225, 0.669),
            vec4( 0.55490, -0.83192, 0.911, 0.234),
            vec4( 0.61898, -0.78541, 0.556, 0.601),
            vec4( 0.67411, -0.73863, 0.984, 0.524),
            vec4( 0.73729, -0.67558, 0.245, 0.614),
            vec4( 0.78589, -0.61837, 0.783, 0.372),
            vec4( 0.82750, -0.56146, 0.892, 0.815),
            vec4( 0.85671, -0.51579, 0.628, 0.493),
            vec4( 0.89716, -0.44170, 0.322, 0.734),
            vec4( 0.93217, -0.36203, 0.901, 0.886),
            vec4( 0.95514, -0.29616, 0.706, 0.714),
            vec4( 0.97494, -0.22245, 0.676, 0.346),
            vec4( 0.98895, -0.14822, 0.429, 0.601),
            vec4( 0.99708, -0.07642, 0.967, 0.366)
        );
        const float rsRayWidthPx = 0.42;    // across a ray: about a pixel of visible width
        const float rsRayLevel = 0.13;      // how bright a ray is drawn, in display units
        const float rsRayTaper = 1.6;       // and how it falls from base to tip, as taper^this
        //
        // Where rays begin at all. The scatter that makes them is always there, but it is a
        // thin thing spread over the retina and it only lifts out of the noise for a source
        // that is overwhelmingly brighter than everything around it. That is why the Sun
        // bursts and Sirius does not, and why a sky of stars should read as points: an eye
        // adapted to the night sees Vega as a point, and the same eye in daylight cannot see
        // Vega at all. Below magnitude -3 nothing in the sky qualifies except Venus, which is
        // about where a burst stops being reported by anyone looking up.
        //
        // The engine has no adaptation, so this stands in for the part of it that matters
        // here: a fixed line, past which rays grow with how far past it a source is.
        const float rsBurstMag = -3.0;
        const float rsRayPxPerMag = 12.0;   // of reach, for each magnitude past that line
        const float rsMaxRayPx = 140.0;     // the Sun alone comes near this
        // How sharply a burst leaves when its source leaves the frame. Over its own reach is
        // far too slow: a hundred and forty pixels of fade is a hundred and forty pixels of
        // rays still streaming in from a source that went several frames ago.
        const float rsBurstEdgePx = 10.0;

        // Blur and colour, which are one thing rather than two. An eye does not bring every
        // wavelength to a focus in the same plane - across the visible range the difference is
        // about two dioptres, blue in front of the retina and red behind it - so a ray is at
        // its sharpest in the middle of the spectrum and spread either side of it, most at the
        // blue end. Each channel gets its own width across the ray and its own reach along it,
        // which leaves a ray white down its middle, cool along its soft edges and warm at its
        // tip, and leaves the burst as a whole the colour of whatever threw it. Aberration
        // moves light about; it does not repaint the source.
        const vec3 rsRayChroma = vec3(0.88, 1.0, 1.18);  // relative defocus, R G B
        const float rsRayBlur = 0.45;       // weight of the soft skirt beside the sharp core
        const float rsRayBlurScale = 3.2;   // and how much wider that skirt is

        // The game's own lens flare setting, carried into these shaders - which cannot see the
        // flare buffer - through the camera UBO's spare word. Starburst.cs writes it: the
        // toggle and the intensity together, and zero when either of them says no.
        float rsBurstStrength()
        {
            return max(intBitsToFloat(global.camera.pad0), 0.0);
        }

        // How far the burst reaches: nothing at all until a source is past rsBurstMag, then
        // so many pixels for every magnitude beyond it. Zero means no burst, and no quad grown
        // to hold one, which is what the whole catalogue gets bar the Sun.
        float rsBurstReachPx(float peak)
        {
            // The peak a source on the line reaches, worked out from the same profile as
            // everything else so the two cannot drift apart.
            float onTheLine = pow(10.0, -0.4 * (rsBurstMag - rsMagRef))
                            * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float mags = 2.5 * log(max(peak, 1e-9) / onTheLine) * 0.4342944819;
            return clamp(mags * rsRayPxPerMag * rsBurstStrength(), 0.0, rsMaxRayPx);
        }

        // The burst at an offset from the source, in screen pixels.
        //
        // A ray's LENGTH comes from the source, through rsBurstReachPx above: a brighter
        // light throws its rays further, which is the whole of how a burst reads as bright.
        // A ray's BRIGHTNESS does not. Scattered light inside an eye is a thin thing spread
        // over the retina, and against a source that has already saturated whatever is
        // looking at it, a ray is faint whoever it belongs to: Venus and Sirius differ in the
        // reach of their rays, not in how hard they glare. So the level is in display units
        // rather than a fraction of a peak that would saturate them all alike.
        vec3 rsBurst(vec2 offsetPx, float reachPx)
        {
            if (reachPx <= 0.0) return vec3(0.0);
            vec3 width = rsRayWidthPx * rsRayChroma;
            vec3 skirt = width * rsRayBlurScale;
            float cutoff = 4.0 * skirt.b;
            vec3 total = vec3(0.0);
            for (int k = 0; k < rsRayCount; k++)
            {
                vec4 ray = rsRays[k];
                // Across the ray first: at four sigma of the widest channel there is nothing
                // left to add, and with this many rays nearly all of them are dismissed on two
                // multiplies and a compare.
                float across = offsetPx.y * ray.x - offsetPx.x * ray.y;
                if (abs(across) > cutoff) continue;
                float along = dot(offsetPx, ray.xy);
                if (along <= 0.0) continue;             // one sided, so lengths can differ
                vec3 taper = max(1.0 - along / max(reachPx * ray.z / rsRayChroma, vec3(1e-3)),
                                 vec3(0.0));
                float a2 = across * across;
                // Matched at the axis rather than in total, so the channels differ in
                // shape and not in how much of each there is: white down the middle.
                vec3 profile = (exp(-0.5 * a2 / (width * width))
                                + rsRayBlur * exp(-0.5 * a2 / (skirt * skirt)))
                             / (1.0 + rsRayBlur);
                total += ray.w * pow(taper, vec3(rsRayTaper)) * profile;
            }
            return total * rsRayLevel * rsBurstStrength();
        }

        // ---- what is in the way ----
        // A sprite is a billboard standing where its source is, so the depth test hides only
        // the part of it a body actually covers. A star behind a planet's limb keeps whatever
        // rays reach past that limb, radiating out of nothing, and the halo does the same. The
        // engine's own bloom answers this by measuring how much of the sun is visible and
        // fading everything by it; this is that, without a depth buffer, which these shaders
        // are not handed.
        //
        // The bodies in the celestial block are the ones that can do the hiding: the nearby
        // parent and its children, positions and radii in metres about the camera, which is
        // exactly how the engine's own shadow code reads them. It covers the case that
        // matters - a source going behind the world you are at - and on the surface it IS the
        // horizon, since the camera sits at the body's own radius, the limb is ninety degrees,
        // and everything below it is gone.
        //
        // The fade runs over softRad, so a point source goes in a pixel and a resolved one
        // over its own disc: an eclipse then dims exactly as the disc is covered.
        // How much of a source the air above a body takes, which is a different question
        // from what its rock takes and answers a good deal higher up. At Titan the haze is
        // already opaque tens of kilometres above ground that cannot be seen at all, so a
        // source behind the haze is gone whatever the surface is doing.
        //
        // The engine's composite extincts the pixels it draws, which is why a core and its
        // halo fade correctly through a limb - but a ray reaching past that limb is a pixel
        // looking at clear sky, and it survived. This fades the source by what the SOURCE is
        // looking through, the same way the geometry did.
        //
        // The two altitudes come from LimbAir.cs, in metres above the surface, because they
        // cannot be got at from here: they depend on the density at the surface and the scale
        // height, and what the shaders are handed is the atmosphere's VISUAL height, which is
        // sized to look right and says nothing about how far up it is opaque. Taking fractions
        // of that was the first attempt; Titan came out right and Earth, Mars and Venus came
        // out far too early, which is the giveaway. Mars has a visual atmosphere and almost no
        // extinction at all.
        float rsAirVisibility(vec3 dirToSource, float sourceDistM)
        {
            float opaque = intBitsToFloat(global.celestial.pad0);
            float clear = intBitsToFloat(global.celestial.pad1);
            if (!(clear > opaque)) return 1.0;      // no air, or none worth the name
            vec3 centre = global.lighting.planetPosition.xyz;
            float d = length(centre);
            if (d < 1.0 || d >= sourceDistM * 0.9999) return 1.0;
            float cosSep = dot(centre / d, dirToSource);
            if (cosSep <= 0.0) return 1.0;          // the body is behind us
            // How high the ray grazes: its distance from the centre, less the surface.
            float graze = d * sqrt(max(1.0 - cosSep * cosSep, 0.0)) - global.lighting.planetRadius;
            return smoothstep(opaque, clear, graze);
        }

        float rsVisibility(vec3 dirToSource, float sourceDistM, float softRad)
        {
            float vis = 1.0;
            for (int i = 0; i < global.celestial.bodyCount; i++)
            {
                vec4 body = global.celestial.bodies[i];
                float d = length(body.xyz);
                // Nothing at the camera, and nothing at or past the source - which is what
                // keeps a body from hiding itself, since it is in this list too.
                if (d < 1.0 || d >= sourceDistM * 0.9999) continue;
                float cosSep = dot(body.xyz / d, dirToSource);
                if (cosSep <= 0.0) continue;            // behind us
                float sep = acos(clamp(cosSep, -1.0, 1.0));
                float limb = asin(clamp(body.w / d, 0.0, 1.0));
                // A source is covered over its OWN angular size: a star is a point and goes
                // out at once, which is what a real occultation does, while the Sun takes the
                // width of its disc and an eclipse dims as the disc is eaten. The floor is
                // only there to keep it off a single frame - at a fiftieth of what it was,
                // because a hundredth of a horizon's limb is three quarters of a degree, half
                // again the Sun's own diameter, and the Sun was going out before it had begun
                // to set. A five hundredth is a tenth of a degree, still tens of seconds to
                // cross, and small enough that anything resolved sets on its own size.
                float soft = max(softRad, limb * 0.002);
                vis = min(vis, smoothstep(limb - soft, limb + soft, sep));
                if (vis <= 0.0) return 0.0;
            }
            return min(vis, rsAirVisibility(dirToSource, sourceDistM));
        }
        """;

    public const string Vert = $$"""
        // Real Stars: patched copy of the stock shader.
        #version 450

        #include "Common/Shared.glsl"
        #include "Common/Camera.glsl"

        // Instance input
        layout (location = 0) in vec3 position;
        layout (location = 1) in uint packed;

        // Output
        layout (location = 0) out vec3 outColor;
        layout (location = 1) out vec2 outUv;
        // x = flux, y = sprite radius in px, z = the source's own disc radius in px (0 for a
        // point source, which is everything except the Sun seen from close by), w = how far the
        // starburst reaches, also in px, and zero when there is not one
        layout (location = 2) out vec4 outStar;
        // How much of the source is left to see. The rays are scatter of the light that gets
        // in, so their brightness goes with it; their reach alone falls by magnitude, far too
        // slowly, and they outlived the source.
        layout (location = 3) out float outBurstLevel;

        {{Tuning}}
        const float rsMinGlowPx = 1.5;            // a faint star must still cover a pixel

        vec2 uv[] =
        {
            vec2(0.5, 0.5),         // Center point
            vec2(1.0, 0.5),         // Point 1 (angle 0 degrees)
            vec2(0.854, 0.854),     // Point 2 (angle 45 degrees)
            vec2(0.5, 1.0),         // Point 3 (angle 90 degrees)
            vec2(0.146, 0.854),     // Point 4 (angle 135 degrees)
            vec2(0.0, 0.5),         // Point 5 (angle 180 degrees)
            vec2(0.146, 0.146),     // Point 6 (angle 225 degrees)
            vec2(0.5, 0.0),         // Point 7 (angle 270 degrees)
            vec2(0.854, 0.146),     // Point 8 (angle 315 degrees)
            vec2(1.0, 0.5),         // Final point to close the octagon
        };

        void main()
        {
            // Unpack the instance data
            vec4 packedData = unpackRGBA(packed);
            outColor = packedData.xyz;

            // The instance carries the star's true position in parsecs rather than a direction,
            // so both where it appears and how bright it looks follow from where the observer
            // is. Move, and near stars slide against far ones: parallax, for free, from the
            // camera position the engine already publishes.
            vec3 toStar = position - global.camera.cameraPosition.xyz / rsParsecMetres;
            float distancePc = max(length(toStar), 1e-6);
            vec3 starDir = toStar / distancePc;

            // The Sun is the one star sitting at the origin, and it is drawn here at every
            // distance. There is no handover: the engine's own sprite is switched off entirely,
            // because any threshold for swapping between them is a threshold in pixels, and a
            // pixel is a different distance at every field of view. The sphere still renders
            // while the Sun is resolved, with this glare around it.
            float flux = rsFlux(packedData.w, distancePc);

            // The Sun's own angular size, in pixels: it is a resolved disc, and both the
            // profile below and the scintillation above need to know that.
            float discPx = dot(position, position) < 1e-12
                         ? max(intBitsToFloat(global.lighting.lpPad1), 0.0) : 0.0;

            // Twinkle. A star is unresolved, so it gets the full effect; the brightness and
            // the colour move together, which is why the size is computed from the flickered
            // flux and the colour carries only what is left over: the chroma.
            // The size suppression needs the source's angular radius, and the Sun has a real
            // one: about a thousand arcseconds from Earth against the 1.5 arcsec scale of the
            // turbulence. Passing zero here is why the Sun shimmered through the atmosphere
            // like a point source, which it is the furthest thing from.
            //
            // It comes straight out of the lighting data rather than back out of a pixel
            // count, because a pixel count needs projection[1][1], and that is NEGATIVE here -
            // the engine's own shaders take abs() of it and call the result oneOverTanHalfFov.
            // Dividing by it unguarded left the Sun with an angular radius of several radians.
            float sunAngularRad = discPx > 0.0
                ? global.lighting.sunRadius / max(length(global.lighting.sunPosition.xyz), 1.0)
                : 0.0;
            vec3 scint = rsScintillation(starDir, sunAngularRad, global.camera.time);
            float scintMean = (scint.r + scint.g + scint.b) / 3.0;
            flux *= scintMean;
            outColor *= scint / max(scintMean, 1e-4);

            // And what is in front of it. Fading the flux rather than the drawn colour means
            // the halo and the rays shrink with it, which is what less light does, and an
            // occulted star leaves nothing sticking out past the limb that hid it.
            // The Sun fades over what it LOOKS like, not what it is. At its brightness the
            // profile stays saturated for four or five pixels past the limb, and nothing inside
            // that ring is any less than white, so to an eye the ring is the disc - at 1 AU the
            // geometric disc is a quarter of the white. Fading over the disc alone put the Sun
            // out behind a limb while most of what looked like it was still in view. The ceiling
            // is the plain one, as the CPU side's matching sum uses: the exposure only lowers it
            // inside a tenth of an AU, where the disc dwarfs the ring anyway.
            float sunLooksRad = sunAngularRad;
            if (discPx > 0.0)
            {
                float fullPeak = flux * rsBrightness * (rsPsfBeta - 1.0)
                               / (rsPi * rsPsfCore * rsPsfCore);
                float whitePx = rsPsfCore * sqrt(max(pow(max(fullPeak / {{MaxOutput}}, 1.0),
                                                         1.0 / rsPsfBeta) - 1.0, 0.0));
                sunLooksRad = sunAngularRad * (discPx + whitePx) / discPx;
            }
            float seen = rsVisibility(starDir, distancePc * rsParsecMetres, sunLooksRad);

            // Vessels too, for the Sun. They are nowhere a shader can reach, so VesselOcclusion
            // casts the engine's own part raycasts across the Sun's disc on the CPU and leaves
            // the share it found hidden in the celestial block's last spare word. Zero, which is
            // also what the engine writes there, means nothing in the way.
            if (discPx > 0.0)
                seen *= 1.0 - clamp(intBitsToFloat(global.celestial.pad2), 0.0, 1.0);
            flux *= seen;

            // Moffat beta = 2 at unit energy peaks at 1/(pi*core^2), so the profile crosses
            // one display level at this radius. Sizing the quad to it means the sprite is
            // exactly the star's visible extent and never a disc with a hard edge.
            float peak = flux * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float glowPx = clamp(
                rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels, 1.0 / rsPsfBeta) - 1.0, 0.0)),
                rsMinGlowPx, rsMaxGlowPx);

            // A resolved source is its disc convolved with the profile, so the halo starts at
            // the limb rather than at the centre. As the disc falls below a pixel this becomes
            // an ordinary point source on its own, with no threshold to cross - which is what
            // stops the Sun snapping to a smaller size when the sphere stops being drawn.
            // The burst is sized by the same rule and reaches further than the halo for
            // anything bright enough to have one, so the quad takes whichever wins.
            // The Sun's burst is not drawn here at all: it goes over the finished image, in the
            // merge pass, because it is an optic effect and belongs on top of the world.
            float burstPx = discPx > 0.0 ? 0.0 : rsBurstReachPx(peak);

            // Drawn on the same shell the game uses, but along the direction just computed.
            vec4 worldPosition = global.camera.viewProjection * vec4(starDir * rsStarShell, 1);

            // Off the edge of the frame is a kind of occlusion as well: light that never
            // entered has no business leaving scatter behind. The halo is small enough to see
            // itself out, which is why the glare already behaves. The magnitude of w keeps
            // anything behind the camera outside rather than mirrored into view.
            vec2 ndc = worldPosition.xy / max(abs(worldPosition.w), 1e-6);
            vec2 pastEdge = 0.5 * vec2(global.camera.screenWidth, global.camera.screenHeight)
                          * (abs(ndc) - vec2(1.0));

            // The quad keeps the size the UNFADED burst asked for, so that the halo - which is
            // taken to nothing over the last fraction of the quad - renders the same whatever
            // the rays are doing. Tying the two together made the glare change as the burst
            // went, which is not a thing the glare should know about.
            float spritePx = discPx + max(glowPx, burstPx);
            burstPx *= 1.0 - smoothstep(-rsBurstEdgePx, rsBurstEdgePx,
                                        max(pastEdge.x, pastEdge.y));

            outUv = uv[gl_VertexIndex];
            outStar = vec4(flux, spritePx, discPx, burstPx);
            outBurstLevel = seen;

            // Pixels to clip units. The offset is added in clip space, so it is divided by w
            // downstream, and x spans the screen width over 2 NDC units; the aspect factor on
            // y then keeps the quad square on screen, as stock does.
            float pxToClip = 2.0 * worldPosition.w / max(global.camera.screenWidth, 1.0);
            vec2 vertPosition = (outUv - vec2(0.5)) * (2.0 * spritePx * pxToClip);

            vec4 screenOffset = vec4(vertPosition.x, vertPosition.y * global.camera.aspectRatio, 1.0f, 0);
            gl_Position = worldPosition + screenOffset;
        }
        """;

    public const string Frag = $$"""
        // Real Stars: patched copy of the stock shader.
        #version 450

        #include "Common/Global.glsl"

        layout (location = 0) in vec3 inColor;
        layout (location = 1) in vec2 inUv;
        layout (location = 2) in vec4 inStar;     // flux, sprite px, disc px, burst reach px
        layout (location = 3) in float inBurstLevel; // how much of the source is left to see

        layout (location = 0) out vec4 outColor;

        {{Tuning}}
        // HDR headroom, shared with the Sun's sphere. See StarShaders.MaxOutput.
        const float rsMaxOutput = {{MaxOutput}};

        void main(void)
        {
            float r = length(inUv.xy - vec2(0.5f)) * 2.0f;   // 0 at the centre, 1 at the quad's edge
            // Pixels from the centre, then from the LIMB: a resolved source is its disc
            // convolved with the profile, so the halo begins at the edge of the disc. For a
            // point source the disc radius is zero and this is the ordinary profile.
            float px = r * inStar.y;
            float x = max(px - inStar.z, 0.0) / rsPsfCore;

            // Moffat, normalised to unit energy:
            //     I(r) = (beta-1)/(pi a^2) * (1 + (r/a)^2)^-beta
            // Falling wings rather than a hard edge are what diffraction and seeing both leave,
            // and they are why a bright star reads as a point with a halo rather than a disc.
            float psf = (rsPsfBeta - 1.0f)
                      / (rsPi * rsPsfCore * rsPsfCore * pow(1.0f + x * x, rsPsfBeta));

            float intensity = inStar.x * rsBrightness * psf;

            // Take the last of it to zero before the quad's edge, so the sprite never shows.
            float edgeFade = smoothstep(1.0f, 0.85f, r);
            intensity *= edgeFade;

            // The Sun gives brightness back as it fills the frame - see SunExposureKnee.
            // inStar.z is the disc radius, and is zero for everything else in the catalogue,
            // so every other star keeps the plain ceiling.
            float cap = inStar.z > 0.0 ? {{SunLevelGlsl}} : rsMaxOutput;

            // The rays go beside the core rather than into it: they are faint by construction,
            // and running them through a ceiling meant for a saturating point would only take
            // the colour back out of them. How far they reach was settled in the vertex
            // shader, where the quad was sized to hold them.
            vec3 burst = rsBurst((inUv - vec2(0.5f)) * 2.0f * inStar.y, inStar.w)
                       * edgeFade * inBurstLevel;

            outColor = vec4(inColor.rgb * (vec3(min(intensity, cap)) + burst), 1.0f);
        }
        """;

    // ---------------------------------------------------------------------------------
    // Distant planets. A planet too far to resolve is a point source like any star, and it
    // should sit on the same brightness scale: Venus at magnitude -4.6 outshining everything,
    // Neptune at +7.8 a dot you have to look for. Stock instead sizes the sprite by the
    // body's RADIUS (see PlanetPhotometry), which inverts that ordering.
    //
    // PlanetPhotometry feeds the real apparent magnitude through scalePixel as a glow radius
    // in pixels, the same quantity the star vertex shader computes, and these two shaders
    // then draw it with the same profile - so a planet and a star of equal magnitude are
    // indistinguishable, which is exactly what a telescope shows.
    // ---------------------------------------------------------------------------------

    public const string PlanetVert = $$"""
        // Real Stars: patched copy of the stock shader.
        #version 450

        #include "Common/Shared.glsl"
        #include "Common/Camera.glsl"
        #include "Common/Global.glsl"

        struct InstanceData
        {
            vec3 positionEgo;
            vec3 color;
            float scalePixel;
        };

        layout(std430, set = 1, binding = 0) readonly buffer InstanceStorageBlock
        {
            InstanceData Data[];
        } InstanceStorage;

        layout (location = 0) out flat vec3 outColor;
        layout (location = 1) out float outDepth;
        layout (location = 2) out flat float outScalePixel;
        layout (location = 3) out vec2 outUV;
        // The quad's radius and the burst's reach, both in pixels. The first used to be
        // scalePixel itself; a burst reaches past the halo, so now it need not be.
        layout (location = 4) out flat float outSpritePx;
        layout (location = 5) out flat float outBurstPx;
        layout (location = 6) out flat float outBurstLevel; // what of the body is left to see

        {{Tuning}}

        vec2 uv[] =
        {
            vec2(0.5, 0.5),         // Center point
            vec2(1.0, 0.5),         // Point 1 (angle 0 degrees)
            vec2(0.854, 0.854),     // Point 2 (angle 45 degrees)
            vec2(0.5, 1.0),         // Point 3 (angle 90 degrees)
            vec2(0.146, 0.854),     // Point 4 (angle 135 degrees)
            vec2(0.0, 0.5),         // Point 5 (angle 180 degrees)
            vec2(0.146, 0.146),     // Point 6 (angle 225 degrees)
            vec2(0.5, 0.0),         // Point 7 (angle 270 degrees)
            vec2(0.854, 0.146),     // Point 8 (angle 315 degrees)
            vec2(1.0, 0.5)          // Final point to close the octagon
        };

        void main()
        {
            InstanceData instanceData = InstanceStorage.Data[gl_InstanceIndex];

            vec2 vertPosition = uv[gl_VertexIndex] - vec2(0.5f);

            // scalePixel is the glow RADIUS in pixels, and the profile was sized so that it
            // reaches one display level exactly there - which is what lets the peak be recovered
            // from it here, and in the fragment shader, with no second channel.
            float glowPx = max(instanceData.scalePixel, 1e-3f);
            float edge = 1.0f + (glowPx / rsPsfCore) * (glowPx / rsPsfCore);
            float peak = pow(edge, rsPsfBeta) / rsDisplayLevels;

            // What is in front of it: a moon behind its planet, a planet behind the world you
            // are at. Taking it off the peak and turning that back into a radius keeps the one
            // channel telling the truth, so the fragment shader needs to know nothing about it.
            float dist = length(instanceData.positionEgo);
            float seen = rsVisibility(instanceData.positionEgo / max(dist, 1.0f), dist, 0.0f);
            peak *= seen;
            glowPx = rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels, 1.0f / rsPsfBeta) - 1.0f, 0.0f));

            // Venus asks for a burst, Neptune does not. The quad takes whichever reaches further.
            float burstPx = rsBurstReachPx(peak);

            vec4 clipPosition = global.camera.viewProjection * vec4(instanceData.positionEgo, 1);

            // Off the edge of the frame is occlusion too - see the star shader, which does the
            // same thing for the same reason, and sizes its quad the same way.
            vec2 ndc = clipPosition.xy / max(abs(clipPosition.w), 1e-6f);
            vec2 pastEdge = 0.5f * vec2(global.camera.screenWidth, global.camera.screenHeight)
                          * (abs(ndc) - vec2(1.0f));

            float spritePx = max(glowPx, burstPx);
            burstPx *= 1.0f - smoothstep(-rsBurstEdgePx, rsBurstEdgePx,
                                         max(pastEdge.x, pastEdge.y));

            // The offset is added after the perspective divide, so a pixel is 2/screen in
            // normalised device coordinates, and the quad spans from -radius to +radius across
            // its 1.0 of uv.
            vertPosition.x *= 4.0f * spritePx / global.camera.screenWidth;
            vertPosition.y *= 4.0f * spritePx / global.camera.screenHeight;

            vec4 worldPosition = clipPosition / clipPosition.w;
            vec4 screenOffset = vec4(vertPosition.x, vertPosition.y, 0.0f, 0);

            vec4 depthCalculation = global.camera.viewProjection * vec4(instanceData.positionEgo, 1);
            outDepth = depthCalculation.z / depthCalculation.w;

            gl_Position = worldPosition + screenOffset;
            gl_Position.z = max(0.0, gl_Position.z); // Prevent early frag culling
            outColor = instanceData.color;

            outScalePixel = glowPx;
            outSpritePx = spritePx;
            outBurstPx = burstPx;
            outBurstLevel = seen;
            outUV = uv[gl_VertexIndex];
        }
        """;

    public const string PlanetFrag = $$"""
        // Real Stars: patched copy of the stock shader.
        #version 450

        #include "Common/Global.glsl"

        layout (location = 0) in flat vec3 inColor;
        layout (location = 1) in float inDepth;
        layout (location = 2) in flat float inScalePixel;
        layout (location = 3) in vec2 inUV;
        layout (location = 4) in flat float inSpritePx;
        layout (location = 5) in flat float inBurstPx;
        layout (location = 6) in flat float inBurstLevel;

        layout (location = 0) out vec4 outColor;

        {{Tuning}}
        const float rsMaxOutput = {{MaxOutput}};

        void main(void)
        {
            float r = length(inUV.xy - vec2(0.5f)) * 2.0f;   // 0 at the centre, 1 at the edge
            float glow = max(inScalePixel, 1e-3f);
            // The quad is the halo or the burst, whichever reaches further, so pixels from the
            // centre come from ITS radius and the profile is read at that distance.
            float quadPx = max(inSpritePx, glow);
            float x = (r * quadPx) / rsPsfCore;

            // The sprite was sized so the profile reaches one display level exactly at its
            // edge, so the peak follows from the radius alone - no second channel needed, and
            // it stays in step with the star shader by construction.
            float edge = 1.0f + (glow / rsPsfCore) * (glow / rsPsfCore);
            float peak = pow(edge, rsPsfBeta) / rsDisplayLevels;

            float intensity = peak / pow(1.0f + x * x, rsPsfBeta);
            float edgeFade = smoothstep(1.0f, 0.85f, r);
            intensity *= edgeFade;
            // The same rays, from the same eye, as the stars get - and beside the core for the
            // same reason.
            vec3 burst = rsBurst((inUV - vec2(0.5f)) * 2.0f * quadPx, inBurstPx)
                       * edgeFade * inBurstLevel;

            // The engine has already scaled this colour by its own phase term. Ours is in the
            // magnitude, so take the hue and leave the brightness alone.
            float hue = max(max(inColor.r, inColor.g), inColor.b);
            vec3 tint = hue > 1e-4f ? inColor / hue : vec3(1.0f);

            // The core is clamped, the rays are not - they are already faint, and a ceiling
            // meant for a saturating point would only take the colour back out of them. Alpha
            // follows the brightest channel, so a ray blends in rather than being dropped.
            float brightness = min(intensity, rsMaxOutput);
            vec3 rgb = tint * (vec3(brightness) + burst);
            outColor = vec4(rgb, min(max(max(rgb.r, rgb.g), rgb.b), 1.0f));

            if (brightness >= 1.0f)
            {
                gl_FragDepth = inDepth;
            }
            else
            {
                gl_FragDepth = 0.0;
            }
        }
        """;
}
