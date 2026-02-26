namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// Paths to OCR model files for a specific model type.
/// </summary>
/// <param name="DetectionModelPath">Path to the text detection ONNX model (DBNet).</param>
/// <param name="RecognitionModelPath">Path to the text recognition ONNX model (SVTR).</param>
/// <param name="DictionaryPath">Path to the character dictionary file.</param>
public record OcrModelPaths(
    string DetectionModelPath,
    string RecognitionModelPath,
    string DictionaryPath);
