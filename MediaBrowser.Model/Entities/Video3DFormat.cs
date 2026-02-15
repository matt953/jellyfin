#pragma warning disable CS1591

namespace MediaBrowser.Model.Entities
{
    /// <summary>
    /// Represents the 3D or spatial video format of a video.
    /// </summary>
    public enum Video3DFormat
    {
        // Traditional 3D formats
        HalfSideBySide,
        FullSideBySide,
        FullTopAndBottom,
        HalfTopAndBottom,
        MVC,

        // Spatial/VR formats for visionOS/Apple Vision Pro
        Stereo180Sbs,
        Stereo180Ou,
        Stereo360Sbs,
        Stereo360Ou,
        Mono360
    }
}
