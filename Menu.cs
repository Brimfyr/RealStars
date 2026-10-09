using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Real Stars' page in the game's Mods menu, which ModMenu (MrJeranimo) adds when it is installed. ModMenu finds the
/// page by its attribute's name and calls it while its menu is open; without ModMenu nothing calls it, and the
/// settings stay as their file has them (Settings).
/// </summary>
public static class Menu
{
    private const int Logarithmic = 32, AlwaysClamp = 1536;    // ImGuiSliderFlags
    private static bool _broken;

    [ModMenuEntry("Real Stars")]
    public static void Draw()
    {
        if (_broken || !ImGuiCalls.Ready) return;
        try
        {
            Settings.Load();
            if (ImGuiCalls.MenuItem("Colour adaptation##realstars", Settings.ColourAdaptation))
                Settings.SetColourAdaptation(!Settings.ColourAdaptation);
            ImGuiCalls.SetItemTooltip(
                "The eye adapting to the colour of the light it is in, as far as people do: daylight shows white,\n"
                + "a red dwarf's light warm. Unticked, every light is shown in its raw colour.");
            float seconds = Settings.AdaptationFastSeconds;
            // The whole course's time rides in the slider's own read-out, where a line of text under it stood.
            string format = FormattableString.Invariant($"%.2f s ({Settings.AdaptationSeconds:0} s in all)");
            if (ImGuiCalls.SliderFloat("Adaptation time##realstars", ref seconds, Settings.MinAdaptationFastSeconds,
                                       Settings.MaxAdaptationFastSeconds, format, Logarithmic | AlwaysClamp))
                Settings.SetAdaptationFastSeconds(seconds);
            ImGuiCalls.SetItemTooltip(
                "How long the quick part of the eye's adaptation to a new light takes: the change you see happen.\n"
                + "The eye adapts in two stages. The quick one is a little over half of the change; a slow one\n"
                + "finishes the rest over thirty times as long, too gradually to notice.\n"
                + "3 s is how long people take (Fairchild and Reniff 1995).");
            if (ImGuiCalls.MenuItem(FormattableString.Invariant($"Reset adaptation time to {Settings.DefaultAdaptationFastSeconds:0.##} s")))
                Settings.SetAdaptationFastSeconds(Settings.DefaultAdaptationFastSeconds);

            ImGuiCalls.Separator();
            if (ImGuiCalls.MenuItem("Twinkle##realstars", Settings.Twinkle))
                Settings.SetTwinkle(!Settings.Twinkle);
            ImGuiCalls.SetItemTooltip("Stars and planets twinkling when seen through an atmosphere.");

            ImGuiCalls.Separator();
            if (ImGuiCalls.MenuItem("Starburst and glare##realstars", Settings.Glare))
                Settings.SetGlare(!Settings.Glare);
            ImGuiCalls.SetItemTooltip(
                "The starburst and soft glow round the Sun, the bright planets and the brightest stars.\n"
                + "It also follows the game's Lens Flare setting and its intensity: either one off is no glare.");
            Settings.SaveIfSettled();
        }
        catch (Exception ex)
        {
            _broken = true;
            ShaderShadow.Log("WARN: Real Stars' page in the Mods menu stopped: " + ex.GetBaseException().Message);
        }
    }
}

/// <summary>The marker ModMenu looks for, by name (its own source asks a mod to copy it): a page and its title.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ModMenuEntryAttribute : Attribute
{
    public string MenuName { get; }
    public string? IsModMenuActivePropertyName { get; }

    public ModMenuEntryAttribute(string menuName, string? isModMenuActivePropertyName = null)
    {
        MenuName = menuName;
        IsModMenuActivePropertyName = isModMenuActivePropertyName;
    }
}

/// <summary>
/// The few ImGui calls the page makes, through the game's own ImGui (Brutal.ImGuiApi). Its labels are an ImString,
/// a ref struct that reflection cannot box, so each call is a small method built here in IL that turns our strings
/// into ImStrings on the stack and calls straight through. Fail-soft: anything not found leaves the page empty.
/// </summary>
internal static class ImGuiCalls
{
    internal delegate void TextCall(string text);
    internal delegate bool SliderCall(string label, ref float value, float min, float max, string format, int flags);
    internal delegate bool MenuItemCall(string label, bool selected);

    private static TextCall? _tooltip;
    private static SliderCall? _slider;
    private static MenuItemCall? _menuItem;
    private static Action? _separator;
    private static bool _resolved, _ready;

    internal static bool Ready
    {
        get
        {
            if (!_resolved)
            {
                _resolved = true;
                try
                {
                    Resolve();
                    _ready = true;
                }
                catch (Exception ex)
                {
                    ShaderShadow.Log("WARN: the game's ImGui calls were not found; Real Stars' page in the Mods menu is empty: "
                                     + ex.GetBaseException().Message);
                }
            }
            return _ready;
        }
    }

    // A tooltip is a printf format, so a percent sign is written twice to show once - a bare one began a
    // conversion and printed whatever was on the stack.
    internal static void SetItemTooltip(string text) => _tooltip!(text.Replace("%", "%%"));
    internal static bool SliderFloat(string label, ref float value, float min, float max, string format, int flags) =>
        _slider!(label, ref value, min, max, format, flags);
    /// <summary>A menu item, ticked when <paramref name="selected"/>; true on the frame it is clicked.</summary>
    internal static bool MenuItem(string label, bool selected = false) => _menuItem!(label, selected);

    /// <summary>A dividing line between sections, as the game's own menus have.</summary>
    internal static void Separator() => _separator!();

    /// <summary>Builds the calls; throws if the game's ImGui is not as expected.</summary>
    internal static void Resolve()
    {
        Type imgui = AccessTools.TypeByName("Brutal.ImGuiApi.ImGui") ?? throw new InvalidOperationException("Brutal.ImGuiApi.ImGui not found");
        Type imString = AccessTools.TypeByName("Brutal.ImGuiApi.ImString") ?? throw new InvalidOperationException("ImString not found");
        MethodInfo fromString = imString.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == imString
                                 && m.GetParameters() is { Length: 1 } p && p[0].ParameterType == typeof(string))
            ?? throw new InvalidOperationException("ImString from string not found");

        MethodInfo Find(string name, params Type[] types) =>
            imgui.GetMethod(name, BindingFlags.Public | BindingFlags.Static, types)
            ?? throw new InvalidOperationException($"ImGui.{name} not found");

        _separator = (Action)Delegate.CreateDelegate(typeof(Action), Find("Separator"));
        _tooltip = TextLike(Find("SetItemTooltip", imString), fromString);

        MethodInfo slider = imgui.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "SliderFloat" && m.GetParameters() is { Length: 6 } p && p[0].ParameterType == imString
                                 && p[1].ParameterType == typeof(float).MakeByRefType() && p[4].ParameterType == imString
                                 && p[5].ParameterType.IsEnum && m.ReturnType == typeof(bool))
            ?? throw new InvalidOperationException("ImGui.SliderFloat(label, ref v, min, max, format, flags) not found");
        var sliderIl = new DynamicMethod("RealStars_ImGui_SliderFloat", typeof(bool),
            new[] { typeof(string), typeof(float).MakeByRefType(), typeof(float), typeof(float), typeof(string), typeof(int) },
            typeof(ImGuiCalls).Module, skipVisibility: true);
        ILGenerator il = sliderIl.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fromString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Call, fromString);
        il.Emit(OpCodes.Ldarg_S, (byte)5);                     // the flags, an int enum
        il.Emit(OpCodes.Call, slider);
        il.Emit(OpCodes.Ret);
        _slider = (SliderCall)sliderIl.CreateDelegate(typeof(SliderCall));

        MethodInfo menuItem = imgui.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "MenuItem" && m.GetParameters() is { Length: 4 } p && p[0].ParameterType == imString
                                 && p[1].ParameterType == imString && p[2].ParameterType == typeof(bool)
                                 && p[3].ParameterType == typeof(bool) && m.ReturnType == typeof(bool))
            ?? throw new InvalidOperationException("ImGui.MenuItem(label, shortcut, selected, enabled) not found");
        var itemIl = new DynamicMethod("RealStars_ImGui_MenuItem", typeof(bool), new[] { typeof(string), typeof(bool) },
                                       typeof(ImGuiCalls).Module, skipVisibility: true);
        il = itemIl.GetILGenerator();
        LocalBuilder none = il.DeclareLocal(imString);         // the shortcut: an empty ImString, as its default is
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fromString);
        il.Emit(OpCodes.Ldloca_S, none);
        il.Emit(OpCodes.Initobj, imString);
        il.Emit(OpCodes.Ldloc, none);
        il.Emit(OpCodes.Ldarg_1);                              // selected: shown ticked
        il.Emit(OpCodes.Ldc_I4_1);                             // enabled
        il.Emit(OpCodes.Call, menuItem);
        il.Emit(OpCodes.Ret);
        _menuItem = (MenuItemCall)itemIl.CreateDelegate(typeof(MenuItemCall));
    }

    private static TextCall TextLike(MethodInfo call, MethodInfo fromString)
    {
        var method = new DynamicMethod("RealStars_ImGui_" + call.Name, typeof(void), new[] { typeof(string) },
                                       typeof(ImGuiCalls).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fromString);
        il.Emit(OpCodes.Call, call);
        il.Emit(OpCodes.Ret);
        return (TextCall)method.CreateDelegate(typeof(TextCall));
    }
}
