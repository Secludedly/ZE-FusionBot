using PKHeX.Core;
using SysBot.Pokemon.Discord.Helpers;
using System;
using System.Security.Cryptography;

public static class ForceNatureHelper
{
    public static void ForceNature(PKM pkm, Nature desiredNature, bool isShiny = false, int maxAttempts = 5_200_000)
    {
        if (pkm == null)
            throw new ArgumentNullException(nameof(pkm));

        // Nothing to do if Nature is random and no shiny requested
        if (desiredNature == Nature.Random && !isShiny)
            return;

        // Already correct nature & shiny status
        if (pkm.Nature == desiredNature && (!isShiny || pkm.IsShiny))
        {
            pkm.StatNature = pkm.Nature;
            pkm.RefreshChecksum();
            return;
        }

        // Backup IVs
        var iv_hp = pkm.IV_HP;
        var iv_atk = pkm.IV_ATK;
        var iv_def = pkm.IV_DEF;
        var iv_spe = pkm.IV_SPE;
        var iv_spa = pkm.IV_SPA;
        var iv_spd = pkm.IV_SPD;

        Span<byte> buf = stackalloc byte[4];
        uint newPid = 0;
        int targetNature = (int)desiredNature;
        uint id32 = pkm.ID32;

        for (int attempts = 0; attempts < maxAttempts; attempts++)
        {
            RandomNumberGenerator.Fill(buf);
            newPid = BitConverter.ToUInt32(buf);

            int natureCheck = (int)(newPid % 25u);

            // If shiny is requested and not shiny-locked, enforce shiny PID
            bool shinyCheck = !isShiny || ((ushort)((id32 ^ newPid) ^ ((id32 ^ newPid) >> 16)) < 8);

            if (natureCheck == targetNature && shinyCheck)
                break;

            if (attempts == maxAttempts - 1)
                throw new InvalidOperationException(
                    $"Failed to produce PID for Nature {desiredNature}" +
                    (isShiny ? " and Shiny" : "") +
                    $" after {maxAttempts} attempts.");
        }

        // Apply PID & Nature
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
