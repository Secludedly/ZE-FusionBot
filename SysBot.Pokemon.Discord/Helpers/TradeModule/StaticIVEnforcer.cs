using PKHeX.Core;
using System.Collections.Generic;

namespace SysBot.Pokemon.Discord.Helpers.TradeModule
{
    public static class StaticIVEnforcer
    {
        // PKHeX order: HP / ATK / DEF / SPE / SPA / SPD
        public static readonly Dictionary<Species, int[]> IVs = new()
        {
            { Species.Zeraora,   new[] { 31, 31, 19, 27, 31, 15 } },
            { Species.Latias,    new[] { 12, 7, 31, 16, 31, 31 } },
            { Species.Latios,    new[] { 31, 31, 31, 16, 16, 7 } },
            { Species.Kyogre,    new[] { 31, 31, 12, 22, 31, 28 } },
            { Species.Terrakion, new[] { 31, 31, 16, 31, 12, 7 } },
            { Species.Rayquaza,  new[] { 31, 31, 29, 20, 31, 29 } },
            { Species.Virizion,  new[] { 16, 31, 12, 31, 31, 7 } },
            { Species.Cobalion,  new[] { 12, 31, 31, 31, 16, 7 } },
            { Species.Keldeo,    new[] { 7, 31, 31, 31, 16, 12 } },
            { Species.Heatran,   new[] { 31, 31, 28, 8, 31, 25 } },
            { Species.Meloetta,  new[] { 7, 31, 12, 31, 31, 16 } },
            { Species.Groudon,   new[] { 31, 31, 24, 25, 31, 18 } },
            { Species.Darkrai,   new[] { 31, 31, 18, 26, 31, 21 } },
            { Species.Zygarde,   new[] { 31, 31, 15, 31, 28, 19 } },
        };

        // -----------------------------
        // SPECIES CAST HELPER
        // -----------------------------
        public static Species GetSpecies(PKM pk)
            => (Species)pk.Species;

        // -----------------------------
        // LOOKUP HELPER
        // -----------------------------
        public static bool TryGetIVs(PKM pk, out int[] ivs)
            => IVs.TryGetValue(GetSpecies(pk), out ivs);
    }
}
