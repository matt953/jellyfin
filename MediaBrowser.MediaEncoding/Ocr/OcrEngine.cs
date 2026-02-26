using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// OCR engine using PaddleOCR models via ONNX Runtime.
/// Thread-safe singleton that keeps ONNX sessions loaded.
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private const int RecognitionHeight = 48;
    private const int MaxRecognitionWidth = 1920;

    private readonly OcrModelManager _modelManager;
    private readonly ILogger<OcrEngine> _logger;
    private readonly ConcurrentDictionary<OcrModelType, OcrModelSession> _sessions = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OcrEngine"/> class.
    /// </summary>
    /// <param name="modelManager">The OCR model manager.</param>
    /// <param name="logger">Logger instance.</param>
    public OcrEngine(OcrModelManager modelManager, ILogger<OcrEngine> logger)
    {
        _modelManager = modelManager;
        _logger = logger;
    }

    /// <summary>
    /// Recognize text from an RGBA image.
    /// </summary>
    /// <param name="rgbaPixels">RGBA pixel data (4 bytes per pixel).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="modelType">The OCR model type to use.</param>
    /// <returns>OCR result with extracted text.</returns>
    public async Task<OcrResult> RecognizeAsync(byte[] rgbaPixels, int width, int height, OcrModelType modelType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = await GetOrLoadSessionAsync(modelType).ConfigureAwait(false);
        if (session is null)
        {
            return new OcrResult(string.Empty, 0, 0);
        }

        return RecognizeInternal(rgbaPixels, width, height, session);
    }

    /// <summary>
    /// Recognize text from multiple images in parallel.
    /// </summary>
    /// <param name="images">Collection of images with their dimensions.</param>
    /// <param name="modelType">The OCR model type to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of OCR results.</returns>
    public async Task<OcrResult[]> RecognizeBatchAsync(
        IReadOnlyList<(byte[] Pixels, int Width, int Height)> images,
        OcrModelType modelType,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = await GetOrLoadSessionAsync(modelType).ConfigureAwait(false);
        if (session is null)
        {
            return images.Select(_ => new OcrResult(string.Empty, 0, 0)).ToArray();
        }

        // Process all images in parallel
        var tasks = images.Select(img =>
            Task.Run(() => RecognizeInternal(img.Pixels, img.Width, img.Height, session), cancellationToken));

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private OcrResult RecognizeInternal(byte[] rgbaPixels, int width, int height, OcrModelSession session)
    {
        try
        {
            // Composite RGBA onto white background for OCR
            var rgbPixels = CompositeOnWhite(rgbaPixels, width, height);

            // Find text lines by scanning rows
            var lines = FindTextLines(rgbPixels, width, height);

            if (lines.Count == 0)
            {
                return new OcrResult(string.Empty, 0, 0);
            }

            var results = new List<(string Text, float Confidence)>();

            foreach (var line in lines)
            {
                var linePixels = CropRegion(rgbPixels, width, height, line);
                var (text, confidence) = RecognizeLine(linePixels, line.Width, line.Height, session);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add((text.Trim(), confidence));
                }
            }

            if (results.Count == 0)
            {
                return new OcrResult(string.Empty, 0, 0);
            }

            var combinedText = string.Join("\n", results.Select(r => r.Text));
            var avgConfidence = results.Average(r => r.Confidence);

            return new OcrResult(combinedText, avgConfidence, results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR recognition failed");
            return new OcrResult(string.Empty, 0, 0);
        }
    }

    private async Task<OcrModelSession?> GetOrLoadSessionAsync(OcrModelType modelType)
    {
        if (_sessions.TryGetValue(modelType, out var existing))
        {
            return existing;
        }

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_sessions.TryGetValue(modelType, out existing))
            {
                return existing;
            }

            if (!_modelManager.AreModelsAvailable(modelType))
            {
                _logger.LogWarning("OCR models not available for {ModelType}", modelType);
                return null;
            }

            var paths = _modelManager.GetModelPaths(modelType);
            var session = LoadSession(paths, modelType);

            _sessions[modelType] = session;
            return session;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private OcrModelSession LoadSession(OcrModelPaths paths, OcrModelType modelType)
    {
        _logger.LogInformation("Loading OCR models for {ModelType}", modelType);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 4,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        var recSession = new InferenceSession(paths.RecognitionModelPath, sessionOptions);

        // Load dictionary
        var dictContent = File.ReadAllText(paths.DictionaryPath);
        var dictionary = dictContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart('\uFEFF').Trim()) // Remove BOM
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();

        _logger.LogDebug("Loaded dictionary with {Count} characters for {ModelType}", dictionary.Length, modelType);

        sw.Stop();
        _logger.LogInformation("Loaded OCR models for {ModelType} in {ElapsedMs}ms", modelType, sw.ElapsedMilliseconds);

        return new OcrModelSession(recSession, dictionary);
    }

    private static byte[] CompositeOnWhite(byte[] rgbaPixels, int width, int height)
    {
        var rgb = new byte[width * height * 3];

        for (int i = 0; i < width * height; i++)
        {
            int rgbaIdx = i * 4;
            int rgbIdx = i * 3;

            byte r = rgbaPixels[rgbaIdx];
            byte g = rgbaPixels[rgbaIdx + 1];
            byte b = rgbaPixels[rgbaIdx + 2];
            byte a = rgbaPixels[rgbaIdx + 3];

            // Alpha composite onto white background
            float alpha = a / 255f;
            rgb[rgbIdx] = (byte)((r * alpha) + (255 * (1 - alpha)));
            rgb[rgbIdx + 1] = (byte)((g * alpha) + (255 * (1 - alpha)));
            rgb[rgbIdx + 2] = (byte)((b * alpha) + (255 * (1 - alpha)));
        }

        return rgb;
    }

    private static List<TextRegion> FindTextLines(byte[] rgbPixels, int width, int height)
    {
        var regions = new List<TextRegion>();

        // Convert to grayscale and find rows with dark pixels
        var rowHasText = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = ((y * width) + x) * 3;
                byte gray = (byte)((rgbPixels[idx] + rgbPixels[idx + 1] + rgbPixels[idx + 2]) / 3);

                if (gray < 200) // Dark pixel = text
                {
                    rowHasText[y] = true;
                    break;
                }
            }
        }

        // Find contiguous text regions
        bool inText = false;
        int startY = 0;

        for (int y = 0; y < height; y++)
        {
            if (rowHasText[y])
            {
                if (!inText)
                {
                    startY = y;
                    inText = true;
                }
            }
            else if (inText)
            {
                // End of text line - add padding
                const int padding = 5;
                int paddedY = Math.Max(0, startY - padding);
                int paddedH = Math.Min(height - paddedY, y - startY + (2 * padding));

                // Trim horizontal whitespace
                var (trimmedX, trimmedWidth) = TrimHorizontalWhitespace(rgbPixels, width, paddedY, paddedH);

                if (trimmedWidth > 5)
                {
                    regions.Add(new TextRegion(trimmedX, paddedY, trimmedWidth, paddedH));
                }

                inText = false;
            }
        }

        // Handle text at bottom edge
        if (inText)
        {
            const int padding = 5;
            int paddedY = Math.Max(0, startY - padding);
            int paddedH = Math.Min(height - paddedY, height - startY + padding);

            var (trimmedX, trimmedWidth) = TrimHorizontalWhitespace(rgbPixels, width, paddedY, paddedH);

            if (trimmedWidth > 5)
            {
                regions.Add(new TextRegion(trimmedX, paddedY, trimmedWidth, paddedH));
            }
        }

        return regions;
    }

    private static (int X, int Width) TrimHorizontalWhitespace(byte[] rgbPixels, int imageWidth, int regionY, int regionHeight)
    {
        int left = imageWidth;
        int right = 0;

        for (int y = regionY; y < regionY + regionHeight; y++)
        {
            for (int x = 0; x < imageWidth; x++)
            {
                int idx = ((y * imageWidth) + x) * 3;
                byte gray = (byte)((rgbPixels[idx] + rgbPixels[idx + 1] + rgbPixels[idx + 2]) / 3);

                if (gray < 200)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }

        if (left > right)
        {
            return (0, imageWidth);
        }

        const int padding = 5;
        int trimmedX = Math.Max(0, left - padding);
        int trimmedRight = Math.Min(imageWidth, right + padding);

        return (trimmedX, trimmedRight - trimmedX);
    }

    private static byte[] CropRegion(byte[] rgbPixels, int imageWidth, int imageHeight, TextRegion region)
    {
        var cropped = new byte[region.Width * region.Height * 3];

        for (int y = 0; y < region.Height; y++)
        {
            int srcY = region.Y + y;
            if (srcY >= imageHeight)
            {
                break;
            }

            for (int x = 0; x < region.Width; x++)
            {
                int srcX = region.X + x;
                if (srcX >= imageWidth)
                {
                    break;
                }

                int srcIdx = ((srcY * imageWidth) + srcX) * 3;
                int dstIdx = ((y * region.Width) + x) * 3;

                cropped[dstIdx] = rgbPixels[srcIdx];
                cropped[dstIdx + 1] = rgbPixels[srcIdx + 1];
                cropped[dstIdx + 2] = rgbPixels[srcIdx + 2];
            }
        }

        return cropped;
    }

    private (string Text, float Confidence) RecognizeLine(byte[] rgbPixels, int width, int height, OcrModelSession session)
    {
        // Resize to recognition height while maintaining aspect ratio
        float aspect = (float)width / height;
        int targetW = Math.Clamp((int)(aspect * RecognitionHeight), 1, MaxRecognitionWidth);

        var resized = ResizeImage(rgbPixels, width, height, targetW, RecognitionHeight);

        // Create input tensor [1, 3, H, W] normalized to [-1, 1]
        var inputTensor = new DenseTensor<float>(new[] { 1, 3, RecognitionHeight, targetW });

        for (int y = 0; y < RecognitionHeight; y++)
        {
            for (int x = 0; x < targetW; x++)
            {
                int idx = ((y * targetW) + x) * 3;

                // Normalize: (x/255 - 0.5) / 0.5 = x/127.5 - 1
                inputTensor[0, 0, y, x] = (resized[idx] / 127.5f) - 1f;     // R
                inputTensor[0, 1, y, x] = (resized[idx + 1] / 127.5f) - 1f; // G
                inputTensor[0, 2, y, x] = (resized[idx + 2] / 127.5f) - 1f; // B
            }
        }

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("x", inputTensor)
        };

        using var results = session.RecSession.Run(inputs);
        var resultsList = results.ToList();
        var output = resultsList[0].AsTensor<float>();

        // CTC decode
        return CtcDecode(output, session.Dictionary);
    }

    private static byte[] ResizeImage(byte[] rgbPixels, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        var resized = new byte[dstWidth * dstHeight * 3];

        float xRatio = (float)srcWidth / dstWidth;
        float yRatio = (float)srcHeight / dstHeight;

        for (int y = 0; y < dstHeight; y++)
        {
            for (int x = 0; x < dstWidth; x++)
            {
                int srcX = Math.Min((int)(x * xRatio), srcWidth - 1);
                int srcY = Math.Min((int)(y * yRatio), srcHeight - 1);

                int srcIdx = ((srcY * srcWidth) + srcX) * 3;
                int dstIdx = ((y * dstWidth) + x) * 3;

                resized[dstIdx] = rgbPixels[srcIdx];
                resized[dstIdx + 1] = rgbPixels[srcIdx + 1];
                resized[dstIdx + 2] = rgbPixels[srcIdx + 2];
            }
        }

        return resized;
    }

    private static (string Text, float Confidence) CtcDecode(Tensor<float> logits, string[] dictionary)
    {
        var dimensions = logits.Dimensions;
        int timeSteps = dimensions[1];
        int numClasses = dimensions[2];

        const int blankIdx = 0;
        var chars = new List<char>();
        float totalConfidence = 0;
        int numChars = 0;
        int prevIdx = blankIdx;

        for (int t = 0; t < timeSteps; t++)
        {
            // Find argmax
            int maxIdx = 0;
            float maxVal = float.NegativeInfinity;

            for (int c = 0; c < numClasses; c++)
            {
                float val = logits[0, t, c];
                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = c;
                }
            }

            // CTC: skip blanks and repeated characters
            if (maxIdx != blankIdx && maxIdx != prevIdx)
            {
                int dictIdx = maxIdx - 1; // Offset by 1 since blank is at 0

                if (dictIdx >= 0 && dictIdx < dictionary.Length)
                {
                    var charStr = dictionary[dictIdx];
                    foreach (var ch in charStr)
                    {
                        chars.Add(ch);
                    }
                }
                else if (maxIdx == dictionary.Length + 1)
                {
                    // Space token
                    chars.Add(' ');
                }

                // Calculate confidence via softmax
                float expSum = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    expSum += MathF.Exp(logits[0, t, c] - maxVal);
                }

                totalConfidence += 1f / expSum;
                numChars++;
            }

            prevIdx = maxIdx;
        }

        string text = new(chars.ToArray());
        float avgConfidence = numChars > 0 ? totalConfidence / numChars : 0;

        return (text, avgConfidence);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var session in _sessions.Values)
        {
            session.RecSession.Dispose();
        }

        _sessions.Clear();
        _loadLock.Dispose();
    }

    private readonly record struct TextRegion(int X, int Y, int Width, int Height);

    private sealed record OcrModelSession(InferenceSession RecSession, string[] Dictionary);
}
