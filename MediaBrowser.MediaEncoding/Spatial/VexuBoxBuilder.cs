using System;
using System.Buffers.Binary;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.MediaEncoding.Spatial;

/// <summary>
/// Builds VEXU (Video Extended Usage) boxes for Apple Vision Pro spatial video playback.
/// </summary>
/// <remarks>
/// The vexu box contains spatial video metadata that tells visionOS how to render
/// the content. Structure varies by format:
/// <list type="bullet">
///   <item>180°/360° stereo: eyes + proj + pack</item>
///   <item>360° mono: proj only</item>
///   <item>Flat SBS/MVC: eyes + pack (no proj)</item>
/// </list>
/// </remarks>
public static class VexuBoxBuilder
{
    // Projection types (4-char codes)
    private static readonly byte[] ProjectionHequ = "hequ"u8.ToArray(); // Half equirectangular (180°)
    private static readonly byte[] ProjectionEqui = "equi"u8.ToArray(); // Full equirectangular (360°)

    // Packing types (4-char codes)
    private static readonly byte[] PackingSide = "side"u8.ToArray(); // Side-by-side
    private static readonly byte[] PackingOver = "over"u8.ToArray(); // Over-under (top-bottom)

    /// <summary>
    /// Builds a VEXU box for the specified Video3DFormat.
    /// </summary>
    /// <param name="format">The spatial video format.</param>
    /// <returns>The VEXU box bytes, or empty array if format doesn't require VEXU.</returns>
    public static byte[] BuildVexuForFormat(Video3DFormat format)
    {
        return format switch
        {
            Video3DFormat.Stereo180Sbs => BuildVexuStereo(ProjectionHequ, PackingSide),
            Video3DFormat.Stereo180Ou => BuildVexuStereo(ProjectionHequ, PackingOver),
            Video3DFormat.Stereo360Sbs => BuildVexuStereo(ProjectionEqui, PackingSide),
            Video3DFormat.Stereo360Ou => BuildVexuStereo(ProjectionEqui, PackingOver),
            Video3DFormat.Mono360 => BuildVexuMono(ProjectionEqui),
            Video3DFormat.HalfSideBySide or Video3DFormat.FullSideBySide or Video3DFormat.MVC
                => BuildVexuFlatSbs(),
            Video3DFormat.HalfTopAndBottom or Video3DFormat.FullTopAndBottom
                => BuildVexuFlatOu(),
            _ => Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Checks if the format requires VEXU injection.
    /// </summary>
    /// <param name="format">The spatial video format.</param>
    /// <returns>True if the format requires VEXU metadata injection.</returns>
    public static bool RequiresVexu(Video3DFormat? format)
    {
        return format is Video3DFormat.Stereo180Sbs
            or Video3DFormat.Stereo180Ou
            or Video3DFormat.Stereo360Sbs
            or Video3DFormat.Stereo360Ou
            or Video3DFormat.Mono360
            or Video3DFormat.HalfSideBySide
            or Video3DFormat.FullSideBySide
            or Video3DFormat.HalfTopAndBottom
            or Video3DFormat.FullTopAndBottom
            or Video3DFormat.MVC;
    }

    /// <summary>
    /// Build a vexu box for stereo (two-eye) content with projection.
    /// </summary>
    /// <remarks>
    /// Structure:
    /// <code>
    /// vexu
    ///   eyes
    ///     stri (stereo_indication=0x03, both eyes)
    ///     hero (hero_eye=0x01, right eye primary)
    ///     cams
    ///       blin (baseline=65000 micrometers)
    ///   proj
    ///     prji (projection type: hequ or equi)
    ///   pack
    ///     pkin (packing type: side or over)
    /// </code>
    /// </remarks>
    private static byte[] BuildVexuStereo(byte[] projection, byte[] packing)
    {
        // Build eyes box
        var stri = BuildFullBox("stri"u8, [0x03]); // stereo_indication=0x03 (both eyes)
        var hero = BuildFullBox("hero"u8, [0x01]); // hero_eye=0x01 (right eye primary)
        var blin = BuildFullBox("blin"u8, GetUInt32BigEndian(65000)); // baseline=65000 micrometers (~65mm IPD)
        var cams = BuildBox("cams"u8, blin);

        var eyesContent = new byte[stri.Length + hero.Length + cams.Length];
        Buffer.BlockCopy(stri, 0, eyesContent, 0, stri.Length);
        Buffer.BlockCopy(hero, 0, eyesContent, stri.Length, hero.Length);
        Buffer.BlockCopy(cams, 0, eyesContent, stri.Length + hero.Length, cams.Length);
        var eyes = BuildBox("eyes"u8, eyesContent);

        // Build proj box
        var prji = BuildFullBox("prji"u8, projection);
        var proj = BuildBox("proj"u8, prji);

        // Build pack box
        var pkin = BuildFullBox("pkin"u8, packing);
        var pack = BuildBox("pack"u8, pkin);

        // Build vexu container
        var vexuContent = new byte[eyes.Length + proj.Length + pack.Length];
        Buffer.BlockCopy(eyes, 0, vexuContent, 0, eyes.Length);
        Buffer.BlockCopy(proj, 0, vexuContent, eyes.Length, proj.Length);
        Buffer.BlockCopy(pack, 0, vexuContent, eyes.Length + proj.Length, pack.Length);

        return BuildBox("vexu"u8, vexuContent);
    }

    /// <summary>
    /// Build a vexu box for mono (single-eye) content.
    /// </summary>
    /// <remarks>
    /// Structure:
    /// <code>
    /// vexu
    ///   proj
    ///     prji (projection type: equi)
    /// </code>
    /// </remarks>
    private static byte[] BuildVexuMono(byte[] projection)
    {
        var prji = BuildFullBox("prji"u8, projection);
        var proj = BuildBox("proj"u8, prji);
        return BuildBox("vexu"u8, proj);
    }

    /// <summary>
    /// Build a vexu box for flat 3D stereo content (MVC Blu-ray, SBS).
    /// </summary>
    /// <remarks>
    /// Flat 3D has stereo information but NO projection (already rectilinear).
    /// Structure:
    /// <code>
    /// vexu
    ///   eyes
    ///     stri (stereo_indication=0x03, both eyes)
    ///     hero (hero_eye=0x01, right eye primary)
    ///     cams
    ///       blin (baseline=65000 micrometers)
    ///   pack
    ///     pkin (packing type: side)
    /// </code>
    /// </remarks>
    private static byte[] BuildVexuFlatSbs()
    {
        // Build eyes box
        var stri = BuildFullBox("stri"u8, [0x03]);
        var hero = BuildFullBox("hero"u8, [0x01]);
        var blin = BuildFullBox("blin"u8, GetUInt32BigEndian(65000));
        var cams = BuildBox("cams"u8, blin);

        var eyesContent = new byte[stri.Length + hero.Length + cams.Length];
        Buffer.BlockCopy(stri, 0, eyesContent, 0, stri.Length);
        Buffer.BlockCopy(hero, 0, eyesContent, stri.Length, hero.Length);
        Buffer.BlockCopy(cams, 0, eyesContent, stri.Length + hero.Length, cams.Length);
        var eyes = BuildBox("eyes"u8, eyesContent);

        // Build pack box (side-by-side)
        var pkin = BuildFullBox("pkin"u8, PackingSide);
        var pack = BuildBox("pack"u8, pkin);

        // Build vexu container (NO proj box for flat content)
        var vexuContent = new byte[eyes.Length + pack.Length];
        Buffer.BlockCopy(eyes, 0, vexuContent, 0, eyes.Length);
        Buffer.BlockCopy(pack, 0, vexuContent, eyes.Length, pack.Length);

        return BuildBox("vexu"u8, vexuContent);
    }

    /// <summary>
    /// Build a vexu box for flat 3D over-under content.
    /// </summary>
    private static byte[] BuildVexuFlatOu()
    {
        // Build eyes box
        var stri = BuildFullBox("stri"u8, [0x03]);
        var hero = BuildFullBox("hero"u8, [0x01]);
        var blin = BuildFullBox("blin"u8, GetUInt32BigEndian(65000));
        var cams = BuildBox("cams"u8, blin);

        var eyesContent = new byte[stri.Length + hero.Length + cams.Length];
        Buffer.BlockCopy(stri, 0, eyesContent, 0, stri.Length);
        Buffer.BlockCopy(hero, 0, eyesContent, stri.Length, hero.Length);
        Buffer.BlockCopy(cams, 0, eyesContent, stri.Length + hero.Length, cams.Length);
        var eyes = BuildBox("eyes"u8, eyesContent);

        // Build pack box (over-under)
        var pkin = BuildFullBox("pkin"u8, PackingOver);
        var pack = BuildBox("pack"u8, pkin);

        // Build vexu container (NO proj box for flat content)
        var vexuContent = new byte[eyes.Length + pack.Length];
        Buffer.BlockCopy(eyes, 0, vexuContent, 0, eyes.Length);
        Buffer.BlockCopy(pack, 0, vexuContent, eyes.Length, pack.Length);

        return BuildBox("vexu"u8, vexuContent);
    }

    /// <summary>
    /// Build an ISO base media file format box.
    /// </summary>
    /// <remarks>
    /// Box structure:
    /// <code>
    /// [4 bytes: size (big-endian)]
    /// [4 bytes: type (4-char code)]
    /// [N bytes: content]
    /// </code>
    /// </remarks>
    private static byte[] BuildBox(ReadOnlySpan<byte> boxType, ReadOnlySpan<byte> content)
    {
        var size = 8 + content.Length;
        var result = new byte[size];

        // Write size (big-endian)
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)size);

        // Write type
        boxType.CopyTo(result.AsSpan(4, 4));

        // Write content
        content.CopyTo(result.AsSpan(8));

        return result;
    }

    /// <summary>
    /// Build an ISO base media file format FullBox.
    /// </summary>
    /// <remarks>
    /// FullBox structure:
    /// <code>
    /// [4 bytes: size (big-endian)]
    /// [4 bytes: type (4-char code)]
    /// [1 byte: version (typically 0)]
    /// [3 bytes: flags (typically 0)]
    /// [N bytes: content]
    /// </code>
    /// </remarks>
    private static byte[] BuildFullBox(ReadOnlySpan<byte> boxType, ReadOnlySpan<byte> content)
    {
        var size = 12 + content.Length;
        var result = new byte[size];

        // Write size (big-endian)
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)size);

        // Write type
        boxType.CopyTo(result.AsSpan(4, 4));

        // Write version=0, flags=0
        result[8] = 0;
        result[9] = 0;
        result[10] = 0;
        result[11] = 0;

        // Write content
        content.CopyTo(result.AsSpan(12));

        return result;
    }

    /// <summary>
    /// Get a uint32 as big-endian bytes.
    /// </summary>
    private static byte[] GetUInt32BigEndian(uint value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }
}
