namespace Emby.Naming.Video
{
    /// <summary>
    /// Helper object to return data from <see cref="Format3DParser"/>.
    /// </summary>
    public class Format3DResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Format3DResult"/> class.
        /// </summary>
        /// <param name="is3D">A value indicating whether the parsed string contains 3D tokens.</param>
        /// <param name="format3D">The 3D format. Value might be null if [is3D] is <c>false</c>.</param>
        /// <param name="precedingToken">The preceding token (e.g., "180" or "360" for spatial formats).</param>
        public Format3DResult(bool is3D, string? format3D, string? precedingToken = null)
        {
            Is3D = is3D;
            Format3D = format3D;
            PrecedingToken = precedingToken;
        }

        /// <summary>
        /// Gets a value indicating whether [is3 d].
        /// </summary>
        /// <value><c>true</c> if [is3 d]; otherwise, <c>false</c>.</value>
        public bool Is3D { get; }

        /// <summary>
        /// Gets the format3 d.
        /// </summary>
        /// <value>The format3 d.</value>
        public string? Format3D { get; }

        /// <summary>
        /// Gets the preceding token (e.g., "3d", "180", or "360" for spatial formats).
        /// </summary>
        /// <value>The preceding token, or null if none.</value>
        public string? PrecedingToken { get; }
    }
}
