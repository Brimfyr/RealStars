using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace RealStars;

/// <summary>
/// A ring system's texture as the ring passes see it: one row from the inner radius to the
/// outer, whose alpha is the rings' opacity seen straight through them and whose colour, taken
/// to linear light as the passes take it (gammaToLinear, a 2.2 power), is how much of the light
/// they stop comes back.
/// </summary>
internal sealed class RingProfile
{
    public RingProfile(float[] alpha, float[] albedo)
    {
        Alpha = alpha;
        Albedo = albedo;
    }

    /// <summary>Opacity straight through, per texel, from 0 to 1.</summary>
    public float[] Alpha { get; }

    /// <summary>The colour's luminance in linear light, per texel.</summary>
    public float[] Albedo { get; }

    private double _inner = double.NaN, _outer = double.NaN, _area;

    /// <summary>
    /// How much of the annulus sends light back, in square metres over pi: each texel's ring of
    /// area, times its opacity and its albedo. Over pi so that it compares with the planet's
    /// radius squared, as the bare annulus it replaced did.
    /// </summary>
    public double ReflectingArea(double innerM, double outerM)
    {
        if (innerM == _inner && outerM == _outer) return _area;
        double step = (outerM - innerM) / Alpha.Length, sum = 0.0;
        for (int i = 0; i < Alpha.Length; i++)
        {
            double r0 = innerM + i * step, r1 = r0 + step;
            sum += Alpha[i] * (double)Albedo[i] * (r1 * r1 - r0 * r0);
        }
        (_inner, _outer, _area) = (innerM, outerM, sum);
        return sum;
    }
}

/// <summary>
/// Ring textures, read on the CPU for what the engine does not work out for us: the Sun's burst
/// behind the rings (RingOcclusion) and the light the rings add to their planet seen as a point
/// (PlanetPhotometry). As much of PNG and KTX2 as a ring texture needs: PNG in grey, grey and
/// alpha, RGB or RGBA at 8 or 16 bits, not interlaced; KTX2 uncompressed at 8 bits per channel
/// or in half or full floats, which is what the engine loads without block compression.
/// </summary>
internal static class RingTexture
{
    private static readonly Dictionary<string, RingProfile?> _files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, FieldInfo?> _textureFields = new();
    private static readonly Dictionary<Type, PropertyInfo?> _modPaths = new();

    private static ReadOnlySpan<byte> PngSignature => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static ReadOnlySpan<byte> Ktx2Signature =>
        new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>A ring's texture, read once per file. Null when it cannot be read, which leaves
    /// those rings out of the burst and the planet's light rather than anything else.</summary>
    public static RingProfile? Of(object rings)
    {
        string? path = null;
        try
        {
            Type rt = rings.GetType();
            if (!_textureFields.TryGetValue(rt, out FieldInfo? tf))
                _textureFields[rt] = tf = AccessTools.Field(rt, "Texture");
            object? texture = tf?.GetValue(rings);
            if (texture == null) return null;
            Type tt = texture.GetType();
            if (!_modPaths.TryGetValue(tt, out PropertyInfo? mp))
                _modPaths[tt] = mp = AccessTools.Property(tt, "ModPath");
            path = mp?.GetValue(texture) as string;
            if (string.IsNullOrEmpty(path)) return null;
            if (_files.TryGetValue(path, out RingProfile? known)) return known;

            RingProfile profile = Read(File.ReadAllBytes(path));
            _files[path] = profile;
            return profile;
        }
        catch (Exception ex)
        {
            ShaderShadow.Log($"WARN: ring texture {path ?? "(unnamed)"} could not be read, so its rings "
                             + "neither dim the Sun's burst nor add to their planet's light: "
                             + (ex.InnerException ?? ex).Message);
            if (path != null) _files[path] = null;
            return null;
        }
    }

    public static RingProfile Read(byte[] file)
    {
        if (file.AsSpan().StartsWith(PngSignature)) return FromPng(file);
        if (file.AsSpan().StartsWith(Ktx2Signature)) return FromKtx2(file);
        throw new InvalidDataException("neither PNG nor KTX2");
    }

    /// <summary>The first row of a PNG. A format without alpha reads as opaque, as the GPU
    /// would sample it.</summary>
    public static RingProfile FromPng(byte[] png)
    {
        if (!png.AsSpan().StartsWith(PngSignature)) throw new InvalidDataException("not a PNG");

        int width = 0, height = 0, depth = 0, colour = -1, interlace = 0;
        using var idat = new MemoryStream();
        for (int i = 8; i + 8 <= png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(i));
            string type = Encoding.ASCII.GetString(png, i + 4, 4);
            int data = i + 8;
            if (length < 0 || data + length > png.Length) throw new InvalidDataException("truncated PNG");
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data + 4));
                depth = png[data + 8];
                colour = png[data + 9];
                interlace = png[data + 12];
            }
            else if (type == "IDAT") idat.Write(png, data, length);
            else if (type == "IEND") break;
            i = data + length + 4;                                // past the CRC
        }

        int channels = colour switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (width <= 0 || height <= 0 || channels == 0 || (depth != 8 && depth != 16) || interlace != 0)
            throw new InvalidDataException($"unsupported PNG (colour type {colour}, {depth} bit, interlace {interlace})");

        int bytes = depth / 8, pixel = channels * bytes;
        var row = new byte[1 + width * pixel];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            z.ReadExactly(row);

        // The first row's filter, with the row above it all zeros: Up changes nothing, Average
        // adds half the left neighbour, and Paeth always predicts the left neighbour, as Sub does.
        Span<byte> line = row.AsSpan(1);
        switch (row[0])
        {
            case 0: case 2: break;
            case 1: case 4:
                for (int i = pixel; i < line.Length; i++) line[i] += line[i - pixel];
                break;
            case 3:
                for (int i = pixel; i < line.Length; i++) line[i] += (byte)(line[i - pixel] >> 1);
                break;
            default: throw new InvalidDataException("unknown PNG filter " + row[0]);
        }

        var alpha = new float[width];
        var albedo = new float[width];
        bool hasAlpha = colour == 4 || colour == 6, grey = colour == 0 || colour == 4;
        for (int x = 0; x < width; x++)
        {
            int at = 1 + x * pixel;
            float Channel(int c) => bytes == 1 ? row[at + c] / 255f : ((row[at + 2 * c] << 8) | row[at + 2 * c + 1]) / 65535f;
            alpha[x] = hasAlpha ? Channel(channels - 1) : 1f;
            albedo[x] = grey ? Luminance(Channel(0), Channel(0), Channel(0))
                             : Luminance(Channel(0), Channel(1), Channel(2));
        }
        return new RingProfile(alpha, albedo);
    }

    /// <summary>Level 0's first row of an uncompressed KTX2: RGBA at 8 bits (unorm or sRGB),
    /// or in half or full floats.</summary>
    public static RingProfile FromKtx2(byte[] ktx)
    {
        if (ktx.Length < 104 || !ktx.AsSpan().StartsWith(Ktx2Signature)) throw new InvalidDataException("not a KTX2");
        uint format = BinaryPrimitives.ReadUInt32LittleEndian(ktx.AsSpan(12));
        long width = BinaryPrimitives.ReadUInt32LittleEndian(ktx.AsSpan(20));
        if (BinaryPrimitives.ReadUInt32LittleEndian(ktx.AsSpan(44)) != 0)
            throw new InvalidDataException("supercompressed KTX2");
        // The level index follows the 80-byte header; its first entry is level 0.
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(ktx.AsSpan(80));
        int texel = format switch
        {
            37 or 43 => 4,        // R8G8B8A8_UNORM, R8G8B8A8_SRGB
            97 => 8,              // R16G16B16A16_SFLOAT
            109 => 16,            // R32G32B32A32_SFLOAT
            _ => throw new InvalidDataException($"unsupported KTX2 format {format}"),
        };
        if (width <= 0 || offset + (ulong)(width * texel) > (ulong)ktx.Length)
            throw new InvalidDataException("truncated KTX2");

        var alpha = new float[width];
        var albedo = new float[width];
        for (int x = 0; x < width; x++)
        {
            int at = (int)offset + x * texel;
            float Channel(int c) => format switch
            {
                37 => ktx[at + c] / 255f,
                // The GPU takes sRGB to linear before the shader applies its own 2.2 power.
                43 => c == 3 ? ktx[at + c] / 255f : SrgbToLinear(ktx[at + c] / 255f),
                97 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(ktx.AsSpan(at + 2 * c))),
                _ => BinaryPrimitives.ReadSingleLittleEndian(ktx.AsSpan(at + 4 * c)),
            };
            alpha[x] = Math.Clamp(Channel(3), 0f, 1f);
            albedo[x] = Luminance(Channel(0), Channel(1), Channel(2));
        }
        return new RingProfile(alpha, albedo);
    }

    /// <summary>The luminance the ring passes light: each channel through gammaToLinear's 2.2
    /// power, then weighted as for sRGB primaries.</summary>
    private static float Luminance(float r, float g, float b)
        => (float)(0.2126 * Math.Pow(Math.Max(r, 0f), 2.2) + 0.7152 * Math.Pow(Math.Max(g, 0f), 2.2)
                   + 0.0722 * Math.Pow(Math.Max(b, 0f), 2.2));

    private static float SrgbToLinear(float v)
        => v <= 0.04045f ? v / 12.92f : (float)Math.Pow((v + 0.055) / 1.055, 2.4);
}
