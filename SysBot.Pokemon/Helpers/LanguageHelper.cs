using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;

namespace SysBot.Pokemon.Helpers;

public static class LanguageHelper
{
    public static byte GetFinalLanguage(string content, ShowdownSet? set, byte configLanguage, Func<string, byte> detectLanguageFunc)
    {
        // Check if user explicitly specified a language in the showdown set
        var lines = content.Split('\n', StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase))
            {
                var languageValue = line["Language:".Length..].Trim();

                // Try to parse as LanguageID enum
                if (Enum.TryParse<LanguageID>(languageValue, true, out var langId))
                {
                    return (byte)langId;
                }

                // Handle common language names
                var explicitLang = languageValue.ToLower() switch
                {
                    "japanese" or "jpn" or "日本語" => (byte)LanguageID.Japanese,
                    "english" or "eng" => (byte)LanguageID.English,
                    "french" or "fre" or "fra" or "français" => (byte)LanguageID.French,
                    "italian" or "ita" or "italiano" => (byte)LanguageID.Italian,
                    "german" or "ger" or "deu" or "deutsch" => (byte)LanguageID.German,
                    "spanish" or "spa" or "esp" or "español" => (byte)LanguageID.Spanish,
                    "spanish-latam" or "spanishl" or "es-419" or "latam" => (byte)LanguageID.SpanishL,
                    "korean" or "kor" or "한국어" => (byte)LanguageID.Korean,
                    "chinese" or "chs" or "中文" or "简体中文" => (byte)LanguageID.ChineseS,
                    "cht" or "chineset" or "繁體中文" => (byte)LanguageID.ChineseT,
                    _ => 0
                };

                if (explicitLang != 0)
                {
                    return (byte)explicitLang;
                }
            }
        }

        // No explicit language found, use detection
        byte detectedLanguage = detectLanguageFunc(content);

        // If no language was detected (0), use the config language setting
        if (detectedLanguage == 0)
        {
            return configLanguage;
        }

        return detectedLanguage;
    }

    // NOTE: This method is no longer used. We generate Pokemon with the normal trainer
    // and set the language AFTER generation to avoid encounter matching issues.
    // Kept for reference only.
    /*
    public static ITrainerInfo GetTrainerInfoWithLanguage<T>(LanguageID language) where T : PKM, new()
    {
        // This approach caused "No valid matching encounter" errors
        // because ALM couldn't find encounters with language-specific trainers
        return AutoLegalityWrapper.GetTrainerInfo<T>();
    }
    */
}
