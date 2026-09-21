using System.Reflection;
using HarmonyLib;
using StarMap.API;

namespace RealStars;

/// <summary>
/// RealStars - a physically grounded star field for Kitten Space Agency, as a
/// deployable StarMap mod. At [StarMapBeforeMain] (invoked before the game's Main,
/// so before any content loads) it builds its own copy of the shader tree with the
/// star shaders patched, then installs one Harmony prefix:
///
///   RenderCore.ShaderModuleUtils.FromFile - remaps Star.vert / Star.frag only
///
/// No game files are modified, no elevation is needed, and all KSA/engine access is
/// through reflection, so the build references only StarMap.API and Harmony.
/// </summary>
[StarMapMod]
public class ModMain
{
    [StarMapBeforeMain]
    public void BeforeMain()
    {
        try
        {
            Install();
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("FAILED to install (stars will render stock): " + ex);
        }
    }

    private static void Install()
    {
        string modDir = Path.GetDirectoryName(typeof(ModMain).Assembly.Location)!;

        Type modType = AccessTools.TypeByName("KSA.Mod")
            ?? throw new InvalidOperationException("KSA.Mod not found");
        string installRoot = Path.GetDirectoryName(modType.Assembly.Location)!;

        // Planet.Render.Core loads lazily and is not in the process yet at BeforeMain:
        // pre-load it into KSA's own load context, so the instance we patch is the one
        // the game will use.
        Type? shaderUtils = AccessTools.TypeByName("RenderCore.ShaderModuleUtils");
        if (shaderUtils == null)
        {
            var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(modType.Assembly)
                ?? System.Runtime.Loader.AssemblyLoadContext.Default;
            var prc = alc.LoadFromAssemblyPath(Path.Combine(installRoot, "Planet.Render.Core.dll"));
            shaderUtils = prc.GetType("RenderCore.ShaderModuleUtils")
                ?? throw new InvalidOperationException("RenderCore.ShaderModuleUtils not found");
        }

        string coreDir = Path.Combine(installRoot, "Content", "Core");
        if (!Directory.Exists(coreDir))
            throw new DirectoryNotFoundException("Core content not found at " + coreDir);

        // Resolved before the shadow is built: the planet shaders are only ours if we can also
        // patch the sizing that feeds them, or they would be reading the game's numbers in our
        // units.
        Type? distance = AccessTools.TypeByName("KSA.StaticCelestialDistanceRendering");
        MethodInfo? sizeScale = distance == null ? null : AccessTools.Method(distance, "GetApparentSizeScale");

        ShaderShadow.Build(coreDir, modDir, patchPlanets: sizeScale != null);

        var harmony = new Harmony("gunshy.realstars");

        MethodInfo fromFile = shaderUtils
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "FromFile"
                && m.GetParameters().Length == 4
                && m.GetParameters()[1].ParameterType == typeof(string))
            ?? throw new InvalidOperationException("ShaderModuleUtils.FromFile(4) not found");
        harmony.Patch(fromFile,
            prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.ShaderFromFilePrefix)));

        // Our catalogue replaces Core's. Both methods read the same private field, and the
        // count one sizes the instance buffer, so patching only the loader would truncate
        // the sky to the stock file's 99,038 entries.
        Patches.StarBinary = Path.Combine(modDir, "assets", "RealStars.bin");
        if (File.Exists(Patches.StarBinary))
        {
            var starsPrefix = new HarmonyMethod(typeof(Patches), nameof(Patches.ModStarsPrefix));
            foreach (string name in new[] { "GetStarCount", "LoadStarBinaries" })
            {
                MethodInfo m = AccessTools.Method(modType, name)
                    ?? throw new InvalidOperationException($"Mod.{name} not found");
                harmony.Patch(m, prefix: starsPrefix);
            }

            // The count still comes from our file's header through the swap above, but the
            // loading itself is ours: the game's reader would normalise away the distances.
            MethodInfo load = AccessTools.Method(modType, "LoadStarBinaries")!;
            harmony.Patch(load, prefix: new HarmonyMethod(typeof(Patches),
                                            nameof(Patches.LoadStarBinariesPrefix)) { priority = Priority.Low });
        }
        else
        {
            Patches.StarBinary = null;
            ShaderShadow.Log("WARN: assets\\RealStars.bin is missing; the stock catalogue stays, "
                             + "and its brightness bytes mean something else, so the sky will look wrong");
        }

        // Distant planets, onto the same magnitude scale as the stars. Fail-soft: without this
        // the sky is still ours, the planets just keep the game's own look entirely.
        if (sizeScale != null)
            harmony.Patch(sizeScale,
                postfix: new HarmonyMethod(typeof(PlanetPhotometry), nameof(PlanetPhotometry.ApparentSizeScalePostfix)));
        else
            ShaderShadow.Log("WARN: StaticCelestialDistanceRendering.GetApparentSizeScale not found; "
                             + "distant planets keep the game's own brightness");

        // Scintillation needs the observer's air, which the shader cannot work out: the engine
        // only refreshes its planet fields for a body with an atmosphere, so beside an airless
        // moon they hold the last one's values. Running on that same method lets us write zero
        // there instead of letting stars twinkle over the Moon.
        // Declared on Program, not on a renderer, and the lighting array it fills is static.
        Type? renderProgram = AccessTools.TypeByName("KSA.Program");
        MethodInfo? updatePlanet = renderProgram == null
            ? null : AccessTools.Method(renderProgram, "UpdatePlanetShaderData");
        if (updatePlanet != null)
            harmony.Patch(updatePlanet,
                postfix: new HarmonyMethod(typeof(Scintillation), nameof(Scintillation.UpdatePlanetShaderDataPostfix)));
        else
            ShaderShadow.Log("WARN: PlanetRenderer.UpdatePlanetShaderData not found; stars will not twinkle");

        // The Sun's distant sprite, onto the same scale as the stars. Its two sizes are floored
        // by the engine, so it stops shrinking once it is a sprite at all.
        MethodInfo? updateShaderData = renderProgram == null
            ? null : AccessTools.Method(renderProgram, "UpdateShaderData", new[] { typeof(double), AccessTools.TypeByName("KSA.IViewport")! });
        if (updateShaderData != null)
            harmony.Patch(updateShaderData,
                postfix: new HarmonyMethod(typeof(SunGlow), nameof(SunGlow.UpdateShaderDataPostfix)));
        else
            ShaderShadow.Log("WARN: Program.UpdateShaderData not found; the Sun keeps its stock sprite");

        ShaderShadow.Log("installed (star shader redirect active"
                         + (Patches.StarBinary != null ? ", own catalogue" : ", stock catalogue")
                         + (sizeScale != null ? ", photometric planets)" : ")"));
    }
}

internal static class Patches
{
    private const string StockMarker = @"\Content\Core\Shaders\";
    private const string ShadowMarker = @"\ShadowContent\Core\Shaders\";

    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Prefix on ShaderModuleUtils.FromFile: point Star.vert and Star.frag at our copies
    /// and leave every other shader alone.
    ///
    /// Narrow by design. Real Atmospheres patches the atmosphere and cloud shaders in a
    /// copy of its own and redirects the whole tree into it, so anything we claim here is
    /// something it cannot patch. Two mods, two trees, one file each.
    ///
    /// Order-independent by design too. Whichever mod's prefix runs first rewrites the
    /// path into its own tree, and the other's stock-path test then fails to match — so
    /// this also accepts a path already pointing into some mod's ShadowContent, which is
    /// what a Real Atmospheres redirect leaves behind when it runs first. Running first
    /// ourselves ([HarmonyPriority] below) is the common case; this is the safety net.
    /// </summary>
    [HarmonyPriority(Priority.First)]
    public static void ShaderFromFilePrefix(ref string filePath)
    {
        string? shadowRoot = ShaderShadow.ShadersRoot;
        if (shadowRoot == null) return;

        // Matched on the path BELOW the shader root, not on the file name: a shader can live in
        // a subdirectory, and a bare name would also claim a same-named file elsewhere.
        string full = Normalize(filePath);
        int start = -1;
        int marker = full.IndexOf(StockMarker, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) start = marker + StockMarker.Length;
        else
        {
            marker = full.IndexOf(ShadowMarker, StringComparison.OrdinalIgnoreCase);
            if (marker >= 0) start = marker + ShadowMarker.Length;
        }
        if (start < 0) return;

        string relative = full[start..];
        string? name = ShaderShadow.PatchedShaders.FirstOrDefault(p =>
            p.Replace('/', '\\').Equals(relative, StringComparison.OrdinalIgnoreCase));
        if (name == null) return;

        string candidate = Path.Combine(shadowRoot, name.Replace('/', '\\'));
        if (!File.Exists(candidate)) return;
        if (string.Equals(full, candidate, StringComparison.OrdinalIgnoreCase)) return;

        filePath = candidate;
        if (Logged.Add(name))
            ShaderShadow.Log($"redirected {name}");
    }

    /// <summary>Absolute path of our catalogue, or null when it is missing.</summary>
    public static string? StarBinary;

    private static MethodInfo? _addInstance;
    private static ConstructorInfo? _float3Ctor;
    private static Type? _spriteType;
    private static FieldInfo? _spritePosition, _spritePacked;
    private static PropertyInfo? _mainViewportProp;
    private static bool _loadedStars;

    /// <summary>
    /// Prefix on Mod.LoadStarBinaries: load our own catalogue and skip the game's reader.
    ///
    /// The game's reader normalises every star onto a shell of one radius, which is all a
    /// painted sky needs. Ours keeps the star's true position in parsecs, so the shader can
    /// work out the direction from wherever the observer is - that is the whole of parallax -
    /// and the distance that sets how bright it looks from there.
    ///
    /// The public AddInstance normalises too, so this uses the overload that takes the instance
    /// struct whole.
    /// </summary>
    public static bool LoadStarBinariesPrefix(object __instance, object starTechnique)
    {
        if (StarBinary == null || !File.Exists(StarBinary)) return true;

        _idProp ??= __instance.GetType().GetProperty("Id");
        if (_idProp?.GetValue(__instance) is not string id
            || !id.Equals("Core", StringComparison.OrdinalIgnoreCase))
            return true;                                   // another mod's stars: leave them be

        try
        {
            _spriteType ??= AccessTools.TypeByName("KSA.SpriteInstance")
                         ?? throw new InvalidOperationException("SpriteInstance not found");
            _spritePosition ??= AccessTools.Field(_spriteType, "Position");
            _spritePacked ??= AccessTools.Field(_spriteType, "PackedData");
            _float3Ctor ??= _spritePosition!.FieldType.GetConstructor(
                                new[] { typeof(float), typeof(float), typeof(float) })
                            ?? throw new InvalidOperationException("float3(x,y,z) not found");
            _addInstance ??= AccessTools.Method(starTechnique.GetType(), "AddInstance",
                                new[] { AccessTools.TypeByName("KSA.IViewport")!, _spriteType })
                             ?? throw new InvalidOperationException("AddInstance(viewport, instance) not found");
            _mainViewportProp ??= AccessTools.Property(AccessTools.TypeByName("KSA.Program")!, "MainViewport");
            object viewport = _mainViewportProp?.GetValue(null)
                              ?? throw new InvalidOperationException("no main viewport");

            using var stream = File.OpenRead(StarBinary);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            var args = new object[2];
            args[0] = viewport;

            for (int i = 0; i < count; i++)
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                byte magnitude = reader.ReadByte();
                byte r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte();

                object sprite = Activator.CreateInstance(_spriteType)!;
                _spritePosition!.SetValue(sprite, _float3Ctor!.Invoke(new object[] { x, y, z }));
                // Same packing the game uses: colour in the top three bytes, magnitude in the low one.
                _spritePacked!.SetValue(sprite, ((uint)b << 24) | ((uint)g << 16) | ((uint)r << 8) | magnitude);
                args[1] = sprite;
                _addInstance!.Invoke(starTechnique, args);
            }

            if (!_loadedStars)
            {
                ShaderShadow.Log($"loaded {count} stars as 3D positions; parallax is live");
                _loadedStars = true;
            }
            return false;                                  // ours is loaded; skip the game's reader
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: could not load the 3D catalogue, falling back to the game's "
                             + "reader (the sky will be flat and lit wrong): " + ex.Message);
            return true;
        }
    }

    private static PropertyInfo? _idProp;
    private static FieldInfo? _starBinariesField;
    private static bool _loggedStars;

    /// <summary>
    /// Prefix on Mod.GetStarCount / Mod.LoadStarBinaries: swap the Core mod's star binary
    /// entry for ours. The loader does Path.Combine(DirectoryPath, entry), which passes a
    /// rooted path straight through, so an absolute path is all it takes.
    ///
    /// The swap is permanent and idempotent, which is what keeps the two methods consistent:
    /// the count sizes the instance buffer and the loader fills it, and they must read the
    /// same file. Only Core's list is touched, so another mod's stars are left alone.
    /// </summary>
    public static void ModStarsPrefix(object __instance)
    {
        if (StarBinary == null) return;

        _idProp ??= __instance.GetType().GetProperty("Id");
        if (_idProp?.GetValue(__instance) is not string id
            || !id.Equals("Core", StringComparison.OrdinalIgnoreCase))
            return;

        _starBinariesField ??= AccessTools.Field(__instance.GetType(), "StarBinaries");
        if (_starBinariesField?.GetValue(__instance) is not string[] binaries || binaries.Length == 0)
            return;

        if (binaries.Length > 1)
            binaries = new[] { binaries[0] };      // one sky, not ours stacked on theirs

        if (binaries[0] != StarBinary)
        {
            binaries[0] = StarBinary;
            _starBinariesField.SetValue(__instance, binaries);
            if (!_loggedStars)
            {
                ShaderShadow.Log("replaced the Core star catalogue");
                _loggedStars = true;
            }
        }
        else
        {
            _starBinariesField.SetValue(__instance, binaries);
        }
    }

    /// <summary>Full path with consistent separators; relative paths resolve against the
    /// CWD, which the game requires to be its install root.</summary>
    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Replace('/', '\\')); }
        catch { return path; }
    }
}
