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
    // Here the byte means FLUX instead. The catalogue's generator wrote size = k*10^(-0.164 m),
    // which is Pogson's law in disguise, so flux = 10^(-0.4 m) = size^(0.4/0.164) up to a
    // constant. The profile is a normalised Moffat with beta = 2, whose 1/r^2 wings are what
    // both diffraction and atmospheric seeing leave behind, and the quad is sized to exactly
    // contain it: a star's apparent size becomes a consequence of its brightness rather than
    // the way it is stored.
    // ---------------------------------------------------------------------------------

    /// <summary>Shared tuning. Both stages need the same numbers, so they are written once.</summary>
    private const string Tuning = """
        // ---- Real Stars tuning ----
        // The catalogue's floor value: ~90% of its 99,038 stars sit at 17/255, so this is
        // both the reference point for flux and the faintest thing on the sky.
        const float rsSizeFloor = 17.0 / 255.0;
        // flux = 10^(-0.4 m) from size = k*10^(-0.164 m)
        const float rsFluxExponent = 0.4 / 0.164;
        // Core width of the point spread function in pixels. About a pixel is right for an
        // unresolved source: wider reads as a soft lens, narrower aliases into a hard dot.
        const float rsPsfCore = 1.0;
        // Overall scale, set so a floor star peaks near 1.0 - the dimmest value that still
        // reads as a star against black.
        const float rsBrightness = 3.1;
        // One display level out of 8-bit, the level below which a wing cannot show.
        const float rsDisplayLevels = 255.0;
        const float rsPi = 3.14159265;
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

            // Size byte back to flux
            float flux = pow(max(packedData.w, 1e-4) / rsSizeFloor, rsFluxExponent);

            // Moffat beta = 2 at unit energy peaks at 1/(pi*core^2), so the profile crosses
            // one display level at this radius. Sizing the quad to it means the sprite is
            // exactly the star's visible extent and never a disc with a hard edge.
            float peak = flux * rsBrightness / (rsPi * rsPsfCore * rsPsfCore);
            float glowPx = max(rsMinGlowPx,
                               rsPsfCore * sqrt(max(sqrt(peak * rsDisplayLevels) - 1.0, 0.0)));

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

            // Moffat, beta = 2, normalised to unit energy:
            //     I(r) = 1/(pi a^2) * (1 + (r/a)^2)^-2
            // The 1/r^2 wings are what diffraction and seeing both leave, and they are why a
            // bright star should read as a point with a halo rather than as a disc.
            float falloff = 1.0f + x * x;
            float psf = 1.0f / (rsPi * rsPsfCore * rsPsfCore * falloff * falloff);

            float intensity = inStar.x * rsBrightness * psf;

            // Take the last of it to zero before the quad's edge, so the sprite never shows.
            intensity *= smoothstep(1.0f, 0.85f, r);

            outColor = vec4(inColor.rgb * min(intensity, rsMaxOutput), 1.0f);
        }
        """;
}
