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

        ShaderShadow.Build(coreDir, modDir);

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
        }
        else
        {
            Patches.StarBinary = null;
            ShaderShadow.Log("WARN: assets\\RealStars.bin is missing; the stock catalogue stays, "
                             + "and its brightness bytes mean something else, so the sky will look wrong");
        }

        ShaderShadow.Log("installed (star shader redirect active"
                         + (Patches.StarBinary != null ? ", own catalogue)" : ", stock catalogue)"));
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

        string name = Path.GetFileName(filePath);
        if (!ShaderShadow.PatchedShaders.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        string full = Normalize(filePath);
        if (full.IndexOf(StockMarker, StringComparison.OrdinalIgnoreCase) < 0
            && full.IndexOf(ShadowMarker, StringComparison.OrdinalIgnoreCase) < 0) return;

        string candidate = Path.Combine(shadowRoot, name);
        if (!File.Exists(candidate)) return;
        if (string.Equals(full, candidate, StringComparison.OrdinalIgnoreCase)) return;

        filePath = candidate;
        if (Logged.Add(name))
            ShaderShadow.Log($"redirected {name}");
    }

    /// <summary>Absolute path of our catalogue, or null when it is missing.</summary>
    public static string? StarBinary;

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
