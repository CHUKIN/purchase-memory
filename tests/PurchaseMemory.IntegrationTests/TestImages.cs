using System.Buffers.Binary;
using SkiaSharp;

namespace PurchaseMemory.IntegrationTests;

/// <summary>Real, decodable images built in memory for the compression tests.</summary>
internal static class TestImages
{
    /// <summary>A white picture with a red block in its top-left corner.</summary>
    public static byte[] Jpeg(int width, int height, int quality = 90) =>
        Encode(width, height, SKEncodedImageFormat.Jpeg, quality, transparent: false);

    public static byte[] Png(int width, int height, bool transparent = false) =>
        Encode(width, height, SKEncodedImageFormat.Png, 100, transparent);

    /// <summary>
    /// Inserts an EXIF APP1 segment right after SOI: IFD0 carries the
    /// orientation, camera make and a GPS IFD (latitude reference + value).
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var tiff = BuildTiff(orientation);
        var exifPayload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(exifPayload);
        tiff.CopyTo(exifPayload, 6);

        var segment = new byte[4 + exifPayload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), checked((ushort)(exifPayload.Length + 2)));
        exifPayload.CopyTo(segment, 4);

        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    /// <summary>
    /// Rewrites the declared size in a PNG header (and its CRC): a tiny file that
    /// claims to be huge, like a decompression bomb. Nothing else is changed.
    /// </summary>
    public static byte[] PngDeclaring(byte[] png, int width, int height, bool interlaced = false)
    {
        var copy = (byte[])png.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(20), (uint)height);
        copy[28] = interlaced ? (byte)1 : (byte)0;
        var crc = Crc32(copy.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(29), crc);
        return copy;
    }

    /// <summary>Rewrites the declared size in the JPEG's start-of-frame segment.</summary>
    public static byte[] JpegDeclaring(byte[] jpeg, int width, int height)
    {
        var copy = (byte[])jpeg.Clone();
        for (var i = 2; i < copy.Length - 9; i++)
        {
            if (copy[i] == 0xFF && copy[i + 1] is 0xC0 or 0xC2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(i + 5), (ushort)height);
                BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(i + 7), (ushort)width);
                return copy;
            }
        }

        throw new InvalidOperationException("No start-of-frame segment.");
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    public static SKBitmap Decode(byte[] bytes) =>
        SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Test image did not decode.");

    public static bool ContainsSequence(byte[] haystack, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, int quality, bool transparent)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 4f, height / 4f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static byte[] BuildTiff(ushort orientation)
    {
        // Little-endian TIFF: header (8) + IFD0 (2 + 3*12 + 4) + GPS IFD (2 + 2*12 + 4) + data.
        const int ifd0Offset = 8;
        const int ifd0Size = 2 + (3 * 12) + 4;
        const int gpsOffset = ifd0Offset + ifd0Size;
        const int gpsSize = 2 + (2 * 12) + 4;
        const int makeOffset = gpsOffset + gpsSize;
        var make = "SecretCam\0"u8.ToArray();
        var latitudeOffset = makeOffset + make.Length;
        var buffer = new byte[latitudeOffset + 24];
        var span = buffer.AsSpan();

        "II"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifd0Offset);

        var ifd0 = span[ifd0Offset..];
        BinaryPrimitives.WriteUInt16LittleEndian(ifd0, 3);
        WriteEntry(ifd0[2..], tag: 0x010F, type: 2, count: (uint)make.Length, value: (uint)makeOffset); // Make
        WriteEntry(ifd0[14..], tag: 0x0112, type: 3, count: 1, value: orientation);                       // Orientation
        WriteEntry(ifd0[26..], tag: 0x8825, type: 4, count: 1, value: gpsOffset);                         // GPS IFD

        var gps = span[gpsOffset..];
        BinaryPrimitives.WriteUInt16LittleEndian(gps, 2);
        WriteEntry(gps[2..], tag: 0x0001, type: 2, count: 2, value: 'N');                                   // GPSLatitudeRef "N"
        WriteEntry(gps[14..], tag: 0x0002, type: 5, count: 3, value: (uint)latitudeOffset);               // GPSLatitude

        make.CopyTo(span[makeOffset..]);
        var latitude = span[latitudeOffset..];
        uint[] rationals = [52, 1, 13, 1, 3700, 100]; // 52°13'37" — somewhere in Warsaw
        for (var i = 0; i < rationals.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(latitude[(i * 4)..], rationals[i]);
        }

        return buffer;
    }

    private static void WriteEntry(Span<byte> entry, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], count);
        if (type == 3)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
        }
    }
}
