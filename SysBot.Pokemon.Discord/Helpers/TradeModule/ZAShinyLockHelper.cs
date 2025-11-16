using PKHeX.Core;
using System.Collections.Generic;

namespace SysBot.Pokemon.Discord.Helpers
{
    public static class ZAShinyLockHelper
    {
        // ZA shiny-locked Pokémon species IDs
        private static readonly HashSet<ushort> ShinyLockedSpecies = new()
        {
            150, // Mewtwo
            716, // Xerneas
            717, // Yveltal
            718, // Zygarde
            719, // Diancie
            720, // Hoopa
            721, // Volcanion
        };

        /// <summary>
        /// Returns true if the PKM species is shiny-locked in ZA.
        /// </summary>
        public static bool IsShinyLocked(this PKM pkm)
        {
            return ShinyLockedSpecies.Contains(pkm.Species);
        }
    }
}
