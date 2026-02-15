using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using J2N.Collections.Generic.Extensions;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Trickplay;

/// <summary>
/// ITrickplayManager implementation.
/// </summary>
public class TrickplayManager : ITrickplayManager
{
    private readonly ILogger<TrickplayManager> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IFileSystem _fileSystem;
    private readonly EncodingHelper _encodingHelper;
    private readonly IServerConfigurationManager _config;
    private readonly IImageEncoder _imageEncoder;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IApplicationPaths _appPaths;
    private readonly IPathManager _pathManager;

    private static readonly AsyncNonKeyedLocker _resourcePool = new(1);
    private static readonly string[] _trickplayImgExtensions = [".jpg"];

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="mediaEncoder">The media encoder.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="encodingHelper">The encoding helper.</param>
    /// <param name="config">The server configuration manager.</param>
    /// <param name="imageEncoder">The image encoder.</param>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="pathManager">The path manager.</param>
    public TrickplayManager(
        ILogger<TrickplayManager> logger,
        IMediaEncoder mediaEncoder,
        IFileSystem fileSystem,
        EncodingHelper encodingHelper,
        IServerConfigurationManager config,
        IImageEncoder imageEncoder,
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IApplicationPaths appPaths,
        IPathManager pathManager)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _encodingHelper = encodingHelper;
        _config = config;
        _imageEncoder = imageEncoder;
        _dbProvider = dbProvider;
        _appPaths = appPaths;
        _pathManager = pathManager;
    }

    /// <inheritdoc />
    public async Task MoveGeneratedTrickplayDataAsync(Video video, LibraryOptions libraryOptions, CancellationToken cancellationToken)
    {
        var options = _config.Configuration.TrickplayOptions;
        if (libraryOptions is null || !libraryOptions.EnableTrickplayImageExtraction || !CanGenerateTrickplay(video, options.Interval))
        {
            return;
        }

        var existingTrickplayResolutions = await GetTrickplayResolutions(video.Id).ConfigureAwait(false);
        foreach (var resolution in existingTrickplayResolutions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existingResolution = resolution.Key;
            var tileWidth = resolution.Value.TileWidth;
            var tileHeight = resolution.Value.TileHeight;
            var shouldBeSavedWithMedia = libraryOptions is not null && libraryOptions.SaveTrickplayWithMedia;
            var localOutputDir = new DirectoryInfo(GetTrickplayDirectory(video, tileWidth, tileHeight, existingResolution, false));
            var mediaOutputDir = new DirectoryInfo(GetTrickplayDirectory(video, tileWidth, tileHeight, existingResolution, true));
            if (shouldBeSavedWithMedia && localOutputDir.Exists)
            {
                var localDirFiles = localOutputDir.EnumerateFiles();
                var mediaDirExists = mediaOutputDir.Exists;
                if (localDirFiles.Any() && ((mediaDirExists && mediaOutputDir.EnumerateFiles().Any()) || !mediaDirExists))
                {
                    // Move images from local dir to media dir
                    MoveContent(localOutputDir.FullName, mediaOutputDir.FullName);
                    _logger.LogInformation("Moved trickplay images for {ItemName} to {Location}", video.Name, mediaOutputDir);
                }
            }
            else if (!shouldBeSavedWithMedia && mediaOutputDir.Exists)
            {
                var mediaDirFiles = mediaOutputDir.EnumerateFiles();
                var localDirExists = localOutputDir.Exists;
                if (mediaDirFiles.Any() && ((localDirExists && localOutputDir.EnumerateFiles().Any()) || !localDirExists))
                {
                    // Move images from media dir to local dir
                    MoveContent(mediaOutputDir.FullName, localOutputDir.FullName);
                    _logger.LogInformation("Moved trickplay images for {ItemName} to {Location}", video.Name, localOutputDir);
                }
            }
        }
    }

    private void MoveContent(string sourceFolder, string destinationFolder)
    {
        _fileSystem.MoveDirectory(sourceFolder, destinationFolder);
        var parent = Directory.GetParent(sourceFolder);
        if (parent is not null)
        {
            var parentContent = parent.EnumerateDirectories();
            if (!parentContent.Any())
            {
                parent.Delete();
            }
        }
    }

    /// <inheritdoc />
    public async Task RefreshTrickplayDataAsync(Video video, bool replace, LibraryOptions libraryOptions, CancellationToken cancellationToken)
    {
        var options = _config.Configuration.TrickplayOptions;
        if (!CanGenerateTrickplay(video, options.Interval) || libraryOptions is null)
        {
            return;
        }

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var saveWithMedia = libraryOptions.SaveTrickplayWithMedia;
            var trickplayDirectory = _pathManager.GetTrickplayDirectory(video, saveWithMedia);
            if (!libraryOptions.EnableTrickplayImageExtraction || replace)
            {
                // Prune existing data
                if (Directory.Exists(trickplayDirectory))
                {
                    try
                    {
                        Directory.Delete(trickplayDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Unable to clear trickplay directory: {Directory}: {Exception}", trickplayDirectory, ex);
                    }
                }

                await dbContext.TrickplayInfos
                        .Where(i => i.ItemId.Equals(video.Id))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                if (!replace)
                {
                    return;
                }
            }

            _logger.LogDebug("Trickplay refresh for {ItemId} (replace existing: {Replace})", video.Id, replace);

            if (options.Interval < 1000)
            {
                _logger.LogWarning("Trickplay image interval {Interval} is too small, reset to the minimum valid value of 1000", options.Interval);
                options.Interval = 1000;
            }

            foreach (var width in options.WidthResolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshTrickplayDataInternal(
                    video,
                    replace,
                    width,
                    options,
                    saveWithMedia,
                    cancellationToken).ConfigureAwait(false);
            }

            // Cleanup old trickplay files
            if (Directory.Exists(trickplayDirectory))
            {
                var existingFolders = Directory.GetDirectories(trickplayDirectory).ToList();
                var trickplayInfos = await dbContext.TrickplayInfos
                        .AsNoTracking()
                        .Where(i => i.ItemId.Equals(video.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                var expectedFolders = trickplayInfos.Select(i => GetTrickplayDirectory(video, i.TileWidth, i.TileHeight, i.Width, saveWithMedia)).ToList();
                var foldersToRemove = existingFolders.Except(expectedFolders);
                foreach (var folder in foldersToRemove)
                {
                    try
                    {
                        _logger.LogWarning("Pruning trickplay files for {Item}", video.Path);
                        Directory.Delete(folder, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Unable to remove trickplay directory: {Directory}: {Exception}", folder, ex);
                    }
                }
            }

            // Generate I-frame playlist if not disabled
            if (!libraryOptions.DisableIFramePlaylistGeneration)
            {
                await GenerateIFramePlaylistAsync(
                    video,
                    options,
                    saveWithMedia,
                    replace,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RefreshTrickplayDataInternal(
        Video video,
        bool replace,
        int width,
        TrickplayOptions options,
        bool saveWithMedia,
        CancellationToken cancellationToken)
    {
        var imgTempDir = string.Empty;

        using (await _resourcePool.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                // Extract images
                // Note: Media sources under parent items exist as their own video/item as well. Only use this video stream for trickplay.
                var mediaSource = video.GetMediaSources(false).FirstOrDefault(source => Guid.Parse(source.Id).Equals(video.Id));

                if (mediaSource is null)
                {
                    _logger.LogDebug("Found no matching media source for item {ItemId}", video.Id);
                    return;
                }

                // Ensure mediaSource has Video3DFormat from the video for spatial video filtering
                // MVC is decoded as single view and doesn't need special handling
                if (video.Video3DFormat.HasValue && video.Video3DFormat.Value != Video3DFormat.MVC)
                {
                    mediaSource.Video3DFormat = video.Video3DFormat;
                }
                else
                {
                    mediaSource.Video3DFormat = null;
                }

                var mediaPath = mediaSource.Path;
                if (!File.Exists(mediaPath))
                {
                    _logger.LogWarning("Media not found at {Path} for item {ItemID}", mediaPath, video.Id);
                    return;
                }

                // We support video backdrops, but we should not generate trickplay images for them
                var parentDirectory = Directory.GetParent(video.Path);
                if (parentDirectory is not null && string.Equals(parentDirectory.Name, "backdrops", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Ignoring backdrop media found at {Path} for item {ItemID}", video.Path, video.Id);
                    return;
                }

                // The width has to be even, otherwise a lot of filters will not be able to sample it
                var actualWidth = 2 * (width / 2);

                // Get effective dimensions after spatial video transformation (e.g., cropping SBS, converting 360°)
                var sourceWidth = mediaSource.VideoStream.Width ?? 0;
                var sourceHeight = mediaSource.VideoStream.Height ?? 0;
                var (effectiveWidth, _) = EncodingHelper.GetSpatialVideoSourceDimensions(
                    sourceWidth, sourceHeight, video.Video3DFormat);

                // Force using the video width when the trickplay setting has a too large width
                if (effectiveWidth > 0 && effectiveWidth < width)
                {
                    _logger.LogWarning("Video effective width {VideoWidth} is smaller than trickplay setting {TrickPlayWidth}, using video width for thumbnails", effectiveWidth, width);
                    actualWidth = 2 * (effectiveWidth / 2);
                }

                var tileWidth = options.TileWidth;
                var tileHeight = options.TileHeight;
                var outputDir = new DirectoryInfo(GetTrickplayDirectory(video, tileWidth, tileHeight, actualWidth, saveWithMedia));

                // Import existing trickplay tiles
                if (!replace && outputDir.Exists)
                {
                    var existingFiles = outputDir.GetFiles();
                    if (existingFiles.Length > 0)
                    {
                        var hasTrickplayResolution = await HasTrickplayResolutionAsync(video.Id, actualWidth).ConfigureAwait(false);
                        if (hasTrickplayResolution)
                        {
                            _logger.LogDebug("Found existing trickplay files for {ItemId}.", video.Id);
                            return;
                        }

                        // Import tiles
                        var localTrickplayInfo = new TrickplayInfo
                        {
                            ItemId = video.Id,
                            Width = width,
                            Interval = options.Interval,
                            TileWidth = options.TileWidth,
                            TileHeight = options.TileHeight,
                            ThumbnailCount = existingFiles.Length,
                            Height = 0,
                            Bandwidth = 0
                        };

                        foreach (var tile in existingFiles)
                        {
                            var image = _imageEncoder.GetImageSize(tile.FullName);
                            localTrickplayInfo.Height = Math.Max(localTrickplayInfo.Height, (int)Math.Ceiling((double)image.Height / localTrickplayInfo.TileHeight));
                            var bitrate = (int)Math.Ceiling((decimal)tile.Length * 8 / localTrickplayInfo.TileWidth / localTrickplayInfo.TileHeight / (localTrickplayInfo.Interval / 1000));
                            localTrickplayInfo.Bandwidth = Math.Max(localTrickplayInfo.Bandwidth, bitrate);
                        }

                        await SaveTrickplayInfo(localTrickplayInfo).ConfigureAwait(false);

                        _logger.LogDebug("Imported existing trickplay files for {ItemId}.", video.Id);
                        return;
                    }
                }

                // Generate trickplay tiles
                var mediaStream = mediaSource.VideoStream;
                var container = mediaSource.Container;

                _logger.LogInformation("Creating trickplay files at {Width} width, for {Path} [ID: {ItemId}]", actualWidth, mediaPath, video.Id);
                imgTempDir = await _mediaEncoder.ExtractVideoImagesOnIntervalAccelerated(
                    mediaPath,
                    container,
                    mediaSource,
                    mediaStream,
                    actualWidth,
                    TimeSpan.FromMilliseconds(options.Interval),
                    options.EnableHwAcceleration,
                    options.EnableHwEncoding,
                    options.ProcessThreads,
                    options.Qscale,
                    options.ProcessPriority,
                    options.EnableKeyFrameOnlyExtraction,
                    _encodingHelper,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(imgTempDir) || !Directory.Exists(imgTempDir))
                {
                    throw new InvalidOperationException("Null or invalid directory from media encoder.");
                }

                var images = _fileSystem.GetFiles(imgTempDir, _trickplayImgExtensions, false, false)
                    .Select(i => i.FullName)
                    .OrderBy(i => i)
                    .ToList();

                // Create tiles
                var trickplayInfo = CreateTiles(images, actualWidth, options, outputDir.FullName);

                // Save tiles info
                try
                {
                    if (trickplayInfo is not null)
                    {
                        trickplayInfo.ItemId = video.Id;
                        await SaveTrickplayInfo(trickplayInfo).ConfigureAwait(false);

                        _logger.LogInformation("Finished creation of trickplay files for {0}", mediaPath);
                    }
                    else
                    {
                        throw new InvalidOperationException("Null trickplay tiles info from CreateTiles.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while saving trickplay tiles info.");

                    // Make sure no files stay in metadata folders on failure
                    // if tiles info wasn't saved.
                    outputDir.Delete(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating trickplay images.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(imgTempDir))
                {
                    Directory.Delete(imgTempDir, true);
                }
            }
        }
    }

    /// <inheritdoc />
    public TrickplayInfo CreateTiles(IReadOnlyList<string> images, int width, TrickplayOptions options, string outputDir)
    {
        if (images.Count == 0)
        {
            throw new ArgumentException("Can't create trickplay from 0 images.");
        }

        var workDir = Path.Combine(_appPaths.TempDirectory, "trickplay_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var trickplayInfo = new TrickplayInfo
        {
            Width = width,
            Interval = options.Interval,
            TileWidth = options.TileWidth,
            TileHeight = options.TileHeight,
            ThumbnailCount = images.Count,
            // Set during image generation
            Height = 0,
            Bandwidth = 0
        };

        /*
         * Generate trickplay tiles from sets of thumbnails
         */
        var imageOptions = new ImageCollageOptions
        {
            Width = trickplayInfo.TileWidth,
            Height = trickplayInfo.TileHeight
        };

        var thumbnailsPerTile = trickplayInfo.TileWidth * trickplayInfo.TileHeight;
        var requiredTiles = (int)Math.Ceiling((double)images.Count / thumbnailsPerTile);

        for (int i = 0; i < requiredTiles; i++)
        {
            // Set output/input paths
            var tilePath = Path.Combine(workDir, $"{i}.jpg");

            imageOptions.OutputPath = tilePath;
            imageOptions.InputPaths = images.Skip(i * thumbnailsPerTile).Take(Math.Min(thumbnailsPerTile, images.Count - (i * thumbnailsPerTile))).ToList();

            // Generate image and use returned height for tiles info
            var height = _imageEncoder.CreateTrickplayTile(imageOptions, options.JpegQuality, trickplayInfo.Width, trickplayInfo.Height != 0 ? trickplayInfo.Height : null);
            if (trickplayInfo.Height == 0)
            {
                trickplayInfo.Height = height;
            }

            // Update bitrate
            var bitrate = (int)Math.Ceiling(new FileInfo(tilePath).Length * 8m / trickplayInfo.TileWidth / trickplayInfo.TileHeight / (trickplayInfo.Interval / 1000m));
            trickplayInfo.Bandwidth = Math.Max(trickplayInfo.Bandwidth, bitrate);
        }

        /*
         * Move trickplay tiles to output directory
         */
        Directory.CreateDirectory(Directory.GetParent(outputDir)!.FullName);

        // Replace existing tiles if they already exist
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, true);
        }

        _fileSystem.MoveDirectory(workDir, outputDir);

        return trickplayInfo;
    }

    private bool CanGenerateTrickplay(Video video, int interval)
    {
        var videoType = video.VideoType;
        if (videoType == VideoType.Iso || videoType == VideoType.Dvd || videoType == VideoType.BluRay)
        {
            return false;
        }

        if (video.IsPlaceHolder)
        {
            return false;
        }

        if (video.IsShortcut)
        {
            return false;
        }

        if (!video.IsCompleteMedia)
        {
            return false;
        }

        if (!video.RunTimeTicks.HasValue || video.RunTimeTicks.Value < TimeSpan.FromMilliseconds(interval).Ticks)
        {
            return false;
        }

        // Can't extract images if there are no video streams
        return video.GetMediaStreams().Count > 0;
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, TrickplayInfo>> GetTrickplayResolutions(Guid itemId)
    {
        var trickplayResolutions = new Dictionary<int, TrickplayInfo>();

        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var trickplayInfos = await dbContext.TrickplayInfos
                .AsNoTracking()
                .Where(i => i.ItemId.Equals(itemId))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var info in trickplayInfos)
            {
                trickplayResolutions[info.Width] = info;
            }
        }

        return trickplayResolutions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrickplayInfo>> GetTrickplayItemsAsync(int limit, int offset)
    {
        IReadOnlyList<TrickplayInfo> trickplayItems;

        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            trickplayItems = await dbContext.TrickplayInfos
                .AsNoTracking()
                .OrderBy(i => i.ItemId)
                .Skip(offset)
                .Take(limit)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        return trickplayItems;
    }

    /// <inheritdoc />
    public async Task SaveTrickplayInfo(TrickplayInfo info)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var oldInfo = await dbContext.TrickplayInfos.FindAsync(info.ItemId, info.Width).ConfigureAwait(false);
            if (oldInfo is not null)
            {
                dbContext.TrickplayInfos.Remove(oldInfo);
            }

            dbContext.Add(info);

            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteTrickplayDataAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.TrickplayInfos.Where(i => i.ItemId.Equals(itemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, Dictionary<int, TrickplayInfo>>> GetTrickplayManifest(BaseItem item)
    {
        var trickplayManifest = new Dictionary<string, Dictionary<int, TrickplayInfo>>();
        foreach (var mediaSource in item.GetMediaSources(false))
        {
            if (mediaSource.IsRemote || !Guid.TryParse(mediaSource.Id, out var mediaSourceId))
            {
                continue;
            }

            var trickplayResolutions = await GetTrickplayResolutions(mediaSourceId).ConfigureAwait(false);

            if (trickplayResolutions.Count > 0)
            {
                trickplayManifest[mediaSource.Id] = trickplayResolutions;
            }
        }

        return trickplayManifest;
    }

    /// <inheritdoc />
    public async Task<string> GetTrickplayTilePathAsync(BaseItem item, int width, int index, bool saveWithMedia)
    {
        var trickplayResolutions = await GetTrickplayResolutions(item.Id).ConfigureAwait(false);
        if (trickplayResolutions is not null && trickplayResolutions.TryGetValue(width, out var trickplayInfo))
        {
            return Path.Combine(GetTrickplayDirectory(item, trickplayInfo.TileWidth, trickplayInfo.TileHeight, width, saveWithMedia), index + ".jpg");
        }

        return string.Empty;
    }

    /// <inheritdoc />
    public async Task<string?> GetHlsPlaylist(Guid itemId, int width, string? apiKey)
    {
        var trickplayResolutions = await GetTrickplayResolutions(itemId).ConfigureAwait(false);
        if (trickplayResolutions is not null && trickplayResolutions.TryGetValue(width, out var trickplayInfo))
        {
            var builder = new StringBuilder(128);

            if (trickplayInfo.ThumbnailCount > 0)
            {
                const string urlFormat = "{0}.jpg?MediaSourceId={1}&ApiKey={2}";
                const string decimalFormat = "{0:0.###}";

                var resolution = $"{trickplayInfo.Width}x{trickplayInfo.Height}";
                var layout = $"{trickplayInfo.TileWidth}x{trickplayInfo.TileHeight}";
                var thumbnailsPerTile = trickplayInfo.TileWidth * trickplayInfo.TileHeight;
                var thumbnailDuration = trickplayInfo.Interval / 1000d;
                var infDuration = thumbnailDuration * thumbnailsPerTile;
                var tileCount = (int)Math.Ceiling((decimal)trickplayInfo.ThumbnailCount / thumbnailsPerTile);

                builder
                    .AppendLine("#EXTM3U")
                    .Append("#EXT-X-TARGETDURATION:")
                    .AppendLine(tileCount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("#EXT-X-VERSION:7")
                    .AppendLine("#EXT-X-MEDIA-SEQUENCE:1")
                    .AppendLine("#EXT-X-PLAYLIST-TYPE:VOD")
                    .AppendLine("#EXT-X-IMAGES-ONLY");

                for (int i = 0; i < tileCount; i++)
                {
                    // All tiles prior to the last must contain full amount of thumbnails (no black).
                    if (i == tileCount - 1)
                    {
                        thumbnailsPerTile = trickplayInfo.ThumbnailCount - (i * thumbnailsPerTile);
                        infDuration = thumbnailDuration * thumbnailsPerTile;
                    }

                    // EXTINF
                    builder
                        .Append("#EXTINF:")
                        .AppendFormat(CultureInfo.InvariantCulture, decimalFormat, infDuration)
                        .AppendLine(",");

                    // EXT-X-TILES
                    builder
                        .Append("#EXT-X-TILES:RESOLUTION=")
                        .Append(resolution)
                        .Append(",LAYOUT=")
                        .Append(layout)
                        .Append(",DURATION=")
                        .AppendFormat(CultureInfo.InvariantCulture, decimalFormat, thumbnailDuration)
                        .AppendLine();

                    // URL
                    builder
                        .AppendFormat(
                            CultureInfo.InvariantCulture,
                            urlFormat,
                            i.ToString(CultureInfo.InvariantCulture),
                            itemId.ToString("N"),
                            apiKey)
                        .AppendLine();
                }

                builder.AppendLine("#EXT-X-ENDLIST");
                return builder.ToString();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public string GetTrickplayDirectory(BaseItem item, int tileWidth, int tileHeight, int width, bool saveWithMedia = false)
    {
        var path = _pathManager.GetTrickplayDirectory(item, saveWithMedia);
        var subdirectory = string.Format(
            CultureInfo.InvariantCulture,
            "{0} - {1}x{2}",
            width.ToString(CultureInfo.InvariantCulture),
            tileWidth.ToString(CultureInfo.InvariantCulture),
            tileHeight.ToString(CultureInfo.InvariantCulture));

        return Path.Combine(path, subdirectory);
    }

    private async Task<bool> HasTrickplayResolutionAsync(Guid itemId, int width)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.TrickplayInfos
                .AsNoTracking()
                .Where(i => i.ItemId.Equals(itemId))
                .AnyAsync(i => i.Width == width)
                .ConfigureAwait(false);
        }
    }

    private async Task GenerateIFramePlaylistAsync(
        Video video,
        TrickplayOptions options,
        bool saveWithMedia,
        bool replace,
        CancellationToken cancellationToken)
    {
        // Per Apple HLS spec 6.19, we create a single playlist with 160px height thumbnails
        // for scrubbing compatibility with all Apple platforms
        const int TargetHeight = 160;

        var mediaSource = video.GetMediaSources(false).FirstOrDefault(source => Guid.Parse(source.Id).Equals(video.Id));
        if (mediaSource is null)
        {
            _logger.LogDebug("Found no matching media source for I-frame playlist generation for item {ItemId}", video.Id);
            return;
        }

        // Ensure mediaSource has Video3DFormat from the video for spatial video filtering
        // MVC is decoded as single view and doesn't need special handling
        if (video.Video3DFormat.HasValue && video.Video3DFormat.Value != Video3DFormat.MVC)
        {
            mediaSource.Video3DFormat = video.Video3DFormat;
        }
        else
        {
            mediaSource.Video3DFormat = null;
        }

        var mediaPath = mediaSource.Path;
        if (!File.Exists(mediaPath))
        {
            _logger.LogWarning("Media not found at {Path} for I-frame playlist generation {ItemID}", mediaPath, video.Id);
            return;
        }

        var outputDir = GetIFrameDirectoryInternal(video, saveWithMedia);

        // Check if I-frame playlist already exists
        if (!replace && Directory.Exists(outputDir))
        {
            var playlistPath = Path.Combine(outputDir, "iframe.m3u8");
            if (File.Exists(playlistPath))
            {
                var existingInfo = await GetIFramePlaylistInfoAsync(video.Id).ConfigureAwait(false);
                if (existingInfo is not null)
                {
                    _logger.LogDebug("I-frame playlist already exists for {ItemId}", video.Id);
                    return;
                }
            }
        }

        string? tempDir = null;
        try
        {
            _logger.LogInformation("Creating I-frame playlist for {Path} [ID: {ItemId}]", mediaPath, video.Id);

            tempDir = await _mediaEncoder.GenerateIFrameHlsPlaylistAsync(
                mediaPath,
                mediaSource.Container,
                mediaSource,
                mediaSource.VideoStream,
                TargetHeight,
                options.EnableHwAcceleration,
                options.EnableHwEncoding,
                options.ProcessThreads,
                options.ProcessPriority,
                _encodingHelper,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(tempDir) || !Directory.Exists(tempDir))
            {
                throw new InvalidOperationException("Null or invalid directory from media encoder for I-frame playlist.");
            }

            // Move to output directory
            Directory.CreateDirectory(Directory.GetParent(outputDir)!.FullName);
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }

            _fileSystem.MoveDirectory(tempDir, outputDir);
            tempDir = null; // Prevent cleanup since we moved it

            // Calculate and save metadata
            var segmentFiles = Directory.GetFiles(outputDir, "*.m4s");
            var segmentCount = segmentFiles.Length;

            // Calculate peak bandwidth (bits per second) - HLS spec requires peak, not average
            // Each segment is ~1 second, so peak bandwidth = largest segment size * 8 bits
            var maxSegmentSize = segmentFiles.Length > 0 ? segmentFiles.Max(f => new FileInfo(f).Length) : 0;
            var bandwidth = (int)Math.Ceiling((double)maxSegmentSize * 8);

            // Calculate width from aspect ratio (height is fixed at 160)
            // Use effective dimensions after spatial video transformation (e.g., cropping SBS, converting 360°)
            var sourceWidth = mediaSource.VideoStream.Width ?? 0;
            var sourceHeight = mediaSource.VideoStream.Height ?? 0;
            var (effectiveWidth, effectiveHeight) = EncodingHelper.GetSpatialVideoSourceDimensions(
                sourceWidth, sourceHeight, video.Video3DFormat);

            var actualWidth = effectiveHeight > 0
                ? (int)Math.Ceiling((double)TargetHeight * effectiveWidth / effectiveHeight)
                : 0;
            // Ensure width is even (required for H.264)
            actualWidth = 2 * (actualWidth / 2);

            var iframeInfo = new IFramePlaylistInfo
            {
                ItemId = video.Id,
                Width = actualWidth,
                Height = TargetHeight,
                SegmentCount = segmentCount,
                Bandwidth = bandwidth
            };

            await SaveIFramePlaylistInfo(iframeInfo).ConfigureAwait(false);
            _logger.LogInformation("Finished I-frame playlist for {Path}", mediaPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating I-frame playlist for {Path}", mediaPath);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup I-frame temp directory {Dir}", tempDir);
                }
            }
        }
    }

    private string GetIFrameDirectoryInternal(Video video, bool saveWithMedia)
    {
        var trickplayPath = _pathManager.GetTrickplayDirectory(video, saveWithMedia);
        return Path.Combine(trickplayPath, "iframe");
    }

    /// <inheritdoc />
    public async Task<IFramePlaylistInfo?> GetIFramePlaylistInfoAsync(Guid itemId)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.IFramePlaylistInfos
                .AsNoTracking()
                .Where(i => i.ItemId.Equals(itemId))
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetIFrameHlsPlaylist(BaseItem item, bool saveWithMedia, Guid mediaSourceId, string? apiKey)
    {
        if (item is not Video video)
        {
            return null;
        }

        var playlistPath = Path.Combine(GetIFrameDirectoryInternal(video, saveWithMedia), "iframe.m3u8");
        if (!File.Exists(playlistPath))
        {
            return null;
        }

        // Read the static playlist and add auth tokens to segment URLs
        var content = await File.ReadAllTextAsync(playlistPath).ConfigureAwait(false);
        var builder = new StringBuilder();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmedLine = line.TrimEnd('\r');

            // Check if this is a segment or init file reference (not a comment/directive)
            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith('#'))
            {
                // This is a segment filename - add auth params
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0}?MediaSourceId={1}&ApiKey={2}",
                    trimmedLine,
                    mediaSourceId.ToString("N"),
                    apiKey);
                builder.AppendLine();
            }
            else if (trimmedLine.StartsWith("#EXT-X-MAP:URI=\"", StringComparison.Ordinal))
            {
                // Handle #EXT-X-MAP:URI="init.mp4" - add auth params
                var uriStart = trimmedLine.IndexOf("URI=\"", StringComparison.Ordinal) + 5;
                var uriEnd = trimmedLine.IndexOf('"', uriStart);
                if (uriEnd > uriStart)
                {
                    var initFile = trimmedLine.Substring(uriStart, uriEnd - uriStart);
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "#EXT-X-MAP:URI=\"{0}?MediaSourceId={1}&ApiKey={2}\"",
                        initFile,
                        mediaSourceId.ToString("N"),
                        apiKey);
                    builder.AppendLine();
                }
                else
                {
                    builder.AppendLine(trimmedLine);
                }
            }
            else
            {
                builder.AppendLine(trimmedLine);
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public string? GetIFramePlaylistPath(BaseItem item, bool saveWithMedia)
    {
        if (item is not Video video)
        {
            return null;
        }

        var path = Path.Combine(GetIFrameDirectoryInternal(video, saveWithMedia), "iframe.m3u8");
        if (File.Exists(path))
        {
            return path;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetIFrameDirectory(BaseItem item, bool saveWithMedia)
    {
        if (item is not Video video)
        {
            return null;
        }

        var path = GetIFrameDirectoryInternal(video, saveWithMedia);
        if (Directory.Exists(path))
        {
            return path;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task SaveIFramePlaylistInfo(IFramePlaylistInfo info)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var oldInfo = await dbContext.IFramePlaylistInfos.FindAsync(info.ItemId).ConfigureAwait(false);
            if (oldInfo is not null)
            {
                dbContext.IFramePlaylistInfos.Remove(oldInfo);
            }

            dbContext.Add(info);

            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteIFramePlaylistDataAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.IFramePlaylistInfos.Where(i => i.ItemId.Equals(itemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
