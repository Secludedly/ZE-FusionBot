using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public static class AutoLegalityWrapper
{
    private static bool Initialized;
    private static LegalitySettings? ConfiguredSettings;

    public static void EnsureInitialized(LegalitySettings cfg)
    {
        if (Initialized)
            return;
        Initialized = true;
        ConfiguredSettings = cfg; // Cache for later use
        InitializeAutoLegality(cfg);
    }

    private static void InitializeAutoLegality(LegalitySettings cfg)
    {
        InitializeCoreStrings();
        // Updated to convert the string to a ReadOnlySpan<string> array as required by the method signature.
        EncounterEvent.RefreshMGDB([cfg.MGDBPath]);
        InitializeTrainerDatabase(cfg);
        InitializeSettings(cfg);
    }

    // The list of encounter types in the priority we prefer if no order is specified.
    private static readonly EncounterTypeGroup[] EncounterPriority = [EncounterTypeGroup.Egg, EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Mystery, EncounterTypeGroup.Trade];

    private static void InitializeSettings(LegalitySettings cfg)
    {
        // Disable expensive PID+ validation for PLZA shiny Pokemon
        // PKHeX will skip correlation checks when SearchShiny1 is false (see EncounterGift9a.TryGetSeed)
        LumioseSolver.SearchShiny1 = false;

        APILegality.SetAllLegalRibbons = cfg.SetAllLegalRibbons;
        APILegality.SetMatchingBalls = cfg.SetMatchingBalls;
        APILegality.ForceSpecifiedBall = cfg.ForceSpecifiedBall;
        APILegality.ForceLevel100for50 = cfg.ForceLevel100for50;
        Legalizer.EnableEasterEggs = cfg.EnableEasterEggs;
        APILegality.AllowTrainerOverride = cfg.AllowTrainerDataOverride;
        APILegality.AllowBatchCommands = cfg.AllowBatchCommands;
        APILegality.GameVersionPriority = cfg.GameVersionPriority;
        APILegality.SetBattleVersion = cfg.SetBattleVersion;
        APILegality.Timeout = cfg.Timeout;
        var settings = ParseSettings.Settings;
        settings.WordFilter.CheckWordFilter = false;
        settings.Handler.CheckActiveHandler = false;
        settings.Handler.Restrictions.Disable();
        var validRestriction = new NicknameRestriction { NicknamedTrade = Severity.Fishy, NicknamedMysteryGift = Severity.Fishy };
        settings.Nickname.SetAllTo(validRestriction);

        // As of February 2024, the default setting in PKHeX is Invalid for missing HOME trackers.
        // If the host wants to allow missing HOME trackers, we need to disable the default setting.
        bool allowMissingHOME = !cfg.EnableHOMETrackerCheck;
        if (allowMissingHOME)
            settings.HOMETransfer.Disable();

        // We need all the encounter types present, so add the missing ones at the end.
        var missing = EncounterPriority.Except(cfg.PrioritizeEncounters);
        cfg.PrioritizeEncounters.AddRange(missing);
        cfg.PrioritizeEncounters = [.. cfg.PrioritizeEncounters.Distinct()]; // Don't allow duplicates.
        EncounterMovesetGenerator.PriorityList = cfg.PrioritizeEncounters;
    }

    private static List<GameVersion> SanitizePriorityOrder(List<GameVersion> versionList)
    {
        var validVersions = Enum.GetValues<GameVersion>().Where(GameUtil.IsValidSavedVersion).Reverse().ToList();

        foreach (var ver in validVersions)
        {
            if (!versionList.Contains(ver))
                versionList.Add(ver); // Add any missing versions.
        }

        // Remove any versions in versionList that are not in validVersions and clean up duplicates in the process.
        return [.. versionList.Intersect(validVersions)];
    }

    private static void InitializeTrainerDatabase(LegalitySettings cfg)
    {
        var externalSource = cfg.GeneratePathTrainerInfo;
        if (Directory.Exists(externalSource))
            TrainerSettings.LoadTrainerDatabaseFromPath(externalSource);

        // Seed the Trainer Database with enough fake save files so that we return a generation sensitive format when needed.  
        var fallback = GetDefaultTrainer(cfg);
        for (byte generation = 1; generation <= GameUtil.get_Generation(GameVersion.Gen9); generation++)
        {
            var versions = GameUtil.GetVersionsInGeneration(generation, GameVersion.Any);
            foreach (var version in versions)
                RegisterIfNoneExist(fallback, generation, version);
        }
        // Manually register for LGP/E since Gen7 above will only register the 3DS versions.  
        RegisterIfNoneExist(fallback, 7, GameVersion.GP);
        RegisterIfNoneExist(fallback, 7, GameVersion.GE);
    }

    private static SimpleTrainerInfo GetDefaultTrainer(LegalitySettings cfg)
    {
        var OT = cfg.GenerateOT;
        if (OT.Length == 0)
            OT = "Blank"; // Will fail if actually left blank.
        var fallback = new SimpleTrainerInfo(GameVersion.Any)
        {
            Language = (byte)cfg.GenerateLanguage,
            TID16 = cfg.GenerateTID16,
            SID16 = cfg.GenerateSID16,
            OT = OT,
            Generation = 0,
        };
        return fallback;
    }

    private static void RegisterIfNoneExist(SimpleTrainerInfo fallback, byte generation, GameVersion version)
    {
        fallback = new SimpleTrainerInfo(version)
        {
            Language = fallback.Language,
            TID16 = fallback.TID16,
            SID16 = fallback.SID16,
            OT = fallback.OT,
            Generation = generation,
        };

        // In NET10, ALM has internal defaults that override our configuration
        // We need to register our fallback to ensure it's used instead of the "ALM" defaults
        TrainerSettings.Register(fallback);
    }

    private static void InitializeCoreStrings()
    {
        var lang = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName[..2];
        LocalizationUtil.SetLocalization(typeof(LegalityCheckResultCode), lang);
        LocalizationUtil.SetLocalization(typeof(MessageStrings), lang);

        // Pre-initialize BattleTemplateLocalization to prevent concurrent dictionary access issues
        // This forces all localizations to be loaded at startup before any concurrent operations
        _ = BattleTemplateLocalization.ForceLoadAll();
    }

    public static bool CanBeTraded(this PKM pkm)
    {
        if (pkm.IsNicknamed && StringsUtil.IsSpammyString(pkm.Nickname))
            return false;
        if (StringsUtil.IsSpammyString(pkm.OriginalTrainerName) && !IsFixedOT(new LegalityAnalysis(pkm).EncounterOriginal, pkm))
            return false;
        return !FormInfo.IsFusedForm(pkm.Species, pkm.Form, pkm.Format);
    }

    public static bool IsFixedOT(IEncounterTemplate t, PKM pkm) => t switch
    {
        IFixedTrainer { IsFixedTrainer: true } => true,
        MysteryGift g => !g.IsEgg && g switch
        {
            WC9 wc9 => wc9.GetHasOT(pkm.Language),
            WA8 wa8 => wa8.GetHasOT(pkm.Language),
            WB8 wb8 => wb8.GetHasOT(pkm.Language),
            WC8 wc8 => wc8.GetHasOT(pkm.Language),
            WB7 wb7 => wb7.GetHasOT(pkm.Language),
            { Generation: >= 5 } gift => gift.OriginalTrainerName.Length > 0,
            _ => true,
        },
        _ => false,
    };

    public static ITrainerInfo GetTrainerInfo<T>() where T : PKM, new()
    {
        ITrainerInfo trainerInfo;

        if (typeof(T) == typeof(PB7))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.GG);
        else if (typeof(T) == typeof(PK8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.SWSH);
        else if (typeof(T) == typeof(PB8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.BDSP);
        else if (typeof(T) == typeof(PA8))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.PLA);
        else if (typeof(T) == typeof(PK9))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.SV);
        else if (typeof(T) == typeof(PA9))
            trainerInfo = TrainerSettings.GetSavedTrainerData(GameVersion.ZA);
        else
            throw new ArgumentException("Type does not have a recognized trainer fetch.", typeof(T).Name);

        // NET10 Fix: Force override ALM's internal defaults with our configured values
        return OverrideALMDefaults(trainerInfo);
    }

    public static ITrainerInfo GetTrainerInfo(byte gen)
    {
        var trainerInfo = TrainerSettings.GetSavedTrainerData(gen);
        // NET10 Fix: Force override ALM's internal defaults with our configured values
        return OverrideALMDefaults(trainerInfo);
    }

    /// <summary>
    /// In NET10, ALM has internal "ALM" defaults that override our configuration.
    /// This method intercepts retrieved trainer info and replaces ALM defaults with our configured values.
    /// </summary>
    private static ITrainerInfo OverrideALMDefaults(ITrainerInfo trainerInfo)
    {
        // Check if this is ALM's default trainer info
        if (trainerInfo.OT != "ALM")
            return trainerInfo; // Not ALM defaults, return as-is

        // Ensure we have configured settings cached
        if (ConfiguredSettings == null)
            return trainerInfo; // No configured settings available, return as-is

        // ALM defaults detected - replace with our configured values
        var generation = trainerInfo.Generation;
        var version = trainerInfo.Version;

        var OT = ConfiguredSettings.GenerateOT;
        if (OT.Length == 0)
            OT = "Blank"; // Safety fallback

        // Create a new SimpleTrainerInfo with our configured defaults
        var configuredTrainer = new SimpleTrainerInfo(version)
        {
            Language = (byte)ConfiguredSettings.GenerateLanguage,
            TID16 = ConfiguredSettings.GenerateTID16,
            SID16 = ConfiguredSettings.GenerateSID16,
            OT = OT,
            Generation = generation,
        };

        return configuredTrainer;
    }

    public static PKM GetLegal(this ITrainerInfo sav, IBattleTemplate set, out string res)
    {
        var task = Task.Run(() => sav.GetLegalFromSet(set));
        if (task.Wait(TimeSpan.FromSeconds(30)))
        {
            var result = task.Result;
            res = result.Status switch
            {
                LegalizationResult.Regenerated => "Regenerated",
                LegalizationResult.Failed => "Failed",
                LegalizationResult.Timeout => "Timeout",
                LegalizationResult.VersionMismatch => "VersionMismatch",
                _ => "",
            };

            var pk = result.Created;

            // NET10 Fix: Replace ALM defaults with configured defaults after generation
            if (pk != null && ConfiguredSettings != null)
            {
                // Check if Pokemon has ALM defaults
                if (pk.OriginalTrainerName == "ALM")
                {
                    var OT = ConfiguredSettings.GenerateOT;
                    if (OT.Length == 0)
                        OT = "Blank";

                    // Replace with configured defaults
                    pk.OriginalTrainerName = OT;
                    pk.TrainerTID7 = (uint)((ConfiguredSettings.GenerateSID16 << 16) | ConfiguredSettings.GenerateTID16);
                    pk.Language = (int)ConfiguredSettings.GenerateLanguage;

                    // Recalculate PID for shiny Pokemon to maintain shiny status
                    if (pk.IsShiny)
                    {
                        var shinyXor = pk.ShinyXor;
                        pk.PID = (uint)((pk.TID16 ^ pk.SID16 ^ (pk.PID & 0xFFFF) ^ shinyXor) << 16) | (pk.PID & 0xFFFF);
                    }

                    pk.RefreshChecksum();
                }
            }

            return pk;
        }
        else
        {
            res = "Timeout";
            return null!; // Explicitly return null
        }
    }

    public static string GetLegalizationHint(IBattleTemplate set, ITrainerInfo sav, PKM pk) => set.SetAnalysis(sav, pk);
    public static PKM LegalizePokemon(this PKM pk) => pk.Legalize();
    public static RegenTemplate GetTemplate(ShowdownSet set) => new RegenTemplate(set);
}
