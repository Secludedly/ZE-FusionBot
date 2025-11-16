// ZAHelper.cs
using PKHeX.Core;
using System;
using System.Security.Cryptography;

namespace SysBot.Pokemon.Discord.Helpers.TradeModule
{
    /// <summary>
    /// Helpers for forcing PID-derived fields (Nature) for ZA (PA9) Pokémon.
    /// Designed to work with PA9 / PKM provided in PKHeX.Core.
    /// </summary>
    public static class ZANatureHelper
    {
        /// <summary>
        /// Force a ZA (PA9) Pokémon to have the requested Nature by re-generating PID until it matches.
        /// This preserves the Pokémon's IVs/EVs (via individual properties) and refreshes the checksum.
        /// </summary>
        /// <param name="pkm">PKM instance (PA9/PKM) to modify.</param>
        /// <param name="desiredNature">Desired Nature. If Nature.Random, nothing is done.</param>
        /// <param name="maxAttempts">Max PID attempts before giving up (default 1k).</param>
        public static void ForceNatureZA(PKM pkm, Nature desiredNature, int maxAttempts = 1000)
        {
            if (pkm is null)
                throw new ArgumentNullException(nameof(pkm));

            if (desiredNature == Nature.Random)
                return;

            // If it's already the requested nature, ensure StatNature is consistent and exit.
            if (pkm.Nature == desiredNature)
            {
                // StatNature should match the "PID-derived" nature for ZA; set it to be safe.
                pkm.StatNature = pkm.Nature;
                pkm.RefreshChecksum();
                return;
            }

            // Backup IVs and EVs using the individual properties (PA9/PKM expose IV_HP / EV_HP etc.)
            var iv_hp = pkm.IV_HP;
            var iv_atk = pkm.IV_ATK;
            var iv_def = pkm.IV_DEF;
            var iv_spe = pkm.IV_SPE;
            var iv_spa = pkm.IV_SPA;
            var iv_spd = pkm.IV_SPD;

            // We'll use cryptographic RNG to produce full 32-bit PIDs.
            Span<byte> buf = stackalloc byte[4];
            int attempts = 0;
            uint newPid = 0;
            int target = (int)desiredNature; // cast enum to int for modulo compare

            while (true)
            {
                attempts++;
                // Fill 4 bytes with random data
                RandomNumberGenerator.Fill(buf);
                newPid = (uint)BitConverter.ToUInt32(buf);

                // PID mod 25 -> nature index
                if ((int)(newPid % 25u) == target)
                    break;

                if (attempts >= maxAttempts)
                    throw new InvalidOperationException($"ZAHelper: failed to produce PID for Nature {desiredNature} after {maxAttempts} attempts.");
            }

            // Assign PID and synchronise Nature fields
            pkm.PID = newPid;

            // For PA9 the Nature property is stored directly on the PKM data; set it explicitly.
            pkm.Nature = (Nature)(newPid % 25u);

            // StatNature should be same as Nature unless you explicitly want a different StatNature.
            pkm.StatNature = pkm.Nature;

            // Reapply IVs, safety check
            pkm.IV_HP = iv_hp;
            pkm.IV_ATK = iv_atk;
            pkm.IV_DEF = iv_def;
            pkm.IV_SPE = iv_spe;
            pkm.IV_SPA = iv_spa;
            pkm.IV_SPD = iv_spd;

            // Refresh checksum to commit changes
            pkm.RefreshChecksum();
        }
    }
}
