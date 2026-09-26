using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// Moves the stars on with the game's clock. The catalogue gives each star's position at game
/// time zero and its velocity through space; this carries every position along its straight line
/// to the simulation's date and writes it into the engine's star instances.
///
/// The engine fills its star buffer once, while loading, before any save is open, so the date is
/// only known later: the clock is checked every frame, and the sky moves when the date has moved
/// by a day. Kapteyn's star, the fastest in the catalogue at 8.6" a year, covers 0.024" in a day.
/// At normal speed that is one move per game day, and one when a save loads.
/// </summary>
internal static class StarMotion
{
    /// <summary>Game time zero as a Julian year: every orbit in Core's Astronomicals.xml is a JPL
    /// Horizons state at JD 2461009.5, 2025-11-30 00:00.</summary>
    public const double GameEpochYear = 2000.0 + (2461009.5 - 2451545.0) / 365.25;
    public const double SecondsPerJulianYear = 31557600.0;
    private const double StepYears = 1.0 / 365.25;

    /// <summary>Warping to a target reaches a year a second, which would move the sky every frame.
    /// Ten times a second keeps the fastest star within 0.9" of its place.</summary>
    private const long MinIntervalMs = 100;

    /// <summary>The engine's star instance as far as its bytes go: a float3 position and the packed
    /// magnitude and colour, 16 bytes. <see cref="FitsSprite"/> holds the real type to this before
    /// anything is written through it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    private struct Sprite
    {
        public float X, Y, Z;
        public uint Packed;
    }

    private static Array? _instances;
    private static int _first;
    private static float[]? _start, _velocity;
    private static double _epoch, _shownYears;
    private static long _movedAt;
    private static Func<double>? _clock;
    private static MethodInfo? _upload;
    private static object? _technique;
    private static object[]? _uploadArgs;
    private static bool _logged;

    /// <summary>Whether an engine instance type is laid out as <see cref="Sprite"/>.</summary>
    public static bool FitsSprite(Type element)
    {
        try
        {
            FieldInfo? position = element.GetField("Position"), packed = element.GetField("PackedData");
            return element.IsValueType && position != null && packed?.FieldType == typeof(uint)
                && Marshal.SizeOf(element) == Unsafe.SizeOf<Sprite>()
                && Marshal.SizeOf(position.FieldType) == 3 * sizeof(float)
                && Marshal.OffsetOf(element, "Position") == 0
                && Marshal.OffsetOf(element, "PackedData") == 3 * sizeof(float);
        }
        catch (ArgumentException)
        {
            return false;                              // not a type Marshal can lay out at all
        }
    }

    /// <summary>Our stars in an engine instance array, as <see cref="Sprite"/>s and without a copy.
    /// Only for an array whose element passed <see cref="FitsSprite"/>.</summary>
    private static Span<Sprite> Ours(Array instances, int first, int count) =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<byte, Sprite>(ref MemoryMarshal.GetArrayDataReference(instances)),
                                 instances.Length).Slice(first, count);

    /// <summary>Whether the instances from <paramref name="first"/> on hold exactly these stars.</summary>
    public static bool ReadsBack(Array instances, int first, float[] start, uint[] packed)
    {
        Span<Sprite> ours = Ours(instances, first, packed.Length);
        for (int i = 0, k = 0; i < ours.Length; i++, k += 3)
        {
            if (ours[i].X != start[k] || ours[i].Y != start[k + 1] || ours[i].Z != start[k + 2]
                || ours[i].Packed != packed[i])
                return false;
        }
        return true;
    }

    /// <summary>Positions <paramref name="years"/> past the catalogue's epoch, written over the
    /// instances from <paramref name="first"/> on. Magnitude and colour are left alone.</summary>
    public static void Move(Array instances, int first, float[] start, float[] velocity, double years)
    {
        Span<Sprite> ours = Ours(instances, first, start.Length / 3);
        for (int i = 0, k = 0; i < ours.Length; i++, k += 3)
        {
            ref Sprite s = ref ours[i];
            s.X = (float)(start[k] + velocity[k] * years);
            s.Y = (float)(start[k + 1] + velocity[k + 1] * years);
            s.Z = (float)(start[k + 2] + velocity[k + 2] * years);
        }
    }

    /// <summary>
    /// Called by the loader once our stars are in the technique: <paramref name="start"/> and
    /// <paramref name="packed"/> are what it added, last in the viewport's instances.
    /// Fail-soft: on any mismatch the stars stay where the catalogue put them, at game time zero.
    /// </summary>
    public static void Attach(object technique, object viewport, float[] start, uint[] packed,
                              float[] velocity, double epoch)
    {
        try
        {
            int slot = (int)AccessTools.Property(viewport.GetType(), "ShaderSlot")!.GetValue(viewport)!;
            var perViewport = (Array)AccessTools.Field(technique.GetType(), "Instances")!.GetValue(technique)!;
            var instances = (Array)perViewport.GetValue(slot)!;
            var counts = (int[])AccessTools.Property(technique.GetType(), "InstanceCount")!.GetValue(technique)!;
            int count = packed.Length, first = counts[slot] - count;

            Type element = instances.GetType().GetElementType()!;
            if (!FitsSprite(element))
                throw new InvalidOperationException($"{element.FullName} is not laid out as a float3 and a uint");
            if (first < 0 || counts[slot] > instances.Length || velocity.Length != start.Length)
                throw new InvalidOperationException($"our stars are not where they were added ({first}, {counts[slot]})");
            // What the engine holds must read back as what was loaded, or nothing is written.
            if (!ReadsBack(instances, first, start, packed))
                throw new InvalidOperationException("the instances read back differently from what was loaded");

            _upload = AccessTools.Method(technique.GetType(), "UpdateInstanceBuffer",
                                         new[] { AccessTools.TypeByName("KSA.IViewport")!, typeof(int) })
                      ?? throw new InvalidOperationException("UpdateInstanceBuffer(IViewport, int) not found");
            MethodInfo elapsed = AccessTools.Method(AccessTools.TypeByName("KSA.Universe"), "GetElapsedSeconds")
                                 ?? throw new InvalidOperationException("Universe.GetElapsedSeconds not found");
            _clock = elapsed.CreateDelegate<Func<double>>();

            _technique = technique;
            _uploadArgs = new object[] { viewport, 0 };
            _first = first;
            _start = start;
            _velocity = velocity;
            _epoch = epoch;
            _shownYears = 0.0;
            _movedAt = Environment.TickCount64 - MinIntervalMs;
            _instances = instances;
            ShaderShadow.Log($"star motion ready: {count} stars at {epoch:F2}, following the game's date");
        }
        catch (Exception ex)
        {
            ShaderShadow.Log($"WARN: stars stay at {epoch:F2} whatever the game's date: {ex.Message}");
        }
    }

    /// <summary>Postfix on Program.UpdateShaderData, which runs every frame before the draw.</summary>
    public static void UpdateShaderDataPostfix()
    {
        if (_instances == null) return;
        try
        {
            double years = GameEpochYear - _epoch + _clock!() / SecondsPerJulianYear;
            if (!(Math.Abs(years - _shownYears) >= StepYears)) return;
            long now = Environment.TickCount64;
            if (now - _movedAt < MinIntervalMs) return;

            // Written while earlier frames may still be drawing from the buffer; a frame that
            // catches it half done shows some stars a step behind the rest, and a step is a day.
            Move(_instances, _first, _start!, _velocity!, years);
            _upload!.Invoke(_technique, _uploadArgs);

            if (!_logged || Math.Abs(years - _shownYears) >= 1.0)
                ShaderShadow.Log($"stars moved to {_epoch + years:F2}");
            _logged = true;
            _shownYears = years;
            _movedAt = now;
        }
        catch (Exception ex)
        {
            _instances = null;                         // stop; the sky stays where it was last put
            ShaderShadow.Log("WARN: star motion stopped: " + ex.Message);
        }
    }
}
