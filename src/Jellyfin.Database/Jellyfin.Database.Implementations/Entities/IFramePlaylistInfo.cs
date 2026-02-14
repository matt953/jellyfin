using System;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// An entity representing the metadata for an HLS I-frame playlist.
/// </summary>
public class IFramePlaylistInfo
{
    /// <summary>
    /// Gets or sets the id of the associated item.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets width of I-frame thumbnails.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets height of I-frame thumbnails.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the number of segments in the playlist.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public int SegmentCount { get; set; }

    /// <summary>
    /// Gets or sets bandwidth usage in bits per second.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public int Bandwidth { get; set; }
}
