using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using BCnEncoder.Shared;

namespace CgfConverter.Renderers.Gltf;

internal static class GltfPngWriter
{
    private static readonly byte[] s_signature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static void Write(Stream output, ColorRgba32[] pixels, int width, int height, bool sourceAlphaEnabled,
        float? opacity)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pixels);

        if (!output.CanWrite)
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Length != checked(width * height))
            throw new ArgumentException("The pixel count must match the image dimensions.", nameof(pixels));

        var includeAlpha = sourceAlphaEnabled || opacity is not null;
        var channels = includeAlpha ? 4 : 3;
        var scanlineLength = checked(width * channels + 1);
        var scanline = new byte[scanlineLength];

        using var compressedImage = new MemoryStream();
        using (var zlib = new ZLibStream(compressedImage, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                scanline[0] = 0;
                var pixelIndex = y * width;
                var byteIndex = 1;

                for (var x = 0; x < width; x++)
                {
                    var pixel = pixels[pixelIndex + x];
                    scanline[byteIndex++] = pixel.r;
                    scanline[byteIndex++] = pixel.g;
                    scanline[byteIndex++] = pixel.b;

                    if (!includeAlpha)
                        continue;

                    var alpha = pixel.a;
                    if (opacity is float opacityValue)
                    {
                        alpha = sourceAlphaEnabled
                            ? (byte)(alpha * opacityValue)
                            : (byte)(255 * opacityValue);
                    }

                    scanline[byteIndex++] = alpha;
                }

                zlib.Write(scanline);
            }
        }

        output.Write(s_signature);

        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)height);
        header[8] = 8;
        header[9] = includeAlpha ? (byte)6 : (byte)2;
        WriteChunk(output, "IHDR"u8, header);

        WriteChunk(output, "IDAT"u8, compressedImage.GetBuffer().AsSpan(0, checked((int)compressedImage.Length)));
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> chunkField = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(chunkField, checked((uint)data.Length));
        output.Write(chunkField);
        output.Write(chunkType);
        output.Write(data);

        var crc32 = new Crc32();
        crc32.Append(chunkType);
        crc32.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(chunkField, crc32.GetCurrentHashAsUInt32());
        output.Write(chunkField);
    }
}
