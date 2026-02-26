namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// A palette entry in a PGS subtitle, using YCbCr color space.
/// </summary>
/// <param name="Id">The palette entry ID (0-255).</param>
/// <param name="Y">Luma component (0-255).</param>
/// <param name="Cb">Blue-difference chroma component (0-255).</param>
/// <param name="Cr">Red-difference chroma component (0-255).</param>
/// <param name="Alpha">Alpha transparency (0=transparent, 255=opaque).</param>
public readonly record struct PgsPaletteEntry(byte Id, byte Y, byte Cb, byte Cr, byte Alpha);
