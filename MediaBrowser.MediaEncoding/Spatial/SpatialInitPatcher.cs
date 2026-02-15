using System;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Spatial;

/// <summary>
/// Patches HLS init segments with VEXU spatial metadata for Apple Vision Pro.
/// </summary>
/// <remarks>
/// This service injects the vexu box into HEVC (hvc1/dvh1) sample entries
/// to enable proper spatial video playback on visionOS devices.
/// </remarks>
public class SpatialInitPatcher : ISpatialInitPatcher
{
    private readonly ILogger<SpatialInitPatcher> _logger;

    // Box type constants
    private static readonly byte[] Moov = "moov"u8.ToArray();
    private static readonly byte[] Trak = "trak"u8.ToArray();
    private static readonly byte[] Mdia = "mdia"u8.ToArray();
    private static readonly byte[] Minf = "minf"u8.ToArray();
    private static readonly byte[] Stbl = "stbl"u8.ToArray();
    private static readonly byte[] Stsd = "stsd"u8.ToArray();
    private static readonly byte[] Hvc1 = "hvc1"u8.ToArray();
    private static readonly byte[] Dvh1 = "dvh1"u8.ToArray();
    private static readonly byte[] Sv3D = "sv3d"u8.ToArray();
    private static readonly byte[] St3D = "st3d"u8.ToArray();
    private static readonly byte[] Vexu = "vexu"u8.ToArray();

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialInitPatcher"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SpatialInitPatcher}"/> interface.</param>
    public SpatialInitPatcher(ILogger<SpatialInitPatcher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool RequiresVexu(Video3DFormat? format)
    {
        return VexuBoxBuilder.RequiresVexu(format);
    }

    /// <inheritdoc />
    public byte[] PatchInitSegment(byte[] initData, Video3DFormat format)
    {
        // Check if this is HEVC
        if (!Mp4BoxHelper.HasHvc1Box(initData))
        {
            _logger.LogDebug("Skipping non-HEVC init segment (VEXU requires HEVC)");
            return initData;
        }

        // Build the vexu box for this format
        var vexu = VexuBoxBuilder.BuildVexuForFormat(format);
        if (vexu.Length == 0)
        {
            _logger.LogDebug("No VEXU box needed for format {Format}", format);
            return initData;
        }

        try
        {
            var result = InjectVexuBox(initData, vexu);
            _logger.LogInformation("Injected VEXU metadata ({Format}) into init segment", format);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject VEXU metadata into init segment");
            return initData;
        }
    }

    /// <summary>
    /// Inject vexu box into the hvc1/dvh1 sample entry box.
    /// </summary>
    /// <remarks>
    /// This traverses the box hierarchy: moov -> trak -> mdia -> minf -> stbl -> stsd -> hvc1
    /// and inserts the vexu box at the end of hvc1, updating all parent box sizes.
    /// Also strips any existing sv3d (Google Spherical Video V2) and st3d (Stereo 3D) boxes
    /// from the hvc1 sample entry, as they conflict with Apple's vexu metadata.
    /// </remarks>
    private byte[] InjectVexuBox(byte[] data, byte[] vexu)
    {
        // Find moov box
        var moovPos = Mp4BoxHelper.FindBox(data, 0, data.Length, Moov);
        if (moovPos < 0)
        {
            // Try scanning for moov (might not be at expected position)
            moovPos = Mp4BoxHelper.ScanForBox(data, Moov);
            if (moovPos < 0)
            {
                throw new InvalidOperationException("moov box not found");
            }
        }

        var moovSize = Mp4BoxHelper.GetBoxSize(data, moovPos);

        // Find trak -> mdia -> minf -> stbl -> stsd -> hvc1
        var trakPos = Mp4BoxHelper.FindBox(data, moovPos + 8, moovPos + moovSize, Trak);
        if (trakPos < 0)
        {
            throw new InvalidOperationException("trak box not found");
        }

        var trakSize = Mp4BoxHelper.GetBoxSize(data, trakPos);

        var mdiaPos = Mp4BoxHelper.FindBox(data, trakPos + 8, trakPos + trakSize, Mdia);
        if (mdiaPos < 0)
        {
            throw new InvalidOperationException("mdia box not found");
        }

        var mdiaSize = Mp4BoxHelper.GetBoxSize(data, mdiaPos);

        var minfPos = Mp4BoxHelper.FindBox(data, mdiaPos + 8, mdiaPos + mdiaSize, Minf);
        if (minfPos < 0)
        {
            throw new InvalidOperationException("minf box not found");
        }

        var minfSize = Mp4BoxHelper.GetBoxSize(data, minfPos);

        var stblPos = Mp4BoxHelper.FindBox(data, minfPos + 8, minfPos + minfSize, Stbl);
        if (stblPos < 0)
        {
            throw new InvalidOperationException("stbl box not found");
        }

        var stblSize = Mp4BoxHelper.GetBoxSize(data, stblPos);

        var stsdPos = Mp4BoxHelper.FindBox(data, stblPos + 8, stblPos + stblSize, Stsd);
        if (stsdPos < 0)
        {
            throw new InvalidOperationException("stsd box not found");
        }

        var stsdSize = Mp4BoxHelper.GetBoxSize(data, stsdPos);

        // stsd has 8-byte header + 4-byte version/flags + 4-byte entry_count = 16 bytes before entries
        // Try hvc1 first, then dvh1 (Dolby Vision HEVC for MV-HEVC/DV Profile 20)
        var hvc1Pos = Mp4BoxHelper.FindBox(data, stsdPos + 16, stsdPos + stsdSize, Hvc1);
        if (hvc1Pos < 0)
        {
            hvc1Pos = Mp4BoxHelper.FindBox(data, stsdPos + 16, stsdPos + stsdSize, Dvh1);
        }

        if (hvc1Pos < 0)
        {
            throw new InvalidOperationException("hvc1/dvh1 box not found");
        }

        var hvc1Size = Mp4BoxHelper.GetBoxSize(data, hvc1Pos);

        // hvc1/dvh1 structure: [8 header][78 sample entry fields][child boxes...]
        var hvc1ContentStart = hvc1Pos + 86;
        var hvc1End = hvc1Pos + hvc1Size;

        var totalRemoved = 0;
        var currentData = data;

        // Remove sv3d box if present (Google Spherical Video V2 - causes 360° interpretation issues)
        var sv3dPos = Mp4BoxHelper.FindBox(currentData, hvc1ContentStart, hvc1End, Sv3D);
        if (sv3dPos >= 0)
        {
            var sv3dSize = Mp4BoxHelper.GetBoxSize(currentData, sv3dPos);
            _logger.LogDebug("Stripping sv3d box ({Size} bytes) - conflicts with VEXU", sv3dSize);
            currentData = Mp4BoxHelper.StripBox(currentData, sv3dPos, sv3dSize);
            totalRemoved += sv3dSize;
        }

        // Remove st3d box if present (Stereo 3D box - vexu provides stereo info)
        var hvc1EndAfterSv3d = hvc1Pos + hvc1Size - totalRemoved;
        var st3dPos = Mp4BoxHelper.FindBox(currentData, hvc1ContentStart, hvc1EndAfterSv3d, St3D);
        if (st3dPos >= 0)
        {
            var st3dSize = Mp4BoxHelper.GetBoxSize(currentData, st3dPos);
            _logger.LogDebug("Stripping st3d box ({Size} bytes) - conflicts with VEXU", st3dSize);
            currentData = Mp4BoxHelper.StripBox(currentData, st3dPos, st3dSize);
            totalRemoved += st3dSize;
        }

        // Remove any existing vexu boxes to avoid duplicates
        var hvc1EndAfterSt3d = hvc1Pos + hvc1Size - totalRemoved;
        var oldVexuPos = Mp4BoxHelper.FindBox(currentData, hvc1ContentStart, hvc1EndAfterSt3d, Vexu);
        if (oldVexuPos >= 0)
        {
            var oldVexuSize = Mp4BoxHelper.GetBoxSize(currentData, oldVexuPos);
            _logger.LogDebug("Removing existing VEXU box ({Size} bytes) before injecting new one", oldVexuSize);
            currentData = Mp4BoxHelper.StripBox(currentData, oldVexuPos, oldVexuSize);
            totalRemoved += oldVexuSize;
        }

        // Calculate net size change: added vexu - removed boxes
        var sizeDelta = vexu.Length - totalRemoved;

        // Insert vexu at end of hvc1 (adjusted for removed boxes)
        var newHvc1Size = hvc1Size - totalRemoved;
        var insertPos = hvc1Pos + newHvc1Size;
        currentData = Mp4BoxHelper.InsertBytes(currentData, insertPos, vexu);

        // Rename dvwC to dvcC for Vision Pro compatibility
        if (Mp4BoxHelper.RenameDvwcToDvcc(currentData))
        {
            _logger.LogDebug("Renamed dvwC to dvcC for Vision Pro compatibility");
        }

        // Update all parent box sizes
        var newHvc1Final = (uint)(newHvc1Size + vexu.Length);
        Mp4BoxHelper.UpdateBoxSize(currentData, hvc1Pos, newHvc1Final);
        Mp4BoxHelper.UpdateBoxSize(currentData, stsdPos, (uint)(stsdSize + sizeDelta));
        Mp4BoxHelper.UpdateBoxSize(currentData, stblPos, (uint)(stblSize + sizeDelta));
        Mp4BoxHelper.UpdateBoxSize(currentData, minfPos, (uint)(minfSize + sizeDelta));
        Mp4BoxHelper.UpdateBoxSize(currentData, mdiaPos, (uint)(mdiaSize + sizeDelta));
        Mp4BoxHelper.UpdateBoxSize(currentData, trakPos, (uint)(trakSize + sizeDelta));
        Mp4BoxHelper.UpdateBoxSize(currentData, moovPos, (uint)(moovSize + sizeDelta));

        return currentData;
    }
}
