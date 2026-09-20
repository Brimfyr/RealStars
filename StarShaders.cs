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
    private const string Tuning = """
        // ---- Real Stars tuning ----
        // Our catalogue stores V magnitude linearly in the byte: byte 1 is the faint limit
        // and each step is 1/24 of a magnitude. make_star_binary.py writes it.
        const float rsMagFaint = 9.0;
        const float rsBytesPerMag = 24.0;
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

        // Byte back to flux. Pogson: five magnitudes is a factor of a hundred.
        float rsFlux(float packedScale)
        {
            float mag = rsMagFaint - (packedScale * 255.0 - 1.0) / rsBytesPerMag;
            return pow(10.0, -0.4 * (mag - rsMagRef));
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
        const float rsScintFreqHz = 25.0;      // at the zenith; slower low down
        const float rsDispersionArcsec = 0.54; // 400-700nm separation per tan(z), Earth sea level
        const float rsArcsecPerRad = 206265.0;

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

        // Two octaves, scaled to about unit variance so sigma means what it says.
        float rsNoise(float t, float seed)
        {
            return (rsFlicker(t, seed) + 0.5 * rsFlicker(t * 2.17 + 13.0, seed)) * 1.9;
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

            float sigma = min(sigmaZenith * pow(airmass, 0.92), 1.0) * suppression;
            if (sigma < 1e-3) return vec3(1.0);

            float seed = rsHash(starDir * 811.7);
            float t = time * rsScintFreqHz / sqrt(airmass);

            // Colour separation, as a fraction of the correlation scale: nil overhead, total
            // by ten degrees up, which is exactly when a bright star starts flashing colours.
            float dispersion = rsDispersionArcsec * (sigmaZenith / 0.25) * tan(radians(min(zDeg, 89.0)));
            float decorrelate = clamp(dispersion / (thetaC * rsArcsecPerRad), 0.0, 1.0);

            vec3 n = vec3(rsNoise(t + decorrelate * 0.7, seed),
                          rsNoise(t, seed),
                          rsNoise(t - decorrelate * 0.7, seed));
            return exp(sigma * n - 0.5 * sigma * sigma);
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
        layout (location = 2) out vec2 outStar;   // x = flux, 1.0 at the catalogue floor; y = glow radius in px

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

            float flux = rsFlux(packedData.w);

            // Twinkle. A star is unresolved, so it gets the full effect; the brightness and
            // the colour move together, which is why the size is computed from the flickered
            // flux and the colour carries only what is left over: the chroma.
            vec3 scint = rsScintillation(normalize(position), 0.0, global.camera.time);
            float scintMean = (scint.r + scint.g + scint.b) / 3.0;
            flux *= scintMean;
            outColor *= scint / max(scintMean, 1e-4);

            // Moffat beta = 2 at unit energy peaks at 1/(pi*core^2), so the profile crosses
            // one display level at this radius. Sizing the quad to it means the sprite is
            // exactly the star's visible extent and never a disc with a hard edge.
            float peak = flux * rsBrightness * (rsPsfBeta - 1.0) / (rsPi * rsPsfCore * rsPsfCore);
            float glowPx = clamp(
                rsPsfCore * sqrt(max(pow(peak * rsDisplayLevels, 1.0 / rsPsfBeta) - 1.0, 0.0)),
                rsMinGlowPx, rsMaxGlowPx);

            outUv = uv[gl_VertexIndex];
            outStar = vec2(flux, glowPx);

            vec4 worldPosition = global.camera.viewProjection * vec4(position, 1);

            // Pixels to clip units. The offset is added in clip space, so it is divided by w
            // downstream, and x spans the screen width over 2 NDC units; the aspect factor on
            // y then keeps the quad square on screen, as stock does.
            float pxToClip = 2.0 * worldPosition.w / max(global.camera.screenWidth, 1.0);
            vec2 vertPosition = (outUv - vec2(0.5)) * (2.0 * glowPx * pxToClip);

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
        layout (location = 2) in vec2 inStar;     // x = flux, y = glow radius in px

        layout (location = 0) out vec4 outColor;

        {{Tuning}}
        // HDR headroom. A bright star's core genuinely does saturate a fixed exposure, so this
        // is not a look but a limit: it keeps one star out of the range where any later bloom
        // would smear it across the frame.
        const float rsMaxOutput = 24.0;

        void main(void)
        {
            float r = length(inUv.xy - vec2(0.5f)) * 2.0f;   // 0 at the centre, 1 at the quad's edge
            float x = (r * inStar.y) / rsPsfCore;            // pixels from the centre, in core widths

            // Moffat, normalised to unit energy:
            //     I(r) = (beta-1)/(pi a^2) * (1 + (r/a)^2)^-beta
            // Falling wings rather than a hard edge are what diffraction and seeing both leave,
            // and they are why a bright star reads as a point with a halo rather than a disc.
            float psf = (rsPsfBeta - 1.0f)
                      / (rsPi * rsPsfCore * rsPsfCore * pow(1.0f + x * x, rsPsfBeta));

            float intensity = inStar.x * rsBrightness * psf;

            // Take the last of it to zero before the quad's edge, so the sprite never shows.
            intensity *= smoothstep(1.0f, 0.85f, r);

            outColor = vec4(inColor.rgb * min(intensity, rsMaxOutput), 1.0f);
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

    public const string PlanetVert = """
        // Real Stars: patched copy of the stock shader.
        #version 450

        #include "Common/Shared.glsl"
        #include "Common/Camera.glsl"

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

            // scalePixel is the glow RADIUS in pixels. The offset is added after the
            // perspective divide, so a pixel is 2/screen in normalised device coordinates,
            // and the quad spans from -radius to +radius across its 1.0 of uv.
            vertPosition.x *= 4.0f * instanceData.scalePixel / global.camera.screenWidth;
            vertPosition.y *= 4.0f * instanceData.scalePixel / global.camera.screenHeight;

            vec4 worldPosition = global.camera.viewProjection * vec4(instanceData.positionEgo, 1);
            worldPosition /= worldPosition.w;
            vec4 screenOffset = vec4(vertPosition.x, vertPosition.y, 0.0f, 0);

            vec4 depthCalculation = global.camera.viewProjection * vec4(instanceData.positionEgo, 1);
            outDepth = depthCalculation.z / depthCalculation.w;

            gl_Position = worldPosition + screenOffset;
            gl_Position.z = max(0.0, gl_Position.z); // Prevent early frag culling
            outColor = instanceData.color;

            outScalePixel = instanceData.scalePixel;
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

        layout (location = 0) out vec4 outColor;

        {{Tuning}}
        const float rsMaxOutput = 24.0;

        void main(void)
        {
            float r = length(inUV.xy - vec2(0.5f)) * 2.0f;   // 0 at the centre, 1 at the edge
            float glow = max(inScalePixel, 1e-3f);
            float x = (r * glow) / rsPsfCore;

            // The sprite was sized so the profile reaches one display level exactly at its
            // edge, so the peak follows from the radius alone - no second channel needed, and
            // it stays in step with the star shader by construction.
            float edge = 1.0f + (glow / rsPsfCore) * (glow / rsPsfCore);
            float peak = pow(edge, rsPsfBeta) / rsDisplayLevels;

            float intensity = peak / pow(1.0f + x * x, rsPsfBeta);
            intensity *= smoothstep(1.0f, 0.85f, r);

            // The engine has already scaled this colour by its own phase term. Ours is in the
            // magnitude, so take the hue and leave the brightness alone.
            float hue = max(max(inColor.r, inColor.g), inColor.b);
            vec3 tint = hue > 1e-4f ? inColor / hue : vec3(1.0f);

            float brightness = min(intensity, rsMaxOutput);
            outColor = vec4(tint * brightness, min(brightness, 1.0f));

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
