using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using System;
using System.Security.Cryptography;

namespace SysBot.Pokemon.Discord.Helpers.TradeModule
{
    public static class NatureEnforcer
    {
        /// <summary>
        /// Forces the PKM to have the desired nature (and shiny if requested), keeping IVs intact.
        /// Supports nature minting: if a forced nature is required, applies it as the actual nature
        /// while preserving the user's desired nature as the stat nature (minted appearance).
        /// </summary>
        /// <param name="pkm">The PKM to modify</param>
        /// <param name="desiredNature">The Nature to force (or stat nature if minted)</param>
        /// <param name="isShiny">Whether the PKM should be shiny</param>
        /// <param name="maxAttempts">How many random PIDs to try</param>
        public static void ForceNature(PKM pkm, Nature desiredNature, bool isShiny = false, int maxAttempts = 2_000_000)
        {
            if (pkm == null)
                throw new ArgumentNullException(nameof(pkm));

            if (pkm.Version != GameVersion.ZA)
                throw new InvalidOperationException("NatureEnforcer is only supported for ZA Pokémon.");

            if (pkm.FatefulEncounter)
                return;

            // Check if user explicitly requested a different StatNature via batch command (.StatNature=)
            // If pkm.StatNature differs from pkm.Nature, it means user wants manual nature minting
            // IMPORTANT: Capture this BEFORE any modifications
            bool hasExplicitStatNature = pkm.StatNature != pkm.Nature;
            Nature explicitStatNature = hasExplicitStatNature ? pkm.StatNature : Nature.Random;

            // -----------------------------
            // FORCE static encounter nature with minting support
            // -----------------------------
            Nature userRequestedNature = desiredNature; // Store user's requested nature for stat nature (minting)
            bool isMinted = false;

            if (ForcedEncounterEnforcer.TryGetForcedNature(pkm, out var forcedNature))
            {
                // Priority for StatNature when forced nature exists:
                // 1. If user explicitly set Stat Nature via batch command, use that (no minting message)
                // 2. Else if user requested a different nature than forced, mint it (log minting)
                // 3. Else use forced nature as stat nature
                if (hasExplicitStatNature)
                {
                    LogUtil.LogInfo(
                        $"{(Species)pkm.Species}: Nature forced to {forcedNature} with explicit StatNature {explicitStatNature} (static encounter)",
                        nameof(NatureEnforcer));
                }
                else if (desiredNature != Nature.Random && desiredNature != forcedNature)
                {
                    isMinted = true;
                    LogUtil.LogInfo(
                        $"{(Species)pkm.Species}: Nature minted from {forcedNature} (actual) to {desiredNature} (stat nature) due to static encounter",
                        nameof(NatureEnforcer));
                }
                else
                {
                    LogUtil.LogInfo(
                        $"{(Species)pkm.Species}: Nature forced to {forcedNature} due to static encounter",
                        nameof(NatureEnforcer));
                }
                desiredNature = forcedNature; // Use forced nature for PID generation
            }

            if (desiredNature == Nature.Random && !isShiny)
                return;

            if (pkm.Nature == desiredNature && (!isShiny || pkm.IsShiny))
            {
                // Apply stat nature:
                // 1. If user explicitly requested a Stat Nature via batch command, use that.
                // 2. Else if minted (forced nature), use user's requested nature.
                // 3. Else use the actual nature
                pkm.StatNature = hasExplicitStatNature ? explicitStatNature : (isMinted ? userRequestedNature : pkm.Nature);
                pkm.RefreshChecksum();
                return;
            }

            // Backup IVs
            int iv_hp = pkm.IV_HP;
            int iv_atk = pkm.IV_ATK;
            int iv_def = pkm.IV_DEF;
            int iv_spe = pkm.IV_SPE;
            int iv_spa = pkm.IV_SPA;
            int iv_spd = pkm.IV_SPD;

            Span<byte> buf = stackalloc byte[4];
            uint newPid = 0;
            int targetNature = (int)desiredNature;
            uint id32 = pkm.ID32;

            for (int attempts = 0; attempts < maxAttempts; attempts++)
            {
                RandomNumberGenerator.Fill(buf);
                newPid = BitConverter.ToUInt32(buf);

                int natureCheck = (int)(newPid % 25u);
                bool shinyCheck = !isShiny || ((ushort)((id32 ^ newPid) ^ ((id32 ^ newPid) >> 16)) < 8);

                if (natureCheck == targetNature && shinyCheck)
                    break;

                if (attempts == maxAttempts - 1)
                    throw new InvalidOperationException(
                        $"Failed to produce PID for Nature {desiredNature}" +
                        (isShiny ? " and shiny" : "") +
                        $" after {maxAttempts} attempts.");
            }

            // Apply PID & nature
            pkm.PID = newPid;
            pkm.Nature = (Nature)(newPid % 25u);

            // Apply stat nature:
            // 1. If user explicitly requested a StatNature via batch command, use that
            // 2. Else if minted (forced nature), use user's requested nature
            // 3. Else use the actual nature
            pkm.StatNature = hasExplicitStatNature ? explicitStatNature : (isMinted ? userRequestedNature : pkm.Nature);

            // Restore IVs
            pkm.IV_HP = iv_hp;
            pkm.IV_ATK = iv_atk;
            pkm.IV_DEF = iv_def;
            pkm.IV_SPE = iv_spe;
            pkm.IV_SPA = iv_spa;
            pkm.IV_SPD = iv_spd;

            pkm.RefreshChecksum();
        }
    }
}
