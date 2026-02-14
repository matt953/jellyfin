using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Api.Attributes;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Trickplay controller.
/// </summary>
[Route("")]
[Authorize]
public class TrickplayController : BaseJellyfinApiController
{
    private readonly ILibraryManager _libraryManager;
    private readonly ITrickplayManager _trickplayManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/>.</param>
    /// <param name="trickplayManager">Instance of <see cref="ITrickplayManager"/>.</param>
    public TrickplayController(
        ILibraryManager libraryManager,
        ITrickplayManager trickplayManager)
    {
        _libraryManager = libraryManager;
        _trickplayManager = trickplayManager;
    }

    /// <summary>
    /// Gets an image tiles playlist for trickplay.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="width">The width of a single tile.</param>
    /// <param name="mediaSourceId">The media version id, if using an alternate version.</param>
    /// <response code="200">Tiles playlist returned.</response>
    /// <returns>A <see cref="FileResult"/> containing the trickplay playlist file.</returns>
    [HttpGet("Videos/{itemId}/Trickplay/{width}/tiles.m3u8")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesPlaylistFile]
    public async Task<ActionResult> GetTrickplayHlsPlaylist(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] int width,
        [FromQuery] Guid? mediaSourceId)
    {
        string? playlist = await _trickplayManager.GetHlsPlaylist(mediaSourceId ?? itemId, width, User.GetToken()).ConfigureAwait(false);

        if (string.IsNullOrEmpty(playlist))
        {
            return NotFound();
        }

        return Content(playlist, MimeTypes.GetMimeType("playlist.m3u8"), Encoding.UTF8);
    }

    /// <summary>
    /// Gets a trickplay tile image.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="width">The width of a single tile.</param>
    /// <param name="index">The index of the desired tile.</param>
    /// <param name="mediaSourceId">The media version id, if using an alternate version.</param>
    /// <response code="200">Tile image returned.</response>
    /// <response code="200">Tile image not found at specified index.</response>
    /// <returns>A <see cref="FileResult"/> containing the trickplay tiles image.</returns>
    [HttpGet("Videos/{itemId}/Trickplay/{width}/{index}.jpg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesImageFile]
    public async Task<ActionResult> GetTrickplayTileImage(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] int width,
        [FromRoute, Required] int index,
        [FromQuery] Guid? mediaSourceId)
    {
        var item = _libraryManager.GetItemById<BaseItem>(mediaSourceId ?? itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        var saveWithMedia = _libraryManager.GetLibraryOptions(item).SaveTrickplayWithMedia;
        var path = await _trickplayManager.GetTrickplayTilePathAsync(item, width, index, saveWithMedia).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            Response.Headers.ContentDisposition = "attachment";
            return PhysicalFile(path, MediaTypeNames.Image.Jpeg);
        }

        return NotFound();
    }

    /// <summary>
    /// Gets an HLS I-frame playlist for Apple device scrubbing.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="mediaSourceId">The media version id, if using an alternate version.</param>
    /// <response code="200">I-frame playlist returned.</response>
    /// <response code="404">I-frame playlist not found.</response>
    /// <returns>A <see cref="FileResult"/> containing the I-frame playlist file.</returns>
    [HttpGet("Videos/{itemId}/IFrame/iframe.m3u8")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesPlaylistFile]
    public async Task<ActionResult> GetIFramePlaylist(
        [FromRoute, Required] Guid itemId,
        [FromQuery] Guid? mediaSourceId)
    {
        var item = _libraryManager.GetItemById<BaseItem>(mediaSourceId ?? itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        var saveWithMedia = _libraryManager.GetLibraryOptions(item).SaveTrickplayWithMedia;
        string? playlist = await _trickplayManager.GetIFrameHlsPlaylist(item, saveWithMedia, mediaSourceId ?? itemId, User.GetToken()).ConfigureAwait(false);

        if (string.IsNullOrEmpty(playlist))
        {
            return NotFound();
        }

        return Content(playlist, "application/vnd.apple.mpegurl");
    }

    /// <summary>
    /// Gets an I-frame segment or init file.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="fileName">The segment file name.</param>
    /// <param name="mediaSourceId">The media version id, if using an alternate version.</param>
    /// <response code="200">I-frame segment returned.</response>
    /// <response code="404">I-frame segment not found.</response>
    /// <returns>A <see cref="FileResult"/> containing the I-frame segment.</returns>
    [HttpGet("Videos/{itemId}/IFrame/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetIFrameSegment(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] string fileName,
        [FromQuery] Guid? mediaSourceId)
    {
        var item = _libraryManager.GetItemById<BaseItem>(mediaSourceId ?? itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        var saveWithMedia = _libraryManager.GetLibraryOptions(item).SaveTrickplayWithMedia;
        var dir = _trickplayManager.GetIFrameDirectory(item, saveWithMedia);
        if (string.IsNullOrEmpty(dir))
        {
            return NotFound();
        }

        var path = System.IO.Path.Combine(dir, fileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var contentType = fileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? MimeTypes.GetMimeType("playlist.m3u8")
            : "video/mp4";
        return PhysicalFile(path, contentType);
    }
}
