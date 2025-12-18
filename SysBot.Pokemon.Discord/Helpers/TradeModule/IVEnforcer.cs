using PKHeX.Core;
using SysBot.Base;
using System;
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
            int maxPidAttempts = DEFAULT_PID_ATTEMPTS)
        {
            // IVEnforcer only supports ZA Pokémon
            if (pk.Version != GameVersion.ZA)
                throw new InvalidOperationException("IVEnforcer may only be used for ZA Pokémon.");

            // Enforce forced encounters first
            if (ForcedEncounterEnforcer.TryGetForcedNature(pk, out var forcedNature))
            {
                LogUtil.LogInfo(
                    $"{(Species)pk.Species}: Nature forced to {forcedNature} due to static encounter",
                    nameof(IVEnforcer));
                desiredNature = forcedNature; // Ignore whatever user requested
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

            // HARD OVERRIDE for static encounters
            if (ForcedEncounterEnforcer.TryGetFixedIVs(pk, out var forcedIVs))
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
            // 2) Clear ALL hypertrain flags - we want actual IVs, not faked ones
            // -----------------------------
            if (pk is IHyperTrain ht)
            {
                ht.HT_HP = false;
                ht.HT_ATK = false;
                ht.HT_DEF = false;
                ht.HT_SPE = false;
                ht.HT_SPA = false;
                ht.HT_SPD = false;
            }
            ApplyHyperTrainingIfNeeded(pk);
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
                pk.StatNature = pk.Nature;

                // Reapply IVs after PID change
                pk.SetIVs(ivs);

                // Clear hypertrain flags again
                if (pk is IHyperTrain htCheck)
                {
                    htCheck.HT_HP = false;
                    htCheck.HT_ATK = false;
                    htCheck.HT_DEF = false;
                    htCheck.HT_SPE = false;
                    htCheck.HT_SPA = false;
                    htCheck.HT_SPD = false;
                }
                ApplyHyperTrainingIfNeeded(pk);
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
                    ApplyHyperTrainingIfNeeded(pk);
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
            pk.StatNature = pk.Nature;
            pk.SetIVs(ivs);

            if (pk is IHyperTrain htRestore)
            {
                htRestore.HT_HP = false;
                htRestore.HT_ATK = false;
                htRestore.HT_DEF = false;
                htRestore.HT_SPE = false;
                htRestore.HT_SPA = false;
                htRestore.HT_SPD = false;
            }
            ApplyHyperTrainingIfNeeded(pk);
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
