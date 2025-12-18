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
        /// </summary>
        /// <param name="pkm">The PKM to modify</param>
        /// <param name="desiredNature">The Nature to force</param>
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

            // -----------------------------
            // FORCE static encounter nature
            // -----------------------------
            if (ForcedEncounterEnforcer.TryGetForcedNature(pkm, out var forcedNature))
            {
                desiredNature = forcedNature; // Ignore whatever the user requested
                LogUtil.LogInfo(
                    $"{(Species)pkm.Species}: Nature forced to {forcedNature} due to static encounter",
                    nameof(NatureEnforcer));
            }

            if (desiredNature == Nature.Random && !isShiny)
                return;

            if (pkm.Nature == desiredNature && (!isShiny || pkm.IsShiny))
            {
                pkm.StatNature = pkm.Nature;
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
            pkm.StatNature = pkm.Nature;

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
