using System;

namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// Converts palette-indexed pixels to RGBA format for OCR processing.
/// </summary>
public static class PaletteConverter
{
    /// <summary>
    /// Converts palette-indexed pixels to RGBA format.
    /// Uses actual palette colors (converted from YCbCr to RGB).
    /// </summary>
    /// <param name="paletteIndices">Array of palette indices (one per pixel).</param>
    /// <param name="palette">The palette entries indexed by ID.</param>
    /// <returns>RGBA pixel data (4 bytes per pixel: R, G, B, A).</returns>
    public static byte[] ToRgba(ReadOnlySpan<byte> paletteIndices, ReadOnlySpan<PgsPaletteEntry> palette)
    {
        var rgba = new byte[paletteIndices.Length * 4];

        for (int i = 0; i < paletteIndices.Length; i++)
        {
            int paletteIdx = paletteIndices[i];
            int rgbaIdx = i * 4;

            if (paletteIdx >= palette.Length)
            {
                // Invalid palette index - transparent pixel
                rgba[rgbaIdx] = 0;     // R
                rgba[rgbaIdx + 1] = 0; // G
                rgba[rgbaIdx + 2] = 0; // B
                rgba[rgbaIdx + 3] = 0; // A
            }
            else
            {
                var entry = palette[paletteIdx];

                // Convert YCbCr to RGB using actual palette colors
                var (r, g, b) = YCbCrToRgb(entry.Y, entry.Cb, entry.Cr);

                rgba[rgbaIdx] = r;
                rgba[rgbaIdx + 1] = g;
                rgba[rgbaIdx + 2] = b;
                rgba[rgbaIdx + 3] = entry.Alpha;
            }
        }

        return rgba;
    }

    /// <summary>
    /// Converts YCbCr palette entry to RGB values.
    /// </summary>
    /// <param name="y">Luma component.</param>
    /// <param name="cb">Blue-difference chroma.</param>
    /// <param name="cr">Red-difference chroma.</param>
    /// <returns>Tuple of (R, G, B) values clamped to 0-255.</returns>
    public static (byte R, byte G, byte B) YCbCrToRgb(byte y, byte cb, byte cr)
    {
        // ITU-R BT.601 conversion
        int r = (int)(y + (1.402 * (cr - 128)));
        int g = (int)(y - (0.344136 * (cb - 128)) - (0.714136 * (cr - 128)));
        int b = (int)(y + (1.772 * (cb - 128)));

        return (
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255));
    }
}
