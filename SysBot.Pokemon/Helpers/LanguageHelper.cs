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
                    "french" or "fre" or "fra" => (byte)LanguageID.French,
                    "italian" or "ita" => (byte)LanguageID.Italian,
                    "german" or "ger" or "deu" => (byte)LanguageID.German,
                    "spanish" or "spa" or "esp" => (byte)LanguageID.Spanish,
                    "spanish-latam" or "spanishl" or "es-419" or "latam" => (byte)LanguageID.SpanishL,
                    "korean" or "kor" or "한국어" => (byte)LanguageID.Korean,
                    "chinese" or "chs" or "中文" => (byte)LanguageID.ChineseS,
                    "cht" => (byte)LanguageID.ChineseT,
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

    public static ITrainerInfo GetTrainerInfoWithLanguage<T>(LanguageID language) where T : PKM, new()
    {
        return typeof(T) switch
        {
            Type t when t == typeof(PK8) => TrainerSettings.GetSavedTrainerData(GameVersion.SWSH, language),
            Type t when t == typeof(PB8) => TrainerSettings.GetSavedTrainerData(GameVersion.BDSP, language),
            Type t when t == typeof(PA8) => TrainerSettings.GetSavedTrainerData(GameVersion.PLA, language),
            Type t when t == typeof(PK9) => TrainerSettings.GetSavedTrainerData(GameVersion.SV, language),
            Type t when t == typeof(PA9) => TrainerSettings.GetSavedTrainerData(GameVersion.ZA, language),
            Type t when t == typeof(PB7) => TrainerSettings.GetSavedTrainerData(GameVersion.GE, language),
            _ => throw new ArgumentException("Type does not have a recognized trainer fetch.", typeof(T).Name)
        };
    }
}
