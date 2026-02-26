namespace MediaBrowser.MediaEncoding.Subtitles.Pgs;

/// <summary>
/// PGS segment types as defined in the Blu-ray Disc specification.
/// </summary>
public enum PgsSegmentType
{
    /// <summary>
    /// Unknown or unsupported segment type.
    /// </summary>
    None = 0,

    /// <summary>
    /// Palette Definition Segment - defines colors for the subtitle.
    /// </summary>
    PaletteDefinition = 0x14,

    /// <summary>
    /// Object Definition Segment - contains RLE-compressed bitmap data.
    /// </summary>
    ObjectDefinition = 0x15,

    /// <summary>
    /// Presentation Composition Segment - timing and positioning information.
    /// </summary>
    PresentationComposition = 0x16,

    /// <summary>
    /// Window Definition Segment - defines display area boundaries.
    /// </summary>
    WindowDefinition = 0x17,

    /// <summary>
    /// End of Display Set Segment - marks the end of a display set.
    /// </summary>
    EndOfDisplaySet = 0x80
}
