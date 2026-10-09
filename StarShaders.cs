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
        { "Sun/Sun.frag", "PostProcess/sunbloom.frag", "PostProcess/sunbloom_merge.comp",
          "PostProcess/kawase_downsample.comp", "PostProcess/kawase_downsample_with_thresholds.comp",
          "PostProcess/composite.frag" };

    /// <summary>
    /// The vessel shaders, served from our tree for one line: the fade that switches planetshine off by 0.0001 AU
    /// (15,000 km) from the planet's centre, whatever the planet, which leaves the giants none at all. BodyColours
    /// sets planetshine to the real irradiance at the camera, which dims with distance by itself until the shaders'
    /// own threshold, 0.01 of the game's light (a thousandth of the Sun's), drops it. None of these includes a file
    /// Real Atmospheres patches, so serving them from here takes nothing from it. All or none: a vessel shader left
    /// fading would light its parts differently from the rest of the vessel.
    /// </summary>
    public static readonly string[] VesselShaders =
    {
        "Mesh/ModelPbr.frag", "Mesh/MeshIndirect.frag", "Mesh/MeshIndirectRaytraced.frag", "Mesh/MeshGlassIndirect.frag",
        "Mesh/MeshGlassIndirectRaytraced.frag", "Mesh/ModelTranslucent.frag", "Mesh/Fur.frag", "Spline.frag",
    };

    public const string PlanetshineFade = "float taperFactor = smoothstep(0.0001, 0.0, distanceToPlanet);";
    public const string PlanetshineUnfaded =
        "float taperFactor = 1.0;   // Real Stars: planetshine is the real irradiance, dimming with distance (BodyColours)";

    /// <summary>
    /// Shaders we change a line of rather than replace. The Sun's surface is a raymarch of
    /// several hundred lines that do their job, and taking custody of all of it to fix a few
    /// numbers would mean maintaining it forever. Each anchor is a whole line, so an update
    /// that touches one leaves the file stock and says so in the log.
    /// </summary>
    public static readonly (string Name, string Find, string Replace)[] Edits =
    {
        // The eye's adaptation to the light it is in, over the whole frame before the tone curve:
        // each cone's signal scaled from the light's raw colour to the white an adapted eye settles
        // on. Adaptation writes the three scales into spare floats of this pass's own push constants
        // every frame; until it has, they are zero and the frame is left as it is.
        ("PostProcess/composite.frag",
         "    color = tonemapSwitch(tonemapperIndex, color, tonemapExposure, tonemapGamma, hableTonemapUniforms);",
         "    // Real Stars: the eye adapted to the light it is in (Adaptation.cs)\n"
         + "    vec3 rsAdapt = vec3(hableTonemapUniforms.segments[0].padding0, hableTonemapUniforms.segments[0].padding1,\n"
         + "                        hableTonemapUniforms.segments[1].padding0);\n"
         + "    if (rsAdapt.x > 0.0)\n"
         + "    {\n"
         + "        const mat3 rsToCone = " + Adaptation.ToConeGlsl + ";\n"
         + "        const mat3 rsFromCone = " + Adaptation.FromConeGlsl + ";\n"
         + "        color = max(rsFromCone * (rsAdapt * (rsToCone * color)), vec3(0.0));\n"
         + "    }\n"
         + "    color = tonemapSwitch(tonemapperIndex, color, tonemapExposure, tonemapGamma, hableTonemapUniforms);"),

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
         + "    // And only ahead of the camera. The sight line is the same through either face of the\n"
         + "    // mesh, and the far faces are the ones drawn (SunSphere), so that the star is there from\n"
         + "    // inside the mesh as well; but from inside, a line the other way meets the star behind\n"
         + "    // the camera.\n"
         + "    if (dot(centerPos, ray_direction) <= 0. && length(centerPos) > sunRadius) alpha = 0.;\n"
         + "\n"
         + "    // The engine draws this mesh below 88 solar radii and not above it, in one\n"
         + "    // step. Our sprite draws the Sun underneath at every distance, at the same\n"
         + "    // angular size and now the same level, so the two carry the same disc - but the\n"
         + "    // sphere has a limb and a darkened edge where the sprite has a halo, and trading\n"
         + "    // one for the other between two frames is the jump that was left. It fades in\n"
         + "    // across the top half of the range instead, and has reached nothing by the time\n"
         + "    // the engine drops it; and the sprite fades out by as much (rsSphereShown, on\n"
         + "    // these same two distances), so only one of them shows. Both distances are read\n"
         + "    // off the radius the lighting carries, the number the engine reads them from.\n"
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
        //
        // And in the star's own colour, as the sprite is: its light at unit luminance. The
        // palette above is white at every temperature near the Sun's, and it is the same for
        // every star, so the sphere of a red dwarf was as white as the Sun's - and, since the
        // frame's adaptation takes the star's raw colour to the eye's white, a little blue
        // (user, 2026-10-06: "Proxima turns white"). The light is the lighting star's, the one
        // this sphere is drawn for, raw as SunLight leaves it.
        ("Sun/Sun.frag",
           "    // Apply bloom\n"
         + "    col.rgb *= 1.3;",
           "    // Real Stars: the level our own sprite draws the star at, so they can be traded, in the\n"
         + "    // star's own colour: its light at unit luminance, as the sprite's is.\n"
         + "    vec3 rsStarLight = max(global.lighting.sunColor.rgb, vec3(0.0));\n"
         + "    float rsStarLuma = dot(rsStarLight, vec3(0.2126, 0.7152, 0.0722));\n"
         + "    col.rgb *= " + SunLevelGlsl + " * (rsStarLuma > 1e-6 ? rsStarLight / rsStarLuma : vec3(1.0));"),

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
        // the Sun is in view. The spokes and ghosts in sunbloom_blur.comp are left alone here:
        // SunGlow sets SunDot to zero, and that gates both.
        // The Sun's starburst is drawn here, over the finished image, rather than with the
        // stars. It is scatter inside whoever is looking, so it lies over everything they are
        // looking at - and drawn with the stars it went UNDER the first thing in front of the
        // Sun, a hull or a ridge or a limb taking every ray that crossed it. This pass runs
        // after the world, writes the final HDR image, skips what is under the UI, and does not
        // include the atmosphere functions, so Real Atmospheres keeps its own.
        ("PostProcess/sunbloom_merge.comp",
         "layout (set = 2, binding = 2) uniform sampler2D bloomColor;",
         "layout (set = 2, binding = 2) uniform sampler2D bloomColor;\n" + MergeSunBurst),
        ("PostProcess/sunbloom_merge.comp",
         "    uvec2 screenCoords = gl_GlobalInvocationID.xy;",
         "    uvec2 screenCoords = gl_GlobalInvocationID.xy;\n"
         + "    // Real Stars: how much of the Sun the frame lets through, and where, gathered by the\n"
         + "    // whole workgroup before any invocation can leave - see rsSunGather.\n"
         + "    rsSunGather(dim);"),
        ("PostProcess/sunbloom_merge.comp",
         "    vec4 finalColor = screenCol + vec4(bloom.rgb, 0.0);",
         "    vec4 finalColor = screenCol + vec4(bloom.rgb, 0.0);\n"
         + "    // Real Stars: the Sun's starburst, over everything, and the Sun held to its own hue\n"
         + "    // where it burns out - see rsSunOver.\n"
         + "    finalColor.rgb = rsSunOver(finalColor.rgb, vec2(screenCoords) + vec2(0.5), dim);"),

        ("PostProcess/sunbloom.frag",
         "    float sunPower = innerSun * innerSunScalar + outerSun * sunData.outerSunColorScalar;",
         "    float sunPower = 0.;  // Real Stars: the Sun is drawn as the star it is"),

        // The engine's two blooms - Global Bloom and Threshold Bloom in the settings - each start
        // with one of these downsamples, and the Sun reached both at 24, burying its needles in
        // a white blob. Each tap now passes BloomSun's rsBloomTap first. The plain downsample is
        // also every later level of both pyramids, where the Sun is already capped and this
        // does nothing. One anchor per file, from the signature through the taps, so an update
        // that changes any of it leaves the file stock rather than half patched.
        ("PostProcess/kawase_downsample.comp",
           "vec3 dualDownSample(vec2 uv, vec2 halfpixel)\n"
         + "{\n"
         + "    // a   b  -- corners of the pixel + 4 times the pixel\n"
         + "    // c   d\n"
         + "    vec3 downsample = textureLod(srcTexture, uv, 0.0).rgb * 4.0;\n"
         + "    downsample += textureLod(srcTexture, uv  - halfpixel, 0.0).rgb;\n"
         + "    downsample += textureLod(srcTexture, uv  + halfpixel, 0.0).rgb;\n"
         + "    downsample += textureLod(srcTexture, uv - vec2(halfpixel.x, - halfpixel.y), 0.0).rgb;\n"
         + "    downsample += textureLod(srcTexture, uv + vec2(halfpixel.x, - halfpixel.y), 0.0).rgb;",
           BloomSun
         + "vec3 dualDownSample(vec2 uv, vec2 halfpixel)\n"
         + "{\n"
         + "    // Real Stars: each tap as the bloom may see it - see rsBloomTap.\n"
         + "    float rsSun = rsBloomSun();\n"
         + "    vec2 rsFlip = vec2(halfpixel.x, -halfpixel.y);\n"
         + "    vec3 downsample = rsBloomTap(textureLod(srcTexture, uv, 0.0).rgb * 4.0, uv, 4.0, rsSun);\n"
         + "    downsample += rsBloomTap(textureLod(srcTexture, uv - halfpixel, 0.0).rgb, uv - halfpixel, 1.0, rsSun);\n"
         + "    downsample += rsBloomTap(textureLod(srcTexture, uv + halfpixel, 0.0).rgb, uv + halfpixel, 1.0, rsSun);\n"
         + "    downsample += rsBloomTap(textureLod(srcTexture, uv - rsFlip, 0.0).rgb, uv - rsFlip, 1.0, rsSun);\n"
         + "    downsample += rsBloomTap(textureLod(srcTexture, uv + rsFlip, 0.0).rgb, uv + rsFlip, 1.0, rsSun);"),
        ("PostProcess/kawase_downsample_with_thresholds.comp",
           "vec3 dualDownSample(vec2 uv, vec2 halfpixel)\n"
         + "{\n"
         + "    // a   b  -- corners of the pixel + 4 times the pixel\n"
         + "    //   e\n"
         + "    // c   d\n"
         + "    vec3 e = textureLod(srcTexture, uv, 0.0).rgb * 4.0;\n"
         + "    vec3 a = textureLod(srcTexture, uv - vec2(halfpixel.x, - halfpixel.y), 0.0).rgb;\n"
         + "    vec3 b = textureLod(srcTexture, uv  + halfpixel, 0.0).rgb;\n"
         + "    vec3 c = textureLod(srcTexture, uv  - halfpixel, 0.0).rgb;\n"
         + "    vec3 d = textureLod(srcTexture, uv + vec2(halfpixel.x, - halfpixel.y), 0.0).rgb;",
           BloomSun
         + "vec3 dualDownSample(vec2 uv, vec2 halfpixel)\n"
         + "{\n"
         + "    // Real Stars: each tap as the bloom may see it, before the threshold - see rsBloomTap.\n"
         + "    float rsSun = rsBloomSun();\n"
         + "    vec2 rsFlip = vec2(halfpixel.x, -halfpixel.y);\n"
         + "    vec3 e = rsBloomTap(textureLod(srcTexture, uv, 0.0).rgb * 4.0, uv, 4.0, rsSun);\n"
         + "    vec3 a = rsBloomTap(textureLod(srcTexture, uv - rsFlip, 0.0).rgb, uv - rsFlip, 1.0, rsSun);\n"
         + "    vec3 b = rsBloomTap(textureLod(srcTexture, uv + halfpixel, 0.0).rgb, uv + halfpixel, 1.0, rsSun);\n"
         + "    vec3 c = rsBloomTap(textureLod(srcTexture, uv - halfpixel, 0.0).rgb, uv - halfpixel, 1.0, rsSun);\n"
         + "    vec3 d = rsBloomTap(textureLod(srcTexture, uv + rsFlip, 0.0).rgb, uv + rsFlip, 1.0, rsSun);"),
    };

    /// <summary>
    /// What the engine's two blooms may see of the Sun, written into their downsamples. Past
    /// white a core shows only through the blooms (see rsBloomWhite), and the Sun's glare is
    /// already ours. The Sun is the one source these passes can find: its place and size are in
    /// the global block, which every compute pass is bound to, read as the merge pass reads
    /// them. Stars and planets hold their own cores to the same level, in their own shaders.
    /// </summary>
    private const string BloomSun = $$"""
        // ---- Real Stars ----
        #include "../Common/Shared.glsl"
        #include "../Common/Camera.glsl"
        {{Tuning}}
        {{SunPlane}}

        // The Sun's white, as the merge pass finds it: its radius in pixels on the Sun's plane (see
        // rsSunOnPlane, which rsBloomSun sets up) - the disc, the ring its profile holds over
        // rsBloomSeen past the limb, and two pixels for a tap's footprint. Zero when none of it can be
        // in view.
        //
        // At the default exposure nothing round the Sun is over rsBloomSeen but that. At a lower
        // one white is higher (rsWhiteScale) and the burnt-out part of the Sun's glare with it,
        // while the blooms' own thresholds stay where they are, and the blooms come after the
        // merge pass: they would lay a second glow round the Sun's. So then the radius takes in
        // the glare as far as it can be over rsBloomSeen: by the glow's own fall, in its reddest
        // band, for a lane three times the mean.
        float rsBloomSun()
        {
            if (!rsSunPlaneSet(vec2(global.camera.screenWidth, global.camera.screenHeight))) return 0.0;
            float dist = rsSunAway;
            float mag = rsLightAbsMag() + 5.0 * log(dist / (10.0 * rsParsecMetres)) * 0.4342944819;
            float peak = pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef))
                       * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float discPx = rsSunDiscPx();
            float whitePx = rsPsfCore * sqrt(max(pow(max(peak / rsBloomSeen, 1.0),
                                                     1.0 / rsPsfBeta) - 1.0, 0.0));
            float glarePx = 0.0;
            if (rsWhiteScale() > 1.0)
                glarePx = min(rsGlareCorePx * sqrt(max(pow(max(3.0 * rsGlareLevel(peak) / rsBloomSeen, 1.0),
                                                           2.0 / rsGlareFall) - 1.0, 0.0))
                              / rsBands[rsBandCount - 1].w, rsMaxRayPx);
            return discPx + max(whitePx, glarePx) + 2.0;
        }

        // One tap of a downsample, carrying `weight` samples, as the bloom may see it: inside the
        // Sun's white, no brighter than rsBloomSeen. What is drawn is untouched.
        vec3 rsBloomTap(vec3 tap, vec2 uv, float weight, float sun)
        {
            if (sun <= 0.0) return tap;
            vec2 px = rsSunOnPlane(uv * vec2(global.camera.screenWidth, global.camera.screenHeight));
            if (dot(px, px) >= sun * sun) return tap;
            float luma = dot(tap, rsLuma);
            float limit = rsBloomSeen * weight;
            return luma > limit ? tap * (limit / luma) : tap;
        }

        """;

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
    /// The brightest value any of our sprites writes, and the value the Sun's sphere is drawn
    /// at too. The Sun's own profile is far past it at every distance the sphere is on screen,
    /// so the sphere matching it is what makes the two interchangeable. The default tonemap is
    /// white from about 1.2, so what the rest buys is headroom for the air: the engine dims the
    /// sky by its transmittance after we draw, and this is what keeps a low Sun white. The
    /// engine's blooms see none of it - see rsBloomWhite and BloomSun.
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
    /// White now over white at the game's default exposure: what every level here that means
    /// "white on the screen" is multiplied by, so that it still does when the player has moved
    /// the exposure (Exposure.cs, which leaves the setting in the session constants' spare word;
    /// zero there is the default). Written out in full because the Sun's sphere reads it too,
    /// and its shader takes one line from here and none of the tuning.
    /// </summary>
    public const string WhiteScaleGlsl =
        "(intBitsToFloat(globalConstants.gtPad0) > 0.0"
        + " ? clamp(1.25 / intBitsToFloat(globalConstants.gtPad0), 0.001, 1000.0) : 1.0)";

    /// <summary>
    /// The level the Sun's disc is drawn at. Written once and read by both the sprite and the
    /// sphere, because the moment they disagree the handover becomes visible again. At the
    /// game's exposure: every figure above is the default's.
    /// </summary>
    public const string SunLevelGlsl =
        "(" + WhiteScaleGlsl + " * clamp(" + MaxOutput + " * pow(" + SunExposureKnee + " / max(" + SunFractionGlsl + ", 1e-6), "
        + SunExposurePower + "), " + SunExposureFloor + ", " + MaxOutput + "))";

    /// <summary>
    /// The Sun's starburst as the merge pass draws it, over the finished image. The tuning comes
    /// with it, so the rays are the same rays the sprites draw - the same eye - and the
    /// occlusion is the finished depth buffer, which is the best answer there is to what is in
    /// front of the Sun.
    /// </summary>
    private const string MergeSunBurst = $$"""

        // ---- Real Stars ----
        {{Tuning}}
        {{SunPlane}}
        float rsMaxOutput = {{MaxOutput}} * rsWhiteScale();    // the Sun's level, at the game's exposure

        // Where the Sun is, what of it is left to see, and where that is, from the finished frame.
        //
        // Being drawn after the world means the depth buffer is complete, and that answers what
        // is in front of the Sun better than anything worked out beforehand: hulls, ridges,
        // moons, any of it, to the pixel. The Sun as it LOOKS - its disc and the white ring the
        // profile saturates past the limb - is sampled at rsSunSamples points on a sunflower
        // spiral, spread evenly over its area. A sample lets the Sun's light through unless
        // something drawn nearer than the Sun covers it, and the air along its own ray dims what it
        // lets through, since air writes no depth. The Sun's own sphere writes none either, so it
        // cannot hide itself. Places are on the Sun's plane (rsSunOnPlane), which is the screen for
        // a Sun far off.
        //
        // THE FRAME'S EDGE COVERS NOTHING. A sample off the frame has no depth to read, and used to
        // count as covered. So a Sun crossing the edge of the screen lost its glare as it went, the
        // glow drew back to the part still in view, and the disc stood bare at the edge (user,
        // 2026-10-08, from Mercury's orbit inward); and low over a star, where a hundredth of the
        // disc is on the screen, there was a hundredth of the glare, or none. But the screen's edge
        // is not in front of the Sun. A sample off the frame is now judged by what can be known of
        // it: the bodies on its sight line, worked out as for a star (rsSunOffFrame), and otherwise
        // open. The glare is then the whole Sun's, round, from its own centre, on or off the screen.
        // It leaves with the Sun: as the last rsSunFrameInPx of the Sun as it looks go over the
        // edge, or the whole of a Sun smaller than that (rsSunFrameFade).
        //
        // Each sample reads the four depth texels round it, weighted by how near it lies to
        // each, so it dims smoothly as an edge crosses it rather than going out at once. And the
        // samples are shared out across the pass's workgroup, four to an invocation, and summed
        // once for all of them, so 256 cost each pixel less than the 32 it used to take alone -
        // whose steps showed at a close Sun as the glare's source jumping when a hull slid over.
        //
        // The glare is the light that got in. It is as strong as what the samples let through,
        // by the same law as distance (rsGlareLevel), and it radiates from where that light is:
        // their centre, weighted by it. A hull across three quarters of the Sun leaves the glare
        // coming from the sliver still in view, and a limb lifts it as the Sun sets into the air -
        // where before it came from a centre that was out of sight in both of those cases. The part
        // left open, as a disc of the same area, is what the glare's glow starts at and widens its
        // needles by.
        const int rsSunSamples = 256;
        const float rsSunFrameInPx = 48.0;          // how far into the frame the Sun reaches before its glare is whole
        const int rsSunGroup = 64;                  // the pass's workgroup, 8 by 8
        // One centre and one round disc stand for the Sun well while what is left of it is near
        // enough round. A large disc with a planet or a hull across it is not: the round stand-in
        // of a 40 pixel disc a third covered reaches 14 pixels over the planet, and laid its glow
        // there (user, 2026-10-06, of the crescent suns of Barnard's Star's close planets).
        //
        // Drawing the Sun as several patches, each with a glare of its own, was tried, and it was
        // plainly several glares (user, 2026-10-07): a patch throws the whole pattern from its
        // own centre, and more patches are only more copies. So it stays ONE glare, from the one
        // centre where the light is, and what changes is where its glow starts: at the outline of
        // what is left of the Sun, in place of the edge of a round disc of the same area. For a
        // whole disc the two are the same thing.
        //
        // The outline is traced by rays from the Sun's centre, rsSunRays of them, two to an
        // invocation. Each is walked out to the limb, in steps that shorten toward it, where a
        // crescent is thin, and the first and the last open point on it are found to a fraction
        // of a pixel by halving. What lies between a ray's two is taken as open, so a strut across
        // the Sun leaves no hole in it: the glare lies over a strut anyway. A pixel's distance
        // from the outline is its distance from the line through those points (rsSunFromShape).
        //
        // The outline is used by how far the open part is from round: how much further its
        // points lie from their middle than a disc of its area would have them, in pixels. That
        // is 0 for a whole disc and about a third of the radius for a half or a crescent, so it
        // follows the size of the miss on the screen: a large disc takes its shape as soon as
        // something crosses it, and the Sun from Earth, 9 pixels with its ring, stays a point
        // however it is covered. Faded in over the range, so nothing steps, and the rays are
        // only traced while it is in use.
        const float rsSunShapeFromPx = 3.0;
        const float rsSunShapeByPx = 6.0;
        const int rsSunRays = 128;
        const int rsSunRaySteps = 16;
        const int rsSunRayHalvings = 5;
        shared vec4 rsSunShare[rsSunGroup];
        shared vec4 rsSunShape[rsSunGroup];
        // A ray: its first and last open point, in pixels from the Sun's centre (-1: none), and
        // its direction.
        shared vec4 rsSunRay[rsSunRays];
        // Where the open part ends between an open ray and a shut one beside it, the end is found
        // too: the turn at which it stops being open, a quarter and three quarters of the way
        // along the open ray's span, toward the next ray (xy) and toward the one before (zw).
        // Left at the open ray itself, an edge running straight out from the Sun's centre, as a
        // half-covered Sun's does, was up to a ray's spacing out: 5 pixels at the limb of a 100
        // pixel disc.
        shared vec4 rsSunEnd[rsSunRays];
        // A sliver. The samples lie evenly over the Sun, a ninth of its radius apart, so on a
        // large disc a crescent thinner than that passes between them: the last of a star going
        // behind a planet was a bright crescent with no glare at all (user, 2026-10-08). The rays
        // do not miss it: they are walked out to the limb in steps that shorten toward it. So on
        // a disc this wide, while the samples find little of it open and something of its limb
        // is, the rays are traced as well, and what they found open is taken in place of the
        // samples' answer, faded in as the samples run out. A ray stands for its slice of the
        // disc, between its two open points, with the air's share of the light along it.
        const float rsSunSliverPx = 24.0;       // the Sun at least this wide in radius: its samples are 2.7 px apart
        const float rsSunSliverFrom = 4.0;      // samples open: at this few the rays' answer alone
        const float rsSunSliverBy = 12.0;       // and at this many the samples' alone
        shared float rsSunRayAir[rsSunRays];

        // What the gather found, for rsSunBurst. No light means no glare from this group. The centre
        // of the light is a place on the Sun's plane.
        vec2 rsSunCentre;
        // A sight line's turn for a pixel across the screen where the sprite's Sun is, and for one up it.
        vec3 rsSunPerX = vec3(0.0), rsSunPerY = vec3(0.0);
        float rsSunPeak, rsSunDisc, rsSunLight = 0.0, rsSunOpen;
        vec3 rsSunHue = vec3(1.0);
        float rsSunCloud = 1.0, rsSunRing = 1.0;
        // How far the outline is used, and the disc the needles and lobes are blurred by when it
        // is: as wide as the open part lies about its middle.
        float rsSunShaped = 0.0, rsSunWide = 0.0;
        // The outline is used as far as the needles could reach, from the Sun's centre, and given
        // up over the next rsSunShapeFadePx: out there only the veil is left, which a round
        // stand-in serves as well, and the veil reaches far enough that tracing the rays for all
        // of it would double their cost. A sliver's is used everywhere: the rays are its light.
        float rsSunShapeEnds = 0.0, rsSunSliver = 0.0;
        const float rsSunShapeFadePx = 64.0;
        // The Sun as the frame has it past white: how far from its centre that reaches, in pixels
        // (its disc, and the ring its sprite's profile holds past the limb), and its depth, so
        // that only what is the Sun is held to its hue (rsSunOver). Zero: the Sun is not in view.
        float rsSunHoldPx = 0.0, rsSunDepthSeen = 0.0;

        // The colour sunlight arrives in along a direction, at unit luminance: the engine's own
        // transmittance, the table the ground is lit by, for a camera inside the air. The raw
        // texel, without the table's planet shadow, so the last of the Sun setting keeps the
        // horizon's colour instead of losing one. Outside the air the table does not apply, and
        // the sunlight is taken as it is.
        vec3 rsArrivingHue(vec3 towardLight)
        {
            vec3 light = global.lighting.sunColor.rgb;
            vec3 centre = global.lighting.planetPosition.xyz;
            float r = length(centre);
            float top = global.lighting.planetRadius + global.lighting.atmosphereHeight;
            if (rsHasAir() && r < top)
            {
                vec2 uv = GetTransmittanceLutUV(global.lighting.planetRadius, top, r,
                                                dot(-centre / r, towardLight),
                                                vec2(textureSize(TRANSMITTANCE_LUT_SAMPLER, 0).xy));
                light *= textureLod(TRANSMITTANCE_LUT_SAMPLER,
                                    vec3(uv, float(global.lighting.atmosphereLutLayer)), 0.0).rgb;
            }
            float luma = dot(light, rsLuma);
            return luma > 1e-20 ? light / luma : vec3(1.0);
        }

        // The sight line of a place on the Sun's plane, for its air and for what may cover it: for the
        // sprite the Sun's own, turned the way the engine's pixels turn where it is on the screen.
        vec3 rsSunSight(vec2 offset)
        {
            if (rsSunTurned >= 1.0) return rsSunPlaneRay(offset);
            vec3 byScreen = normalize(rsSunToward + rsSunPerX * offset.x + rsSunPerY * offset.y);
            return rsSunTurned <= 0.0 ? byScreen : normalize(mix(byScreen, rsSunPlaneRay(offset), rsSunTurned));
        }

        // Whether a sight line toward the Sun is clear of the bodies round about, for a place the frame
        // does not hold: the celestial block's spheres, as rsVisibility takes them for a point. Terrain
        // and vessels off the frame are not known, and are taken as not there.
        float rsSunOffFrame(vec3 toward)
        {
            float open = 1.0;
            for (int i = 0; i < global.celestial.bodyCount; i++)
            {
                vec4 body = global.celestial.bodies[i];
                float d = length(body.xyz);
                if (d < 1.0 || d >= rsSunAway * 0.9999) continue;
                float cosSep = dot(body.xyz / d, toward);
                if (cosSep <= 0.0) continue;
                float sep = acos(clamp(cosSep, -1.0, 1.0));
                float limb = asin(clamp(body.w / d, 0.0, 1.0));
                float soft = limb * 0.002;
                open = min(open, smoothstep(limb - soft, limb + soft, sep));
                if (open <= 0.0) return 0.0;
            }
            return open;
        }

        // How much of the Sun's light a place on its plane lets through, by the four texels round
        // where that is on the screen: nothing where the depth has something nearer than the Sun
        // (reversed: nearer is larger), and off the frame what rsSunOffFrame can tell.
        float rsSunOpenAt(vec2 offset, ivec2 dim, float sunDepth)
        {
            vec2 f = rsSunOnScreen(offset, vec2(dim)) - 0.5;
            ivec2 first = ivec2(floor(f));
            vec2 frac = f - vec2(first);
            float open = 0.0, unseen = 0.0;
            for (int n = 0; n < 4; n++)
            {
                ivec2 texel = first + ivec2(n & 1, n >> 1);
                float share = ((n & 1) != 0 ? frac.x : 1.0 - frac.x) * ((n >> 1) != 0 ? frac.y : 1.0 - frac.y);
                if (any(lessThan(texel, ivec2(0))) || any(greaterThanEqual(texel, dim)))
                {
                    unseen += share;
                    continue;
                }
                if (texelFetch(samplerDepth, texel, 0).r > sunDepth) continue;
                open += share;
            }
            if (unseen > 0.0) open += unseen * rsSunOffFrame(rsSunSight(offset));
            return open;
        }

        // The least angle between two directions on the sky's sphere and a third: from s to the arc
        // from a to b (the short way).
        float rsArcAngle(vec3 s, vec3 a, vec3 b)
        {
            vec3 n = cross(a, b);
            float len = length(n);
            if (len > 1e-9)
            {
                n /= len;
                float off = dot(s, n);
                vec3 foot = s - off * n;
                if (dot(cross(a, foot), n) >= 0.0 && dot(cross(foot, b), n) >= 0.0)
                    return asin(clamp(abs(off), 0.0, 1.0));
            }
            return acos(clamp(max(dot(s, a), dot(s, b)), -1.0, 1.0));
        }

        // How much of its glare a Sun at the frame's edge keeps: all of it once the Sun as it looks
        // reaches rsSunFrameInPx into the frame (or is wholly in it, if it is smaller), none once it
        // has left. By how near the frame comes to the Sun's centre, on the Sun's plane: for the
        // sprite the distance from its pixel to the screen's rectangle, for the sphere the least angle
        // from its centre to the frame's four edges, as pixels.
        float rsSunFrameFade(vec2 frame, float looksPx)
        {
            float byPixel = length(max(max(-rsSunPx, rsSunPx - frame), vec2(0.0)));
            float least = byPixel;
            if (rsSunTurned > 0.0)
            {
                float byAngle = 0.0;
                vec2 centre = rsSunOnScreen(vec2(0.0), frame);
                if (any(lessThan(centre, vec2(0.0))) || any(greaterThan(centre, frame)))
                {
                    vec3 c0 = rsPixelDirection(vec2(0.0)), c1 = rsPixelDirection(vec2(frame.x, 0.0));
                    vec3 c2 = rsPixelDirection(frame), c3 = rsPixelDirection(vec2(0.0, frame.y));
                    float angle = min(min(rsArcAngle(rsSunToward, c0, c1), rsArcAngle(rsSunToward, c1, c2)),
                                      min(rsArcAngle(rsSunToward, c2, c3), rsArcAngle(rsSunToward, c3, c0)));
                    byAngle = rsSunFocal * angle;
                }
                least = rsSunTurned >= 1.0 ? byAngle : mix(byPixel, byAngle, rsSunTurned);
            }
            return smoothstep(0.0, min(2.0 * looksPx, rsSunFrameInPx), looksPx - least);
        }

        // One ray of the Sun's outline: its first and last open point, in pixels from the Sun's
        // centre, or -1 twice where nothing on it is open. See rsSunRays.
        vec2 rsSunTrace(vec2 along, ivec2 dim, float sunDepth)
        {
            float inner = -1.0, outer = -1.0;           // the first and the last open place walked over
            float innerShut = -1.0, outerShut = -1.0;   // and the shut one before the first, after the last
            float before = 0.0;
            for (int k = 0; k <= rsSunRaySteps; k++)
            {
                float back = float(rsSunRaySteps - k) / float(rsSunRaySteps);
                float rho = rsSunDisc * (1.0 - back * back);
                if (rsSunOpenAt(along * rho, dim, sunDepth) >= 0.5)
                {
                    if (inner < 0.0)
                    {
                        inner = rho;
                        innerShut = k > 0 ? before : -1.0;
                    }
                    outer = rho;
                    outerShut = -1.0;
                }
                else if (outer >= 0.0 && outerShut < 0.0)
                    outerShut = rho;
                before = rho;
            }
            if (inner < 0.0) return vec2(-1.0);
            // Open at the centre itself: the open part starts there. Otherwise its edge lies
            // between the last shut place and the first open one.
            if (innerShut < 0.0)
                inner = 0.0;
            else
            {
                for (int h = 0; h < rsSunRayHalvings; h++)
                {
                    float middle = 0.5 * (innerShut + inner);
                    if (rsSunOpenAt(along * middle, dim, sunDepth) >= 0.5) inner = middle;
                    else innerShut = middle;
                }
                inner = 0.5 * (innerShut + inner);
            }
            // Open at the limb: the open part ends there. Otherwise likewise.
            if (outerShut >= 0.0)
            {
                for (int h = 0; h < rsSunRayHalvings; h++)
                {
                    float middle = 0.5 * (outer + outerShut);
                    if (rsSunOpenAt(along * middle, dim, sunDepth) >= 0.5) outer = middle;
                    else outerShut = middle;
                }
                outer = 0.5 * (outer + outerShut);
            }
            return vec2(inner, outer);
        }

        // The end of the open part beside a ray: the turns at which it stops being open, going
        // from the ray at `turn` toward its shut neighbour `toward` away, a quarter and three
        // quarters of the way along the ray's open span. See rsSunEnd.
        vec2 rsSunEndOf(vec2 span, float turn, float toward, ivec2 dim, float sunDepth)
        {
            vec2 found;
            for (int part = 0; part < 2; part++)
            {
                float rho = mix(span.x, span.y, part == 0 ? 0.25 : 0.75);
                float open = 0.0, shut = 1.0;           // shares of the way to the neighbour
                for (int h = 0; h < rsSunRayHalvings; h++)
                {
                    float middle = 0.5 * (open + shut);
                    float at = turn + toward * middle;
                    if (rsSunOpenAt(rho * vec2(cos(at), sin(at)), dim, sunDepth) >= 0.5) open = middle;
                    else shut = middle;
                }
                found[part] = turn + toward * 0.5 * (open + shut);
            }
            return found;
        }

        // A point's distance from a line between two others, squared.
        float rsSegment2(vec2 p, vec2 a, vec2 b)
        {
            vec2 ab = b - a;
            float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-9), 0.0, 1.0);
            vec2 d = p - a - t * ab;
            return dot(d, d);
        }

        // A place's distance from the outline the rays traced, in pixels: 0 inside it. The place is on
        // the Sun's plane, as the rays are. Read after the gather's second barrier, when every ray is in.
        float rsSunFromShape(vec2 q)
        {
            // Inside: between the pixel's two rays, both open, and between their open points.
            float turn = atan(q.y, q.x);
            if (turn < 0.0) turn += 6.28318531;
            float u = turn / 6.28318531 * float(rsSunRays);
            int j0 = int(floor(u)) % rsSunRays;
            int j1 = (j0 + 1) % rsSunRays;
            vec4 left = rsSunRay[j0], right = rsSunRay[j1];
            float away = length(q);
            if (left.y >= 0.0 && right.y >= 0.0)
            {
                float f = fract(u);
                if (away >= mix(left.x, right.x, f) && away <= mix(left.y, right.y, f)) return 0.0;
            }
            else if (left.y >= 0.0 || right.y >= 0.0)
            {
                // One open, one shut: inside as far as the end found between them.
                bool forward = left.y >= 0.0;
                vec4 end = forward ? left : right;
                vec2 turns = forward ? rsSunEnd[j0].xy : rsSunEnd[j1].zw;
                if (away >= end.x && away <= end.y)
                {
                    float base = 6.28318531 * float(forward ? j0 : j1) / float(rsSunRays);
                    float along = clamp((away - mix(end.x, end.y, 0.25)) / max(0.5 * (end.y - end.x), 1e-6), 0.0, 1.0);
                    float endFrom = mix(turns.x, turns.y, along) - base;
                    float mine = turn - base;
                    mine -= 6.28318531 * round(mine / 6.28318531);
                    if (forward ? mine <= endFrom : mine >= endFrom) return 0.0;
                }
            }
            // Outside: the nearest of the lines from ray to ray, along the open part's outer edge
            // and its inner one, and of the rays that end it.
            float best = 1e30;
            for (int j = 0; j < rsSunRays; j++)
            {
                vec4 here = rsSunRay[j], next = rsSunRay[(j + 1) % rsSunRays];
                bool hereOpen = here.y >= 0.0, nextOpen = next.y >= 0.0;
                if (!hereOpen && !nextOpen) continue;
                if (hereOpen && nextOpen)
                {
                    best = min(best, rsSegment2(q, here.zw * here.y, next.zw * next.y));
                    if (max(here.x, next.x) > 0.25)
                        best = min(best, rsSegment2(q, here.zw * here.x, next.zw * next.x));
                }
                else
                {
                    // The open part ends between these two: along the end found there, from the
                    // open ray's first open point to its last, and across to it from the ray's own.
                    vec4 end = hereOpen ? here : next;
                    vec2 turns = hereOpen ? rsSunEnd[j].xy : rsSunEnd[(j + 1) % rsSunRays].zw;
                    vec2 low = vec2(cos(turns.x), sin(turns.x)), high = vec2(cos(turns.y), sin(turns.y));
                    vec2 p0 = low * end.x, p1 = low * mix(end.x, end.y, 0.25);
                    vec2 p2 = high * mix(end.x, end.y, 0.75), p3 = high * end.y;
                    best = min(best, min(rsSegment2(q, p0, p1), min(rsSegment2(q, p1, p2), rsSegment2(q, p2, p3))));
                    best = min(best, rsSegment2(q, end.zw * end.y, p3));
                    if (end.x > 0.25) best = min(best, rsSegment2(q, end.zw * end.x, p0));
                }
            }
            return sqrt(best);
        }

        // Called at the top of main by every invocation, before any of them can return: the
        // barriers inside hold only where the whole group arrives.
        void rsSunGather(ivec2 dim)
        {
            rsSunLight = 0.0;
            rsSunHoldPx = 0.0;
            rsSunSliver = 0.0;
            // Where the Sun is, on the screen and on its own plane; nothing if none of it can show.
            if (!rsSunPlaneSet(vec2(dim))) return;
            float dist = rsSunAway;

            // The whole Sun, before anything is in the way: how big it looks, and how far its
            // glare could reach from anywhere in that.
            float mag = rsLightAbsMag() + 5.0 * log(dist / (10.0 * rsParsecMetres)) * 0.4342944819;
            rsSunPeak = pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef))
                      * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            rsSunDisc = rsSunDiscPx();
            float whitePx = rsPsfCore * sqrt(max(pow(max(rsSunPeak / rsMaxOutput, 1.0),
                                                     1.0 / rsPsfBeta) - 1.0, 0.0));
            // The white ring is our sprite's, and it draws in to the limb as the sphere takes
            // over (see Star.frag): once the sphere is whole, the Sun looks like its disc.
            float looksPx = rsSunDisc + whitePx * (1.0 - rsSphereShown());
            // And how far it is past the blooms' white, for rsSunOver, which holds the Sun whether
            // or not there is a burst to draw. The depth as rsSunPlaneSet has it: nothing lies
            // behind a Sun past the far plane.
            rsSunDepthSeen = rsSunDepth;
            rsSunHoldPx = rsSunDisc + (1.0 - rsSphereShown()) * rsPsfCore
                        * sqrt(max(pow(max(rsSunPeak / rsBloomWhite, 1.0), 1.0 / rsPsfBeta) - 1.0, 0.0));
            if (rsBurstStrength() <= 0.0) return;
            float reach = rsGlareReachPx(rsGlareLevel(rsSunPeak), rsSunDisc);
            // Only groups the glare could reach gather. Nothing here is the invocation's own,
            // so every invocation of a group answers the same way and the group stays together.
            vec2 corner = vec2(gl_WorkGroupID.xy * gl_WorkGroupSize.xy);
            // The group's nearest to the Sun's centre, on the Sun's plane. For the sprite that is
            // the screen and exact. For the sphere, the group's middle less the group's own
            // size: a place's distance from the centre changes by a pixel a pixel at the most,
            // an angle being largest in pixels at the middle of the screen.
            float nearest = distance(clamp(rsSunPx, corner, corner + vec2(gl_WorkGroupSize.xy)), rsSunPx);
            if (rsSunTurned > 0.0)
                nearest = max(length(rsSunOnPlane(corner + 0.5 * vec2(gl_WorkGroupSize.xy)))
                              - 0.75 * length(vec2(gl_WorkGroupSize.xy)), 0.0);
            if (reach <= 0.0 || nearest > reach + looksPx) return;
            rsSunShapeEnds = max(rsNeedleReachPx(rsGlareLevel(rsSunPeak), rsSunDisc), rsSunDisc) + looksPx - rsSunDisc;
            bool shapeHere = nearest <= rsSunShapeEnds + rsSunShapeFadePx;

            float sunDepth = rsSunDepth;
            // Each sample's sight line, for its air (rsSunSight): for the sprite the Sun's, turned
            // the way the engine's pixels turn where it is on the screen.
            bool air = rsHasAir();
            if (rsSunTurned < 1.0)
            {
                vec3 base = rsPixelDirection(rsSunPx);
                rsSunPerX = rsPixelDirection(rsSunPx + vec2(1.0, 0.0)) - base;
                rsSunPerY = rsPixelDirection(rsSunPx + vec2(0.0, 1.0)) - base;
            }
            // And how much of its glare the frame's edge leaves it: see rsSunFrameFade.
            float inFrame = rsSunFrameFade(vec2(dim), looksPx);
            if (inFrame <= 0.0) return;
            vec4 mine = vec4(0.0);          // light, open, and the light's weighted place
            vec4 shape = vec4(0.0);         // what is open: its place from the Sun's centre, its spread, and its limb
            for (int k = 0; k < rsSunSamples / rsSunGroup; k++)
            {
                int i = int(gl_LocalInvocationIndex) + rsSunGroup * k;
                float t = (float(i) + 0.5) / float(rsSunSamples);
                float turn = float(i) * 2.39996323;                 // the golden angle
                vec2 offset = looksPx * sqrt(t) * vec2(cos(turn), sin(turn));
                float open = rsSunOpenAt(offset, dim, sunDepth);
                float light = open;
                if (air && open > 0.0)
                    light *= rsAirVisibility(rsSunSight(offset), dist);
                mine += vec4(light, open, light * offset);
                shape.xyz += open * vec3(offset, dot(offset, offset));
            }
            // And the limb itself, at this invocation's two rays, a pixel in from it: whether any
            // of it is open, which is what says a sliver may be there (rsSunSliverPx).
            if (looksPx >= rsSunSliverPx)
                for (int n = 0; n < rsSunRays / rsSunGroup; n++)
                {
                    float turn = 6.28318531 * float(int(gl_LocalInvocationIndex) * (rsSunRays / rsSunGroup) + n)
                               / float(rsSunRays);
                    shape.w += rsSunOpenAt(max(rsSunDisc - 1.0, 0.0) * vec2(cos(turn), sin(turn)), dim, sunDepth);
                }
            rsSunShare[gl_LocalInvocationIndex] = mine;
            rsSunShape[gl_LocalInvocationIndex] = shape;
            memoryBarrierShared();
            barrier();
            vec4 sum = vec4(0.0);
            vec4 open3 = vec4(0.0);
            for (int j = 0; j < rsSunGroup; j++)
            {
                sum += rsSunShare[j];
                open3 += rsSunShape[j];
            }
            rsSunLight = sum.x / float(rsSunSamples);
            rsSunOpen = sum.y / float(rsSunSamples);
            rsSunCentre = sum.x > 0.0 ? sum.zw / sum.x : vec2(0.0);

            // How far the open part is from round, in pixels, and how wide it lies. By what is
            // open, not by its light: air dimming a whole Sun leaves it a whole disc.
            rsSunShaped = 0.0;
            rsSunWide = rsSunDisc;
            if (sum.y > 0.0)
            {
                vec2 middle = open3.xy / sum.y;
                float lies = max(open3.z / sum.y - dot(middle, middle), 0.0);   // mean square, from its middle
                float equal = looksPx * sqrt(rsSunOpen);            // the round disc of the same area
                float beyond = lies - 0.5 * equal * equal;
                rsSunShaped = smoothstep(rsSunShapeFromPx, rsSunShapeByPx, sqrt(max(beyond, 0.0)));
                // A disc's points lie its radius over root two from its middle, in the mean.
                rsSunWide = rsSunDisc * sqrt(2.0 * lies) / max(looksPx, 1e-3);
            }
            // How far the samples have run out on a disc wide enough to hide a sliver from them,
            // with some of its limb open: see rsSunSliverPx.
            float sliver = looksPx >= rsSunSliverPx && open3.w > 0.0
                         ? 1.0 - smoothstep(rsSunSliverFrom, rsSunSliverBy, sum.y) : 0.0;
            // The outline, while it is in use and where: see rsSunShapeEnds. Every invocation of
            // the group has the same answer to that, from the same sums and the group's own place,
            // so the group is still together at the second barrier.
            if ((rsSunShaped > 0.0 && shapeHere) || sliver > 0.0)
            {
                for (int n = 0; n < rsSunRays / rsSunGroup; n++)
                {
                    int j = int(gl_LocalInvocationIndex) * (rsSunRays / rsSunGroup) + n;
                    float turn = 6.28318531 * float(j) / float(rsSunRays);
                    vec2 along = vec2(cos(turn), sin(turn));
                    vec2 span = rsSunTrace(along, dim, sunDepth);
                    rsSunRay[j] = vec4(span, along);
                    float through = 1.0;
                    if (air && sliver > 0.0 && span.y >= 0.0)
                    {
                        vec2 middle = along * 0.5 * (span.x + span.y);
                        through = rsAirVisibility(rsSunSight(middle), dist);
                    }
                    rsSunRayAir[j] = through;
                }
                memoryBarrierShared();
                barrier();
                // And where the open part ends beside one of this invocation's rays: see rsSunEnd.
                float spacing = 6.28318531 / float(rsSunRays);
                for (int n = 0; n < rsSunRays / rsSunGroup; n++)
                {
                    int j = int(gl_LocalInvocationIndex) * (rsSunRays / rsSunGroup) + n;
                    vec4 here = rsSunRay[j];
                    vec4 ends = vec4(-100.0);
                    if (here.y >= 0.0)
                    {
                        float turn = spacing * float(j);
                        if (rsSunRay[(j + 1) % rsSunRays].y < 0.0)
                            ends.xy = rsSunEndOf(here.xy, turn, spacing, dim, sunDepth);
                        if (rsSunRay[(j + rsSunRays - 1) % rsSunRays].y < 0.0)
                            ends.zw = rsSunEndOf(here.xy, turn, -spacing, dim, sunDepth);
                    }
                    rsSunEnd[j] = ends;
                }
                memoryBarrierShared();
                barrier();
                if (sliver > 0.0)
                {
                    // What the rays found open. A slice between radii a and b has its area at
                    // 2/3 (b^3 - a^3) / (b^2 - a^2) from the centre in the mean, and (a^2 + b^2) / 2
                    // in the mean square.
                    float area = 0.0, lit = 0.0, square = 0.0;
                    vec2 middle = vec2(0.0), place = vec2(0.0);
                    for (int j = 0; j < rsSunRays; j++)
                    {
                        vec4 ray = rsSunRay[j];
                        float inner2 = ray.x * ray.x, outer2 = ray.y * ray.y;
                        if (ray.y < 0.0 || outer2 <= inner2) continue;
                        float part = 0.5 * (outer2 - inner2);
                        float mean = (ray.y * outer2 - ray.x * inner2) / (1.5 * (outer2 - inner2));
                        area += part;
                        middle += part * mean * ray.zw;
                        square += part * 0.5 * (outer2 + inner2);
                        lit += part * rsSunRayAir[j];
                        place += part * rsSunRayAir[j] * mean * ray.zw;
                    }
                    // A slice's share of the Sun as it looks: (2 pi / rays) over pi looks^2.
                    float slice = 2.0 / (float(rsSunRays) * looksPx * looksPx);
                    rsSunOpen = mix(rsSunOpen, area * slice, sliver);
                    rsSunLight = mix(rsSunLight, lit * slice, sliver);
                    rsSunCentre = mix(rsSunCentre, lit > 0.0 ? place / lit : vec2(0.0), sliver);
                    if (area > 0.0)
                    {
                        middle /= area;
                        float lies = max(square / area - dot(middle, middle), 0.0);
                        rsSunWide = mix(rsSunWide, rsSunDisc * sqrt(2.0 * lies) / max(looksPx, 1e-3), sliver);
                        // A sliver is as far from round as a shape gets: its glare starts at its outline.
                        rsSunSliver = sliver;
                    }
                }
            }

            // Clouds, which neither the depth nor the air above sees: the engine's cloud shadow,
            // the direct sunlight it lights the ground and vessels by, read where the camera is.
            rsSunCloud = GetCloudShadow(vec3(0.0));
            // Rings, which this pass cannot read either: RingOcclusion works out their optical
            // depth toward the Sun from the ring texture on the CPU, and leaves it in the high
            // half of the celestial block's last spare word. The ring passes dim the sprites drawn
            // before them; only this burst, drawn after, needed telling.
            rsSunRing = exp(-max(unpackHalf2x16(uint(global.celestial.pad2)).y, 0.0));
            // And in the colour the light arrives in, from where the light that got in is.
            rsSunHue = rsArrivingHue(rsSunSight(rsSunCentre));
            // The frame's edge last: it takes the glare down as the Sun leaves, not its shape.
            rsSunLight *= inFrame;
        }

        // The Sun's glare at a place on the Sun's plane (rsSunOnPlane).
        vec3 rsSunBurst(vec2 place)
        {
            if (rsSunLight <= 0.0 || rsSunCloud <= 0.0) return vec3(0.0);
            float discSeen = rsSunDisc * sqrt(rsSunOpen);
            // At unit luminance, as the stars' colours are, so the level alone sets how bright.
            // It was the Sun's own colour at its brightest channel: white, whatever the air, so
            // a burst through thick air came out a dim gray round an orange Sun.
            vec3 tint = rsSunHue;
            // The clouds take the glare itself down, in proportion, rather than the light it is
            // worked from. The level's peak^kappa is the eye adapting to the source it looks at,
            // and under a cloud deck it is adapted to the clouds, which the Sun still lights: only
            // the direct beam that makes the glare falls with the cloud. Through the law, a deck
            // that passed a millionth of the light still left a burst, deep in Jupiter's clouds
            // and on Venus's surface. In proportion it ends near optical depth 10, about where
            // the disc stops outshining the cloud around it. Rings the same way: they scatter
            // the Sun's light too, and it is their direct beam that makes the glare.
            float level = rsGlareLevel(rsSunPeak * rsSunLight) * rsSunCloud * rsSunRing;
            // The one glare, its glow starting at the round stand-in's edge, at the outline of
            // what is left, or part way between: see rsSunRays.
            vec2 offset = place - rsSunCentre;
            float outline = max(rsSunSliver, rsSunShaped * (1.0 - smoothstep(rsSunShapeEnds, rsSunShapeEnds + rsSunShapeFadePx,
                                                                              length(place))));
            vec3 burst = vec3(0.0);
            if (outline < 1.0)
                burst = (1.0 - outline)
                      * rsGlareOf(offset, rsGlareReachPx(level, discSeen), discSeen, level,
                                  max(length(offset) - discSeen, 0.0));
            if (outline > 0.0)
                burst += outline
                       * rsGlareOf(offset, rsGlareReachPx(level, rsSunWide), rsSunWide, level,
                                   rsSunFromShape(place));
            // Held to what the blooms may see, as rsGlare holds a round source's, and then in the
            // star's hue: burnt out, a red dwarf's glare is orange, not white. (Making it white
            // beside a white disc was tried and was the wrong side to change: see rsSunOver.)
            return tint * min(burst, vec3(rsBloomWhite));
        }

        // The frame with the Sun's burst over it, and the Sun held to its own hue where it burns
        // out.
        //
        // The Sun's disc is drawn far past white, for the air to dim (rsMaxOutput), and what is
        // past white shows white whatever its colour. Its glare is held at the blooms' white in
        // the star's hue. For the Sun those are one white. For a red dwarf they were a white disc
        // in an orange glare with a hard rim between them (user, 2026-10-07, of Proxima from close
        // by), and the white was the wrong one of the two. The glare is the disc's own light, so
        // they are one colour; and a red dwarf's surface is some five hundred times fainter than
        // the Sun's, about a setting Sun's, which an eye sees as an orange disc. So where the
        // frame shows the Sun itself, or its glare at its ceiling, the whole is held to the blooms'
        // white in luminance and keeps its hue, and the disc runs into its glare without an edge.
        // The Sun's own hue is near enough white that it looks as it did.
        vec3 rsSunOver(vec3 frame, vec2 pixel, ivec2 dim)
        {
            if (rsSunHoldPx <= 0.0 && rsSunLight <= 0.0) return frame;     // no Sun in view: nothing to add, nothing to hold
            vec2 place = rsSunOnPlane(pixel);
            vec3 burst = rsSunBurst(place);
            vec3 sum = frame + burst;
            float held = clamp(dot(burst, rsLuma) / rsBloomWhite, 0.0, 1.0);
            if (rsSunHoldPx > 0.0)
            {
                float inside = 1.0 - smoothstep(rsSunHoldPx - 1.0, rsSunHoldPx + 1.0, length(place));
                // Only where it is the Sun that shows: nothing drawn nearer (reversed: nearer is larger).
                if (inside > 0.0
                    && texelFetch(samplerDepth, clamp(ivec2(pixel), ivec2(0), dim - 1), 0).r <= rsSunDepthSeen)
                    held = max(held, inside);
            }
            float luma = dot(sum, rsLuma);
            if (held <= 0.0 || luma <= rsBloomWhite) return sum;
            return sum * (mix(luma, rsBloomWhite, held) / luma);
        }
        """;

    /// <summary>The Sun's place on the screen, shared by the merge pass and the blooms' downsamples.</summary>
    private const string SunPlane = """
        // ---- the Sun's plane ----
        // Where the Sun is on the screen, for everything worked out about its centre: the gather's
        // samples, the glare, the hold, the blooms' cap.
        //
        // Far off, the Sun is our sprite: a round disc about its centre's pixel, whatever part of the
        // screen it is on, and places are counted in screen pixels from that pixel. Close to, it is the
        // engine's sphere, drawn in true perspective: toward the edge of a wide view a sphere's outline
        // is an ellipse, up to twice as long as wide, and not about its centre's pixel; and from low
        // over a star the centre is beside the view or behind it while the limb crosses the screen.
        // Everything here took the Sun for the round disc, so the sphere stood out past it, raw (user,
        // 2026-10-08: "the raw, unprocessed crisp disk appears from behind the glare ... like the glare
        // can't handle the star being such a large source while also not being circular").
        //
        // So for the sphere, places are counted on the Sun's own plane: by the angle from the Sun's
        // centre, in the pixels an angle is at the middle of the screen, and the way round it. There
        // the sphere is a round disc exactly (rsSunDiscPx), wherever it is on the screen and whichever
        // way the camera points, and its glare lies about it by angle, as an eye's does: as wide beside
        // the limb of a star that fills the view as beside one that is a dot. (By the tangent of the
        // angle, a flat picture's way, a glare beside a limb 80 degrees from the star's centre would be
        // a thirtieth as wide.) The two are blended by how far the sphere has taken over from the
        // sprite (rsSphereShown): between 44 and 88 radii the star is a few tens of pixels, and they
        // differ by a pixel or two at the edge of a wide view.
        vec2 rsSunPx = vec2(0.0);                   // the Sun's centre on the screen, in pixels, while it is ahead of the camera
        float rsSunTurned = 0.0;                    // how far the Sun's plane is used
        vec3 rsSunToward = vec3(0.0, 0.0, 1.0);     // unit, about the camera: to the Sun's centre,
        vec3 rsSunAcross = vec3(1.0, 0.0, 0.0);     // and the screen's x and y, turned with the view to face it
        vec3 rsSunUp = vec3(0.0, 1.0, 0.0);
        float rsSunFocal = 1.0;                     // pixels to a radian at the middle of the screen, as rsLightDiscPx has them
        float rsSunAway = 0.0;                      // the Sun's distance, in metres
        // The Sun's depth, for what is in front of it (reversed: nearer is larger). The camera's far
        // plane is one AU - exactly - so past Earth's orbit the Sun lies beyond it and its reversed
        // depth goes negative. Every sky pixel is then "nearer" than the Sun, and the burst was judged
        // hidden and went out. Clamped at zero, which is what the engine's own flare does with its sun
        // depth: the Sun is then behind everything drawn, which a Sun past the far plane is, and in
        // front of empty sky.
        float rsSunDepth = 0.0;

        // Sets the above for a frame of this size in pixels. False: nothing of the Sun can be in view.
        bool rsSunPlaneSet(vec2 frame)
        {
            vec3 sunEgo = global.lighting.sunPosition.xyz;
            rsSunAway = length(sunEgo);
            if (rsSunAway <= 0.0) return false;
            rsSunToward = sunEgo / rsSunAway;
            vec4 clip = global.camera.viewProjection * vec4(sunEgo, 1.0);
            rsSunTurned = rsSphereShown();
            // Behind the camera: gone, unless it is the sphere whole, whose limb can still be ahead.
            if (clip.w <= 0.0 && rsSunTurned < 1.0) return false;
            rsSunPx = clip.w > 0.0 ? (clip.xy / clip.w * 0.5 + 0.5) * frame : vec2(0.0);
            rsSunDepth = clip.w > 0.0 ? max(clip.z / clip.w, 0.0) : 0.0;
            if (rsSunTurned <= 0.0) return true;
            vec2 screen = vec2(global.camera.screenWidth, global.camera.screenHeight);
            vec3 ahead = rsPixelDirection(0.5 * screen);
            float facing = 1.0 + dot(ahead, rsSunToward);
            if (facing < 1e-3)
            {
                // Dead astern, where no turn of the view is the least one. No sphere shows from there.
                if (rsSunTurned >= 1.0) return false;
                rsSunTurned = 0.0;
                return true;
            }
            vec3 across = rsPixelDirection(0.5 * screen + vec2(64.0, 0.0));
            vec3 up = rsPixelDirection(0.5 * screen + vec2(0.0, 64.0));
            across = normalize(across - ahead * dot(across, ahead));
            up = normalize(up - ahead * dot(up, ahead));
            // The least turn that takes the view's axis to the Sun takes these two with it.
            vec3 turn = (ahead + rsSunToward) / facing;
            rsSunAcross = across - dot(across, rsSunToward) * turn;
            rsSunUp = up - dot(up, rsSunToward) * turn;
            rsSunFocal = 0.5 * abs(global.camera.projection[1][1]) * screen.y;
            if (clip.w <= 0.0)
            {
                // The centre is behind the camera and has no depth: the limb's, then, which is as far
                // as a sight line that grazes the sphere runs.
                float size = global.lighting.sunRadius;
                vec4 limb = global.camera.viewProjection
                          * vec4(ahead * sqrt(max(rsSunAway * rsSunAway - size * size, 1e-6 * size * size)), 1.0);
                rsSunDepth = limb.w > 0.0 ? max(limb.z / limb.w, 0.0) : 0.0;
            }
            return true;
        }

        // The Sun's disc on its plane, in pixels of radius: the sprite's, rsLightDiscPx, which is the
        // sphere's across the middle of a flat picture (its focal length times the tangent of the
        // sphere's angle), and for the sphere that angle itself.
        float rsSunDiscPx()
        {
            float byScreen = rsLightDiscPx();
            if (rsSunTurned <= 0.0) return byScreen;
            return mix(byScreen, rsSunFocal * atan(byScreen / rsSunFocal), rsSunTurned);
        }

        // A screen pixel's place on the Sun's plane, in pixels from the Sun's centre.
        vec2 rsSunOnPlane(vec2 pixel)
        {
            if (rsSunTurned <= 0.0) return pixel - rsSunPx;
            vec3 along = rsPixelDirection(pixel);
            vec2 side = vec2(dot(along, rsSunAcross), dot(along, rsSunUp));
            float off = length(side);
            vec2 turned = off > 1e-9 ? side * (rsSunFocal * atan(off, dot(along, rsSunToward)) / off) : vec2(0.0);
            return rsSunTurned >= 1.0 ? turned : mix(pixel - rsSunPx, turned, rsSunTurned);
        }

        // The sight line of a place on the Sun's plane, about the camera: unit.
        vec3 rsSunPlaneRay(vec2 offset)
        {
            float far = length(offset);
            if (far < 1e-6) return rsSunToward;
            float angle = far / rsSunFocal;
            vec2 way = offset / far;
            return cos(angle) * rsSunToward + sin(angle) * (way.x * rsSunAcross + way.y * rsSunUp);
        }

        // And back: where a place on the Sun's plane is, on a frame of this size. Far off the frame
        // (-1e6) where it is behind the camera.
        vec2 rsSunOnScreen(vec2 offset, vec2 frame)
        {
            if (rsSunTurned <= 0.0) return rsSunPx + offset;
            vec4 clip = global.camera.viewProjection * vec4(rsSunPlaneRay(offset) * rsSunAway, 1.0);
            vec2 turned = clip.w > 0.0 ? (clip.xy / clip.w * 0.5 + 0.5) * frame : vec2(-1e6);
            return rsSunTurned >= 1.0 ? turned : mix(rsSunPx + offset, turned, rsSunTurned);
        }
        """;

    private const string Tuning = $$"""
        // ---- Real Stars tuning ----
        // Our catalogue stores ABSOLUTE magnitude linearly in the byte: byte 1 is the faint
        // limit and each step is a tenth of a magnitude. make_star_binary.py writes it, along
        // with the star's position in parsecs, and the apparent magnitude follows from the
        // distance to wherever the observer is standing.
        const float rsMagFaint = 16.5;
        const float rsBytesPerMag = 10.0;
        const float rsParsecMetres = 3.0856775814913673e16;
        const float rsSunAbsMag = 4.83;        // the Sun's, until the C# side says which star lights the scene
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
        // Luminance, Rec. 709: near enough what the V band measures, and what the engine's bloom
        // thresholds on. A colour at unit luminance carries hue and nothing of brightness.
        const vec3 rsLuma = vec3(0.2126, 0.7152, 0.0722);
        // The sprites are fans of eight triangles, corners on a circle; this is where their flat
        // edges come in to, as a share of it: cos(22.5 deg).
        const float rsFanInradius = 0.92387953;
        // The most the engine's blooms may see of any of our sources. Past about 1.2 the default
        // tonemap is already white (Hable at exposure 1.25), so whatever a core writes above that
        // shows only through the blooms: the threshold bloom adds a third of everything over 3
        // back as a wide glow, and the global bloom mixes a tenth of a blur into the image. Both
        // drew a second glare around sources that carry their own. 1.5 is still white after the
        // global bloom takes its tenth, and half the threshold.
        const float rsBloomSeen = 1.5;
        // White now over white at the game's default exposure (Exposure.cs). All of the above is
        // at 1.25, the default. The player's setting multiplies the finished frame, so at a
        // tenth of that 1.5 is a dim gray, and every source held to it was one: the Sun's glare
        // a flat gray star, no smaller than before (user, 2026-10-08). So what is held to white,
        // or has to clear the dark, is held to white as the screen has it now.
        float rsWhiteScale()
        {
            return {{WhiteScaleGlsl}};
        }
        // What our own sources are held to: rsBloomSeen at the default exposure, and white with
        // the same margin at any other. Below the default that is more than the blooms may see;
        // a star's core is too small to feed them, and the Sun's is kept from them (BloomSun).
        float rsBloomWhite = rsBloomSeen * rsWhiteScale();
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

        // The catalogue's byte back to an absolute magnitude.
        float rsAbsMag(float packedScale)
        {
            return rsMagFaint - (packedScale * 255.0 - 1.0) / rsBytesPerMag;
        }

        // Absolute magnitude to flux, through the distance the observer actually is from the
        // star. Pogson: five magnitudes is a factor of a hundred, and an absolute magnitude is
        // what a star would show at ten parsecs.
        float rsFlux(float absMag, float distancePc)
        {
            float mag = absMag + 5.0 * log(distancePc / 10.0) * 0.4342944819;
            return pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef));
        }

        // The star lighting the scene, which since KSA 2026.10 need not be the Sun: the radius of
        // its disc in pixels and its absolute magnitude, as SunGlow writes them, two halves of the
        // lighting block's last spare int. The int is zero until it is written, and then the
        // Sun's magnitude stands in.
        vec2 rsLightWord()
        {
            return unpackHalf2x16(uint(global.lighting.lpPad4));
        }

        // The lighting star's disc on the screen, in pixels of radius: where a sight line grazes
        // its sphere, through this view's own projection. That is the size the engine draws the
        // star's sphere, which the sprite hands over to, and the two have to agree: the sprite
        // stays burnt out until the last of its fade, so whatever of it stood outside the sphere
        // went in one step, and the star looked a size smaller (user, 2026-10-07). The word's own
        // figure is the engine's, an angle times the frame's height over its field of view, which
        // is 10% over the projected size in a 60 degree view and 27% in a 90 degree one, and it
        // is the width over the distance, short of the grazing line close to. It still says
        // whether a lighting star is known.
        float rsLightDiscPx()
        {
            if (rsLightWord().x <= 0.0) return 0.0;
            float size = global.lighting.sunRadius;
            float away = length(global.lighting.sunPosition.xyz);
            float focal = 0.5 * abs(global.camera.projection[1][1]) * global.camera.screenHeight;
            return focal * size / sqrt(max(away * away - size * size, 1e-6 * size * size));
        }

        float rsLightAbsMag()
        {
            return global.lighting.lpPad4 != 0 ? rsLightWord().y : rsSunAbsMag;
        }

        // ---- scintillation ----
        // Turbulence rearranges a wavefront on its way down, and a point source' brightness
        // wanders as a result. Three scalings, all consequences rather than choices:
        //   amplitude  sigma^2 goes as (sec z)^(11/6), so sigma goes as airmass^0.92 and
        //              saturates near 1. The zenith value arrives in lpPad2's low half from the C# side,
        //              already carrying the body's air density and the observer's altitude.
        //   speed      the pattern drifts past on the high-altitude wind. It is milliseconds
        //              in truth; what an eye or a frame resolves is the low end, and it slows
        //              towards the horizon as the path lengthens.
        //   colour     refraction is wavelength dependent, so a low star smears into a small
        //              spectrum. Past the angular scale in lpPad2's high half (in arcseconds, which
        //              half precision holds where radians would not) the colours cross different
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
            vec2 rsAir = unpackHalf2x16(uint(global.lighting.lpPad2));
            float sigmaZenith = rsAir.x;
            float thetaC = rsAir.y / rsArcsecPerRad;
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
            float simTime = intBitsToFloat(global.lighting.lpPad3);
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

        // ---- the observer's glare ----
        // What a bright point does to an EYE rather than to a camera, after Ritschel et al.,
        // "Temporal Glare" (Eurographics 2009). The eye diffracts light at the edge of its pupil
        // and at particles in its lens, vitreous and cornea, and the Fourier transform of that
        // aperture is its point spread function. A camera's blades give N even spikes. The
        // particles give speckle instead, and since each wavelength sees the same pattern scaled
        // by its own length, a white source's speckle is drawn out into radial needles: the
        // ciliary corona. glare_sim.py runs that model (a 2.2 mm pupil, as an eye looking at the
        // Sun has, 870 particles, a 4096-sample aperture and 32 wavelengths).
        //
        // What it shows is a glow, falling as r^-2.8, whose texture is a fan of about a thousand
        // needles. Every direction is a lane of summed speckle, one diffraction spot (about a
        // pixel) wide, so near the source the lanes overlap into the glow and further out they
        // part into needles. That is what is drawn: rsLaneCount lanes evenly spaced in angle,
        // each with a hashed jitter and strength, so a pixel only visits the lanes beside its own
        // direction. A strength is a sum of speckle, so it is gamma-distributed.
        //
        // It is one point spread function for every source, the core above plus this, and only
        // the level differs. Nothing is decided per source: no magnitude line, no reach per
        // magnitude. A glare is seen where it clears a floor, the level the eye is adapted to,
        // and each lane carries its share of the glow, so each needle ends where its own share
        // sinks to the floor. Strong lanes end further out, and a brighter source clears the
        // floor further out, so its needles are longer. A faint star's glow is under the floor
        // everywhere, and it is a point.
        //
        // The pattern belongs to whoever is looking, not to what is being looked at: every light
        // in the frame shows the same needles, fixed in the screen.
        //
        // The paper's other feature, the lenticular halo, is left out on purpose. It is the lens
        // cortex's fibres acting as a grating, and the cortex only lies inside a pupil wider than
        // about 4 mm: a dark-adapted eye at a streetlamp. Looking at the Sun the pupil is near
        // 2 mm, and nothing else in the sky is bright enough to show one.
        const int rsLaneCount = 1024;          // lanes around the source
        const int rsLaneWindow = 24;           // most lanes a pixel visits either side of its own
        const float rsNeedleWidthPx = 0.42;    // across a needle: the speckle, about an arcminute
        // The glow. Its fall is glare_sim.py's, and the CIE's glare formula falls the same way
        // over the same angles. It starts at the limb, as the core does.
        const float rsGlareFall = 2.8;         // the glow falls as r^-this
        const float rsGlareCorePx = 1.5;       // and flattens inside this
        // Its level at the source goes as peak^kappa. At kappa = 1 the glare would be a fixed
        // share of the light, and the Sun's would fill the screen whenever Venus's showed at
        // all. The eye adapts to what it looks at, so its floor rises with the source; at the
        // de Vries-Rose end of adaptation that is peak^0.5. Set between the two, by eye.
        const float rsGlareScatter = 0.008;    // the level for a peak of 1
        const float rsGlareKappa = 0.62;
        // What the glow must clear to be seen: a display level at the default exposure, and at
        // the game's (rsWhiteScale), so a glare draws in as the exposure comes down.
        float rsGlareFloor = 0.004 * rsWhiteScale();
        // There is no ceiling on the glow itself. It had one, a quarter of white, so the glare
        // never burnt out: a gray shelf round the Sun, where an eye looking at the Sun sees white.
        // Close to a bright source the glare now runs past white, and the whole of it is held at
        // what the engine's blooms may see (rsBloomWhite), where rsGlare ends.
        //
        // The lobes. What burns out is not round. glare_sim.py's needles are one width across,
        // about an arcminute, at every radius, so near the source there are a few dozen round the
        // circle where further out there are a thousand; the lanes below are a thousand at every
        // radius, lie seven to a pixel close in, and average out to an even glow there. So the glow
        // is first cut into lobes: rsLobeCount round the source, each split in two again and
        // again, a level joining in over the octave of radius below where its lobes are
        // rsLobeWidthPx wide and giving way beyond it as the next takes over, so a lobe is a
        // streak a few times its own radius long. A lobe's strength is speckle, exponential, and
        // multiplies what it sits in. The lobes show in step with the glow (rsLobeNear): where the
        // glare burns out its outline is uneven, and far out, where the lanes part into needles of
        // their own, nothing is changed. Lifting the ceiling alone gave a larger round disc. Count,
        // depth and strength by eye, from offline sheets (user, 2026-10-06: the subtlest of three).
        const int rsLobeCount = 12;            // lobes round the source at the coarsest level
        const int rsLobeLevels = 6;            // each level has twice the lobes of the last
        const float rsLobeWidthPx = 1.2;       // a level joins in where its lobes are this wide
        const float rsLobeAlong = 1.5;         // a lobe's cells along the radius, to the octave
        const float rsLobeAmp = 0.35;          // how far a lobe moves the glow
        float rsLobeNear = 0.3 * rsWhiteScale();   // the glow at which the lobes are half shown: a fifth of white
        // The veil: the eye's other glare. The needles are the diffraction part of its point
        // spread function; light is also scattered in the cornea, the lens and off the back of
        // the eye, smoothly and with little colour, and that veiling glare is what reads as a
        // corona round the Sun. The CIE's formula for it goes as 10/theta^3 + 5/theta^2 of the
        // light reaching the eye, theta in degrees, so it falls more slowly than the needles:
        // the 1/theta^2 term takes over past two degrees. The same glow and level as the needles,
        // at a share of it, falling between the formula's two terms; share and fall set by eye.
        // It clears the same floor, so a faint source has none, and Venus a faint few pixels.
        const float rsVeilShare = 0.5;
        const float rsVeilFall = 2.2;
        const float rsVeilCorePx = 1.5;
        // It sinks out of sight; it does not end. It was cut off at the needles' floor, which is
        // eight display levels near black, not one, so the veil fell in a straight line from
        // twenty levels to none and stopped: a soft disc with an edge, 110 pixels out round the
        // Sun at Saturn (user, 2026-10-08: "fall off smoother towards the edges"). The floor now
        // bends it instead, v = g^2 / (g + floor): what it was well above the floor, and below it
        // falling twice as fast as the glow, with no end until it is under one display level
        // (rsVeilSeen of the floor, where the glow is rsVeilEnds of it). So it reaches further
        // than the needles, faintly, and has its own furthest reach, drawn in over the last of it.
        const float rsVeilSeen = 0.125;
        const float rsVeilEnds = 0.4215;       // (seen + sqrt(seen^2 + 4 seen)) / 2
        const float rsVeilMaxPx = 320.0;
        // A glare shorter than this lies under its own source's core, so it is not drawn, and
        // fades in over the next as much again: it is the bright stars' twinkle that crosses it.
        const float rsGlareMinPx = 3.0;
        // The furthest a glare reaches past its source's edge; the Sun alone comes near it. It
        // was the furthest from the source's CENTRE, which a point's edge is. But a star's disc
        // grows as you close on it, and once it was 177 pixels in radius there was no room left
        // under the cap: the glare, and the burnt-out margin that makes the star look its
        // brightness, were squeezed out over the last tenth of the approach and then gone, leaving
        // a bare sphere that looked a size smaller (user, 2026-10-07).
        const float rsMaxRayPx = 180.0;
        // At this level and over, a pixel inside the source's own edge is burnt out whatever the
        // lanes and lobes say, and is not sent round them: a star close enough to fill the frame
        // would visit them for every pixel of it. Against white, as the ceiling it clears is.
        float rsGlareSureLevel = 100.0 * rsWhiteScale();
        // How sharply a star's or planet's glare leaves when its source leaves the frame: over
        // about the width of its core, since a point's light is in the frame or it is not, and
        // a glare left behind it streams in from nowhere. The Sun's is measured over its disc
        // instead, in the merge pass.
        const float rsBurstEdgePx = 2.0;

        // Colour, the paper's way. The pattern at wavelength lambda is the 575 nm one scaled by
        // lambda/575, so the glow's red runs further than its blue and a needle ends warm, while
        // the core of the glare stays the colour of whatever threw it. Six bands of the CIE 1931
        // observer in linear sRGB, summing to white: xyz is a band's weight, w is 575/lambda at
        // its middle. The negative weights are sRGB's gamut, clamped where the colour is summed.
        const int rsBandCount = 6;
        const vec4 rsBands[rsBandCount] = vec4[rsBandCount](
            vec4( 0.0561, -0.0658,  0.5416, 1.3529),   // 425 nm
            vec4(-0.0889,  0.0703,  0.5692, 1.2105),   // 475 nm
            vec4(-0.2752,  0.6151, -0.0263, 1.0952),   // 525 nm
            vec4( 0.5007,  0.4254, -0.0686, 1.0000),   // 575 nm
            vec4( 0.7211, -0.0379, -0.0145, 0.9200),   // 625 nm
            vec4( 0.0862, -0.0071, -0.0014, 0.8519)    // 675 nm
        );
        // Along a lane its brightness is speckle too, one spot (about a pixel) long, and each
        // spot is read at radius * 575/lambda like everything else, so it lands as a short radial
        // spectrum, blue side in: the paper's rainbow fringes along the needles.
        const float rsSpecklePx = 1.0;         // a spot's length along the lane
        const float rsSpeckle = 0.6;           // and how far it moves the lane's brightness
        // Beside the sharp core, a soft skirt: the needle as a slightly defocused eye sees it.
        const float rsNeedleSkirt = 0.45;      // weight of the skirt beside the core
        const float rsNeedleSkirtScale = 3.2;  // and how much wider it is
        // A source wider than the 20' ray-formation angle blurs its own needles, each convolved
        // with its disc as it looks on the screen, so zooming in, which grows the disc, softens
        // them as well. A flat disc spreads a line to sigma R/2, a limb-darkened Sun to about
        // 0.47 R; this is set below both, by eye, because the full amount washed the needles out
        // more than an eye seems to.
        const float rsDiscSpread = 0.35;
        // And no more than this, in pixels: what the Sun's own disc does to its needles from the
        // Earth. Left to grow with the disc, the blur was a third of a close star's radius and
        // more, while its glare reaches no further from the limb than a far one's: the needles
        // went, the lobes ran together, and a star seen from close by was a blob (user,
        // 2026-10-08: "it gets relatively stronger the closer you get to the source").
        const float rsDiscSpreadMaxPx = 2.0;
        //
        // The paper's glare moves in time, with the pupil and the particles in the eye. Both
        // were tried and both are left out: the pattern holds still.

        {{EyeColour.Glsl}}

        // How far the engine's Sun sphere has faded in, as Sun.frag fades it: none at 88 solar
        // radii, whole by 44. Our sprite of the Sun gives way to it by as much, so only one of
        // the two is ever showing; in a view that draws no sphere it never gives way.
        const float rsSphereWholeRadii = 44.0;
        const float rsSphereGoneRadii = 88.0;
        float rsSphereShown()
        {
            if (!rsSphereHere()) return 0.0;
            float radii = length(global.lighting.sunPosition.xyz) / max(global.lighting.sunRadius, 1.0);
            return 1.0 - smoothstep(rsSphereWholeRadii, rsSphereGoneRadii, radii);
        }

        // The glow's level at the source, from the peak its core would reach unclamped. Whatever
        // dims the source - distance, a limb, the edge of the frame - goes into the peak, so the
        // glare follows it by the same law.
        float rsGlareLevel(float peak)
        {
            return rsGlareScatter * pow(max(peak, 0.0), rsGlareKappa) * rsBurstStrength();
        }

        // How far the needles reach: where a strong lane, three times the mean, sinks to the
        // floor, in the reddest band, which reads the pattern furthest out. From the limb, and
        // capped from it (rsMaxRayPx). Zero means none.
        float rsNeedleReachPx(float level, float discPx)
        {
            float excess = 3.0 * level / rsGlareFloor;
            if (excess <= 1.0) return 0.0;
            float limb = rsGlareCorePx * sqrt(pow(excess, 2.0 / rsGlareFall) - 1.0)
                       / rsBands[rsBandCount - 1].w;
            float reach = discPx + min(limb, rsMaxRayPx);
            return reach - discPx < rsGlareMinPx ? 0.0 : reach;
        }

        // How far the veil reaches: where it sinks under a display level (rsVeilSeen). Past the
        // needles: slower to fall is what makes it a corona.
        float rsVeilReachPx(float level, float discPx)
        {
            float excess = rsVeilShare * level / (rsVeilEnds * rsGlareFloor);
            if (excess <= 1.0) return 0.0;
            float limb = rsVeilCorePx * sqrt(pow(excess, 2.0 / rsVeilFall) - 1.0);
            float reach = discPx + min(limb, rsVeilMaxPx);
            return reach - discPx < rsGlareMinPx ? 0.0 : reach;
        }

        // How far the glare reaches, needles or veil. Zero means no glare, and no quad grown to
        // hold one.
        float rsGlareReachPx(float level, float discPx)
        {
            return max(rsNeedleReachPx(level, discPx), rsVeilReachPx(level, discPx));
        }

        // One lane's random numbers, in [0, 1).
        float rsLane(int k, float salt)
        {
            return rsHash(vec3(float(k), salt, 17.0));
        }

        // Abramowitz and Stegun 7.1.26, good to 1.5e-7.
        float rsErf(float x)
        {
            float t = 1.0 / (1.0 + 0.3275911 * abs(x));
            float y = ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t
                        - 0.284496736) * t + 0.254829592) * t;
            return sign(x) * (1.0 - y * exp(-x * x));
        }

        // One cell of one level of the lobes: its strength, exponential, mean 1.
        float rsLobeCell(float lobe, float along, float level)
        {
            return -log(max(1.0 - rsHash(vec3(lobe, along + 31.0 * level, 91.0)), 1e-6));
        }

        // The lobes' strength in a direction, at a radius in pixels, for lobes widthPx wide where
        // they join in: mean 1, with detail down to that width at this radius. See rsLobeCount.
        float rsLobes(float theta, float radius, float widthPx)
        {
            float strength = 1.0;
            float logR = log2(max(radius, 1e-3));
            for (int o = 0; o < rsLobeLevels; o++)
            {
                float count = float(rsLobeCount << o);
                float home = count * widthPx / 6.28318531;      // where this level's lobes are widthPx wide
                float t = clamp(logR - log2(home) + 1.0, 0.0, 1.0);
                if (t <= 0.0) break;                            // and every finer level joins in further out
                t = t * t * (3.0 - 2.0 * t) * home / max(radius, home);
                float u = theta / 6.28318531 * count;
                float lobe = mod(floor(u), count);
                float next = mod(lobe + 1.0, count);
                float f = fract(u);
                f = f * f * (3.0 - 2.0 * f);
                float v = logR * rsLobeAlong + 7.3 * float(o);
                float cell = floor(v);
                float g = v - cell;
                g = g * g * (3.0 - 2.0 * g);
                float n = mix(mix(rsLobeCell(lobe, cell, float(o)), rsLobeCell(next, cell, float(o)), f),
                              mix(rsLobeCell(lobe, cell + 1.0, float(o)), rsLobeCell(next, cell + 1.0, float(o)), f),
                              g);
                strength *= 1.0 + rsLobeAmp * t * (n - 1.0);
            }
            return max(strength, 0.0);
        }

        // The glare at an offset from the source, in screen pixels, for a source whose own disc
        // is discPx in radius (0 for a point), at the level rsGlareLevel gave and the reach
        // rsGlareReachPx made of it. Not yet held to white: see rsGlare.
        //
        // fromLimb is how far the pixel is from the source's edge, in pixels: where the glow
        // starts, and what it falls with. For a round source that is the offset less the disc's
        // radius, and rsGlare passes it so. The Sun's burst passes its own when what is left of
        // the Sun is not round (rsSunBurst): the distance from the outline of the part still in
        // view. The needles and the lobes turn about the offset's origin either way, so it is one
        // glare whatever the shape, and it ends as far from the edge as a round source's would.
        vec3 rsGlareOf(vec2 offsetPx, float reachPx, float discPx, float level, float fromLimb)
        {
            // Nudged off its exact origin, which has no direction: left at zero, that pixel went
            // dark, which only showed once the origin could lie over a hull rather than the Sun.
            if (dot(offsetPx, offsetPx) < 1e-6) offsetPx = vec2(1e-3, 0.0);
            float r = length(offsetPx);
            // From the centre, as the reach is measured: the edge's distance, and the pixel's past it.
            float outPx = fromLimb + discPx;
            if (reachPx <= 0.0 || level <= 0.0 || outPx >= reachPx) return vec3(0.0);
            // Inside a bright source's own edge: burnt out, at the glow's own level there, which is
            // past what any caller holds it to in any colour.
            if (fromLimb <= 0.0 && level >= rsGlareSureLevel) return vec3(level);

            const float tau = 6.28318531;
            float theta = atan(offsetPx.y, offsetPx.x);
            if (theta < 0.0) theta += tau;
            // A resolved source blurs its lobes as it blurs its needles, up to rsDiscSpreadMaxPx.
            float spread = min(rsDiscSpread * discPx, rsDiscSpreadMaxPx);
            float lobeWidth = sqrt(rsLobeWidthPx * rsLobeWidthPx + spread * spread);

            // The veil, smooth and colourless, from the limb as the rest is. It sinks under what
            // shows by itself (rsVeilSeen); only where its furthest reach cuts it short is it
            // drawn in, over the last three tenths of its reach past the limb. It is cut into the
            // same lobes, or it would lay a round disc of white over the needles' uneven one: by
            // the needles' glow, which its own is half of at the limb.
            vec3 veil = vec3(0.0);
            float veilReach = min(rsVeilReachPx(level, discPx), reachPx);
            if (outPx < veilReach)
            {
                float x = fromLimb / rsVeilCorePx;
                float g = rsVeilShare * level * pow(1.0 + x * x, -0.5 * rsVeilFall);
                float shown = 2.0 * g / (2.0 * g + rsLobeNear);
                if (shown > 0.01) g *= 1.0 + shown * (rsLobes(theta, r, lobeWidth) - 1.0);
                float v = max(g * g / (g + rsGlareFloor) - rsVeilSeen * rsGlareFloor, 0.0);
                veil = vec3(v * (1.0 - smoothstep(0.7, 1.0, fromLimb / max(veilReach - discPx, 1e-3)))
                              * smoothstep(rsGlareMinPx, 2.0 * rsGlareMinPx, veilReach - discPx));
            }

            // Past the needles only the veil is left, and the lanes need not be visited at all.
            float needleReach = min(rsNeedleReachPx(level, discPx), reachPx);
            if (outPx >= needleReach) return veil;
            float sharp = rsNeedleWidthPx;
            float width = sqrt(sharp * sharp + spread * spread);
            // The skirt is a sharp needle's. Once the disc has blurred a needle wider there is
            // nothing left for it to add, and it would triple the lanes each pixel visits.
            float skirt = max(sharp * rsNeedleSkirtScale, width);

            float spacing = tau / float(rsLaneCount);
            int centre = int(floor(theta / spacing));
            int span = min(int(ceil(4.0 * skirt / (r * spacing))), rsLaneWindow);

            // Each band's glow here, cut into its lobes where it is bright. A band reads the
            // 575 nm glow, and the lobes, at radius * 575/lambda, like everything else.
            float glow[rsBandCount];
            for (int b = 0; b < rsBandCount; b++)
            {
                float x = fromLimb * rsBands[b].w / rsGlareCorePx;
                float g = level * pow(1.0 + x * x, -0.5 * rsGlareFall);
                float shown = g / (g + rsLobeNear);
                if (shown > 0.01) g *= 1.0 + shown * (rsLobes(theta, r * rsBands[b].w, lobeWidth) - 1.0);
                glow[b] = g;
            }

            // One lane's share of the glow: the lanes' spacing here over a lane's area, so they
            // average to the glow. Close in, where the window holds only part of a lane's
            // profile, the part it holds.
            float held = (float(span) + 0.5) * spacing * r * 0.70710678;
            float area = 2.50662827 * (width * rsErf(held / width)
                                       + rsNeedleSkirt * skirt * rsErf(held / skirt))
                       / (1.0 + rsNeedleSkirt);
            float share = r * spacing / max(area, 1e-6);

            vec3 total = vec3(0.0);
            for (int dk = -span; dk <= span; dk++)
            {
                int k = (centre + dk + rsLaneCount) % rsLaneCount;
                float d = theta - (float(k) + rsLane(k, 1.0)) * spacing;
                float along = r * cos(d);
                if (along <= 0.0) continue;             // one sided, so lengths can differ
                float across = r * sin(d);
                // A sum of three speckle intensities, each exponential, averaged: gamma(3).
                float strength = -(log(max(1.0 - rsLane(k, 5.0), 1e-6))
                                   + log(max(1.0 - rsLane(k, 6.0), 1e-6))
                                   + log(max(1.0 - rsLane(k, 7.0), 1e-6))) / 3.0;
                float a2 = across * across;
                float profile = (exp(-0.5 * a2 / (width * width))
                                 + rsNeedleSkirt * exp(-0.5 * a2 / (skirt * skirt)))
                              / (1.0 + rsNeedleSkirt);
                vec3 colour = vec3(0.0);
                for (int b = 0; b < rsBandCount; b++)
                {
                    float spot = 1.0 + rsSpeckle * rsFlicker(along * rsBands[b].w / rsSpecklePx,
                                                             float(k) + 23.0);
                    colour += rsBands[b].rgb * max(glow[b] * strength * spot - rsGlareFloor, 0.0);
                }
                total += profile * colour;
            }
            // Taken to nothing over the last fifth of their reach, where only the strongest lanes
            // are left, and faded in over the shortest glares.
            float fade = clamp((needleReach - outPx) / (0.2 * max(needleReach - discPx, 1e-3)), 0.0, 1.0)
                       * smoothstep(rsGlareMinPx, 2.0 * rsGlareMinPx, needleReach - discPx);
            return max(total, vec3(0.0)) * share * fade + veil;
        }

        // The glare of a round source, held to what the engine's blooms may see. That is past
        // white, so the glare burns out where it is bright enough, and the blooms add no second
        // glare to it.
        vec3 rsGlare(vec2 offsetPx, float reachPx, float discPx, float level)
        {
            return min(rsGlareOf(offsetPx, reachPx, discPx, level, max(length(offsetPx) - discPx, 0.0)),
                       vec3(rsBloomWhite));
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
        // How much of a source the air around the body takes, which is a different question
        // from what its rock takes, and answered much higher up. At Titan the haze is opaque
        // over a surface that cannot be seen at all, so a source behind it is gone whatever the
        // ground is doing. The engine's composite dims the pixels it draws through the air,
        // which is why a core and its halo fade through a limb - but a ray reaching past the
        // limb is a pixel looking at clear sky, so the source is faded by what IT looks through.
        //
        // LimbAir.cs publishes the air as the engine describes it: vertical optical depth and
        // scale height, Rayleigh then Mie, the same Visual numbers that draw the sky. The slant
        // depth is the vertical one times the Chapman function - how much longer a slanted path
        // through an exponential atmosphere is than a straight-up one. This is Schueler's form,
        // exactly as the engine's CPU transmittance writes it, but with the grazing constant
        // corrected from sqrt(x) to sqrt(pi x / 2): the engine's version reads a ray skimming
        // the limb as eighty percent of its true column, and the rendered sky - which is what
        // dims the Sun this is keeping pace with - integrates the whole of it.
        //
        // It works from inside the air as well as from orbit: upward rays take the first branch,
        // and a ray that dips through a tangent point on its way out takes the second.
        float rsChapman(float planetInHeights, float altitudeInHeights, float mu)
        {
            float x = planetInHeights + altitudeInHeights;
            float c = sqrt(1.5707963 * x);
            float atCamera = c * exp(-altitudeInHeights);
            if (mu >= 0.0) return atCamera / (c * mu + 1.0);
            float xt = sqrt(max(1.0 - mu * mu, 0.0)) * x;         // the tangent point
            float ct = sqrt(1.5707963 * xt);
            return 2.0 * ct * exp(planetInHeights - xt) - atCamera / (1.0 - c * mu);
        }

        bool rsHasAir()
        {
            vec2 rayleigh = unpackHalf2x16(uint(global.celestial.pad0));  // depth, height in km
            vec2 mie = unpackHalf2x16(uint(global.celestial.pad1));
            return (rayleigh.x > 0.0 && rayleigh.y > 0.0) || (mie.x > 0.0 && mie.y > 0.0);
        }

        // Slant optical depth of one ray through the nearby body's air.
        float rsAirDepth(float planet, float altitude, float mu)
        {
            vec2 rayleigh = unpackHalf2x16(uint(global.celestial.pad0));
            vec2 mie = unpackHalf2x16(uint(global.celestial.pad1));
            float tau = 0.0;
            if (rayleigh.x > 0.0 && rayleigh.y > 0.0)
            {
                float h = rayleigh.y * 1000.0;
                tau += rayleigh.x * rsChapman(planet / h, altitude / h, mu);
            }
            if (mie.x > 0.0 && mie.y > 0.0)
            {
                float h = mie.y * 1000.0;
                tau += mie.x * rsChapman(planet / h, altitude / h, mu);
            }
            return tau;
        }

        //
        // Every source carries the WHOLE air of its ray, however near it is. A source nearer than the
        // body's centre used to be taken as in front of the air and left undimmed, to be undone and
        // dimmed like any other pixel and so come out whole. Two things were wrong with that. A
        // vessel coming out from behind the limb is past the limb's air and nearer than the centre
        // both: from 400 km up the limb is 2,300 km away and the centre 6,800. And what is left of
        // a source that keeps its light is the air the game DRAWS over the air assumed here, which
        // only holds while the two agree: they are the Visual block's plain exponentials here, and
        // whatever the game's shaders make of it there - with Real Atmospheres' measured densities
        // 7 times less air over Venus's cloud tops, 10 times less through Titan's upper haze. So a
        // distant vessel flared as it rose out of a limb (user, 2026-10-06). Carrying its ray's air,
        // a source is undone by that same air and is left with the game's dimming, once, whatever
        // the game draws: a vessel in front of a limb's air is dimmed with what is behind it, as
        // the game without this mod does it, and nothing can come out brighter than it is.
        float rsAirVisibility(vec3 dirToSource, float sourceDistM)
        {
            if (!rsHasAir()) return 1.0;                           // no air, or none worth it
            vec3 centre = global.lighting.planetPosition.xyz;
            float d = length(centre);
            if (d < 1.0) return 1.0;
            float planet = global.lighting.planetRadius;
            float mu = -dot(centre / d, dirToSource);              // up is away from the centre
            return exp(-rsAirDepth(planet, max(d - planet, 0.0), mu));
        }

        // The same for a source with a size, over its disc instead of along its centre. The air
        // under a limb is a few scale heights deep, while the Sun as it looks spans twenty km of
        // limb altitude from low orbit and thousands from the Moon's distance. The ray through
        // its centre meets the ground when half of it is still in clear sky, and the whole Sun
        // went out with that ray, in Earth's and Mars's air; Titan's haze dims it long before.
        // The disc is sliced parallel to the limb: the share the rock leaves is a circular
        // segment, exactly, returned in segment, and the air is averaged over that share at
        // eight heights, within a percent of the full integral and smooth as the Sun sets.
        float rsAirOverDisc(vec3 dirToSource, float sourceDistM, float radius, out float segment)
        {
            segment = 1.0;
            if (!rsHasAir()) return 1.0;
            vec3 centre = global.lighting.planetPosition.xyz;
            float d = length(centre);
            if (d < 1.0 || d >= sourceDistM * 0.9999) return 1.0;
            float planet = global.lighting.planetRadius;
            float altitude = max(d - planet, 0.0);
            float sep = acos(clamp(dot(centre / d, dirToSource), -1.0, 1.0));
            float limb = asin(clamp(planet / d, 0.0, 1.0));
            float a = clamp((limb - sep) / max(radius, 1e-9), -1.0, 1.0);   // the limb, across the disc
            segment = (acos(a) - a * sqrt(1.0 - a * a)) / rsPi;
            if (segment <= 0.0) return 0.0;
            float air = 0.0, weights = 0.0;
            for (int k = 0; k < 8; k++)
            {
                float u = mix(a, 1.0, (float(k) + 0.5) / 8.0);            // up the visible share
                float w = sqrt(max(1.0 - u * u, 0.0));                     // the disc's width there
                air += w * exp(-rsAirDepth(planet, altitude, -cos(sep + u * radius)));
                weights += w;
            }
            return weights > 0.0 ? air / weights : 1.0;
        }

        float rsVisibility(vec3 dirToSource, float sourceDistM, float softRad)
        {
            // A source with a size, behind air, is covered by the air and the rock together, over
            // its disc. rsAirOverDisc takes the nearby body whole, so the loop leaves it out.
            bool overDisc = softRad > 0.0 && rsHasAir();
            vec3 airBody = global.lighting.planetPosition.xyz;
            float vis = 1.0;
            for (int i = 0; i < global.celestial.bodyCount; i++)
            {
                vec4 body = global.celestial.bodies[i];
                if (overDisc && distance(body.xyz, airBody) < 1e-3 * body.w) continue;
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
            if (!overDisc) return min(vis, rsAirVisibility(dirToSource, sourceDistM));
            float segment;
            float air = rsAirOverDisc(dirToSource, sourceDistM, softRad, segment);
            return min(vis, segment * air);
        }

        // The direction a screen pixel looks along, about the camera, reconstructed the way the
        // engine's atmosphere pass reconstructs a sky pixel (Shared.glsl's getWorldPosition at
        // depth 0), so the air read here for a pixel is the air that pass dims it by.
        vec3 rsPixelDirection(vec2 pixel)
        {
            vec2 uv = pixel / vec2(global.camera.screenWidth, global.camera.screenHeight);
            vec4 view = global.camera.inverseProjection * vec4(uv * 2.0 - 1.0, 0.0, 1.0);
            return normalize((global.camera.inverseView * (view / view.w)).xyz);
        }

        // What to multiply a sky pixel by before the engine dims it. The engine's atmosphere pass
        // multiplies every sky pixel by the transmittance along its own ray once we have drawn,
        // and a star's flux already carries the air in front of the star - which is also the air
        // every ray of its glare carries, wherever on the screen it lands. Dividing by the pixel's
        // own air undoes the engine's, so a core is dimmed once, still in the engine's colours,
        // and a glare lying over clearer or thicker air than its source keeps the source's air.
        // Stars had been dimmed twice: at 10 degrees up on Earth, most of an extra magnitude.
        // Held under 32, which is a pixel on the horizon.
        float rsAirUndo(vec2 pixel)
        {
            if (!rsHasAir()) return 1.0;
            return 1.0 / max(rsAirVisibility(rsPixelDirection(pixel), 1e30), 1.0 / 32.0);
        }

        // The same for the source itself, worked out once in the vertex shader. A pixel is never
        // undone by more than its source is: where the air assumed here is thicker than the air the
        // game draws, a halo or a ray lying over the limb would otherwise be multiplied up past the
        // dimming it gets. Lying over thicker air than its source it now dims a little there instead.
        float rsAirUndoOf(vec3 dirToSource)
        {
            if (!rsHasAir()) return 1.0;
            return 1.0 / max(rsAirVisibility(dirToSource, 1e30), 1.0 / 32.0);
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
        // glare reaches, also in px, and zero when there is none
        layout (location = 2) out vec4 outStar;
        // The glare's level at the source: see rsGlareLevel.
        layout (location = 3) out float outGlareLevel;
        // How far the source's own air is undone, the most any of its pixels may be: see rsAirUndoOf.
        layout (location = 4) out flat float outSourceUndo;

        {{Tuning}}
        const float rsMinGlowPx = 1.5;            // a faint star must still cover a pixel

        // The game's own stars other than the lighting one carry their place relative to it, in
        // parsecs times a power of two (GameStars.cs): any coordinate past 2^40 is one of them,
        // since the catalogue's largest is 20 kpc. They are kept in two halves, each at its own
        // scale and told apart at 2^83, and a frame draws the half its camera block names
        // (rsGameStarsSecond). The other half is about another lighting star, a frame before this
        // one or after it: the buffer is shared by every frame still being drawn, and is rewritten
        // when the lighting star changes.
        const float rsGameStarMark = 1.099511627776e12;
        const float rsGameStarSplit = 9.671406556917033e24;
        const int rsGameStarScale = -72;
        const int rsGameStarScaleSecond = -116;

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
            vec4 packedData = unpackRGBA(packed);

            // Three kinds of instance (GameStars.cs). The star lighting the scene sits at the
            // origin. The game's other stars carry their place relative to it, scaled past
            // anything in the catalogue. The rest is the catalogue, in parsecs from the Sun.
            bool lighting = dot(position, position) < 1e-12;
            vec3 extent = abs(position);
            float furthest = max(extent.x, max(extent.y, extent.z));
            bool gameStar = furthest > rsGameStarMark;
            bool secondHalf = furthest > rsGameStarSplit;

            // A magnitude byte of zero is a star not to draw: a catalogue star the game draws as
            // one of its own, or room kept for one. And with the stars switched off in the
            // settings the engine skips this pass, and StarsOff draws it anyway so the Sun stays;
            // the game's own stars stay too, as the engine's dots for them did, and only the
            // catalogue is left out, placed off the screen, where nothing of it is drawn.
            // And a game star in the half that is not this frame's: see rsGameStarSplit.
            if (packedData.w <= 0.0 || (rsStarsHidden() && !lighting && !gameStar)
                || (gameStar && secondHalf != rsGameStarsSecond()))
            {
                outColor = vec3(0.0);
                outUv = vec2(0.5);
                outStar = vec4(0.0);
                outGlareLevel = 0.0;
                outSourceUndo = 1.0;
                gl_Position = vec4(2.0, 2.0, 0.5, 1.0);
                return;
            }

            // Unpack the instance data. The catalogue stores each colour with its brightest
            // channel at 1, which keeps the bytes' precision; here it is brought to unit
            // luminance instead, so the magnitude alone sets how bright a star is. Left at its
            // peak, every coloured star was dimmed by its own colour - the Sun by 0.11 mag, blue
            // stars and red giants by 0.4 to 0.6. The colours are raw, as the light is; the eye's
            // adaptation to the light it is in is applied to the whole frame (Adaptation).
            outColor = packedData.xyz / max(dot(packedData.xyz, rsLuma), 1e-3);
            // The lighting star in the colour of its light, as its sphere is (Sun.frag), from this
            // frame's own lighting block. It is the same colour as its instance's, which is
            // rewritten when the lighting star changes: a frame still being drawn would show the
            // star it was lit by in the next one's colour.
            if (lighting && global.lighting.lpPad4 != 0)
            {
                vec3 light = max(global.lighting.sunColor.rgb, vec3(0.0));
                float lightLuma = dot(light, rsLuma);
                if (lightLuma > 1e-6) outColor = light / lightLuma;
            }

            // The instance carries the star's true position in parsecs rather than a direction,
            // so both where it appears and how bright it looks follow from where the observer
            // is. Move, and near stars slide against far ones: parallax, for free, from the
            // camera position the engine already publishes.
            //
            // Not for the game's own stars. A place in parsecs from the Sun keeps a float's 7
            // digits, millions of kilometres at Alpha Centauri, and from a planet there its stars
            // would wander by minutes of arc as the camera moved. The lighting star is where the
            // engine's lighting block puts it, relative to the camera, which holds its precision
            // anywhere, and the game's other stars are placed from it.
            vec3 lightPc = global.lighting.sunPosition.xyz / rsParsecMetres;
            vec3 toStar = position - global.camera.cameraPosition.xyz / rsParsecMetres;
            if (lighting && dot(lightPc, lightPc) > 0.0)
                toStar = lightPc;
            else if (gameStar)
                toStar = ldexp(position, ivec3(secondHalf ? rsGameStarScaleSecond : rsGameStarScale)) + lightPc;
            // The direction is the direction at any distance, and the lighting star's distance is
            // its distance. Both were floored at a millionth of a parsec, 0.2 AU, which is further
            // than a red dwarf's planets are from it: inside that the star's direction came out
            // shorter than a unit, so every angle taken from it below was wrong (a limb and its
            // air were worked out for a star nearer the horizon than it was: from Proxima b, never
            // more than 14 degrees from it), and the star grew no brighter. The Sun never showed
            // it, because its sphere is whole inside 44 of its radii, which is that same 0.2 AU.
            // The floor stays for the catalogue, where it only guards the log.
            float away = length(toStar);
            vec3 starDir = toStar / max(away, 1e-30);
            float distancePc = max(away, lighting ? 1e-12 : 1e-6);

            // The star lighting the scene is drawn here at every distance. There is no handover:
            // the engine's own sprite is switched off entirely, because any threshold for
            // swapping between them is a threshold in pixels, and a pixel is a different distance
            // at every field of view. The sphere still renders while the star is resolved, with
            // this glare around it. Its magnitude comes with its disc, as the C# side has it;
            // every other star's is in its byte.
            float absMag = lighting && global.lighting.lpPad4 != 0 ? rsLightWord().y
                                                                    : rsAbsMag(packedData.w);
            float flux = rsFlux(absMag, distancePc);

            // The lighting star's own angular size, in pixels: it is a resolved disc, and both
            // the profile below and the scintillation above need to know that.
            float discPx = lighting ? rsLightDiscPx() : 0.0;

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
                float whitePx = rsPsfCore * sqrt(max(pow(max(fullPeak / ({{MaxOutput}} * rsWhiteScale()), 1.0),
                                                         1.0 / rsPsfBeta) - 1.0, 0.0));
                // The ring draws in to the limb as the sphere takes over - see Star.frag.
                sunLooksRad = sunAngularRad * (discPx + whitePx * (1.0 - rsSphereShown())) / discPx;
            }
            float seen = rsVisibility(starDir, distancePc * rsParsecMetres, sunLooksRad);

            // Vessels too, for the Sun. They are nowhere a shader can reach, so VesselOcclusion
            // casts the engine's own part raycasts across the Sun's disc on the CPU and leaves
            // the share it found hidden in the low half of the celestial block's last spare word.
            // Zero, which is also what the engine writes there, means nothing in the way. The
            // high half is the rings', for the merge pass: the rings dim this sprite themselves.
            if (discPx > 0.0)
                seen *= 1.0 - clamp(unpackHalf2x16(uint(global.celestial.pad2)).x, 0.0, 1.0);
            flux *= seen;

            // And what an eye makes of its colour at this brightness (EyeColour.cs): the colour of a faint star
            // fades into the eye's white, blue first, as a person's sky is mostly white stars with a few bright
            // orange ones. Its light is unchanged; only what of its colour the cones can see.
            outColor = rsSeenColour(outColor, rsMagRef - 2.5 * log(max(flux, 1e-30)) * 0.4342944819);

            // Moffat beta = 2 at unit energy peaks at 1/(pi*core^2), so the profile crosses
            // one display level at this radius. Sizing the quad to it means the sprite is
            // exactly the star's visible extent and never a disc with a hard edge. A display
            // level at the game's exposure: see rsWhiteScale.
            float peak = flux * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float glowPx = clamp(
                rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels / rsWhiteScale(), 1.0 / rsPsfBeta) - 1.0, 0.0)),
                rsMinGlowPx, rsMaxGlowPx);

            // A resolved source is its disc convolved with the profile, so the halo starts at
            // the limb rather than at the centre. As the disc falls below a pixel this becomes
            // an ordinary point source on its own, with no threshold to cross - which is what
            // stops the Sun snapping to a smaller size when the sphere stops being drawn.
            // The glare comes from the same peak and reaches further than the halo for anything
            // bright enough to show one, so the quad takes whichever wins.
            // The Sun's glare is not drawn here at all: it goes over the finished image, in the
            // merge pass, because it is an optic effect and belongs on top of the world.
            float glareLevel = discPx > 0.0 ? 0.0 : rsGlareLevel(peak);
            float burstPx = rsGlareReachPx(glareLevel, 0.0);

            // Drawn on the same shell the game uses, but along the direction just computed.
            vec4 worldPosition = global.camera.viewProjection * vec4(starDir * rsStarShell, 1);

            // Off the edge of the frame is a kind of occlusion as well: light that never
            // entered has no business leaving scatter behind. The halo is small enough to see
            // itself out, which is why the glare already behaves. The magnitude of w keeps
            // anything behind the camera outside rather than mirrored into view.
            vec2 ndc = worldPosition.xy / max(abs(worldPosition.w), 1e-6);
            vec2 pastEdge = 0.5 * vec2(global.camera.screenWidth, global.camera.screenHeight)
                          * (abs(ndc) - vec2(1.0));

            // The quad keeps the size the UNFADED glare asked for, so that the halo - which is
            // taken to nothing over the last fraction of the quad - renders the same whatever
            // the glare is doing. Tying the two together made the halo change as the glare
            // went, which is not a thing the halo should know about.
            // The quad is a fan of eight triangles with its corners on a circle, so its flat
            // edges come in to cos(22.5 deg) of it. With the corners on the content's own radius,
            // a close Sun's white ring - and past that its disc - was cut to an octagon; the
            // corners go that much further out instead, so the whole circle is inside.
            float spritePx = (discPx + max(glowPx, burstPx)) / rsFanInradius;
            glareLevel *= 1.0 - smoothstep(-rsBurstEdgePx, rsBurstEdgePx,
                                           max(pastEdge.x, pastEdge.y));
            burstPx = rsGlareReachPx(glareLevel, 0.0);

            outUv = uv[gl_VertexIndex];
            outStar = vec4(flux, spritePx, discPx, burstPx);
            outGlareLevel = glareLevel;
            outSourceUndo = rsAirUndoOf(starDir);

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
        layout (location = 2) in vec4 inStar;     // flux, sprite px, disc px, glare reach px
        layout (location = 3) in float inGlareLevel; // the glare's level at the source
        layout (location = 4) in flat float inSourceUndo; // the most a pixel is undone by: its source's

        layout (location = 0) out vec4 outColor;

        {{Tuning}}

        void main(void)
        {
            float r = length(inUv.xy - vec2(0.5f)) * 2.0f;   // 0 at the centre, 1 at the quad's edge
            // Pixels from the centre, then from the LIMB: a resolved source is its disc
            // convolved with the profile, so the halo begins at the edge of the disc. For a
            // point source the disc radius is zero and this is the ordinary profile.
            float px = r * inStar.y;
            // Where the engine's sphere takes the Sun over, the white ring - the disc spread past
            // its limb by the point spread function - draws in to the limb as the sphere fades
            // in, so the two are one size by the time the sphere is whole: the eye resolving the
            // Sun's real size as it closes on it. See rsSphereShown.
            float spread = inStar.z > 0.0 ? max(1.0 - rsSphereShown(), 1e-3) : 1.0;
            float x = max(px - inStar.z, 0.0) / (rsPsfCore * spread);

            // Moffat, normalised to unit energy:
            //     I(r) = (beta-1)/(pi a^2) * (1 + (r/a)^2)^-beta
            // Falling wings rather than a hard edge are what diffraction and seeing both leave,
            // and they are why a bright star reads as a point with a halo rather than a disc.
            float psf = (rsPsfBeta - 1.0f)
                      / (rsPi * rsPsfCore * rsPsfCore * pow(1.0f + x * x, rsPsfBeta));

            float intensity = inStar.x * rsBrightness * psf;

            // Take the last of it to zero before the fan's flat edges, so the sprite never shows:
            // over the halo's outer part only, since a disc's own edge must not be dimmed.
            float contentPx = inStar.y * rsFanInradius;
            float edgeFade = smoothstep(contentPx, contentPx - 0.15 * max(contentPx - inStar.z, 1e-3), px);
            intensity *= edgeFade;

            // The Sun gives brightness back as it fills the frame - see SunExposureKnee.
            // inStar.z is the disc radius, and is zero for everything else in the catalogue.
            // Every other star is held to what the engine's blooms may see, rsBloomWhite: past
            // white only the blooms would show it, as a second glare around its own.
            bool sun = inStar.z > 0.0;
            float cap = sun ? {{SunLevelGlsl}} : rsBloomWhite;

            // The glare goes beside the core rather than into it: it is held to white by itself
            // (rsGlare), and where it is fainter than that it keeps its colour, which a ceiling
            // meant for a saturating point would take back out of it. How far it reaches was
            // settled in the vertex shader, where the quad was sized to hold it.
            vec3 burst = rsGlare((inUv - vec2(0.5f)) * 2.0f * inStar.y, inStar.w, 0.0, inGlareLevel)
                       * edgeFade;

            // A star's flux already carries its air, so the engine's dimming of this pixel is
            // undone - see rsAirUndo. Not the Sun's: its core sits at its ceiling whatever the
            // air, so the engine's is the only dimming it gets, which is what turns it orange as
            // it sets.
            float undo = sun ? 1.0 : min(rsAirUndo(gl_FragCoord.xy), inSourceUndo);
            // And as its ring draws in, the Sun's sprite hands its light to the sphere over the
            // same distances, so nothing of it is left under the sphere to show through the
            // sphere's softened edge once its limb darkening shows: rsSphereShown.
            float giveWay = sun ? 1.0 - rsSphereShown() : 1.0;
            // A bright star's glare can burn out as its core does (rsGlare), so the two together
            // are held to what the blooms may see, as the core alone was. Not the Sun's: its
            // glare is the merge pass's, and its core has its own level.
            vec3 light = vec3(min(intensity, cap)) + burst;
            if (!sun) light = min(light, vec3(rsBloomWhite));
            outColor = vec4(inColor.rgb * light * undo * giveWay, 1.0f);
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
        // The quad's radius and the glare's reach, both in pixels. The first used to be
        // scalePixel itself; a glare reaches past the halo, so now it need not be.
        layout (location = 4) out flat float outSpritePx;
        layout (location = 5) out flat float outBurstPx;
        layout (location = 6) out flat float outGlareLevel; // see rsGlareLevel
        layout (location = 7) out flat float outSourceUndo; // see rsAirUndoOf

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
            outSourceUndo = rsAirUndoOf(instanceData.positionEgo / max(dist, 1.0f));
            glowPx = rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels, 1.0f / rsPsfBeta) - 1.0f, 0.0f));

            // Venus and Jupiter clear the glare's floor, Neptune does not. The quad takes
            // whichever reaches further, the halo or the glare.
            float glareLevel = rsGlareLevel(peak);
            float burstPx = rsGlareReachPx(glareLevel, 0.0f);

            vec4 clipPosition = global.camera.viewProjection * vec4(instanceData.positionEgo, 1);

            // Off the edge of the frame is occlusion too - see the star shader, which does the
            // same thing for the same reason, and sizes its quad the same way.
            vec2 ndc = clipPosition.xy / max(abs(clipPosition.w), 1e-6f);
            vec2 pastEdge = 0.5f * vec2(global.camera.screenWidth, global.camera.screenHeight)
                          * (abs(ndc) - vec2(1.0f));

            // With its corners past the content, as the star shader's: see rsFanInradius. At a
            // higher exposure than the default the halo shows further out than glowPx, which is
            // its reach at the default and stays what carries the peak, so the quad grows to it
            // as a star's does; it is never smaller than glowPx, which the fragment shader takes
            // the quad to hold.
            float shownPx = rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels / rsWhiteScale(), 1.0f / rsPsfBeta) - 1.0f, 0.0f));
            float spritePx = max(max(glowPx, min(shownPx, rsMaxGlowPx)), burstPx) / rsFanInradius;
            glareLevel *= 1.0f - smoothstep(-rsBurstEdgePx, rsBurstEdgePx,
                                            max(pastEdge.x, pastEdge.y));
            burstPx = rsGlareReachPx(glareLevel, 0.0f);

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
            outGlareLevel = glareLevel;
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
        layout (location = 6) in flat float inGlareLevel;
        layout (location = 7) in flat float inSourceUndo;

        layout (location = 0) out vec4 outColor;

        {{Tuning}}

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
            // To nothing before the fan's flat edges, as the star shader does.
            float contentPx = quadPx * rsFanInradius;
            float edgeFade = smoothstep(contentPx, 0.85f * contentPx, r * quadPx);
            intensity *= edgeFade;
            // The same glare, from the same eye, as the stars get - and beside the core for the
            // same reason.
            vec3 burst = rsGlare((inUV - vec2(0.5f)) * 2.0f * quadPx, inBurstPx, 0.0, inGlareLevel)
                       * edgeFade;

            // The engine has already scaled this colour by its own phase term. Ours is in the
            // magnitude, so take the hue at unit luminance and leave the brightness to that. At
            // its brightest channel, as it was, a coloured planet was dimmed by its own colour.
            float luma = dot(inColor, rsLuma);
            vec3 tint = luma > 1e-4f ? inColor / luma : vec3(1.0f);

            // The core is clamped, to what the engine's blooms may see (rsBloomWhite), and so is
            // the core with its glare: close to a bright source the glare burns out too (rsGlare),
            // and the two together would be twice that. Further out the rays are faint and keep
            // their colour. The engine's dimming of this pixel by its air is undone, as for a
            // star, and by no more than the source's own: see rsAirUndo.
            float brightness = min(intensity, rsBloomWhite);
            vec3 rgb = tint * min(vec3(brightness) + burst, vec3(rsBloomWhite))
                     * min(rsAirUndo(gl_FragCoord.xy), inSourceUndo);
            // This pipeline blends by source alpha, and alpha follows the brightest channel, so
            // the core covers what is behind it. The colour is divided by that alpha, so a halo
            // or a ray ADDS its light, as a star's does. Written straight, the blend multiplied
            // it by its own alpha: everything under 1 went in as its own square, so a planet's
            // halo read smaller than a star's of the same magnitude and Venus's rays all but
            // vanished.
            float cover = min(max(max(rgb.r, rgb.g), rgb.b), 1.0f);
            outColor = vec4(rgb / max(cover, 1e-6f), cover);

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
