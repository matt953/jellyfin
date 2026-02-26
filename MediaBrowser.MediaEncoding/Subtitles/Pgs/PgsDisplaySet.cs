using System;
using System.Diagnostics.CodeAnalysis;

namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// Represents a decoded PGS display set containing a subtitle bitmap.
/// </summary>
/// <param name="StartTime">The start time of the subtitle.</param>
/// <param name="EndTime">The end time of the subtitle.</param>
/// <param name="Width">The width of the bitmap in pixels.</param>
/// <param name="Height">The height of the bitmap in pixels.</param>
/// <param name="RgbaPixels">The decoded RGBA pixel data (4 bytes per pixel).</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Byte array required for ONNX Runtime tensor operations.")]
public record PgsDisplaySet(
    TimeSpan StartTime,
    TimeSpan EndTime,
    int Width,
    int Height,
    byte[] RgbaPixels);
