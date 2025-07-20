using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;

namespace SysBot.Pokemon.Helpers;

public static class LanguageHelper
{
    public static string GetLocalizedSpeciesName(int speciesIndex, LanguageID lang)
    {
        try
        {
            var strings = GameInfo.GetStrings((int)lang);
            if (strings?.Species == null || speciesIndex < 0 || speciesIndex >= strings.Species.Count)
                return "???";

            return strings.Species[speciesIndex];
        }
        catch
        {
            return "???";
        }
    }

    public static string GetLocalizedSpeciesLog(PKM pkm)
    {
        if (pkm == null)
            return "(Invalid Pokémon)";

        var langID = (LanguageID)pkm.Language;
        var langName = GetLanguageName(langID);

        string localizedName = TryGetSpeciesName(pkm.Species, langID);
        string englishName = TryGetSpeciesName(pkm.Species, LanguageID.English);

        if (langID == LanguageID.English || localizedName == englishName)
            return englishName;

        return $"{localizedName} ({englishName}, {langName})";
    }

    private static string TryGetSpeciesName(int speciesIndex, LanguageID lang)
    {
        try
        {
            var strings = GameInfo.GetStrings((int)lang);
            if (strings?.Species == null || speciesIndex < 0 || speciesIndex >= strings.Species.Count)
                return "???";

            return strings.Species[speciesIndex];
        }
        catch
        {
            return "???";
        }
    }

    public static byte GetFinalLanguage(string content, ShowdownSet? set, byte configLanguage, Func<string, byte> detectLanguageFunc)
    {
        var lines = content.Split('\n', StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase))
            {
                var languageValue = line["Language:".Length..].Trim();

                if (Enum.TryParse<LanguageID>(languageValue, true, out var langId))
                    return (byte)langId;

                var explicitLang = languageValue.ToLower() switch
                {
                    "japanese" or "jpn" or "日本語" => (byte)LanguageID.Japanese,
                    "english" or "eng" => (byte)LanguageID.English,
                    "french" or "fre" or "fra" => (byte)LanguageID.French,
                    "italian" or "ita" => (byte)LanguageID.Italian,
                    "german" or "ger" or "deu" => (byte)LanguageID.German,
                    "spanish" or "spa" or "esp" => (byte)LanguageID.Spanish,
                    "korean" or "kor" or "한국어" => (byte)LanguageID.Korean,
                    "chinese" or "chs" or "中文" => (byte)LanguageID.ChineseS,
                    "cht" => (byte)LanguageID.ChineseT,
                    _ => 0
                };

                if (explicitLang != 0)
                    return (byte)explicitLang;
            }
        }

        byte detectedLanguage = detectLanguageFunc(content);
        if (detectedLanguage == (byte)LanguageID.English || detectedLanguage == 0)
            return configLanguage;

        return detectedLanguage;
    }

    public static ITrainerInfo GetTrainerInfoWithLanguage<T>(LanguageID language) where T : PKM, new()
    {
        return typeof(T) switch
        {
            Type t when t == typeof(PK8) => TrainerSettings.GetSavedTrainerData(GameVersion.SWSH, 8, lang: language),
            Type t when t == typeof(PB8) => TrainerSettings.GetSavedTrainerData(GameVersion.BDSP, 8, lang: language),
            Type t when t == typeof(PA8) => TrainerSettings.GetSavedTrainerData(GameVersion.PLA, 8, lang: language),
            Type t when t == typeof(PK9) => TrainerSettings.GetSavedTrainerData(GameVersion.SV, 9, lang: language),
            Type t when t == typeof(PB7) => TrainerSettings.GetSavedTrainerData(GameVersion.GE, 7, lang: language),
            _ => throw new ArgumentException("Type does not have a recognized trainer fetch.", typeof(T).Name)
        };
    }

    public static string GetLanguageName(LanguageID lang)
    {
        return lang switch
        {
            LanguageID.Japanese => "Japanese",
            LanguageID.English => "English",
            LanguageID.French => "French",
            LanguageID.Italian => "Italian",
            LanguageID.German => "German",
            LanguageID.Spanish => "Spanish",
            LanguageID.Korean => "Korean",
            LanguageID.ChineseT => "Chinese (Traditional)",
            LanguageID.ChineseS => "Chinese (Simplified)",
            _ => "Unknown"
        };
    }
}
