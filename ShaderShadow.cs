using System.Text;

namespace RealStars;

/// <summary>
/// Builds the mod's own copy of the game's shader tree and patches the two star
/// shaders inside it. Nothing in the game install is touched; the copy is rebuilt
/// from the current game files on every launch, so a game update simply changes
/// what we copy from.
///
/// The whole tree is copied rather than the two files alone because shaderc
/// resolves <c>#include</c> relative to the requesting file: Star.vert pulls in
/// Common/Shared.glsl and Common/Camera.glsl, and those reach further into
/// Common/, ../Planet/ and elsewhere. Copying the tree keeps every include
/// resolving without rewriting a single path.
///
/// Only Star.vert and Star.frag are ever redirected into this copy (see
/// <see cref="ModMain"/>). That matters when Real Atmospheres is installed: it
/// copies the same tree and patches the atmosphere and cloud shaders in ITS copy,
/// so a broad redirect here would quietly serve unpatched atmospheres. The
/// consequence is that our star shaders see stock versions of anything they
/// include, which is fine today and is a decision point if the scintillation work
/// ever wants the atmosphere functions RA patches.
/// </summary>
internal static class ShaderShadow
{
    /// <summary>Absolute path of our patched Shaders directory, or null if the build failed.</summary>
    public static string? ShadersRoot;

    /// <summary>
    /// The shaders we patch, by file name; nothing else is redirected. Set by <see cref="Build"/>,
    /// because the planet pair is only ours when the photometry that feeds them is in place: our
    /// shaders read scalePixel as a glow radius, and serving them the game's own sizing draws
    /// every distant body four times too large and far too bright with it.
    /// </summary>
    public static string[] PatchedShaders { get; private set; } = Array.Empty<string>();

    private static readonly string[] PlanetShaders =
        { "StaticCelestialDistance.vert", "StaticCelestialDistance.frag" };

    private static string? _logFile;

    /// <summary>
    /// Console plus a file beside the mod. The console is where StarMap's own log picks
    /// messages up, but a launcher may stop capturing stdout early (Borea's does, after
    /// the mods report they have loaded), and the redirect only fires later, when the
    /// renderer first compiles a shader. The file is what proves it ran.
    /// </summary>
    public static void Log(string message)
    {
        string line = "RealStars - " + message;
        Console.WriteLine(line);
        try
        {
            if (_logFile != null)
                File.AppendAllText(_logFile, DateTime.Now.ToString("HH:mm:ss.fff  ") + line + Environment.NewLine);
        }
        catch { /* logging must never take the game down */ }
    }

    public static void Build(string coreDir, string modDir, bool patchPlanets = true)
    {
        PatchedShaders = StarShaders.All.Select(s => s.Name)
            .Where(n => patchPlanets || !PlanetShaders.Contains(n))
            .ToArray();
        try
        {
            _logFile = Path.Combine(modDir, "RealStars.log");
            File.WriteAllText(_logFile, $"Real Stars, launched {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        }
        catch { _logFile = null; }

        string srcShaders = Path.Combine(coreDir, "Shaders");
        if (!Directory.Exists(srcShaders))
            throw new DirectoryNotFoundException("game shaders not found at " + srcShaders);

        string dstShaders = Path.Combine(modDir, "ShadowContent", "Core", "Shaders");
        if (Directory.Exists(dstShaders))
            Directory.Delete(dstShaders, recursive: true);
        CopyTree(srcShaders, dstShaders);

        int patched = 0;
        foreach (string name in PatchedShaders)
            patched += PatchStarShader(Path.Combine(dstShaders, name)) ? 1 : 0;

        ShadersRoot = dstShaders;
        Log($"shader tree ready ({patched}/{PatchedShaders.Length} star shaders patched) -> {dstShaders}");
    }

    /// <summary>
    /// Writes our version of a star shader over the copy in our tree, but only after the
    /// stock file proves to be the one we designed against. A game update that rewrites
    /// these shaders therefore leaves the sky stock rather than broken, and says so in the
    /// log. Both files carry a marker on line 1, which GLSL allows before <c>#version</c>,
    /// so a launch can be shown to be running our copy.
    /// </summary>
    private static bool PatchStarShader(string path)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            Log($"WARN: {name} not found in the copied tree; leaving it stock");
            return false;
        }

        var entry = StarShaders.All.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry.Name == null)
        {
            Log($"WARN: no replacement defined for {name}");
            return false;
        }
        (_, string fingerprint, string replacement) = entry;

        string stock = File.ReadAllText(path);
        if (!stock.Contains(fingerprint, StringComparison.Ordinal))
        {
            Log($"WARN: {name} is not the version this mod was written against; leaving it stock");
            return false;
        }

        File.WriteAllText(path, replacement, new UTF8Encoding(false));
        return true;
    }

    private static void CopyTree(string src, string dst)
    {
        foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dst, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
