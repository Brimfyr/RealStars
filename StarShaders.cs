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
          "PostProcess/kawase_downsample.comp", "PostProcess/kawase_downsample_with_thresholds.comp" };

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
        // the Sun is in view. The spokes and ghosts in sunbloom_blur.comp are left alone here:
        // SunGlow sets SunDot to zero, and that gates both.
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
         + "    vec3 rsSun = rsBloomSun();\n"
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
         + "    vec3 rsSun = rsBloomSun();\n"
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

        // The Sun's white, as the merge pass finds it: its centre as uv, and its radius in screen
        // pixels - the disc, the ring its profile holds over rsBloomWhite past the limb, and two
        // pixels for a tap's footprint. A radius of zero when it is behind the camera.
        vec3 rsBloomSun()
        {
            vec3 sunEgo = global.lighting.sunPosition.xyz;
            float dist = length(sunEgo);
            vec4 clip = global.camera.viewProjection * vec4(sunEgo, 1.0);
            if (dist <= 0.0 || clip.w <= 0.0) return vec3(0.0);
            float mag = rsSunAbsMag + 5.0 * log(dist / (10.0 * rsParsecMetres)) * 0.4342944819;
            float peak = pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef))
                       * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float discPx = max(intBitsToFloat(global.lighting.lpPad1), 0.0);
            float whitePx = rsPsfCore * sqrt(max(pow(max(peak / rsBloomWhite, 1.0),
                                                     1.0 / rsPsfBeta) - 1.0, 0.0));
            return vec3(clip.xy / clip.w * 0.5 + 0.5, discPx + whitePx + 2.0);
        }

        // One tap of a downsample, carrying `weight` samples, as the bloom may see it: inside the
        // Sun's white, no brighter than rsBloomWhite. What is drawn is untouched.
        vec3 rsBloomTap(vec3 tap, vec2 uv, float weight, vec3 sun)
        {
            vec2 px = (uv - sun.xy) * vec2(global.camera.screenWidth, global.camera.screenHeight);
            if (dot(px, px) >= sun.z * sun.z) return tap;
            float luma = dot(tap, vec3(0.2126, 0.7152, 0.0722));
            float limit = rsBloomWhite * weight;
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

        // Where the Sun is, what of it is left to see, and where that is, from the finished frame.
        //
        // Being drawn after the world means the depth buffer is complete, and that answers what
        // is in front of the Sun better than anything worked out beforehand: hulls, ridges,
        // moons, any of it, to the pixel. The Sun as it LOOKS - its disc and the white ring the
        // profile saturates past the limb - is sampled at rsSunSamples points on a sunflower
        // spiral, spread evenly over its area. A sample lets the Sun's light through unless
        // something drawn nearer than the Sun covers it or it is off the frame, and the air
        // along its own ray dims what it lets through, since air writes no depth. The Sun's own
        // sphere writes none either, so it cannot hide itself.
        //
        // The glare is the light that got in. It is as strong as what the samples let through,
        // by the same law as distance (rsGlareLevel), and it radiates from where that light is:
        // their centre, weighted by it. A hull across three quarters of the Sun leaves the glare
        // coming from the sliver still in view, a limb lifts it as the Sun sets into the air, and
        // the frame's edge keeps it on the part still on screen - where before it came from a
        // centre that was out of sight in every one of those cases. The part left open, as a disc
        // of the same area, is what the glare's glow starts at and widens its needles by.
        const int rsSunSamples = 32;

        vec3 rsSunBurst(vec2 pixel, ivec2 dim)
        {
            if (rsBurstStrength() <= 0.0) return vec3(0.0);

            vec3 sunEgo = global.lighting.sunPosition.xyz;
            float dist = length(sunEgo);
            if (dist <= 0.0) return vec3(0.0);
            vec4 clip = global.camera.viewProjection * vec4(sunEgo, 1.0);
            if (clip.w <= 0.0) return vec3(0.0);                   // behind the camera
            vec2 sunPx = (clip.xy / clip.w * 0.5 + 0.5) * vec2(dim);

            // The whole Sun, before anything is in the way: how big it looks, and how far its
            // glare could reach from anywhere in that.
            float mag = rsSunAbsMag + 5.0 * log(dist / (10.0 * rsParsecMetres)) * 0.4342944819;
            float peak = pow(10.0, -0.4 * (rsCompressMagnitude(mag) - rsMagRef))
                       * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float discPx = max(intBitsToFloat(global.lighting.lpPad1), 0.0);
            float whitePx = rsPsfCore * sqrt(max(pow(max(peak / rsMaxOutput, 1.0),
                                                     1.0 / rsPsfBeta) - 1.0, 0.0));
            // The white ring is our sprite's, and it draws in to the limb as the sphere takes
            // over (see Star.frag): once the sphere is whole, the Sun looks like its disc.
            float looksPx = discPx + whitePx * (1.0 - rsSphereShown());
            float reach = rsGlareReachPx(rsGlareLevel(peak), discPx);
            vec2 fromSun = pixel - sunPx;
            float outer = reach + looksPx;
            if (reach <= 0.0 || dot(fromSun, fromSun) > outer * outer) return vec3(0.0);

            // The camera's far plane is one AU - exactly - so past Earth's orbit the Sun lies
            // beyond it and its reversed depth goes negative. Every sky pixel is then "nearer"
            // than the Sun, and the burst was judged hidden and went out. Clamped at zero, which
            // is what the engine's own flare does with its sun depth: the Sun is then behind
            // everything drawn, which a Sun past the far plane is, and in front of empty sky.
            float sunDepth = max(clip.z / clip.w, 0.0);
            // Each sample's sight line, for its air: the Sun's, turned across the screen the way
            // the engine's pixels turn.
            bool air = rsHasAir();
            vec3 toSun = sunEgo / dist;
            vec3 base = rsPixelDirection(sunPx);
            vec3 perX = rsPixelDirection(sunPx + vec2(1.0, 0.0)) - base;
            vec3 perY = rsPixelDirection(sunPx + vec2(0.0, 1.0)) - base;
            float light = 0.0, open = 0.0;
            vec2 centre = vec2(0.0);
            for (int i = 0; i < rsSunSamples; i++)
            {
                float t = (float(i) + 0.5) / float(rsSunSamples);
                float turn = float(i) * 2.39996323;                 // the golden angle
                vec2 offset = looksPx * sqrt(t) * vec2(cos(turn), sin(turn));
                vec2 at = sunPx + offset;
                vec2 inside = min(at, vec2(dim) - at);              // in from the nearest edges
                float w = clamp(min(inside.x, inside.y) + 0.5, 0.0, 1.0);
                if (w <= 0.0) continue;
                // Reversed depth: nearer is larger, and the Sun is almost at zero.
                if (texelFetch(samplerDepth, ivec2(clamp(at, vec2(0.0), vec2(dim) - 1.0)), 0).r > sunDepth)
                    continue;
                open += w;
                if (air)
                    w *= rsAirVisibility(normalize(toSun + perX * offset.x + perY * offset.y), dist);
                light += w;
                centre += w * at;
            }
            if (light <= 0.0) return vec3(0.0);
            centre /= light;
            float seen = light / float(rsSunSamples);
            float discSeen = discPx * sqrt(open / float(rsSunSamples));

            vec3 sunLight = global.lighting.sunColor.rgb;
            vec3 tint = sunLight / max(max(sunLight.r, sunLight.g), max(sunLight.b, 1e-6));
            float level = rsGlareLevel(peak * seen);
            return tint * rsGlare(pixel - centre, rsGlareReachPx(level, discSeen), discSeen, level);
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
        const float rsSunAbsMag = 4.83;        // what the catalogue carries for the Sun
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
        // The sprites are fans of eight triangles, corners on a circle; this is where their flat
        // edges come in to, as a share of it: cos(22.5 deg).
        const float rsFanInradius = 0.92387953;
        // The most the engine's blooms may see of any of our sources. Past about 1.2 the default
        // tonemap is already white (Hable at exposure 1.25), so whatever a core writes above that
        // shows only through the blooms: the threshold bloom adds a third of everything over 3
        // back as a wide glow, and the global bloom mixes a tenth of a blur into the image. Both
        // drew a second glare around sources that carry their own. 1.5 is still white after the
        // global bloom takes its tenth, and half the threshold.
        const float rsBloomWhite = 1.5;
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
        const float rsGlareFloor = 0.004;      // what the glow must clear to be seen
        const float rsGlareCeiling = 0.25;     // the most the glow adds, however bright
        // A glare shorter than this lies under its own source's core, so it is not drawn, and
        // fades in over the next as much again: it is the bright stars' twinkle that crosses it.
        const float rsGlareMinPx = 3.0;
        const float rsMaxRayPx = 180.0;        // the Sun alone comes near this
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
        //
        // The paper's glare moves in time, with the pupil and the particles in the eye. Both
        // were tried and both are left out: the pattern holds still.

        // The game's own lens flare setting, carried into these shaders - which cannot see the
        // flare buffer - through the camera UBO's spare word, as its low half. Starburst.cs
        // writes it: the toggle and the intensity together, and zero when either of them says no.
        // The high half is flags, the same bits Starburst.cs names.
        vec2 rsCameraWord()
        {
            return unpackHalf2x16(uint(global.camera.pad0));
        }

        float rsBurstStrength()
        {
            return max(rsCameraWord().x, 0.0);
        }

        // Whether this view draws the engine's Sun sphere: only the main one does.
        bool rsSphereHere()
        {
            return (int(rsCameraWord().y) & 1) != 0;
        }

        // Whether the stars are switched off in the settings - see StarsOff.cs.
        bool rsStarsHidden()
        {
            return (int(rsCameraWord().y) & 2) != 0;
        }

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

        // How far the glare reaches: where a strong lane, three times the mean, sinks to the
        // floor, in the reddest band, which reads the pattern furthest out. From the limb, and
        // capped from the centre. Zero means no glare, and no quad grown to hold one.
        float rsGlareReachPx(float level, float discPx)
        {
            float excess = 3.0 * level / rsGlareFloor;
            if (excess <= 1.0) return 0.0;
            float limb = rsGlareCorePx * sqrt(pow(excess, 2.0 / rsGlareFall) - 1.0)
                       / rsBands[rsBandCount - 1].w;
            float reach = min(discPx + limb, rsMaxRayPx);
            return reach - discPx < rsGlareMinPx ? 0.0 : reach;
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

        // The glare at an offset from the source, in screen pixels, for a source whose own disc
        // is discPx in radius (0 for a point), at the level rsGlareLevel gave and the reach
        // rsGlareReachPx made of it.
        vec3 rsGlare(vec2 offsetPx, float reachPx, float discPx, float level)
        {
            // Nudged off its exact origin, which has no direction: left at zero, that pixel went
            // dark, which only showed once the origin could lie over a hull rather than the Sun.
            if (dot(offsetPx, offsetPx) < 1e-6) offsetPx = vec2(1e-3, 0.0);
            float r = length(offsetPx);
            if (reachPx <= 0.0 || level <= 0.0 || r >= reachPx) return vec3(0.0);
            float sharp = rsNeedleWidthPx;
            float spread = rsDiscSpread * discPx;
            float width = sqrt(sharp * sharp + spread * spread);
            // The skirt is a sharp needle's. Once the disc has blurred a needle wider there is
            // nothing left for it to add, and it would triple the lanes each pixel visits.
            float skirt = max(sharp * rsNeedleSkirtScale, width);

            const float tau = 6.28318531;
            float spacing = tau / float(rsLaneCount);
            float theta = atan(offsetPx.y, offsetPx.x);
            if (theta < 0.0) theta += tau;
            int centre = int(floor(theta / spacing));
            int span = min(int(ceil(4.0 * skirt / (r * spacing))), rsLaneWindow);

            // Each band's glow here, under the ceiling. A band reads the 575 nm glow at
            // radius * 575/lambda, like everything else.
            float glow[rsBandCount];
            float fromLimb = max(r - discPx, 0.0);
            for (int b = 0; b < rsBandCount; b++)
            {
                float x = fromLimb * rsBands[b].w / rsGlareCorePx;
                float g = level * pow(1.0 + x * x, -0.5 * rsGlareFall);
                glow[b] = rsGlareCeiling * (1.0 - exp(-g / rsGlareCeiling));
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
            // Taken to nothing over the last fifth of the reach, where only the strongest lanes
            // are left, and faded in over the shortest glares.
            float fade = clamp((reachPx - r) / (0.2 * reachPx), 0.0, 1.0)
                       * smoothstep(rsGlareMinPx, 2.0 * rsGlareMinPx, reachPx - discPx);
            return max(total, vec3(0.0)) * share * fade;
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

        float rsAirVisibility(vec3 dirToSource, float sourceDistM)
        {
            if (!rsHasAir()) return 1.0;                           // no air, or none worth it
            vec3 centre = global.lighting.planetPosition.xyz;
            float d = length(centre);
            if (d < 1.0 || d >= sourceDistM * 0.9999) return 1.0;
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
            // With the stars switched off in the settings the engine skips this pass, and
            // StarsOff draws it anyway so the Sun stays: every other star is left out, placed
            // off the screen, where nothing of it is drawn.
            if (rsStarsHidden() && dot(position, position) >= 1e-12)
            {
                outColor = vec3(0.0);
                outUv = vec2(0.5);
                outStar = vec4(0.0);
                outGlareLevel = 0.0;
                gl_Position = vec4(2.0, 2.0, 0.5, 1.0);
                return;
            }

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
                // The ring draws in to the limb as the sphere takes over - see Star.frag.
                sunLooksRad = sunAngularRad * (discPx + whitePx * (1.0 - rsSphereShown())) / discPx;
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

            // The glare goes beside the core rather than into it: it has its own ceiling, and
            // running it through one meant for a saturating point would only take the colour
            // back out of it. How far it reaches was settled in the vertex shader, where the
            // quad was sized to hold it.
            vec3 burst = rsGlare((inUv - vec2(0.5f)) * 2.0f * inStar.y, inStar.w, 0.0, inGlareLevel)
                       * edgeFade;

            // A star's flux already carries its air, so the engine's dimming of this pixel is
            // undone - see rsAirUndo. Not the Sun's: its core sits at its ceiling whatever the
            // air, so the engine's is the only dimming it gets, which is what turns it orange as
            // it sets.
            float undo = sun ? 1.0 : rsAirUndo(gl_FragCoord.xy);
            // And as its ring draws in, the Sun's sprite hands its light to the sphere over the
            // same distances, so nothing of it is left under the sphere to show through the
            // sphere's softened edge once its limb darkening shows: rsSphereShown.
            float giveWay = sun ? 1.0 - rsSphereShown() : 1.0;
            outColor = vec4(inColor.rgb * (vec3(min(intensity, cap)) + burst) * undo * giveWay, 1.0f);
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

            // With its corners past the content, as the star shader's: see rsFanInradius.
            float spritePx = max(glowPx, burstPx) / rsFanInradius;
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
            // magnitude, so take the hue and leave the brightness alone.
            float hue = max(max(inColor.r, inColor.g), inColor.b);
            vec3 tint = hue > 1e-4f ? inColor / hue : vec3(1.0f);

            // The core is clamped, to what the engine's blooms may see (rsBloomWhite); the rays
            // are not - they are already faint, and a ceiling meant for a saturating point would
            // only take the colour back out of them. The engine's dimming of this pixel by its
            // air is undone, as for a star: see rsAirUndo.
            float brightness = min(intensity, rsBloomWhite);
            vec3 rgb = tint * (vec3(brightness) + burst) * rsAirUndo(gl_FragCoord.xy);
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
