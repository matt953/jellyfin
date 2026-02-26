using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// Manages OCR model downloads and caching.
/// </summary>
public sealed class OcrModelManager : IDisposable
{
    private const string ModelsSubdirectory = "ocr-models";
    private const string DetectionModelFilename = "det.onnx";
    private const string HuggingFaceBaseUrl = "https://huggingface.co/monkt/paddleocr-onnx/resolve/main";

    private bool _disposed;

    private static readonly FrozenDictionary<OcrModelType, string> _modelFolderNames = new Dictionary<OcrModelType, string>
    {
        [OcrModelType.Latin] = "latin",
        [OcrModelType.Chinese] = "chinese",
        [OcrModelType.Korean] = "korean",
        [OcrModelType.Cyrillic] = "cyrillic",
        [OcrModelType.Arabic] = "arabic",
        [OcrModelType.Devanagari] = "devanagari",
        [OcrModelType.Thai] = "thai",
        [OcrModelType.Tamil] = "tamil",
        [OcrModelType.Telugu] = "telugu",
    }.ToFrozenDictionary();

    private readonly string _modelCacheDir;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OcrModelManager> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="OcrModelManager"/> class.
    /// </summary>
    /// <param name="appPaths">Application paths.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public OcrModelManager(
        IApplicationPaths appPaths,
        IHttpClientFactory httpClientFactory,
        ILogger<OcrModelManager> logger)
    {
        _modelCacheDir = Path.Combine(appPaths.DataPath, ModelsSubdirectory);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Checks if models for a specific type are available locally.
    /// </summary>
    /// <param name="modelType">The model type to check.</param>
    /// <returns>True if all required model files exist.</returns>
    public bool AreModelsAvailable(OcrModelType modelType)
    {
        var paths = GetModelPaths(modelType);
        return File.Exists(paths.DetectionModelPath)
            && File.Exists(paths.RecognitionModelPath)
            && File.Exists(paths.DictionaryPath);
    }

    /// <summary>
    /// Checks if models for a language code are available locally.
    /// </summary>
    /// <param name="languageCode">ISO language code.</param>
    /// <returns>True if the language is supported and models are available.</returns>
    public bool AreModelsAvailable(string? languageCode)
    {
        var modelType = LanguageMapping.GetModelType(languageCode);
        return modelType.HasValue && AreModelsAvailable(modelType.Value);
    }

    /// <summary>
    /// Gets the file paths for a specific model type.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <returns>Paths to the model files.</returns>
    public OcrModelPaths GetModelPaths(OcrModelType modelType)
    {
        var folderName = _modelFolderNames[modelType];
        var modelDir = Path.Combine(_modelCacheDir, folderName);

        return new OcrModelPaths(
            DetectionModelPath: Path.Combine(_modelCacheDir, DetectionModelFilename),
            RecognitionModelPath: Path.Combine(modelDir, "rec.onnx"),
            DictionaryPath: Path.Combine(modelDir, "dict.txt"));
    }

    /// <summary>
    /// Ensures models for a specific type are downloaded.
    /// </summary>
    /// <param name="modelType">The model type to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    public async Task EnsureModelsDownloadedAsync(OcrModelType modelType, CancellationToken cancellationToken)
    {
        if (AreModelsAvailable(modelType))
        {
            _logger.LogDebug("OCR models for {ModelType} already available", modelType);
            return;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (AreModelsAvailable(modelType))
            {
                return;
            }

            _logger.LogInformation("Downloading OCR models for {ModelType}", modelType);

            var paths = GetModelPaths(modelType);
            var folderName = _modelFolderNames[modelType];

            // Ensure directories exist
            Directory.CreateDirectory(_modelCacheDir);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.RecognitionModelPath)!);

            // Download detection model (shared across all languages)
            if (!File.Exists(paths.DetectionModelPath))
            {
                await DownloadFileAsync(
                    $"{HuggingFaceBaseUrl}/detection/v5/det.onnx",
                    paths.DetectionModelPath,
                    cancellationToken).ConfigureAwait(false);
            }

            // Download recognition model
            await DownloadFileAsync(
                $"{HuggingFaceBaseUrl}/languages/{folderName}/rec.onnx",
                paths.RecognitionModelPath,
                cancellationToken).ConfigureAwait(false);

            // Download dictionary
            await DownloadFileAsync(
                $"{HuggingFaceBaseUrl}/languages/{folderName}/dict.txt",
                paths.DictionaryPath,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully downloaded OCR models for {ModelType}", modelType);
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    /// Downloads all common model types (Latin and Chinese).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    public async Task EnsureCommonModelsDownloadedAsync(CancellationToken cancellationToken)
    {
        await EnsureModelsDownloadedAsync(OcrModelType.Latin, cancellationToken).ConfigureAwait(false);
        await EnsureModelsDownloadedAsync(OcrModelType.Chinese, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads all available model types.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    public async Task EnsureAllModelsDownloadedAsync(CancellationToken cancellationToken)
    {
        foreach (var modelType in Enum.GetValues<OcrModelType>())
        {
            await EnsureModelsDownloadedAsync(modelType, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + ".tmp";

        try
        {
            _logger.LogDebug("Downloading {Url} to {Path}", url, destinationPath);

            using var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (contentStream.ConfigureAwait(false))
            {
                var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                await using (fileStream.ConfigureAwait(false))
                {
                    await contentStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }
            }

            // Atomic move from temp to final destination
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _downloadLock.Dispose();
    }
}
