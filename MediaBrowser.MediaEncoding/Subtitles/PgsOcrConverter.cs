using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.MediaEncoding.Ocr;
using MediaBrowser.MediaEncoding.Subtitles.Pgs;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Subtitles;

/// <summary>
/// Converts PGS subtitles to text via OCR.
/// </summary>
public class PgsOcrConverter
{
    private readonly PgsParser _pgsParser;
    private readonly OcrEngine _ocrEngine;
    private readonly OcrModelManager _modelManager;
    private readonly ILogger<PgsOcrConverter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgsOcrConverter"/> class.
    /// </summary>
    /// <param name="pgsParser">The PGS parser.</param>
    /// <param name="ocrEngine">The OCR engine.</param>
    /// <param name="modelManager">The OCR model manager.</param>
    /// <param name="logger">Logger instance.</param>
    public PgsOcrConverter(
        PgsParser pgsParser,
        OcrEngine ocrEngine,
        OcrModelManager modelManager,
        ILogger<PgsOcrConverter> logger)
    {
        _pgsParser = pgsParser;
        _ocrEngine = ocrEngine;
        _modelManager = modelManager;
        _logger = logger;
    }

    /// <summary>
    /// Checks if OCR is available for a given language.
    /// </summary>
    /// <param name="languageCode">ISO language code.</param>
    /// <returns>True if OCR can be performed for this language.</returns>
    public bool IsLanguageSupported(string? languageCode)
    {
        return _modelManager.AreModelsAvailable(languageCode);
    }

    /// <summary>
    /// Convert a time range of PGS subtitles to text subtitle events.
    /// This is the main method for on-the-fly HLS segment conversion.
    /// </summary>
    /// <param name="pgsStream">The PGS data stream.</param>
    /// <param name="languageCode">ISO language code for OCR model selection.</param>
    /// <param name="start">Start time of the segment.</param>
    /// <param name="end">End time of the segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subtitle track info with OCR'd text events.</returns>
    public async Task<SubtitleTrackInfo> ConvertTimeRangeAsync(
        Stream pgsStream,
        string? languageCode,
        TimeSpan start,
        TimeSpan end,
        CancellationToken cancellationToken)
    {
        var modelType = LanguageMapping.GetModelType(languageCode);
        if (!modelType.HasValue || !_modelManager.AreModelsAvailable(modelType.Value))
        {
            _logger.LogWarning(
                "OCR models not available for language {Language} (model type: {ModelType})",
                languageCode,
                modelType);
            return new SubtitleTrackInfo();
        }

        // Parse only frames in the time range
        var frames = _pgsParser.ParseTimeRange(pgsStream, start, end).ToList();

        if (frames.Count == 0)
        {
            _logger.LogDebug("No PGS frames found in time range {Start} to {End}", start, end);
            return new SubtitleTrackInfo();
        }

        _logger.LogDebug("Processing {Count} PGS frames for OCR in range {Start} to {End}", frames.Count, start, end);

        // OCR all frames in parallel
        var images = frames
            .Select(f => (f.RgbaPixels, f.Width, f.Height))
            .ToList();

        var ocrResults = await _ocrEngine.RecognizeBatchAsync(images, modelType.Value, cancellationToken)
            .ConfigureAwait(false);

        // Build subtitle track events
        var events = new List<SubtitleTrackEvent>();

        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var result = ocrResults[i];

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                continue;
            }

            events.Add(new SubtitleTrackEvent(
                id: i.ToString(CultureInfo.InvariantCulture),
                text: result.Text)
            {
                StartPositionTicks = frame.StartTime.Ticks,
                EndPositionTicks = frame.EndTime.Ticks
            });
        }

        _logger.LogDebug("OCR produced {Count} subtitle events from {FrameCount} frames", events.Count, frames.Count);

        return new SubtitleTrackInfo { TrackEvents = events };
    }

    /// <summary>
    /// Convert entire PGS file to text subtitle events.
    /// </summary>
    /// <param name="pgsStream">The PGS data stream.</param>
    /// <param name="languageCode">ISO language code for OCR model selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subtitle track info with all OCR'd text events.</returns>
    public async Task<SubtitleTrackInfo> ConvertFullAsync(
        Stream pgsStream,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        var modelType = LanguageMapping.GetModelType(languageCode);
        if (!modelType.HasValue || !_modelManager.AreModelsAvailable(modelType.Value))
        {
            _logger.LogWarning(
                "OCR models not available for language {Language}",
                languageCode);
            return new SubtitleTrackInfo();
        }

        var frames = _pgsParser.Parse(pgsStream).ToList();

        if (frames.Count == 0)
        {
            return new SubtitleTrackInfo();
        }

        _logger.LogInformation("Processing {Count} PGS frames for full OCR conversion", frames.Count);

        // Process in batches to avoid memory issues
        const int batchSize = 50;
        var events = new List<SubtitleTrackEvent>();
        int eventId = 0;

        for (int batchStart = 0; batchStart < frames.Count; batchStart += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = frames.Skip(batchStart).Take(batchSize).ToList();
            var images = batch.Select(f => (f.RgbaPixels, f.Width, f.Height)).ToList();

            var ocrResults = await _ocrEngine.RecognizeBatchAsync(images, modelType.Value, cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < batch.Count; i++)
            {
                var frame = batch[i];
                var result = ocrResults[i];

                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    continue;
                }

                events.Add(new SubtitleTrackEvent(
                    id: (eventId++).ToString(CultureInfo.InvariantCulture),
                    text: result.Text)
                {
                    StartPositionTicks = frame.StartTime.Ticks,
                    EndPositionTicks = frame.EndTime.Ticks
                });
            }

            _logger.LogDebug("Processed batch {BatchNum}/{TotalBatches}", (batchStart / batchSize) + 1, (frames.Count + batchSize - 1) / batchSize);
        }

        _logger.LogInformation("OCR conversion complete: {EventCount} events from {FrameCount} frames", events.Count, frames.Count);

        return new SubtitleTrackInfo { TrackEvents = events };
    }
}
