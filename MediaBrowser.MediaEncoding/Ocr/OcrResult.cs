namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// Result of OCR text extraction.
/// </summary>
/// <param name="Text">The extracted text (all detected text regions combined).</param>
/// <param name="Confidence">Average confidence score across all detected regions (0.0 to 1.0).</param>
/// <param name="RegionCount">Number of text regions detected.</param>
public record OcrResult(string Text, float Confidence, int RegionCount);
