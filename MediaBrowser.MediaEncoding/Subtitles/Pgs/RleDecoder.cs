using System;

namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// Decodes PGS RLE-compressed bitmap data.
/// </summary>
public static class RleDecoder
{
    /// <summary>
    /// Decode PGS RLE-compressed bitmap data to palette indices.
    /// </summary>
    /// <param name="data">The RLE-compressed data.</param>
    /// <param name="width">The expected width in pixels.</param>
    /// <param name="height">The expected height in pixels.</param>
    /// <returns>Array of palette indices, one byte per pixel.</returns>
    /// <remarks>
    /// PGS RLE format:
    /// - If byte != 0: single pixel with that palette index value
    /// - If byte == 0: RLE sequence, read flags byte:
    ///   - flags == 0x00: end of line (pad to width)
    ///   - (flags and 0xC0) == 0x00: short run of color 0 (L pixels, where L = flags and 0x3F)
    ///   - (flags and 0xC0) == 0x40: long run of color 0 ((L shl 8 | LL) transparent pixels)
    ///   - (flags and 0xC0) == 0x80: short run of color (L pixels of color C)
    ///   - (flags and 0xC0) == 0xC0: long run of color ((L shl 8 | LL) pixels of color C).
    /// </remarks>
    public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height)
    {
        int totalPixels = width * height;
        var pixels = new byte[totalPixels];
        int pixelIndex = 0;
        int pos = 0;

        while (pixelIndex < totalPixels && pos < data.Length)
        {
            byte b = data[pos++];

            if (b != 0)
            {
                // Single pixel with color value
                pixels[pixelIndex++] = b;
            }
            else
            {
                // RLE sequence starting with 0x00
                if (pos >= data.Length)
                {
                    break;
                }

                byte flags = data[pos++];

                if (flags == 0)
                {
                    // End of line marker - pad rest of line with transparent pixels
                    int linePos = pixelIndex % width;
                    if (linePos != 0)
                    {
                        int padding = width - linePos;
                        // Already initialized to 0, just advance
                        pixelIndex += padding;
                    }
                }
                else if ((flags & 0xC0) == 0x00)
                {
                    // Short run of color 0: 00 0b00LLLLLL = L pixels of color 0
                    int runLength = flags & 0x3F;
                    // Already initialized to 0, just advance
                    pixelIndex += runLength;
                }
                else if ((flags & 0xC0) == 0x40)
                {
                    // Long run of transparent pixels: 00 0b01LLLLLL LL = (L << 8 | LL) transparent pixels
                    if (pos < data.Length)
                    {
                        int runLength = ((flags & 0x3F) << 8) | data[pos++];
                        // Already initialized to 0, just advance
                        pixelIndex += runLength;
                    }
                }
                else if ((flags & 0xC0) == 0x80)
                {
                    // Short run of color: 00 0b10LLLLLL CC = L pixels of color CC
                    int runLength = flags & 0x3F;
                    if (pos < data.Length)
                    {
                        byte color = data[pos++];
                        int end = Math.Min(pixelIndex + runLength, totalPixels);
                        for (int i = pixelIndex; i < end; i++)
                        {
                            pixels[i] = color;
                        }

                        pixelIndex = end;
                    }
                }
                else
                {
                    // (flags & 0xC0) == 0xC0 - Long run of color
                    // 00 0b11LLLLLL LL CC = (L << 8 | LL) pixels of color CC
                    int runLength = (flags & 0x3F) << 8;
                    if (pos < data.Length)
                    {
                        runLength |= data[pos++];
                    }

                    if (pos < data.Length)
                    {
                        byte color = data[pos++];
                        int end = Math.Min(pixelIndex + runLength, totalPixels);
                        for (int i = pixelIndex; i < end; i++)
                        {
                            pixels[i] = color;
                        }

                        pixelIndex = end;
                    }
                }
            }
        }

        return pixels;
    }
}
