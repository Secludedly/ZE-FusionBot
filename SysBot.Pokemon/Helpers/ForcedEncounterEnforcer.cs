using PKHeX.Core;
using System;
using System.Linq;
using static ForcedEncounterEnforcer;

public static class ForcedEncounterEnforcer
{
    public class Entry
    {
        public Species Species { get; init; }
        public Nature? ForcedNature { get; init; }
        public int[]? FixedIVs { get; init; }

        // For strict static encounters (OT, Location, Nickname, Form)
        public string? OT { get; init; }
        public ushort? Location { get; init; }
        public string? Nickname { get; init; }
        public byte? Form { get; init; }

        // Additional
        public byte? MetLevel { get; init; }
        public byte? Friendship { get; init; }

        // Special handling flags
        public bool IsSpecialNatureHandling { get; init; }  // For Toxtricity-like Pokemon with restricted Natures
        public Nature[]? AllowedNatures { get; init; }      // List of legal actual Natures
        public bool RequiresRandomizedIVs { get; init; }    // For Magearna-like Pokemon with randomized IVs

        // Determines if this Entry is IV-only (ignore everything else)
        public bool IVOnly => ForcedNature == null;

        // Matches a PKM
        public bool Matches(PKM pkm)
        {
            if ((Species)pkm.Species != Species)
                return false;

            // If IV-only, we ignore OT/Nickname/Form/Location
            if (!IVOnly)
            {
                if (Form.HasValue && pkm.Form != Form.Value)
                    return false;
                if (!string.IsNullOrEmpty(OT) && pkm.OriginalTrainerName != OT)
                    return false;
                if (Location.HasValue && pkm.MetLocation != Location.Value)
                    return false;
                if (!string.IsNullOrEmpty(Nickname) && pkm.Nickname != Nickname)
                    return false;
            }

            return true;
        }
    }

    // -----------------------------
    // Categories of Entries
    // -----------------------------

    // IV-only Pokémon (none currently - all static encounters have forced natures in PLZA)
    private static readonly Entry[] IVOnlyEntries = new[]
    {
        new Entry { Species = Species.Latias, FixedIVs = new[] { 12, 7, 31, 16, 31, 31 } },
        new Entry { Species = Species.Latios, FixedIVs = new[] { 31, 31, 31, 16, 16, 7 } },
        new Entry { Species = Species.Terrakion, FixedIVs = new[] { 31, 31, 16, 31, 12, 7 } },
        new Entry { Species = Species.Virizion, FixedIVs = new[] { 16, 31, 12, 31, 31, 7 } },
        new Entry { Species = Species.Cobalion, FixedIVs = new[] { 12, 31, 31, 31, 16, 7 } },
        new Entry { Species = Species.Keldeo, FixedIVs = new[] { 7, 31, 31, 31, 16, 12 } },
        new Entry { Species = Species.Meloetta, FixedIVs = new[] { 7, 31, 12, 31, 31, 16 } },
        };

    // IV+Nature Pokémon - All PLZA static encounters with forced natures
    private static readonly Entry[] IVNatureEntries = new[]
    {
        // Legendary static encounters with both fixed IVs and forced natures
        new Entry { Species = Species.Zeraora, ForcedNature = Nature.Brave, FixedIVs = new[] { 31, 31, 19, 27, 31, 15 } },
        new Entry { Species = Species.Kyogre, ForcedNature = Nature.Modest, FixedIVs = new[] { 31, 31, 12, 22, 31, 28 } },
        new Entry { Species = Species.Rayquaza, ForcedNature = Nature.Brave, FixedIVs = new[] { 31, 31, 29, 20, 31, 29 } },
        new Entry { Species = Species.Heatran, ForcedNature = Nature.Bold, FixedIVs = new[] { 31, 31, 28, 8, 31, 25 } },
        new Entry { Species = Species.Groudon, ForcedNature = Nature.Impish, FixedIVs = new[] { 31, 31, 24, 25, 31, 18 } },
        new Entry { Species = Species.Darkrai, ForcedNature = Nature.Careful, FixedIVs = new[] { 31, 31, 18, 26, 31, 21 } },

        // Zygarde - All gennable forms (10%, 50%)
        // PKHeX form numbering as of Jan 2026: Form 2 = 10%, Form 3 = 50%, Form 4 = Complete
        new Entry { Species = Species.Zygarde, MetLevel = 84, Form = 2, Friendship = 0, Location = 212, ForcedNature = Nature.Quiet, FixedIVs = new[] { 31,31,15,19,31,28 } },
        new Entry { Species = Species.Zygarde, MetLevel = 84, Form = 3, Friendship = 0, Location = 212, ForcedNature = Nature.Quiet, FixedIVs = new[] { 31,31,15,19,31,28 } },

        // Toxtricity - Special PID/Nature/Shiny correlation
        // Only certain Natures are legal as actual Natures due to PID generation constraints
        // Other Natures can only be Stat Natures (minted)
        // Legal regular Natures: Jolly, Adamant, Brave, Docile, Hardy, Hasty, Impish, Lax, Naive, Naughty, Quirky, Rash, Sassy
        // This section is for Toxtricity ONLY
        new Entry
        {
            Species = Species.Toxtricity,
            IsSpecialNatureHandling = true,
            AllowedNatures = new[]
            {
                Nature.Jolly, Nature.Adamant, Nature.Brave, Nature.Docile, Nature.Hardy,
                Nature.Hasty, Nature.Impish, Nature.Lax, Nature.Naive, Nature.Naughty,
                Nature.Quirky, Nature.Rash, Nature.Sassy
            }
        },

        // Magearna - Always Modest Nature with 3 IVs at 31 and 3 IVs at 0 (randomized positions)
        // Shiny-locked static encounter
        new Entry
        {
            Species = Species.Magearna,
            ForcedNature = Nature.Modest,
            RequiresRandomizedIVs = true
        }
    };

    // Optional: full static entries with OT/Nickname/etc. (safety checks)
    private static readonly Entry[] StrictEntries = new[]
    {
        new Entry { Species = Species.Raichu, Form = 1, OT = "Griddella", Nickname = "Floffy", ForcedNature = Nature.Jolly, FixedIVs = new[] {20,20,20,20,20,20} },
        new Entry { Species = Species.Lucario, OT = "Korrina", ForcedNature = Nature.Hardy, FixedIVs = new[] {31,20,31,20,31,20} },
        new Entry { Species = Species.Riolu, OT = "Bond", Nickname = "Riolouie", ForcedNature = Nature.Rash, FixedIVs = new[] {15,31,15,31,31,15} },
        new Entry { Species = Species.Floette, Form = 5, OT = "AZ", ForcedNature = Nature.Modest },
        new Entry { Species = Species.Spewpa, Location = 30029, ForcedNature = Nature.Naive },
        new Entry { Species = Species.Chespin, Location = 30030, ForcedNature = Nature.Impish, FixedIVs = new[] {31,31,31,15,15,15} },
        new Entry { Species = Species.Fennekin, Location = 30030, ForcedNature = Nature.Lonely, FixedIVs = new[] {15,15,15,31,31,31} },
        new Entry { Species = Species.Froakie, Location = 69, ForcedNature = Nature.Sassy, FixedIVs = new[] {15,31,15,31,15,31} },
        new Entry { Species = Species.Heracross, OT = "Tracie", ForcedNature = Nature.Brave, FixedIVs = new[] {31,31,15,15,15,31} },
    };

    // -----------------------------
    // Public helpers
    // -----------------------------

    public static bool TryGetFixedIVs(PKM pkm, out int[] ivs)
    {
        var entry = IVOnlyEntries.Concat(IVNatureEntries).Concat(StrictEntries)
                                  .FirstOrDefault(e => e.Matches(pkm));

        if (entry != null && entry.FixedIVs != null)
        {
            ivs = entry.FixedIVs;
            return true;
        }

        ivs = Array.Empty<int>();
        return false;
    }

    public static bool TryGetForcedNature(PKM pkm, out Nature forcedNature)
    {
        var entry = IVNatureEntries.Concat(StrictEntries)
                                   .FirstOrDefault(e => e.Matches(pkm));

        if (entry != null && entry.ForcedNature.HasValue)
        {
            forcedNature = entry.ForcedNature.Value;
            return true;
        }

        forcedNature = Nature.Random;
        return false;
    }

    /// <summary>
    /// Checks if a Pokemon has special Nature handling (like Toxtricity) and returns a random legal Nature
    /// </summary>
    public static bool HasSpecialNatureHandling(PKM pkm, out Nature randomLegalNature)
    {
        var entry = IVNatureEntries.Concat(StrictEntries)
                                   .FirstOrDefault(e => e.Matches(pkm));

        if (entry != null && entry.IsSpecialNatureHandling && entry.AllowedNatures != null && entry.AllowedNatures.Length > 0)
        {
            // Return a random legal Nature from the allowed list
            var random = new Random();
            randomLegalNature = entry.AllowedNatures[random.Next(entry.AllowedNatures.Length)];
            return true;
        }

        randomLegalNature = Nature.Random;
        return false;
    }

    /// <summary>
    /// Checks if a requested Nature is legal for a Pokemon with special Nature handling
    /// </summary>
    public static bool IsNatureLegal(PKM pkm, Nature requestedNature)
    {
        var entry = IVNatureEntries.Concat(StrictEntries)
                                   .FirstOrDefault(e => e.Matches(pkm));

        if (entry != null && entry.IsSpecialNatureHandling && entry.AllowedNatures != null)
        {
            return entry.AllowedNatures.Contains(requestedNature);
        }

        // If no special handling, all Natures are legal
        return true;
    }

    /// <summary>
    /// Checks if a Pokemon requires randomized IVs (like Magearna) and returns the IVs
    /// </summary>
    public static bool RequiresRandomizedIVs(PKM pkm, out int[] randomizedIVs)
    {
        var entry = IVNatureEntries.Concat(StrictEntries)
                                   .FirstOrDefault(e => e.Matches(pkm));

        if (entry != null && entry.RequiresRandomizedIVs)
        {
            // Generate 3 IVs at 31 and 3 IVs at 0, randomized positions
            randomizedIVs = GenerateRandomizedIVs();
            return true;
        }

        randomizedIVs = Array.Empty<int>();
        return false;
    }

    /// <summary>
    /// Generates randomized IVs: 3 IVs at 31 and 3 IVs at 0 (randomized positions)
    /// </summary>
    private static int[] GenerateRandomizedIVs()
    {
        var random = new Random();
        var ivs = new int[6];

        // Create a list of indices (0-5)
        var indices = Enumerable.Range(0, 6).ToList();

        // Shuffle the indices
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        // Set first 3 shuffled positions to 31
        for (int i = 0; i < 3; i++)
        {
            ivs[indices[i]] = 31;
        }

        // Set last 3 shuffled positions to 0
        for (int i = 3; i < 6; i++)
        {
            ivs[indices[i]] = 0;
        }

        return ivs;
    }

    /// <summary>
    /// Gets all forced encounter entries (for slash command processing)
    /// </summary>
    public static Entry[] GetAllEntries()
    {
        return IVOnlyEntries.Concat(IVNatureEntries).Concat(StrictEntries).ToArray();
    }
}
