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

    /// <summary>The shaders we patch, keyed by file name. Nothing else is redirected.</summary>
    public static readonly string[] PatchedShaders = { "Star.vert", "Star.frag" };

    public static void Log(string message) => Console.WriteLine("RealStars - " + message);

    public static void Build(string coreDir, string modDir)
    {
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
    /// Marks a shader as ours. The renderer gives no way to ask which file a compiled
    /// pipeline came from, so the marker plus the redirect's log line is how a launch
    /// proves it is running our copy rather than stock or Real Atmospheres'.
    ///
    /// GLSL allows comments and whitespace before <c>#version</c> and nothing else, so
    /// the marker goes on the first line and the directive stays where it was.
    /// </summary>
    private static bool PatchStarShader(string path)
    {
        if (!File.Exists(path))
        {
            Log($"WARN: {Path.GetFileName(path)} not found in the copied tree; leaving stars stock");
            return false;
        }

        string text = File.ReadAllText(path);
        if (text.StartsWith(Marker, StringComparison.Ordinal)) return true;

        File.WriteAllText(path, Marker + Environment.NewLine + text, new UTF8Encoding(false));
        return true;
    }

    private const string Marker = "// Real Stars: patched copy of the stock shader.";

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
