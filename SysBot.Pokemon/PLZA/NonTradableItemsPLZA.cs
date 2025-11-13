using PKHeX.Core;
using System;
using System.Collections.Generic;

namespace SysBot.Pokemon
{
    public static class NonTradableItemsPLZA
    {
        // Names must match PKHeX's English item names (case-insensitive compare below)
        private static readonly HashSet<string> BlockedItemNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Abomasite",
            "Absolite",
            "Aerodactylite",
            "Aggronite",
            "Alakazite",
            "Altarianite",
            "Ampharosite",
            "Audinite",
            "Autographed Plush",
            "Banettite",
            "Barbaracite",
            "Beedrillite",
            "Blastoisinite",
            "Blazikenite",
            "Blue Canari Plush Lv. 1",
            "Blue Canari Plush Lv. 2",
            "Blue Canari Plush Lv. 3",
            "Cameruptite",
            "Chandelurite",
            "Charizardite X",
            "Charizardite Y",
            "Cherished Ring",
            "Chesnaughtite",
            "Clefablite",
            "Colorful Screw",
            "Delphoxite",
            "Diancite",
            "Dragalgite",
            "Dragoninite",
            "Drampanite",
            "Eelektrossite",
            "Elevator Key",
            "Emboarite",
            "Excadrite",
            "Falinksite",
            "Feraligite",
            "Floettite",
            "Froslassite",
            "Galladite",
            "Garchompite",
            "Gardevoirite",
            "Gengarite",
            "Glalitite",
            "Gold Canari Plush Lv. 1",
            "Gold Canari Plush Lv. 2",
            "Gold Canari Plush Lv. 3",
            "Green Canari Plush Lv. 1",
            "Green Canari Plush Lv. 2",
            "Green Canari Plush Lv. 3",
            "Greninjite",
            "Gyaradosite",
            "Hawluchanite",
            "Heracronite",
            "Houndoominite",
            "Kangaskhanite",
            "Key to Room 202",
            "Lab Key Card A",
            "Lab Key Card B",
            "Lab Key Card C",
            "Latiasite",
            "Latiosite",
            "Lida's Things",
            "Lopunnite",
            "Lucarionite",
            "Malamarite",
            "Manectite",
            "Mawilite",
            "Medichamite",
            "Mega Ring",
            "Mega Shard",
            "Meganiumite",
            "Metagrossite",
            "Mewtwonite X",
            "Mewtwonite Y",
            "Pebble",
            "Pidgeotite",
            "Pink Canari Plush Lv. 1",
            "Pink Canari Plush Lv. 2",
            "Pink Canari Plush Lv. 3",
            "Pinsirite",
            "Pyroarite",
            "Raichunite X",
            "Raichunite Y",
            "Red Canari Plush Lv. 1",
            "Red Canari Plush Lv. 2",
            "Red Canari Plush Lv. 3",
            "Revitalizing Twig",
            "Sablenite",
            "Salamencite",
            "Sceptilite",
            "Scizorite",
            "Scolipite",
            "Scraftinite",
            "Sharpedonite",
            "Shiny Charm",
            "Skarmorite",
            "Slowbronite",
            "Starminite",
            "Steelixite",
            "Super Lumiose Galette",
            "Swampertite",
            "Tasty Trash",
            "Tyranitarite",
            "Venusaurite",
            "Victreebelite",
            "Zygarde Cube",
            "Zygardite"
        };

        public static bool IsBlocked(PKM pkm)
        {
            var held = pkm.HeldItem;
            if (held <= 0)
                return false;

            var names = GameInfo.GetStrings("en");
            if (held >= 0 && held < names.Item.Count)
            {
                var itemName = names.Item[held];
                return BlockedItemNames.Contains(itemName);
            }

            return false;
        }

        public static bool IsPLZAMode<TPoke>(PokeTradeHub<TPoke> hub) where TPoke : PKM, new()
        {
            // Detect PLZA based on the generic type (used by hub runner)
            return typeof(TPoke) == typeof(PA9);
        }
    }
}
