namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// OCR model types for different language families.
/// Each model type corresponds to a specific PaddleOCR recognition model.
/// </summary>
public enum OcrModelType
{
    /// <summary>
    /// Latin script languages: en, fr, de, es, it, pt, nl, pl, sv, da, no, fi, cs, hu, ro, tr, id, ms.
    /// </summary>
    Latin,

    /// <summary>
    /// Chinese and Japanese (Kanji). Language codes: zh, ja.
    /// </summary>
    Chinese,

    /// <summary>
    /// Korean. Language code: ko.
    /// </summary>
    Korean,

    /// <summary>
    /// Cyrillic script languages: ru, uk, bg, sr, be.
    /// </summary>
    Cyrillic,

    /// <summary>
    /// Arabic script languages: ar, fa, ur.
    /// </summary>
    Arabic,

    /// <summary>
    /// Devanagari script languages: hi, mr, ne, sa.
    /// </summary>
    Devanagari,

    /// <summary>
    /// Thai. Language code: th.
    /// </summary>
    Thai,

    /// <summary>
    /// Tamil. Language code: ta.
    /// </summary>
    Tamil,

    /// <summary>
    /// Telugu. Language code: te.
    /// </summary>
    Telugu
}
