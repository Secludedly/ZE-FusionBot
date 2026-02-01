using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SysBot.Pokemon.Discord.Helpers.TradeModule
{
    public static class IVEnforcer
    {
        private const int DEFAULT_PID_ATTEMPTS = 2_000_000;

        /// <summary>
        /// Fully unified method to apply requested IVs, HyperTraining, Nature/Shiny, and legal fallback.
        /// </summary>
        public static bool ApplyRequestedIVsAndForceNature(
            PKM pk,
            ReadOnlySpan<int> requestedIVs,
            Nature desiredNature,
            bool isShiny,
            ITrainerInfo trainerSav,
            IBattleTemplate template,
            Dictionary<string, bool>? userHTPreferences = null,
            int maxPidAttempts = DEFAULT_PID_ATTEMPTS)
        {
            // IVEnforcer only supports ZA Pokémon
            if (pk.Version != GameVersion.ZA)
                throw new InvalidOperationException("IVEnforcer may only be used for ZA Pokémon.");

            // Check if user explicitly requested a different StatNature via batch command (.StatNature=)
            // If pk.StatNature differs from pk.Nature, it means user wants manual nature minting
            // IMPORTANT: Capture this BEFORE any modifications
            bool hasExplicitStatNature = pk.StatNature != pk.Nature;
            Nature explicitStatNature = hasExplicitStatNature ? pk.StatNature : Nature.Random;

            // Enforce forced encounters first - support nature minting
            Nature userRequestedNature = desiredNature; // Store user's requested nature for stat nature (minting)
            bool isMinted = false;

            // Check for special Nature handling (e.g., Toxtricity)
            if (ForcedEncounterEnforcer.HasSpecialNatureHandling(pk, out var randomLegalNature))
            {
                // Toxtricity-like Pokemon: Only certain Natures are legal as actual Natures
                if (desiredNature != Nature.Random && !ForcedEncounterEnforcer.IsNatureLegal(pk, desiredNature))
                {
                    // User requested an illegal Nature - mint it as Stat Nature
                    isMinted = true;
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Requested Nature {desiredNature} is illegal as actual Nature. Using random legal Nature {randomLegalNature} (actual) with {desiredNature} (stat nature/minted)",
                        nameof(IVEnforcer));

                    // Use random legal Nature as actual, requested as Stat Nature
                    desiredNature = randomLegalNature;
                }
                else if (desiredNature == Nature.Random)
                {
                    // No specific Nature requested, use random legal Nature
                    desiredNature = randomLegalNature;
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Using random legal Nature {randomLegalNature} (special Nature handling)",
                        nameof(IVEnforcer));
                }
                else
                {
                    // User requested a legal Nature
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Using requested legal Nature {desiredNature} (special Nature handling)",
                        nameof(IVEnforcer));
                }
            }
            else if (ForcedEncounterEnforcer.TryGetForcedNature(pk, out var forcedNature))
            {
                // Priority for StatNature when forced nature exists:
                // 1. If user explicitly set StatNature via batch command, use that (no minting message)
                // 2. Else if user requested a different nature than forced, mint it (log minting)
                // 3. Else use forced nature as stat nature
                if (hasExplicitStatNature)
                {
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Nature forced to {forcedNature} with explicit StatNature {explicitStatNature} (static encounter)",
                        nameof(IVEnforcer));
                }
                else if (desiredNature != Nature.Random && desiredNature != forcedNature)
                {
                    isMinted = true;
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Nature minted from {forcedNature} (actual) to {desiredNature} (stat nature) due to static encounter",
                        nameof(IVEnforcer));
                }
                else
                {
                    LogUtil.LogInfo(
                        $"{(Species)pk.Species}: Nature forced to {forcedNature} due to static encounter",
                        nameof(IVEnforcer));
                }
                desiredNature = forcedNature; // Use forced nature for PID generation
            }

            // Skip ALL enforcement if the Pokémon is a Fateful Encounter, even if from ZA
            if (pk.FatefulEncounter)
                return true;

            if (pk == null) throw new ArgumentNullException(nameof(pk));
            if (trainerSav == null) throw new ArgumentNullException(nameof(trainerSav));
            if (template == null) throw new ArgumentNullException(nameof(template));

            // -----------------------------
            // 1) Apply IVs in PKHeX order (HP/ATK/DEF/SPE/SPA/SPD)
            // Input should ALREADY be in PKHeX order from user request
            // -----------------------------
            int[] ivs;

            // Check for randomized IVs first (e.g., Magearna)
            if (ForcedEncounterEnforcer.RequiresRandomizedIVs(pk, out var randomizedIVs))
            {
                ivs = randomizedIVs;
                LogUtil.LogInfo(
                    $"{(Species)pk.Species}: Using randomized IVs (3x31, 3x0) - {string.Join("/", ivs)}",
                    nameof(IVEnforcer));
            }
            // HARD OVERRIDE for static encounters
            else if (ForcedEncounterEnforcer.TryGetFixedIVs(pk, out var forcedIVs))
            {
                ivs = forcedIVs;
            }
            else
            {
                ivs = requestedIVs.Length == 6
                    ? requestedIVs.ToArray()
                    : new int[] { 31, 31, 31, 31, 31, 31 };
            }

            pk.SetIVs(ivs);

            // -----------------------------
            // 2) Apply HyperTraining based on user preferences
            // -----------------------------
            // Check if user wants to disable ALL hypertraining
            bool disableAllHT = userHTPreferences?.ContainsKey("ALL") == true && !userHTPreferences["ALL"];

            // Clear ALL hypertrain flags first
            if (pk is IHyperTrain ht)
            {
                ht.HT_HP = false;
                ht.HT_ATK = false;
                ht.HT_DEF = false;
                ht.HT_SPE = false;
                ht.HT_SPA = false;
                ht.HT_SPD = false;
            }

            if (!disableAllHT)
            {
                // Apply automatic HT to all stats < 31 (default behavior)
                ApplyHyperTrainingIfNeeded(pk);

                // Override with user's specific preferences
                if (userHTPreferences != null && pk is IHyperTrain htOverride)
                {
                    if (userHTPreferences.ContainsKey("HP"))
                        htOverride.HT_HP = userHTPreferences["HP"];
                    if (userHTPreferences.ContainsKey("ATK"))
                        htOverride.HT_ATK = userHTPreferences["ATK"];
                    if (userHTPreferences.ContainsKey("DEF"))
                        htOverride.HT_DEF = userHTPreferences["DEF"];
                    if (userHTPreferences.ContainsKey("SPE"))
                        htOverride.HT_SPE = userHTPreferences["SPE"];
                    if (userHTPreferences.ContainsKey("SPA"))
                        htOverride.HT_SPA = userHTPreferences["SPA"];
                    if (userHTPreferences.ContainsKey("SPD"))
                        htOverride.HT_SPD = userHTPreferences["SPD"];
                }
            }

            pk.ResetPartyStats();
            pk.RefreshChecksum();

            // -----------------------------
            // 3) Quick validity check
            // -----------------------------
            bool alreadyValid = (desiredNature == Nature.Random || pk.Nature == desiredNature)
                                && (!isShiny || pk.IsShiny)
                                && new LegalityAnalysis(pk).Valid;
            if (alreadyValid)
                return true;

            // -----------------------------
            // 4) PID & Nature / Shiny enforcement
            // -----------------------------
            uint originalPid = pk.PID;
            Nature originalNature = pk.Nature;
            Span<byte> buf = stackalloc byte[4];
            uint id32 = pk.ID32;

            for (int attempt = 0; attempt < maxPidAttempts; attempt++)
            {
                RandomNumberGenerator.Fill(buf);
                uint candidate = BitConverter.ToUInt32(buf);

                int candNature = (int)(candidate % 25u);
                bool shinyCheck = !isShiny || ((ushort)((id32 ^ candidate) ^ ((id32 ^ candidate) >> 16)) < 8);

                if (desiredNature != Nature.Random && candNature != (int)desiredNature)
                    continue;
                if (!shinyCheck)
                    continue;

                pk.PID = candidate;
                pk.Nature = (Nature)candNature;

                // Apply stat nature:
                // 1. If user explicitly requested a StatNature via batch command, use that
                // 2. Else if minted (forced nature), use user's requested nature
                // 3. Else use the actual nature
                pk.StatNature = hasExplicitStatNature ? explicitStatNature : (isMinted ? userRequestedNature : pk.Nature);

                // Reapply IVs after PID change
                pk.SetIVs(ivs);

                // Clear hypertrain flags
                if (pk is IHyperTrain htCheck)
                {
                    htCheck.HT_HP = false;
                    htCheck.HT_ATK = false;
                    htCheck.HT_DEF = false;
                    htCheck.HT_SPE = false;
                    htCheck.HT_SPA = false;
                    htCheck.HT_SPD = false;
                }

                // Reapply HT based on preferences
                if (!disableAllHT)
                {
                    ApplyHyperTrainingIfNeeded(pk);

                    if (userHTPreferences != null && pk is IHyperTrain htReapply)
                    {
                        if (userHTPreferences.ContainsKey("HP"))
                            htReapply.HT_HP = userHTPreferences["HP"];
                        if (userHTPreferences.ContainsKey("ATK"))
                            htReapply.HT_ATK = userHTPreferences["ATK"];
                        if (userHTPreferences.ContainsKey("DEF"))
                            htReapply.HT_DEF = userHTPreferences["DEF"];
                        if (userHTPreferences.ContainsKey("SPE"))
                            htReapply.HT_SPE = userHTPreferences["SPE"];
                        if (userHTPreferences.ContainsKey("SPA"))
                            htReapply.HT_SPA = userHTPreferences["SPA"];
                        if (userHTPreferences.ContainsKey("SPD"))
                            htReapply.HT_SPD = userHTPreferences["SPD"];
                    }
                }

                pk.ResetPartyStats();
                pk.RefreshChecksum();

                if (new LegalityAnalysis(pk).Valid)
                    return true;
            }

            // -----------------------------
            // 5) Fallback: AutoLegality if PID attempts failed
            // -----------------------------
            try
            {
                var legalized = trainerSav.GetLegal(template, out string result);
                if (legalized != null)
                {
                    pk.TransferPropertiesWithReflection(legalized);

                    // Force IVs one more time
                    pk.SetIVs(ivs);

                    // Clear hypertrain
                    if (pk is IHyperTrain htFinal)
                    {
                        htFinal.HT_HP = false;
                        htFinal.HT_ATK = false;
                        htFinal.HT_DEF = false;
                        htFinal.HT_SPE = false;
                        htFinal.HT_SPA = false;
                        htFinal.HT_SPD = false;
                    }

                    // Reapply HT based on preferences
                    if (!disableAllHT)
                    {
                        ApplyHyperTrainingIfNeeded(pk);

                        if (userHTPreferences != null && pk is IHyperTrain htFinalRestore)
                        {
                            if (userHTPreferences.ContainsKey("HP"))
                                htFinalRestore.HT_HP = userHTPreferences["HP"];
                            if (userHTPreferences.ContainsKey("ATK"))
                                htFinalRestore.HT_ATK = userHTPreferences["ATK"];
                            if (userHTPreferences.ContainsKey("DEF"))
                                htFinalRestore.HT_DEF = userHTPreferences["DEF"];
                            if (userHTPreferences.ContainsKey("SPE"))
                                htFinalRestore.HT_SPE = userHTPreferences["SPE"];
                            if (userHTPreferences.ContainsKey("SPA"))
                                htFinalRestore.HT_SPA = userHTPreferences["SPA"];
                            if (userHTPreferences.ContainsKey("SPD"))
                                htFinalRestore.HT_SPD = userHTPreferences["SPD"];
                        }
                    }

                    pk.ResetPartyStats();
                    pk.RefreshChecksum();
                    return false;
                }
            }
            catch { }

            // -----------------------------
            // 6) Restore original if all else fails
            // -----------------------------
            pk.PID = originalPid;
            pk.Nature = originalNature;

            // Priority for Stat Nature:
            // 1.If user explicitly set Stat Nature via batch command(.StatNature = / Stat Nature:), use that.
            // 2.However, if a user requested a different nature than a forced one, mint it(use requested as Stat Nature).
            // 3.Otherwise, use forced nature as Stat Nature.
            pk.StatNature = hasExplicitStatNature ? explicitStatNature : (isMinted ? userRequestedNature : pk.Nature);

            pk.SetIVs(ivs);

            if (pk is IHyperTrain htOriginal)
            {
                htOriginal.HT_HP = false;
                htOriginal.HT_ATK = false;
                htOriginal.HT_DEF = false;
                htOriginal.HT_SPE = false;
                htOriginal.HT_SPA = false;
                htOriginal.HT_SPD = false;
            }

            // Reapply HT based on preferences
            if (!disableAllHT)
            {
                ApplyHyperTrainingIfNeeded(pk);

                if (userHTPreferences != null && pk is IHyperTrain htOriginalRestore)
                {
                    if (userHTPreferences.ContainsKey("HP"))
                        htOriginalRestore.HT_HP = userHTPreferences["HP"];
                    if (userHTPreferences.ContainsKey("ATK"))
                        htOriginalRestore.HT_ATK = userHTPreferences["ATK"];
                    if (userHTPreferences.ContainsKey("DEF"))
                        htOriginalRestore.HT_DEF = userHTPreferences["DEF"];
                    if (userHTPreferences.ContainsKey("SPE"))
                        htOriginalRestore.HT_SPE = userHTPreferences["SPE"];
                    if (userHTPreferences.ContainsKey("SPA"))
                        htOriginalRestore.HT_SPA = userHTPreferences["SPA"];
                    if (userHTPreferences.ContainsKey("SPD"))
                        htOriginalRestore.HT_SPD = userHTPreferences["SPD"];
                }
            }

            pk.ResetPartyStats();
            pk.RefreshChecksum();
            return false;
        }

        public static void ApplyHyperTrainingIfNeeded(PKM pk)
        {
            if (pk.Version != GameVersion.ZA)
                return;

            if (ForcedEncounterEnforcer.TryGetFixedIVs(pk, out _))
                return;

            if (pk is not IHyperTrain ht)
                return;

            // Determine minimum level required for hypertraining
            int minLevel = pk.Version == GameVersion.ZA ? 50 : 100;

            if (pk.CurrentLevel < minLevel)
                return;

            // Only hypertrain IVs that are less than 31
            if (pk.IV_HP < 31) ht.HT_HP = true;
            if (pk.IV_ATK < 31) ht.HT_ATK = true;
            if (pk.IV_DEF < 31) ht.HT_DEF = true;
            if (pk.IV_SPE < 31) ht.HT_SPE = true;
            if (pk.IV_SPA < 31) ht.HT_SPA = true;
            if (pk.IV_SPD < 31) ht.HT_SPD = true;
        }

    }
}
