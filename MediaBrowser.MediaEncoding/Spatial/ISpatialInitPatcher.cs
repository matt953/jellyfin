using MediaBrowser.Model.Entities;

namespace MediaBrowser.MediaEncoding.Spatial;

/// <summary>
/// Interface for patching HLS init segments with spatial video metadata.
/// </summary>
public interface ISpatialInitPatcher
{
    /// <summary>
    /// Patches an HEVC init segment with VEXU spatial metadata.
    /// </summary>
    /// <param name="initData">The original init segment data.</param>
    /// <param name="format">The spatial video format.</param>
    /// <returns>The patched init segment data.</returns>
    byte[] PatchInitSegment(byte[] initData, Video3DFormat format);

    /// <summary>
    /// Checks if the format requires VEXU injection.
    /// </summary>
    /// <param name="format">The spatial video format.</param>
    /// <returns>True if the format requires VEXU metadata injection.</returns>
    bool RequiresVexu(Video3DFormat? format);
}
