#nullable enable

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Represents an audio track for multi-audio HLS output.
/// </summary>
public class HlsAudioTrackInfo
{
    /// <summary>
    /// Gets or sets the source stream index from the input file.
    /// </summary>
    public int StreamIndex { get; set; }

    /// <summary>
    /// Gets or sets the output stream index (0-based audio output index).
    /// </summary>
    public int OutputIndex { get; set; }

    /// <summary>
    /// Gets or sets the language code (ISO 639-1 or 639-2).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the track title/name from metadata.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the source codec (e.g., "aac", "ac3", "dts").
    /// </summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of audio channels.
    /// </summary>
    public int Channels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the default audio track.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether transcoding is required.
    /// </summary>
    public bool RequiresTranscode { get; set; }

    /// <summary>
    /// Gets or sets the target codec for HLS (aac, ac3, eac3).
    /// </summary>
    public string HlsCodec { get; set; } = "aac";

    /// <summary>
    /// Gets or sets the HLS codec string for CODECS attribute.
    /// </summary>
    public string HlsCodecString { get; set; } = "mp4a.40.2";

    /// <summary>
    /// Gets the display name for HLS manifest.
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Title)
        ? Title
        : !string.IsNullOrEmpty(Language)
            ? Language.ToUpperInvariant()
            : $"Audio {OutputIndex + 1}";
}
