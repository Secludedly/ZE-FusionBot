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
        new Entry { Species = Species.Zygarde, MetLevel = 84, Form = 3, Friendship = 0, Location = 212, ForcedNature = Nature.Quiet, FixedIVs = new[] { 31,31,15,19,31,28 } }
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
    /// Gets all forced encounter entries (for slash command processing)
    /// </summary>
    public static Entry[] GetAllEntries()
    {
        return IVOnlyEntries.Concat(IVNatureEntries).Concat(StrictEntries).ToArray();
    }
}
