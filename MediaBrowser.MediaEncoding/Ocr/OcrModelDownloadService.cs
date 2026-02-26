using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// Background service that downloads OCR models on startup.
/// </summary>
public class OcrModelDownloadService : IHostedService
{
    private readonly OcrModelManager _modelManager;
    private readonly ILogger<OcrModelDownloadService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OcrModelDownloadService"/> class.
    /// </summary>
    /// <param name="modelManager">The OCR model manager.</param>
    /// <param name="logger">The logger.</param>
    public OcrModelDownloadService(OcrModelManager modelManager, ILogger<OcrModelDownloadService> logger)
    {
        _modelManager = modelManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start download in background (fire and forget)
        _ = Task.Run(
            async () =>
            {
                try
                {
                    _logger.LogInformation("Starting OCR model download in background");
                    await _modelManager.EnsureCommonModelsDownloadedAsync(CancellationToken.None).ConfigureAwait(false);
                    _logger.LogInformation("OCR models downloaded successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download OCR models - PGS OCR will be unavailable until models are downloaded");
                }
            },
            CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
