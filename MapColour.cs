using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RealStars;

/// <summary>
/// A map's average colour, as the game shows it: the 1x1 level of its mip chain, which is the whole map averaged,
/// read through the game's own KTX library. The library streams a KTX2's levels smallest first, so only that level
/// is ever decompressed. One texel per face, decoded here (BC7, BC1-3, 8-bit RGBA), averaged over the faces, and
/// linear as the game samples it: an sRGB format is decoded, a UNORM one taken as it stands.
/// </summary>
internal static class MapColour
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateFn([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint flags, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IterateFn(IntPtr texture, IntPtr callback, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FormatFn(IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyFn(IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LevelFn(int level, int face, int width, int height, int depth, ulong size, IntPtr pixels,
                                 IntPtr user);

    private const int Stop = 1000;      // any error code ends the iteration

    private static CreateFn? _create;
    private static IterateFn? _iterate;
    private static FormatFn? _format;
    private static DestroyFn? _destroy;
    private static readonly LevelFn Level = OnLevel;         // held, so the native pointer stays valid
    private static readonly IntPtr LevelPointer = Marshal.GetFunctionPointerForDelegate(Level);
    private static readonly object Lock = new();
    private static List<byte[]>? _faces;
    private static int _firstLevel;

    /// <summary>Binds the game's Ktx.dll, beside its KSA.dll; false leaves every map unread.</summary>
    internal static bool Resolve(string installRoot)
    {
        try
        {
            IntPtr lib = NativeLibrary.Load(Path.Combine(installRoot, "Ktx.dll"));
            _create = Marshal.GetDelegateForFunctionPointer<CreateFn>(NativeLibrary.GetExport(lib, "ktxTexture2_CreateFromNamedFile1"));
            _iterate = Marshal.GetDelegateForFunctionPointer<IterateFn>(NativeLibrary.GetExport(lib, "ktxTexture_IterateLoadLevelFaces1"));
            _format = Marshal.GetDelegateForFunctionPointer<FormatFn>(NativeLibrary.GetExport(lib, "ktxTexture_GetVkFormat1"));
            _destroy = Marshal.GetDelegateForFunctionPointer<DestroyFn>(NativeLibrary.GetExport(lib, "ktxTexture_Destroy1"));
            return true;
        }
        catch (Exception ex)
        {
            ShaderShadow.Log("WARN: the game's KTX library not found; planetshine from unmeasured bodies keeps their colour: "
                             + ex.Message);
            return false;
        }
    }

    /// <summary>The map's average linear RGB, or null if it cannot be read (not KTX2, no 1x1 level, a format not
    /// decoded here).</summary>
    internal static float[]? Average(string path)
    {
        if (_create == null || !File.Exists(path) || !path.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase))
            return null;
        lock (Lock)
        {
            if (_create(path, 0, out IntPtr texture) != 0 || texture == IntPtr.Zero)
                return null;
            try
            {
                int format = _format!(texture);
                _faces = new List<byte[]>();
                _firstLevel = -1;
                _iterate!(texture, LevelPointer, IntPtr.Zero);
                if (_faces.Count == 0)
                    return null;
                var sum = new double[3];
                foreach (byte[] block in _faces)
                {
                    if (Texel0(block, format) is not { } t)
                        return null;
                    for (int c = 0; c < 3; c++)
                        sum[c] += t[c];
                }
                return sum.Select(s => (float)(s / _faces.Count)).ToArray();
            }
            finally
            {
                _destroy!(texture);
                _faces = null;
            }
        }
    }

    /// <summary>Keeps the first level's faces if it is 1x1, then stops: the rest of the chain is never inflated.</summary>
    private static int OnLevel(int level, int face, int width, int height, int depth, ulong size, IntPtr pixels, IntPtr user)
    {
        if (_firstLevel < 0)
            _firstLevel = level;
        if (level != _firstLevel || width != 1 || height != 1 || _faces == null)
            return Stop;
        var block = new byte[(int)Math.Min(size, 16UL)];
        Marshal.Copy(pixels, block, 0, block.Length);
        _faces.Add(block);
        return 0;
    }

    /// <summary>Linear RGB of texel (0, 0) of one block (or pixel) of a Vulkan format, or null if not decoded here.</summary>
    internal static float[]? Texel0(byte[] data, int vkFormat)
    {
        byte[]? srgb = vkFormat switch
        {
            37 or 43 when data.Length >= 3 => new[] { data[0], data[1], data[2] },          // R8G8B8A8
            44 or 50 when data.Length >= 3 => new[] { data[2], data[1], data[0] },          // B8G8R8A8
            23 or 29 when data.Length >= 3 => new[] { data[0], data[1], data[2] },          // R8G8B8
            >= 131 and <= 134 when data.Length >= 8 => Bc1Texel0(data, vkFormat <= 132),
            >= 135 and <= 138 when data.Length >= 16 => Bc1Texel0(data.AsSpan(8), true),
            145 or 146 when data.Length >= 16 => Bc7Texel0(data),
            _ => null,
        };
        if (srgb == null)
            return null;
        bool isSrgb = vkFormat is 43 or 50 or 29 or 132 or 134 or 136 or 138 or 146;
        return srgb.Select(v => isSrgb ? SrgbToLinear(v / 255f) : v / 255f).ToArray();
    }

    internal static float SrgbToLinear(float v) =>
        v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);

    /// <summary>A BC1 colour block's texel 0 (also BC2/BC3's colour half, always four colours).</summary>
    private static byte[] Bc1Texel0(ReadOnlySpan<byte> b, bool fourColour)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(b), c1 = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        int[] e0 = Rgb565(c0), e1 = Rgb565(c1);
        int index = b[4] & 3;
        bool four = fourColour || c0 > c1;
        var o = new byte[3];
        for (int c = 0; c < 3; c++)
            o[c] = (byte)(index switch
            {
                0 => e0[c],
                1 => e1[c],
                2 => four ? (2 * e0[c] + e1[c]) / 3 : (e0[c] + e1[c]) / 2,
                _ => four ? (e0[c] + 2 * e1[c]) / 3 : 0,
            });
        return o;
    }

    private static int[] Rgb565(ushort c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return new[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
    }

    // BC7 modes: subsets, partition bits, rotation bits, index-selection bit, colour bits, alpha bits,
    // per-endpoint p-bits, shared p-bits, index bits, second index bits.
    private static readonly (int NS, int PB, int RB, int ISB, int CB, int AB, int EPB, int SPB, int IB, int IB2)[] Bc7Modes =
    {
        (3, 4, 0, 0, 4, 0, 1, 0, 3, 0), (2, 6, 0, 0, 6, 0, 0, 1, 3, 0), (3, 6, 0, 0, 5, 0, 0, 0, 2, 0),
        (2, 6, 0, 0, 7, 0, 1, 0, 2, 0), (1, 0, 2, 1, 5, 6, 0, 0, 2, 3), (1, 0, 2, 0, 7, 8, 0, 0, 2, 2),
        (1, 0, 0, 0, 7, 7, 1, 0, 4, 0), (2, 6, 0, 0, 5, 5, 1, 0, 2, 0),
    };

    private static readonly int[][] Bc7Weights =
    {
        Array.Empty<int>(), Array.Empty<int>(), new[] { 0, 21, 43, 64 }, new[] { 0, 9, 18, 27, 37, 46, 55, 64 },
        new[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 },
    };

    /// <summary>A BC7 block's texel 0. Texel 0 is always subset 0's anchor, so no partition table is needed: its
    /// endpoints are the first pair, its index the first in the stream, one bit short.</summary>
    internal static byte[] Bc7Texel0(byte[] b)
    {
        int mode = 0;
        while (mode < 8 && (b[0] & (1 << mode)) == 0)
            mode++;
        if (mode == 8)
            return new byte[3];
        var m = Bc7Modes[mode];
        var bits = new UInt128(BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(8)), BinaryPrimitives.ReadUInt64LittleEndian(b));
        int pos = mode + 1;
        int Take(int n)
        {
            int v = n == 0 ? 0 : (int)(uint)((bits >> pos) & (((UInt128)1 << n) - 1));
            pos += n;
            return v;
        }

        Take(m.PB);
        int rotation = Take(m.RB);
        int selector = Take(m.ISB);
        int endpoints = 2 * m.NS;
        var e = new int[endpoints, 4];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < endpoints; i++)
                e[i, c] = Take(m.CB);
        for (int i = 0; i < endpoints; i++)
            e[i, 3] = m.AB > 0 ? Take(m.AB) : 0;
        int colourBits = m.CB, alphaBits = m.AB;
        if (m.EPB == 1 || m.SPB == 1)
        {
            var p = new int[endpoints];
            if (m.EPB == 1)
                for (int i = 0; i < endpoints; i++)
                    p[i] = Take(1);
            else
                for (int s = 0; s < m.NS; s++)
                    p[2 * s] = p[2 * s + 1] = Take(1);
            for (int i = 0; i < endpoints; i++)
            {
                for (int c = 0; c < 3; c++)
                    e[i, c] = (e[i, c] << 1) | p[i];
                if (m.AB > 0)
                    e[i, 3] = (e[i, 3] << 1) | p[i];
            }
            colourBits++;
            if (alphaBits > 0)
                alphaBits++;
        }

        int primary = Take(m.IB - 1);
        int secondary = 0;
        if (m.IB2 > 0)
        {
            Take(15 * m.IB);
            secondary = Take(m.IB2 - 1);
        }
        int colourIndex = primary, colourIndexBits = m.IB;
        if (m.IB2 > 0 && selector == 1)
        {
            colourIndex = secondary;
            colourIndexBits = m.IB2;
        }
        int alphaIndex = m.IB2 == 0 ? primary : selector == 1 ? primary : secondary;
        int alphaIndexBits = m.IB2 == 0 ? m.IB : selector == 1 ? m.IB : m.IB2;

        var o = new int[4];
        for (int c = 0; c < 4; c++)
        {
            bool alpha = c == 3;
            if (alpha && m.AB == 0)
            {
                o[c] = 255;
                continue;
            }
            int n = alpha ? alphaBits : colourBits;
            int a = Expand(e[0, c], n), z = Expand(e[1, c], n);
            int w = Bc7Weights[alpha ? alphaIndexBits : colourIndexBits][alpha ? alphaIndex : colourIndex];
            o[c] = ((64 - w) * a + w * z + 32) >> 6;
        }
        if (rotation > 0)
            (o[rotation - 1], o[3]) = (o[3], o[rotation - 1]);
        return new[] { (byte)o[0], (byte)o[1], (byte)o[2] };
    }

    private static int Expand(int v, int bits) => (v << (8 - bits)) | (v >> (2 * bits - 8));
}
