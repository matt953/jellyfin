using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace MediaBrowser.MediaEncoding.Ocr;

/// <summary>
/// Maps ISO language codes to OCR model types.
/// </summary>
public static class LanguageMapping
{
    private static readonly FrozenDictionary<string, OcrModelType> _languageToModel = new Dictionary<string, OcrModelType>(StringComparer.OrdinalIgnoreCase)
    {
        // Latin script languages
        ["en"] = OcrModelType.Latin,
        ["eng"] = OcrModelType.Latin,
        ["english"] = OcrModelType.Latin,
        ["fr"] = OcrModelType.Latin,
        ["fra"] = OcrModelType.Latin,
        ["fre"] = OcrModelType.Latin,
        ["french"] = OcrModelType.Latin,
        ["de"] = OcrModelType.Latin,
        ["deu"] = OcrModelType.Latin,
        ["ger"] = OcrModelType.Latin,
        ["german"] = OcrModelType.Latin,
        ["es"] = OcrModelType.Latin,
        ["spa"] = OcrModelType.Latin,
        ["spanish"] = OcrModelType.Latin,
        ["it"] = OcrModelType.Latin,
        ["ita"] = OcrModelType.Latin,
        ["italian"] = OcrModelType.Latin,
        ["pt"] = OcrModelType.Latin,
        ["por"] = OcrModelType.Latin,
        ["portuguese"] = OcrModelType.Latin,
        ["nl"] = OcrModelType.Latin,
        ["nld"] = OcrModelType.Latin,
        ["dut"] = OcrModelType.Latin,
        ["dutch"] = OcrModelType.Latin,
        ["pl"] = OcrModelType.Latin,
        ["pol"] = OcrModelType.Latin,
        ["polish"] = OcrModelType.Latin,
        ["sv"] = OcrModelType.Latin,
        ["swe"] = OcrModelType.Latin,
        ["swedish"] = OcrModelType.Latin,
        ["da"] = OcrModelType.Latin,
        ["dan"] = OcrModelType.Latin,
        ["danish"] = OcrModelType.Latin,
        ["no"] = OcrModelType.Latin,
        ["nor"] = OcrModelType.Latin,
        ["norwegian"] = OcrModelType.Latin,
        ["fi"] = OcrModelType.Latin,
        ["fin"] = OcrModelType.Latin,
        ["finnish"] = OcrModelType.Latin,
        ["cs"] = OcrModelType.Latin,
        ["ces"] = OcrModelType.Latin,
        ["cze"] = OcrModelType.Latin,
        ["czech"] = OcrModelType.Latin,
        ["hu"] = OcrModelType.Latin,
        ["hun"] = OcrModelType.Latin,
        ["hungarian"] = OcrModelType.Latin,
        ["ro"] = OcrModelType.Latin,
        ["ron"] = OcrModelType.Latin,
        ["rum"] = OcrModelType.Latin,
        ["romanian"] = OcrModelType.Latin,
        ["tr"] = OcrModelType.Latin,
        ["tur"] = OcrModelType.Latin,
        ["turkish"] = OcrModelType.Latin,
        ["id"] = OcrModelType.Latin,
        ["ind"] = OcrModelType.Latin,
        ["indonesian"] = OcrModelType.Latin,
        ["ms"] = OcrModelType.Latin,
        ["msa"] = OcrModelType.Latin,
        ["may"] = OcrModelType.Latin,
        ["malay"] = OcrModelType.Latin,
        ["vi"] = OcrModelType.Latin,
        ["vie"] = OcrModelType.Latin,
        ["vietnamese"] = OcrModelType.Latin,

        // Chinese
        ["zh"] = OcrModelType.Chinese,
        ["zho"] = OcrModelType.Chinese,
        ["chi"] = OcrModelType.Chinese,
        ["chinese"] = OcrModelType.Chinese,

        // Japanese (uses Chinese model for Kanji recognition)
        ["ja"] = OcrModelType.Chinese,
        ["jpn"] = OcrModelType.Chinese,
        ["japanese"] = OcrModelType.Chinese,

        // Korean
        ["ko"] = OcrModelType.Korean,
        ["kor"] = OcrModelType.Korean,
        ["korean"] = OcrModelType.Korean,

        // Cyrillic script languages
        ["ru"] = OcrModelType.Cyrillic,
        ["rus"] = OcrModelType.Cyrillic,
        ["russian"] = OcrModelType.Cyrillic,
        ["uk"] = OcrModelType.Cyrillic,
        ["ukr"] = OcrModelType.Cyrillic,
        ["ukrainian"] = OcrModelType.Cyrillic,
        ["bg"] = OcrModelType.Cyrillic,
        ["bul"] = OcrModelType.Cyrillic,
        ["bulgarian"] = OcrModelType.Cyrillic,
        ["sr"] = OcrModelType.Cyrillic,
        ["srp"] = OcrModelType.Cyrillic,
        ["serbian"] = OcrModelType.Cyrillic,
        ["be"] = OcrModelType.Cyrillic,
        ["bel"] = OcrModelType.Cyrillic,
        ["belarusian"] = OcrModelType.Cyrillic,

        // Arabic script languages
        ["ar"] = OcrModelType.Arabic,
        ["ara"] = OcrModelType.Arabic,
        ["arabic"] = OcrModelType.Arabic,
        ["fa"] = OcrModelType.Arabic,
        ["fas"] = OcrModelType.Arabic,
        ["per"] = OcrModelType.Arabic,
        ["persian"] = OcrModelType.Arabic,
        ["ur"] = OcrModelType.Arabic,
        ["urd"] = OcrModelType.Arabic,
        ["urdu"] = OcrModelType.Arabic,

        // Devanagari script languages
        ["hi"] = OcrModelType.Devanagari,
        ["hin"] = OcrModelType.Devanagari,
        ["hindi"] = OcrModelType.Devanagari,
        ["mr"] = OcrModelType.Devanagari,
        ["mar"] = OcrModelType.Devanagari,
        ["marathi"] = OcrModelType.Devanagari,
        ["ne"] = OcrModelType.Devanagari,
        ["nep"] = OcrModelType.Devanagari,
        ["nepali"] = OcrModelType.Devanagari,
        ["sa"] = OcrModelType.Devanagari,
        ["san"] = OcrModelType.Devanagari,
        ["sanskrit"] = OcrModelType.Devanagari,

        // Thai
        ["th"] = OcrModelType.Thai,
        ["tha"] = OcrModelType.Thai,
        ["thai"] = OcrModelType.Thai,

        // Tamil
        ["ta"] = OcrModelType.Tamil,
        ["tam"] = OcrModelType.Tamil,
        ["tamil"] = OcrModelType.Tamil,

        // Telugu
        ["te"] = OcrModelType.Telugu,
        ["tel"] = OcrModelType.Telugu,
        ["telugu"] = OcrModelType.Telugu,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the OCR model type for a given language code.
    /// </summary>
    /// <param name="languageCode">ISO 639-1, 639-2, or 639-3 language code.</param>
    /// <returns>The model type, or null if the language is not supported.</returns>
    public static OcrModelType? GetModelType(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            return null;
        }

        return _languageToModel.TryGetValue(languageCode, out var modelType) ? modelType : null;
    }

    /// <summary>
    /// Checks if a language is supported for OCR.
    /// </summary>
    /// <param name="languageCode">ISO language code.</param>
    /// <returns>True if the language is supported.</returns>
    public static bool IsLanguageSupported(string? languageCode)
    {
        return !string.IsNullOrEmpty(languageCode) && _languageToModel.ContainsKey(languageCode);
    }

    /// <summary>
    /// Gets all supported language codes.
    /// </summary>
    /// <returns>Collection of supported language codes.</returns>
    public static IEnumerable<string> GetSupportedLanguages()
    {
        return _languageToModel.Keys;
    }
}
