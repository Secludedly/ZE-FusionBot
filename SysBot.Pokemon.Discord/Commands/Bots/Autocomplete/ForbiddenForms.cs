using System.Collections.Generic;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Central list of species/form combinations to hide from autocomplete (trade-blocked, fused, etc.).
/// Extend this list as you find more problem cases.
/// </summary>
public static class ForbiddenForms
{
    // Tuple of (species dex number, form index)
    public static readonly HashSet<(ushort Species, byte Form)> List = new()
    {
        // Fusion / rider / mega-fused forms that are not tradeable
        ((ushort)PKHeX.Core.Species.Kyurem, 1), // White Kyurem
        ((ushort)PKHeX.Core.Species.Kyurem, 2), // Black Kyurem
        ((ushort)PKHeX.Core.Species.Necrozma, 1), // Dusk Mane
        ((ushort)PKHeX.Core.Species.Necrozma, 2), // Dawn Wings
        ((ushort)PKHeX.Core.Species.Necrozma, 3), // Ultra
        ((ushort)PKHeX.Core.Species.Calyrex, 1), // Ice Rider
        ((ushort)PKHeX.Core.Species.Calyrex, 2), // Shadow Rider
        ((ushort)PKHeX.Core.Species.Eternatus, 1), // Eternamax
        ((ushort)PKHeX.Core.Species.Aegislash, 1), // Blade Forme 
        ((ushort)PKHeX.Core.Species.Aegislash, 2), // Shield Forme
        ((ushort)PKHeX.Core.Species.Cherrim, 1), // Overcast
        ((ushort)PKHeX.Core.Species.Cherrim, 2), // Sunshine
        ((ushort)PKHeX.Core.Species.Darmanitan, 3), // Galar Zen Mode
        ((ushort)PKHeX.Core.Species.Zamazenta, 1), // Crowned Shield
        ((ushort)PKHeX.Core.Species.Zacian, 1), // Crowned Sword
        ((ushort)PKHeX.Core.Species.Eiscue, 1), // Ice Face
        ((ushort)PKHeX.Core.Species.Cramorant, 1), // Gulping
        ((ushort)PKHeX.Core.Species.Cramorant, 2), // Gorging
        ((ushort)PKHeX.Core.Species.Castform, 1), // Sunny
        ((ushort)PKHeX.Core.Species.Castform, 2), // Rainy
        ((ushort)PKHeX.Core.Species.Castform, 3), // Snowy
        ((ushort)PKHeX.Core.Species.Arceus, 1), // Fighting
        ((ushort)PKHeX.Core.Species.Arceus, 10), // Water
        ((ushort)PKHeX.Core.Species.Arceus, 11), // Grass
        ((ushort)PKHeX.Core.Species.Arceus, 12), // Electric
        ((ushort)PKHeX.Core.Species.Arceus, 13), // Psychic
        ((ushort)PKHeX.Core.Species.Arceus, 14), // Ice
        ((ushort)PKHeX.Core.Species.Arceus, 15), // Dragon
        ((ushort)PKHeX.Core.Species.Arceus, 16), // Dark
        ((ushort)PKHeX.Core.Species.Arceus, 17), // Fairy
        ((ushort)PKHeX.Core.Species.Arceus, 2), // Flying
        ((ushort)PKHeX.Core.Species.Arceus, 3), // Poison
        ((ushort)PKHeX.Core.Species.Arceus, 4), // Ground
        ((ushort)PKHeX.Core.Species.Arceus, 5), // Rock
        ((ushort)PKHeX.Core.Species.Arceus, 6), // Bug
        ((ushort)PKHeX.Core.Species.Arceus, 7), // Ghost
        ((ushort)PKHeX.Core.Species.Arceus, 8), // Steel
        ((ushort)PKHeX.Core.Species.Arceus, 9), // Fire
        ((ushort)PKHeX.Core.Species.Eevee, 1), // Eevee-Starter
        ((ushort)PKHeX.Core.Species.Pikachu, 8), // Pikachu-Starter
        ((ushort)PKHeX.Core.Species.Arcanine, 2), // Arcanine-Lord
        ((ushort)PKHeX.Core.Species.Electrode, 2), // Electrode-Lord
        ((ushort)PKHeX.Core.Species.Avalugg, 2), // Avalugg-Lord
        ((ushort)PKHeX.Core.Species.Kleavor, 1), // Kleavor-Lord
        ((ushort)PKHeX.Core.Species.Meloetta, 1), // Meloetta-Pirouette
        ((ushort)PKHeX.Core.Species.Genesect, 1), // Genesect-Water
        ((ushort)PKHeX.Core.Species.Genesect, 2), // Genesect-Electric
        ((ushort)PKHeX.Core.Species.Genesect, 3), // Genesect-Fire
        ((ushort)PKHeX.Core.Species.Genesect, 4), // Genesect-Ice
        ((ushort)PKHeX.Core.Species.Greninja, 1), // Greninja-Ash
        ((ushort)PKHeX.Core.Species.Zygarde, 4), // Zygarde-Complete
        ((ushort)PKHeX.Core.Species.Magearna, 1), // Magearna-Original
        ((ushort)PKHeX.Core.Species.Magearna, 2), // Magearna
        ((ushort)PKHeX.Core.Species.Magearna, 3), // Magearna
        ((ushort)PKHeX.Core.Species.Morpeko, 1), // Morpeko-Hangry
        ((ushort)PKHeX.Core.Species.Koraidon, 1), // Koraidon-Limited
        ((ushort)PKHeX.Core.Species.Koraidon, 2), // Koraidon-Sprinting
        ((ushort)PKHeX.Core.Species.Koraidon, 3), // Koraidon-Swimming
        ((ushort)PKHeX.Core.Species.Koraidon, 4), // Koraidon-Gliding
        ((ushort)PKHeX.Core.Species.Miraidon, 1), // Miraidon-Low-Power
        ((ushort)PKHeX.Core.Species.Miraidon, 2), // Miraidon-Drive
        ((ushort)PKHeX.Core.Species.Miraidon, 3), // Miraidon-Aquatic
        ((ushort)PKHeX.Core.Species.Miraidon, 4), // Miraidon-Gliding
        ((ushort)PKHeX.Core.Species.Ogerpon, 1), // Ogerpon-Wellspring
        ((ushort)PKHeX.Core.Species.Ogerpon, 2), // Ogerpon-Hearthflame
        ((ushort)PKHeX.Core.Species.Ogerpon, 3), // Ogerpon-Cornerstone
        ((ushort)PKHeX.Core.Species.Ogerpon, 4), // Ogerpon-*Teal
        ((ushort)PKHeX.Core.Species.Ogerpon, 5), // Ogerpon-*Wellspring
        ((ushort)PKHeX.Core.Species.Ogerpon, 6), // Ogerpon-*Hearthflame
        ((ushort)PKHeX.Core.Species.Ogerpon, 7), // Ogerpon-*Cornerstone
        ((ushort)PKHeX.Core.Species.Terapagos, 1), // Terapagos-Terastal
        ((ushort)PKHeX.Core.Species.Terapagos, 2), // Terapagos-Stellar
        ((ushort)PKHeX.Core.Species.Mimikyu, 1), // Mimikyu-Busted
        ((ushort)PKHeX.Core.Species.Necrozma, 1), // Necrozma-Dusk
        ((ushort)PKHeX.Core.Species.Necrozma, 2), // Necrozma-Dawn
        ((ushort)PKHeX.Core.Species.Magearna, 1), // Magearna-Original        
        ((ushort)PKHeX.Core.Species.Palafin, 1), // Palafin-Hero
        ((ushort)PKHeX.Core.Species.Xerneas, 1), // Xerneas-Active
        ((ushort)PKHeX.Core.Species.Wishiwashi, 1), // Wishiwashi-School
        ((ushort)PKHeX.Core.Species.Silvally, 1), // Silvally-Fighting
        ((ushort)PKHeX.Core.Species.Silvally, 10), // Silvally-Water
        ((ushort)PKHeX.Core.Species.Silvally, 11), // Silvally-Grass
        ((ushort)PKHeX.Core.Species.Silvally, 12), // Silvally-Electric
        ((ushort)PKHeX.Core.Species.Silvally, 13), // Silvally-Psychic
        ((ushort)PKHeX.Core.Species.Silvally, 14), // Silvally-Ice
        ((ushort)PKHeX.Core.Species.Silvally, 15), // Silvally-Dragon
        ((ushort)PKHeX.Core.Species.Silvally, 16), // Silvally-Dark
        ((ushort)PKHeX.Core.Species.Silvally, 17), // Silvally-Fairy
        ((ushort)PKHeX.Core.Species.Silvally, 2), // Silvally-Flying
        ((ushort)PKHeX.Core.Species.Silvally, 3), // Silvally-Poison
        ((ushort)PKHeX.Core.Species.Silvally, 4), // Silvally-Ground
        ((ushort)PKHeX.Core.Species.Silvally, 5), // Silvally-Rock
        ((ushort)PKHeX.Core.Species.Silvally, 6), // Silvally-Bug
        ((ushort)PKHeX.Core.Species.Silvally, 7), // Silvally-Ghost
        ((ushort)PKHeX.Core.Species.Silvally, 8), // Silvally-Steel
        ((ushort)PKHeX.Core.Species.Silvally, 9), // Silvally-Fire
        ((ushort)PKHeX.Core.Species.Mimikyu, 1), // Mimikyu-Busted
        ((ushort)PKHeX.Core.Species.Morpeko, 1), // Morpeko-Hangry


        // Add more here as needed
    };

    public static bool IsForbidden(ushort species, byte form, string? formName = null)
    {
        if (List.Contains((species, form)))
            return true;

        // Generic blocks: Mega evolutions and Primal forms are not tradeable
        if (!string.IsNullOrWhiteSpace(formName))
        {
            if (formName.IndexOf("Mega", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (formName.IndexOf("Primal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    // Species where form 0 should not have any suffix even if a form name exists.
    private static readonly HashSet<ushort> NoSuffixForm0Species = new()
    {
        (ushort)PKHeX.Core.Species.Cramorant,
        (ushort)PKHeX.Core.Species.Zacian,
        (ushort)PKHeX.Core.Species.Zamazenta,
        (ushort)PKHeX.Core.Species.Palafin,
        (ushort)PKHeX.Core.Species.Wishiwashi,
        (ushort)PKHeX.Core.Species.Xerneas,
        (ushort)PKHeX.Core.Species.Ogerpon,
        (ushort)PKHeX.Core.Species.Koraidon,
        (ushort)PKHeX.Core.Species.Miraidon,
    };

    public static bool ShouldSuppressSuffix(ushort species, byte form, string? formName)
    {
        if (form != 0)
            return false;

        if (NoSuffixForm0Species.Contains(species))
            return true;

        if (string.IsNullOrWhiteSpace(formName))
            return false;

        // Generic base names that shouldn't show as suffix
        if (formName.Equals("Standard", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (formName.Equals("Hero of Many Battles", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (formName.Equals("Apex Build", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (formName.Equals("Ultimate Mode", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
