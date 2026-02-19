using System;

namespace Jellyfin.MediaEncoding.Hls.Playlist;

/// <summary>
/// Request class for the <see cref="IDynamicHlsPlaylistGenerator.CreateMainPlaylist(CreateMainPlaylistRequest)"/> method.
/// </summary>
public class CreateMainPlaylistRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateMainPlaylistRequest"/> class.
    /// </summary>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <param name="filePath">The absolute file path to the file.</param>
    /// <param name="desiredSegmentLengthMs">The desired segment length in milliseconds.</param>
    /// <param name="totalRuntimeTicks">The total duration of the file in ticks.</param>
    /// <param name="segmentContainer">The desired segment container eg. "ts".</param>
    /// <param name="endpointPrefix">The URI prefix for the relative URL in the playlist.</param>
    /// <param name="queryString">The desired query string to append (must start with ?).</param>
    /// <param name="isRemuxingVideo">Whether the video is being remuxed.</param>
    /// <param name="enableAppleMediaProfile">Whether Apple Projected Media Profile is enabled.</param>
    /// <param name="enableMultiAudio">Whether multi-audio mode is enabled.</param>
    /// <param name="playlistId">The playlist ID for multi-audio segment URLs.</param>
    /// <param name="streamName">The stream name for multi-audio (e.g., "video" or "audio_1").</param>
    /// <param name="useRelativeSegmentUrls">Whether to use relative segment URLs (no hls-ma prefix). True for audio playlists served from hls-ma/ subdirectory.</param>
    public CreateMainPlaylistRequest(Guid? mediaSourceId, string filePath, int desiredSegmentLengthMs, long totalRuntimeTicks, string segmentContainer, string endpointPrefix, string queryString, bool isRemuxingVideo, bool enableAppleMediaProfile = false, bool enableMultiAudio = false, string? playlistId = null, string? streamName = null, bool useRelativeSegmentUrls = false)
    {
        MediaSourceId = mediaSourceId;
        FilePath = filePath;
        DesiredSegmentLengthMs = desiredSegmentLengthMs;
        TotalRuntimeTicks = totalRuntimeTicks;
        SegmentContainer = segmentContainer;
        EndpointPrefix = endpointPrefix;
        QueryString = queryString;
        IsRemuxingVideo = isRemuxingVideo;
        EnableAppleMediaProfile = enableAppleMediaProfile;
        EnableMultiAudio = enableMultiAudio;
        PlaylistId = playlistId;
        StreamName = streamName;
        UseRelativeSegmentUrls = useRelativeSegmentUrls;
    }

    /// <summary>
    /// Gets the media source id.
    /// </summary>
    public Guid? MediaSourceId { get; }

    /// <summary>
    /// Gets the file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the desired segment length in milliseconds.
    /// </summary>
    public int DesiredSegmentLengthMs { get; }

    /// <summary>
    /// Gets the total runtime in ticks.
    /// </summary>
    public long TotalRuntimeTicks { get; }

    /// <summary>
    /// Gets the segment container.
    /// </summary>
    public string SegmentContainer { get; }

    /// <summary>
    /// Gets the endpoint prefix for the URL.
    /// </summary>
    public string EndpointPrefix { get; }

    /// <summary>
    /// Gets the query string.
    /// </summary>
    public string QueryString { get; }

    /// <summary>
    /// Gets a value indicating whether the video is being remuxed.
    /// </summary>
    public bool IsRemuxingVideo { get; }

    /// <summary>
    /// Gets a value indicating whether Apple Projected Media Profile is enabled.
    /// </summary>
    public bool EnableAppleMediaProfile { get; }

    /// <summary>
    /// Gets a value indicating whether multi-audio mode is enabled.
    /// </summary>
    public bool EnableMultiAudio { get; }

    /// <summary>
    /// Gets the playlist ID for multi-audio segment URLs.
    /// </summary>
    public string? PlaylistId { get; }

    /// <summary>
    /// Gets the stream name for multi-audio (e.g., "video" or "audio_1").
    /// </summary>
    public string? StreamName { get; }

    /// <summary>
    /// Gets a value indicating whether to use relative segment URLs.
    /// When true, segment URLs are relative to the playlist location (no hls-ma prefix).
    /// When false, segment URLs include the hls-ma/{playlistId}/ prefix.
    /// </summary>
    public bool UseRelativeSegmentUrls { get; }
}
