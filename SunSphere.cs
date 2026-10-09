using System.Reflection;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// The star's sphere, seen from inside its own mesh.
///
/// The engine draws the sphere on a mesh 5.5 times the star's size, and of that mesh only the faces turned toward the
/// camera. Within 5.5 radii of the star's centre the camera is inside the mesh, every face it can see is turned away,
/// and nothing of the star is drawn: it went out as you closed on it (user, 2026-10-07). Stock has the same gap. Its
/// surface is drawn at half the mesh, so there it opens 2.75 of its radii from the centre; with the star at its real
/// size it opened 4.5 radii above the surface.
///
/// The faces turned away cover the same pixels from outside the mesh, and every pixel from inside it, so the pipeline
/// is set to draw those instead. The shader works the star's edge out from the sight line (the Sun.frag edits), which
/// is the same line through either face, and draws the star only where it lies ahead of the camera. Things in front
/// of the star also hide it now wherever they are: the faces drawn are behind it, where the near ones were in front
/// of anything within the mesh.
///
/// If the pipeline cannot be reached the engine draws as before, and inside the mesh the star's sprite is the star
/// again (Starburst), so it is never gone.
/// </summary>
internal static class SunSphere
{
    /// <summary>The engine's mesh over the star's own radius (KSA.Rendering.Sun.SunRenderer.MeshRadiusOf).</summary>
    internal const double MeshRadii = 5.5;

    /// <summary>Whether the sphere's pipeline draws the faces turned away, and so the star from inside its mesh.</summary>
    internal static bool DrawnFromInside { get; private set; }

    /// <summary>SunRenderer.CreateMeshRenderer, which sets the pipeline up before it is first built; null if it moved.</summary>
    internal static MethodInfo? Target()
    {
        Type? renderer = AccessTools.TypeByName("KSA.Rendering.Sun.SunRenderer")
                      ?? AccessTools.AllTypes().FirstOrDefault(t => t.Name == "SunRenderer");
        return renderer == null ? null : AccessTools.Method(renderer, "CreateMeshRenderer");
    }

    /// <summary>Postfix on SunRenderer.CreateMeshRenderer: the technique it returns is built right after, and at
    /// every rebuild, from the settings changed here.</summary>
    public static void CreateMeshRendererPostfix(object __result)
    {
        try
        {
            object? pipeline = __result == null ? null : AccessTools.Field(__result.GetType(), "PipelineWrapper")?.GetValue(__result);
            object? settings = pipeline == null ? null : AccessTools.Field(pipeline.GetType(), "CreationSettings")?.GetValue(pipeline);
            PropertyInfo? cull = settings == null ? null : AccessTools.Property(settings.GetType(), "CullMode");
            if (cull == null || !cull.PropertyType.IsEnum || !Enum.IsDefined(cull.PropertyType, "FrontBit"))
            {
                ShaderShadow.Log("WARN: the star sphere's pipeline could not be reached; inside its mesh the star is drawn by its sprite");
                return;
            }
            cull.SetValue(settings, Enum.Parse(cull.PropertyType, "FrontBit"));
            DrawnFromInside = true;
            ShaderShadow.Log("the star's sphere is drawn from inside its mesh too (the faces turned away, where the engine drew those turned toward the camera)");
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the star sphere's pipeline was left as the engine sets it: " + ex.Message);
        }
    }
}
