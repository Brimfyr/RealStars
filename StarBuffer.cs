using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RealStars;

/// <summary>
/// The engine's star instances as this mod reads and writes them: the catalogue's stars, as loaded, and the
/// game's own, which <see cref="GameStars"/> places each frame.
///
/// The catalogue is the sky of one moment, the game's start date, and stays there. From 1.1.0 it moved on with
/// the game's clock, each star along its own measured motion. That was taken out on 2026-10-08: the game's own
/// stars stand still, and their planets with them, at their places of the year 2000, so the moving background
/// slid past them and Barnard's Star, the fastest star in the sky, was the one without its motion (user). If
/// motion comes back it is for whole star systems, not for the background alone.
/// </summary>
internal static class StarBuffer
{
    /// <summary>The engine's star instance as far as its bytes go: a float3 position and the packed
    /// magnitude and colour, 16 bytes. <see cref="FitsSprite"/> holds the real type to this before
    /// anything is written through it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    internal struct Sprite
    {
        public float X, Y, Z;
        public uint Packed;
    }

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

    /// <summary>An engine instance array as <see cref="Sprite"/>s, without a copy. Only for an array
    /// whose element passed <see cref="FitsSprite"/>; GameStars writes the game's stars through it.</summary>
    internal static Span<Sprite> Sprites(Array instances) =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<byte, Sprite>(ref MemoryMarshal.GetArrayDataReference(instances)),
                                 instances.Length);

    /// <summary>Whether the instances from <paramref name="first"/> on hold exactly these stars.</summary>
    public static bool ReadsBack(Array instances, int first, float[] start, uint[] packed)
    {
        Span<Sprite> ours = Sprites(instances).Slice(first, packed.Length);
        for (int i = 0, k = 0; i < ours.Length; i++, k += 3)
        {
            if (ours[i].X != start[k] || ours[i].Y != start[k + 1] || ours[i].Z != start[k + 2]
                || ours[i].Packed != packed[i])
                return false;
        }
        return true;
    }
}
