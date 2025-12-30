using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using PKHeX.Core;

namespace SysBot.Pokemon.Discord.Helpers
{
    /// <summary>
    /// Converts Batch Commands to standard Showdown format in a modular, easily extendable way.
    /// </summary>
    public static class BatchCommandNormalizer
    {
        // RNG for all handlers (avoid creating new ones in loops)
        private static readonly Random Rng = new();

        //////////////////////////////////// ALIAS & COMMAND MAPPINGS //////////////////////////////////////

        // Maps common aliases to their normalized command keys
        // New Showdown Set key → Original Batch Command key
        private static readonly Dictionary<string, string> BatchCommandAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Size", "Scale" },
            { "Weight", "WeightScalar" },
            { "Height", "HeightScalar" },
            { "Met Date", "MetDate" },
            { "Met Location", "MetLocation" },
            { "Game", "Version" },
            { "Hypertrain", "HyperTrainFlags" },
            { "Moves", "Moves" },
            { "Relearn Moves", "RelearnMoves" },
            { "Met Level", "MetLevel" },
            { "Ribbons", "Ribbons" },
            { "Mark", "Mark" },
            { "Ribbon", "Ribbon" },
            { "GVs", "GVs" },
            { "Set EVs", "SetEVs" },
            { "Set IVs", "SetIVs" },
            { "OT Friendship", "OriginalTrainerFriendship" },
            { "HT Friendship", "HandlingTrainerFriendship" },
            { "Characteristic", "Characteristic" },
            { "Stat Nature", "StatNature" }
        };

        private static readonly HashSet<string> EqualCommandKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Generation", "Gen", "WasEgg", "Hatched", "Version"
        };

        // Core mapping of functions for each key
        // Batch Command key → Handler function
        private static readonly Dictionary<string, Func<string, string>> CommandProcessors =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Scale", ProcessScale },
                { "WeightScalar", ProcessWeightScalar },
                { "HeightScalar", ProcessHeightScalar },
                { "OriginalTrainerFriendship", ProcessFriendshipOT },
                { "HandlingTrainerFriendship", ProcessFriendshipHT },
                { "MetDate", ProcessMetDate },
                { "Version", ProcessVersion },
                { "MetLocation", ProcessMetLocation },
                { "HyperTrainFlags", ProcessHyperTrainFlags },
                { "Moves", ProcessMoves },
                { "RelearnMoves", ProcessRelearnMoves },
                { "Ribbons", ProcessRibbons },
                { "Mark", ProcessMark },
                { "Ribbon", ProcessRibbon },
                { "GVs", ProcessGVs },
                { "SetEVs", ProcessEVs },
                { "SetIVs", ProcessIVs },
                { "Characteristic", ProcessCharacteristic },
                { "HT", ProcessHyperTrain },
                { "MetLevel", ProcessMetLevel },
                { "Markings", ProcessMarkings },
                { "StatNature", ProcessStatNature }
            };

        //////////////////////////////////// NEW COMMAND DICTIONARIES //////////////////////////////////////

        // Size keywords
        private static readonly Dictionary<string, (int Min, int Max)> SizeKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "XXXS", (0, 0) }, { "XXS", (1, 30) }, { "XS", (31, 60) }, { "S", (61, 100) },
            { "AV", (101, 160) }, { "L", (161, 195) }, { "XL", (196, 241) }, { "XXL", (242, 254) }, { "XXXL", (255, 255) }
        };

        // Weight/Height scalar keywords
        private static readonly Dictionary<string, (int Min, int Max)> ScalarKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "XS", (0, 15) }, { "S", (16, 47) }, { "AV", (48, 207) }, { "L", (208, 239) }, { "XL", (240, 255) }
        };

        // Accepted date formats for Met Date parsing
        private static readonly string[] AcceptedDateFormats =
        {
          "yyyyMMdd",
          "MMddyyyy",
          // slash-separated (variable digits)
          "M/d/yyyy", "MM/dd/yyyy",
          "yyyy/M/d", "yyyy/MM/dd",
          // dash-separated (variable digits)
          "M-d-yyyy", "MM-dd-yyyy",
          "yyyy-M-d", "yyyy-MM-dd"
        };

        // Game name/abbreviation to internal version ID mapping
        private static readonly Dictionary<string, int> GameKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Red", 35 }, { "Blue", 36 }, { "Green", 36 }, { "BlueJP", 37 }, { "Yellow", 38 },
            { "Gold", 39 }, { "Silver", 40 }, { "Crystal", 41 }, { "Sapphire", 1 }, { "Ruby", 2 },
            { "Emerald", 3 }, { "FR", 4 }, { "Fire Red", 4 }, { "LG", 5 }, { "Leaf Green", 5 },
            { "Colosseum", 15 }, { "XD", 15 }, { "HG", 7 }, { "Heart Gold", 7 }, { "SS", 8 }, { "Soul Silver", 8 },
            { "Diamond", 10 }, { "D", 10 }, { "Pearl", 11 }, { "P", 11 }, { "Platinum", 12 }, { "Pt", 12 },
            { "B", 21 }, { "Black", 21 }, { "B2", 23 }, { "Black 2", 23 }, { "W", 20 }, { "White", 20 },
            { "W2", 22 }, { "White 2", 22 }, { "X", 24 }, { "Y", 25 }, { "AS", 26 }, { "Alpha Sapphire", 26 },
            { "OR", 27 }, { "Omega Ruby", 27 }, { "S", 30 }, { "Sun", 30 }, { "M", 31 }, { "Moon", 31 },
            { "US", 32 }, { "Ultra Sun", 32 }, { "UM", 33 }, { "Ultra Moon", 33 },
            { "Pikachu", 42 }, { "LetsGoPikachu", 42 }, { "LGP", 42 }, { "Eevee", 43 }, { "LetsGoEevee", 43 }, { "LGE", 43 },
            { "GO", 34 }, { "Pokemon GO", 34 }, { "SW", 44 }, { "Sword", 44 }, { "SH", 45 }, { "Shield", 45 },
            { "PLA", 47 }, { "Legends Arceus", 47 }, { "BD", 48 }, { "Brilliant Diamond", 48 },
            { "SP", 49 }, { "Shining Pearl", 49 }, { "Scarlet", 50 }, { "SL", 50 }, { "Violet", 51 }, { "VL", 51 },
            { "PLZA", 52 }, { "LZA", 52 }, { "ZA", 52 }
        };

        // Characteristic to IV spread mapping
        private static readonly Dictionary<string, int[]> CharacteristicIVs =
        new(StringComparer.OrdinalIgnoreCase)
    {
    // HP-based
    { "Likes to eat",              new[] {30, 8, 13, 18, 23, 25} },
    { "Takes plenty of siestas",   new[] {31, 6, 26, 22, 10, 0} },
    { "Scatters things often",     new[] {28, 8, 28, 12, 9, 19} },
    { "Likes to relax",            new[] {29, 16, 3, 7, 26, 13} },
    { "Nods off a lot",            new[] {27, 0, 13, 27, 27, 8} },

    // Attack-based
    { "Proud of its power",      new[] {18, 30, 10, 11, 26, 3} },
    { "Likes to thrash about",   new[] {10, 31, 0, 3, 12, 0} },
    { "A little quick tempered", new[] {25, 27, 9, 7, 8, 8} },
    { "Quick tempered",          new[] {0, 29, 6, 23, 4, 17} },
    { "Likes to fight",          new[] {25, 28, 11, 8, 9, 18} },

    // Defense-based
    { "Sturdy body",           new[] {15, 24, 30, 5, 24, 29} },
    { "Capable of taking hits",new[] {6, 0, 21, 2, 18, 3} },
    { "Highly persistent",     new[] {4, 21, 27, 9, 21, 18} },
    { "Good endurance",        new[] {19, 2, 23, 2, 6, 4} },
    { "Good perseverance",     new[] {26, 16, 29, 0, 20, 22} },

    // Sp.Atk-based
    { "Highly curious",        new[] {9, 6, 21, 30, 10, 28} },
    { "Mischievous",           new[] {7, 20, 0, 31, 5, 17} },
    { "Thoroughly cunning",    new[] {5, 4, 20, 27, 12, 26} },
    { "Often lost in thought", new[] {8, 3, 1, 23, 19, 14} },
    { "Very finicky",          new[] {9, 1, 0, 24, 21, 12} },

    // Sp.Def-based
    { "Strong willed",         new[] {14, 6, 29, 16, 30, 0} },
    { "Somewhat vain",         new[] {10, 5, 10, 15, 26, 15} },
    { "Strongly defiant",      new[] {10, 10, 12, 3, 12, 10} },
    { "Hates to lose",         new[] {3, 8, 13, 18, 23, 2} },
    { "Somewhat stubborn",     new[] {4, 9, 14, 19, 24, 15} },

    // Speed-based
    { "Likes to run",            new[] {2, 7, 12, 17, 22, 30} },
    { "Alert to sounds",         new[] {31, 31, 31, 31, 31, 31} },
    { "Impetuous and silly",     new[] {2, 7, 12, 17, 22, 27} },
    { "Somewhat of a clown",     new[] {3, 8, 13, 18, 23, 28} },
    { "Quick to flee",           new[] {4, 9, 14, 19, 24, 29} },
    };

        // Mark Name → Batch Command Key mapping
        private static readonly Dictionary<string, string> MarkingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
    { "Diamond", "MarkingDiamond" },
    { "Heart", "MarkingHeart" },
    { "Square", "MarkingSquare" },
    { "Star", "MarkingStar" },
    { "Triangle", "MarkingTriangle" },
    { "Circle", "MarkingCircle" }
    };

        // Mark Color → Batch Command Value mapping
        private static readonly Dictionary<string, int> MarkingColors = new(StringComparer.OrdinalIgnoreCase)
    {
    { "No", 0 },
    { "Blue", 1 },
    { "Red", 2 }
    };

        // Alcremie topping keywords
        private static readonly Dictionary<string, int> AlcremieFormArguments = new(StringComparer.OrdinalIgnoreCase)
    {
    { "Strawberry", 0 },
    { "Berry", 1 },
    { "Love", 2 },
    { "Star", 3 },
    { "Clover", 4 },
    { "Flower", 5 },
    { "Ribbon", 6 }
    };

        private static readonly HashSet<string> ValidNatures = new(StringComparer.OrdinalIgnoreCase)
{
    "Hardy", "Lonely", "Brave", "Adamant", "Naughty", "Bold", "Docile", "Relaxed",
    "Impish", "Lax", "Timid", "Hasty", "Serious", "Jolly", "Naive", "Modest",
    "Mild", "Quiet", "Bashful", "Rash", "Calm", "Gentle", "Sassy", "Careful",
    "Quirky", "Random"
};

        // Met Location Name → ID mappings for each game
        // Game mode is set at runtime via CurrentGameMode property
        public static ProgramMode CurrentGameMode { get; set; } = ProgramMode.None;

        // Lazy-loaded location dictionaries for each game
        private static Dictionary<string, int>? _swshLocations;
        private static Dictionary<string, int>? _plaLocations;
        private static Dictionary<string, int>? _bdspLocations;
        private static Dictionary<string, int>? _svLocations;
        private static Dictionary<string, int>? _plzaLocations;

        private static Dictionary<string, int> GetLocationDictionary(ProgramMode mode)
        {
            return mode switch
            {
                ProgramMode.SWSH => _swshLocations ??= InitializeSWSHLocations(),
                ProgramMode.LA => _plaLocations ??= InitializePLALocations(),
                ProgramMode.BDSP => _bdspLocations ??= InitializeBDSPLocations(),
                ProgramMode.SV => _svLocations ??= InitializeSVLocations(),
                ProgramMode.PLZA => _plzaLocations ??= InitializePLZALocations(),
                _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            };
        }

        //////////////////////////////////// MAIN ENTRY //////////////////////////////////////

        public static string NormalizeBatchCommands(string input)
        {
            var lines = input.Split('\n');
            var processed = new List<string>();
            bool hasCharacteristic = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!line.Contains(":"))
                {
                    // Alcremie forms can now add a topping to the flavor without batch command
                    // For example, it accepts: Alcremie-Caramel-Swirl-Ribbon
                    // Just affix the topping name to the end of Alcremie's name after its flavor
                    // This code injects FormArgument/Topping for Alcremie based on Showdown Format nickname
                    if (line.StartsWith("Alcremie-", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('-', StringSplitOptions.RemoveEmptyEntries);
                        var lastPart = parts.Last();

                        if (AlcremieFormArguments.TryGetValue(lastPart, out int arg))
                        {
                            var speciesName = string.Join("-", parts.Take(parts.Length - 1));
                            processed.Add(speciesName);
                            processed.Add($".FormArgument={arg}");
                            continue;
                        }
                    }

                    processed.Add(line);
                    continue;
                }

                if (!TrySplitCommand(line, out var key, out var value))
                    continue;

                if (BatchCommandAliasMap.TryGetValue(key, out var normalizedKey))
                    key = normalizedKey;

                // characteristic overrides IVs
                if (hasCharacteristic &&
                    (key.Equals("SetIVs", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("IVs", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (CommandProcessors.TryGetValue(key, out var processor))
                {
                    var processedLine = processor(value);
                    if (!string.IsNullOrWhiteSpace(processedLine))
                        processed.Add(processedLine);
                }
                else if (EqualCommandKeys.Contains(key))
                {
                    processed.Add($"~={key}={value}");
                }
                else
                {
                    processed.Add($"{key}: {value}");
                }
            }

            // Always return at the end
            return string.Join("\n", processed);
        }

        //////////////////////////////////// HANDLER METHODS //////////////////////////////////////

        // .Scale= → Size: or Scale:
        // Value is a size keyword or number 
        private static string ProcessScale(string val) =>
            SizeKeywords.TryGetValue(val, out var range)
                ? $".Scale={Rng.Next(range.Min, range.Max + 1)}"
                : $".Scale={val}";

        // .WeightScalar= → Weight:
        // Value is a size keyword or number 
        private static string ProcessWeightScalar(string val) =>
            ScalarKeywords.TryGetValue(val, out var range)
                ? $".WeightScalar={Rng.Next(range.Min, range.Max + 1)}"
                : $".WeightScalar={val}";

        // HeightScalar= → Height:
        // Value is a size keyword or number
        private static string ProcessHeightScalar(string val) =>
            ScalarKeywords.TryGetValue(val, out var range)
                ? $".HeightScalar={Rng.Next(range.Min, range.Max + 1)}"
                : $".HeightScalar={val}";

        // .OriginalTrainerFriendship= → OT Friendship:
        // Value is between 1-255
        private static string ProcessFriendshipOT(string val) =>
            int.TryParse(val, out int f) && f >= 1 && f <= 255
                ? $".OriginalTrainerFriendship={f}"
                : string.Empty;

        // .HandlingTrainerFriendship= → HT Friendship:
        // Value is between 1-255
        private static string ProcessFriendshipHT(string val) =>
            int.TryParse(val, out int f) && f >= 1 && f <= 255
                ? $".HandlingTrainerFriendship={f}"
                : string.Empty;

        // .MetDate= → Met Date:
        // See the AcceptedDateFormats dictionary
        private static string ProcessMetDate(string val)
        {
            val = val.Trim();
            if (TryParseFlexibleDate(val, out var formatted))
                return $".MetDate={formatted}";

            var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return $".MetDate={today}";
        }

        // ~=Version= → Game: or Version:
        // Value is game name or game abbreviation
        private static string ProcessVersion(string val) =>
            GameKeywords.TryGetValue(val.Replace(" ", ""), out int ver)
                ? $"~=Version={ver}"
                : string.Empty;

        // .MetLocation= → Met Location:
        // Value can be a numeric ID or location name
        private static string ProcessMetLocation(string val)
        {
            val = val.Trim();

            // If it's already a number, use it directly
            if (int.TryParse(val, out int locationId))
                return $".MetLocation={locationId}";

            // Try to look up the location name
            var locationDict = GetLocationDictionary(CurrentGameMode);
            if (locationDict.TryGetValue(val, out int id))
                return $".MetLocation={id}";

            // If not found, return the original value (will likely fail validation, but that's okay)
            return $".MetLocation={val}";
        }

        // .HyperTrainFlags= → HyperTrain: or HyperTrainFlags:
        // Value is "true" or "false"
        private static string ProcessHyperTrainFlags(string val) =>
            TryParseBoolean(val, out var b)
                ? $".HyperTrainFlags={b}"
                : string.Empty;

        // .StatNature= → Stat Nature:
        // Value is a Nature enum name or "Random"
        private static string ProcessStatNature(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return string.Empty;

            var trimmed = val.Trim();

            // Match case-insensitively against valid natures
            var matchedNature = ValidNatures.FirstOrDefault(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (matchedNature == null)
                return string.Empty;

            // Return exact batch command Showdown expects
            return $".StatNature={matchedNature}";
        }

        // .Moves= → Moves:
        // Only accepted options are "Random" for randomized moves
        private static string ProcessMoves(string val) =>
            val.Equals("Random", StringComparison.OrdinalIgnoreCase)
                ? ".Moves=$suggest"
                : $".Moves={val}";

        // .MetLevel= → Met Level:
        // Value must be between 1–100 (game cap for levels)
        private static string ProcessMetLevel(string val) =>
            int.TryParse(val, out int level) && level >= 1 && level <= 100
                ? $".MetLevel={level}"
                : string.Empty;

        // .RelearnMoves= → Relearn Moves:
        // Only accepted options are "All" or "None"
        private static string ProcessRelearnMoves(string value)
        {
            // trim and normalize spacing
            value = value.Trim();

            if (value.Equals("All", StringComparison.OrdinalIgnoreCase))
                return ".RelearnMoves=$suggestAll";

            if (value.Equals("None", StringComparison.OrdinalIgnoreCase))
                return ".RelearnMoves=$suggestNone";

            // fallback in case user typed something weird
            return $".RelearnMoves={value}";
        }

        // .Ribbons= → Ribbons:
        // Only accepted options are "All" or "None"
        private static string ProcessRibbons(string val) =>
            val.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? ".Ribbons=$suggestAll"
                : $".Ribbons={val}";

        // .RibbonMark[mark]=True → Mark:
        // Value is a mark name like "BestFriends," without spaces
        private static string ProcessMark(string val) =>
            $".RibbonMark{val.Replace(" ", "")}=True";

        // .Ribbon[name]= → Ribbon:
        // Value is a Ribbon name like "BattleChampion," without using spaces
        private static string ProcessRibbon(string val) =>
            $".Ribbon{val.Replace(" ", "")}=True";

        // .[MarkingKey]=[ColorVal] → Mark:
        // Value is a series of marking assignments like "Markings: Diamond=Red / Heart=Blue / Circle=No"
        // Any Markings that are not specified or include "No" are ignored
        private static string ProcessMarkings(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return string.Empty;

            var lines = new List<string>();

            var parts = val.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var split = part.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (split.Length != 2)
                    continue;

                var markName = split[0].Trim();
                var colorName = split[1].Trim();

                if (!MarkingKeys.TryGetValue(markName, out var markKey))
                    continue;

                if (!MarkingColors.TryGetValue(colorName, out var colorVal))
                    continue;

                lines.Add($".{markKey}={colorVal}");
            }

            return string.Join('\n', lines);
        }

        // Creates a ".Characteristic=" batch command that can be written as "Characteristic:"
        // Value is a characteristic wordage like "Likes to eat"
        private static string ProcessCharacteristic(string val)
        {
            if (!CharacteristicIVs.TryGetValue(val, out var spread))
                return string.Empty;

            return FormatIVs(spread);
        }

        // .HT_[STAT]= → HT:
        // "HT: HP / Atk / Def" to enable Hyper Training for those stats only
        private static readonly string[] StatKeys = { "HP", "ATK", "DEF", "SPA", "SPD", "SPE" };

        private static string ProcessHyperTrain(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return string.Empty;

            var requestedStats = val.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim().ToUpper())
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var htLines = new List<string>();
            foreach (var stat in StatKeys)
            {
                if (requestedStats.Contains(stat))
                    htLines.Add($".HT_{stat}=True");
            }

            return string.Join('\n', htLines);
        }

        // Creates a ".SetEVs=" batch command that can be written as "Set EVs:" that accepts "Random" or "Suggest" as special values
        // "Set EVs: Random" value randomizes EVs across all stats
        // "Set EVs: Suggest" generates a suggested EV spread like 252/252/4
        private static readonly string[] EvStats = { "HP", "ATK", "DEF", "SPA", "SPD", "SPE" };
        private static string ProcessEVs(string val)
        {
            if (val.Equals("Random", StringComparison.OrdinalIgnoreCase))
            {
                return GenerateRandomEVs();
            }
            else if (val.Equals("Suggest", StringComparison.OrdinalIgnoreCase))
            {
                return GenerateSuggestedEVs();
            }
            else
            {
                return $".SetEVs={val}";
            }
        }
        private static string GenerateRandomEVs()
        {
            int maxTotal = 510;
            int maxPerStat = 252;
            int[] evs = new int[6];

            int remaining = maxTotal;

            for (int i = 0; i < 6; i++)
            {
                int maxForStat = Math.Min(maxPerStat, remaining);
                evs[i] = Rng.Next(0, maxForStat + 1);
                remaining -= evs[i];
            }

            while (remaining > 0)
            {
                int idx = Rng.Next(0, 6);
                if (evs[idx] < maxPerStat)
                {
                    evs[idx]++;
                    remaining--;
                }
            }

            return FormatEVs(evs);
        }
        private static string GenerateSuggestedEVs()
        {
            int[] evs = new int[6];

            var indices = Enumerable.Range(0, 6).OrderBy(_ => Rng.Next()).Take(3).ToArray();

            evs[indices[0]] = 252;
            evs[indices[1]] = 252;
            evs[indices[2]] = 4;

            return FormatEVs(evs);
        }

        // Creates a ".SetIVs=" batch command that can be written as "Set IVs:" that accepts "Random" or "1IV", "2IV", "3IV", "4IV", "5IV", "6IV"
        // "Set IVs: Random" randomizes IVs across all stats
        // "Set IVs: 1IV" sets one random stat to 31 IVs, the rest are random
        // "Set IVs: 6IV" sets all stats to 31 IVs
        private static readonly string[] IvStats = { "HP", "ATK", "DEF", "SPA", "SPD", "SPE" };
        private static string ProcessIVs(string val)
        {
            val = val.Trim();

            if (val.Equals("Random", StringComparison.OrdinalIgnoreCase))
                return GenerateRandomIVs();

            var presetMatch = Regex.Match(val, @"^(\d)IV$", RegexOptions.IgnoreCase);
            if (presetMatch.Success)
            {
                int ivCount = int.Parse(presetMatch.Groups[1].Value);
                if (ivCount >= 1 && ivCount <= 6)
                    return GeneratePresetIVs(ivCount);
            }

            return $".SetIVs={val}";
        }
        private static string GenerateRandomIVs()
        {
            int maxPerStat = 31;
            int[] ivs = new int[6];
            for (int i = 0; i < 6; i++)
                ivs[i] = Rng.Next(0, maxPerStat + 1);
            return FormatIVs(ivs);
        }
        private static string GeneratePresetIVs(int countAt31)
        {
            int maxPerStat = 31;
            int[] ivs = new int[6];

            var indicesAt31 = Enumerable.Range(0, 6).OrderBy(_ => Rng.Next()).Take(countAt31).ToArray();

            foreach (var idx in indicesAt31)
                ivs[idx] = maxPerStat;

            for (int i = 0; i < 6; i++)
            {
                if (!indicesAt31.Contains(i))
                    ivs[i] = Rng.Next(0, maxPerStat + 1);
            }

            return FormatIVs(ivs);
        }

        // .GV_[STAT]= → GVs:
        // GVs now follow the same format as EVs and IVs, like below
        // GVs: 7 HP / 7 Atk / 7 Def / 7 SpA / 7 SpD / 7 Spe
        private static string ProcessGVs(string val)
        {
            var statMatches = Regex.Matches(val, @"(\d+)\s*(HP|Atk|Def|SpA|SpD|Spe)", RegexOptions.IgnoreCase);
            return string.Join("\n", statMatches.Select(stat =>
            {
                var statVal = stat.Groups[1].Value;
                var statKey = stat.Groups[2].Value.ToUpper();
                return statKey switch
                {
                    "HP" => $".GV_HP={statVal}",
                    "ATK" => $".GV_ATK={statVal}",
                    "DEF" => $".GV_DEF={statVal}",
                    "SPA" => $".GV_SPA={statVal}",
                    "SPD" => $".GV_SPD={statVal}",
                    "SPE" => $".GV_SPE={statVal}",
                    _ => string.Empty
                };
            }));
        }

        //////////////////////////////////// HELPERS //////////////////////////////////////

        // Splits a line into key and value parts
        private static bool TrySplitCommand(string line, out string key, out string value)
        {
            key = value = string.Empty;
            var match = Regex.Match(line.Trim(), @"^([\w\s]+)\s*:\s*(.*)$");
            if (!match.Success) return false;

            key = match.Groups[1].Value.Trim();
            value = match.Groups[2].Value.Trim();
            return true;
        }

        // Update the parser to use InvariantCulture and allow spaces:
        private static bool TryParseFlexibleDate(string input, out string formatted)
        {
            input = input.Trim();

            if (DateTime.TryParseExact(
                    input,
                    AcceptedDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var date))
            {
                formatted = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                return true;
            }

            formatted = string.Empty;
            return false;
        }

        // Parses "true"/"false" (case insensitive) values to 1/0 respectively
        private static bool TryParseBoolean(string input, out int result)
        {
            if (input.Equals("true", StringComparison.OrdinalIgnoreCase)) { result = 1; return true; }
            if (input.Equals("false", StringComparison.OrdinalIgnoreCase)) { result = 0; return true; }
            result = -1;
            return false;
        }

        // Formats EVs into batch command lines
        private static string FormatEVs(int[] evs)
        {
            var evLines = new List<string>(6);
            for (int i = 0; i < EvStats.Length; i++)
                evLines.Add($".EV_{EvStats[i]}={evs[i]}");
            return string.Join('\n', evLines);
        }

        // Formats IVs into batch command lines
        private static string FormatIVs(int[] ivs)
        {
            var ivLines = new List<string>(6);
            for (int i = 0; i < IvStats.Length; i++)
                ivLines.Add($".IV_{IvStats[i]}={ivs[i]}");
            return string.Join('\n', ivLines);
        }

        //////////////////////////////////// MET LOCATION INITIALIZERS //////////////////////////////////////

        private static Dictionary<string, int> InitializePLZALocations()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mystery Zone", 2 }, { "Faraway place", 4 }, { "Vert District", 6 }, { "Bleu District", 7 },
                { "Magenta District", 8 }, { "Rouge District", 9 }, { "Jaune District", 10 }, { "Centrico Plaza", 11 },
                { "Vernal Avenue", 12 }, { "Estival Avenue", 13 }, { "Autumnal Avenue", 14 }, { "Hibernal Avenue", 15 },
                { "Saison Canal", 16 }, { "Vert Street", 17 }, { "South Boulevard", 19 }, { "Vert Sector 1", 21 },
                { "Vert Sector 3", 22 }, { "Coulant Waterway", 23 }, { "Vert Sector 6", 24 }, { "Vert Sector 7", 25 },
                { "Station Front", 26 }, { "Vert Sector 2", 27 }, { "Vert Sector 4", 28 }, { "Vert Sector 5", 29 },
                { "Vert Sector 8", 30 }, { "Vert Sector 9", 31 }, { "Vert Plaza", 32 }, { "Bleu Street", 33 },
                { "Bleu Sector 1", 37 }, { "Bleu Sector 3", 38 }, { "Aymlis Park", 39 }, { "Bleu Sector 5", 40 },
                { "Bleu Sector 7", 41 }, { "Bleu Sector 2", 42 }, { "Bleu Sector 4", 43 }, { "Espace Vide", 44 },
                { "Bleu Sector 6", 45 }, { "Bleu Sector 8", 46 }, { "Bleu Sector 9", 47 }, { "Bleu Sector 10", 48 },
                { "Bleu Plaza", 49 }, { "Magenta Street", 50 }, { "North Boulevard", 53 }, { "Magenta Sector 1", 54 },
                { "Magenta Sector 3", 55 }, { "Magenta Sector 5", 56 }, { "Magenta Sector 6", 57 }, { "Electrical Substation", 58 },
                { "Magenta Sector 2", 59 }, { "Magenta Sector 4", 60 }, { "Magenta Sector 7", 61 }, { "Quasartico Inc.", 62 },
                { "Magenta Sector 8", 63 }, { "Magenta Sector 9", 64 }, { "Magenta Plaza", 65 }, { "Rouge Street", 66 },
                { "Rouge Sector 1", 70 }, { "Rouge Sector 3", 71 }, { "Rouge Sector 4", 72 }, { "Rouge Sector 5", 73 },
                { "Rouge Sector 7", 74 }, { "Rouge Sector 2", 75 }, { "Académie Étoile", 76 }, { "Rouge Sector 6", 77 },
                { "Dormez Bien Cemetery", 78 }, { "Rouge Sector 8", 79 }, { "Rouge Plaza", 80 }, { "Jaune Street", 81 },
                { "Jaune Sector 1", 85 }, { "Jaune Sector 3", 86 }, { "Jaune Sector 4", 87 }, { "Jaune Sector 5", 88 },
                { "Jaune Sector 7", 89 }, { "Jaune Sector 8", 90 }, { "Jaune Sector 2", 91 }, { "Saison Canalside", 92 },
                { "Jaune Sector 6", 93 }, { "Jaune Sector 9", 94 }, { "Jaune Sector 10", 95 }, { "Jaune Sector 11", 96 },
                { "Jaune Sector 12", 97 }, { "Jaune Plaza", 98 }, { "Promenade du Vent", 99 }, { "Lumiose Museum", 100 },
                { "Vert Pokémon Center", 101 }, { "Hotel Z", 102 }, { "Pokémon Research Lab", 103 }, { "Prism Tower", 104 },
                { "Looker Bureau", 105 }, { "Racine Construction", 107 }, { "Gare de Lumiose", 109 }, { "Rust Syndicate Office", 111 },
                { "Bleu Pokémon Center", 112 }, { "Vernal Pokémon Center", 113 }, { "Magenta Pokémon Center", 116 },
                { "Magenta Plaza Pokémon Center", 117 }, { "Hotel Richissime", 119 }, { "Rouge Pokémon Center", 120 },
                { "Centrico Pokémon Center", 121 }, { "Justice Dojo", 123 }, { "Jaune Pokémon Center", 124 },
                { "Hibernal Pokémon Center", 125 }, { "Hair Salon", 126 }, { "Stone Emporium", 127 }, { "Poké Ball Boutique", 128 },
                { "Changing Gears", 129 }, { "Boutique Couture", 130 }, { "Restaurant Le Nah", 131 }, { "Café Cyclone", 132 },
                { "Café Classe", 133 }, { "Café Introversion", 134 }, { "Empty Room (Abandoned Building)", 135 }, { "Sewer Entrance", 136 },
                { "Fresh Fits", 137 }, { "BRAVELY", 138 }, { "Dessert du Moment", 139 }, { "Marché Bleu", 140 },
                { "In the Zone", 141 }, { "Kickspin", 142 }, { "Triathlon Bleu", 143 }, { "DEFOG Eyewear", 144 },
                { "Équipement", 145 }, { "Coiffure Clips", 146 }, { "Café Woof", 147 }, { "Café Soleil", 148 },
                { "Shutterbug Café", 149 }, { "Lumiose Sewers", 150 }, { "Café Pokémon-Amie", 151 }, { "Café Rouleau", 152 },
                { "Café Gallant", 153 }, { "Café Triste", 154 }, { "The Usual", 155 }, { "Énergie", 156 },
                { "Le Passe-temps", 157 }, { "Porte-Chance", 158 }, { "Pinceau", 159 }, { "Naptime", 160 },
                { "Soil and Sneaks", 161 }, { "Marquage", 162 }, { "Glammor Girli", 163 }, { "Glammor Pretti", 164 },
                { "Glammor Sporti", 165 }, { "NIGHTSIDE", 166 }, { "Bundle Up", 167 }, { "Midnight Rite", 168 },
                { "Mode Mature", 169 }, { "Wisp", 170 }, { "Mode Magnifique", 171 }, { "FILMFAN", 172 },
                { "DENSOKU Lumiose", 173 }, { "Les Chaussures", 174 }, { "Atelier Heads", 175 }, { "Glammor Cuti", 176 },
                { "Kikonashi", 177 }, { "SUBATOMIC", 178 }, { "SUBATOMIC 4", 179 }, { "Masterpiece", 180 },
                { "Le Pays des Vêtements", 181 }, { "Triathlon Rouge", 182 }, { "Le Pays des Pieds", 183 }, { "La Tornade", 184 },
                { "Restaurant Le Yeah", 185 }, { "Sushi High Roller", 186 }, { "Restaurant Le Wow", 187 }, { "Café Kizuna", 189 },
                { "Café Ultimo", 190 }, { "Café Action!", 191 }, { "Café Bataille", 192 }, { "Wild Zone 17", 193 },
                { "Wild Zone 1", 194 }, { "Wild Zone 2", 195 }, { "Wild Zone 12", 196 }, { "Wild Zone 10", 197 },
                { "Wild Zone 5", 198 }, { "Wild Zone 16", 199 }, { "Wild Zone 14", 200 }, { "Wild Zone 9", 201 },
                { "Wild Zone 18", 202 }, { "Wild Zone 7", 203 }, { "Wild Zone 13", 204 }, { "Wild Zone 4", 205 },
                { "Wild Zone 3", 206 }, { "Wild Zone 15", 207 }, { "Wild Zone 19", 208 }, { "Wild Zone 6", 209 },
                { "Wild Zone 11", 210 }, { "Wild Zone 8", 211 }, { "Wild Zone 20", 212 }, { "Battle Zone", 213 },
                { "Passage de la Félicité", 214 }, { "Passage Ombragé", 215 }, { "Galerie de la Lune", 216 },
                { "Passage du Palais", 217 }, { "Old Building", 231 }, { "Lysandre Café", 233 }, { "Lysandre Labs", 234 },
                { "The Sewers", 235 }, { "Room 201", 236 }, { "Room 202", 237 }, { "Room 203", 238 }, { "Room 204", 239 },
                { "Hyperspace Lumiose", 273 }, { "Hyperspace Entry Point", 274 }, { "Hyperspace Disaster Arena", 275 },
                { "Hyperspace Hunting Grounds", 276 }, { "Hyperspace Sushi Paradise", 277 }, { "Hyperspace Second-Sight Arena", 278 },
                { "Hyperspace Infernal Arena", 279 }, { "Hyperspace Newmoon Nightmare", 280 }, { "Hyperspace Primordial Sea", 281 },
                { "Hyperspace Desolate Land", 282 }, { "Hyperspace Sky Pillar", 283 }
            };
        }

        private static Dictionary<string, int> InitializeSWSHLocations()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mystery Zone", 2 }, { "Faraway Place", 4 }, { "Postwick", 6 }, { "Slumbering Weald", 8 },
                { "Route 1", 12 }, { "Wedgehurst", 14 }, { "Wedgehurst Station", 16 }, { "Route 2", 18 },
                { "Motostoke", 20 }, { "Motostoke Station", 22 }, { "Motostoke Stadium", 24 }, { "Route 3", 28 },
                { "Galar Mine", 30 }, { "Route 4", 32 }, { "Turmeld", 34 }, { "Turmeld Stadium", 36 },
                { "Route 5", 40 }, { "Hulbury", 44 }, { "Hulbury Station", 46 }, { "Hulbury Stadium", 48 },
                { "Motostoke Outskirts", 52 }, { "Galar Mine No. 2", 54 }, { "Hammerlocke", 56 }, { "Hammerlocke Station", 58 },
                { "Hammerlocke Stadium", 60 }, { "Energy Plant", 64 }, { "Tower Summit", 66 }, { "Route 6", 68 },
                { "Stow-on-Side", 70 }, { "Stow-on-Side Stadium", 72 }, { "Glimwood Tangle", 76 }, { "Ballonlea", 78 },
                { "Ballonlea Stadium", 80 }, { "Route 7", 84 }, { "Route 8", 86 }, { "Steamdrifi Way", 89 },
                { "Route 9", 90 }, { "Circhester Bay", 93 }, { "Outer Spikemuth", 95 }, { "Circhester", 96 },
                { "Circhester Stadium", 98 }, { "Spikemuth", 102 }, { "Route 9 Tunnel", 104 }, { "Route 10", 106 },
                { "White Hill Station", 108 }, { "Wyndon", 110 }, { "Wyndon Station", 112 }, { "Wyndon Stadium", 114 },
                { "Pokémon League HQ", 115 }, { "Locker Room", 117 }, { "Meetup Spot", 120 }, { "Wild Area", 121 },
                { "Rolling Fields", 122 }, { "Dappled Grove", 124 }, { "Watchtower Ruins", 126 }, { "East Lake Axewell", 128 },
                { "West Lake Axewell", 130 }, { "Axew's Eye", 132 }, { "South Lake Miloch", 134 }, { "Giant's Seat", 136 },
                { "North Lake Miloch", 138 }, { "Motostoke Riverbank", 140 }, { "Bridge Field", 142 }, { "Stony Wilderness", 144 },
                { "Dusty Bowl", 146 }, { "Giant's Mirror", 148 }, { "Hammerlocke Hills", 150 }, { "Giant's Cap", 152 },
                { "Lake of Outrage", 154 }, { "Wild Area Station", 156 }, { "Battle Tower", 158 }, { "Rose Tower", 160 },
                { "Pokémon Den", 162 }, { "Fields of Honor", 164 }, { "Isle of Armor", 165 }, { "Soothing Wetlands", 166 },
                { "Forest of Focus", 168 }, { "Challenge Beach", 170 }, { "Brawlers' Cave", 172 }, { "Challenge Road", 174 },
                { "Courageous Cavern", 176 }, { "Loop Lagoon", 178 }, { "Training Lowlands", 180 }, { "Warm-Up Tunnel", 182 },
                { "Potbottom Desert", 184 }, { "Workout Sea", 186 }, { "Stepping-Stone Sea", 188 }, { "Insular Sea", 190 },
                { "Honeycalm Sea", 192 }, { "Honeycalm Island", 194 }, { "Master Dojo", 196 }, { "Tower of Darkness", 198 },
                { "Tower of Waters", 200 }, { "Armor Station", 202 }, { "Slippery Slope", 204 }, { "Crown Tundra", 205 },
                { "Freezington", 206 }, { "Frostpoint Field", 208 }, { "Giant's Bed", 210 }, { "Old Cemetery", 212 },
                { "Snowslide Slope", 214 }, { "Tunnel to the Top", 216 }, { "Path to the Peak", 218 }, { "Crown Shrine", 220 },
                { "Giant's Foot", 222 }, { "Roaring-Sea Caves", 224 }, { "Frigid Sea", 226 }, { "Three-Point Pass", 228 },
                { "Ballimere Lake", 230 }, { "Lakeside Cave", 232 }, { "Dyna Tree Hill", 234 }, { "Rock Peak Ruins", 236 },
                { "Iceberg Ruins", 238 }, { "Iron Ruins", 240 }, { "Split-Decision Ruins", 242 }, { "Max Lair", 244 },
                { "Crown Tundra Station", 246 }, { "Link Trade", 30001 }, { "Kanto region", 30003 }, { "Johto region", 30004 },
                { "Hoenn region", 30005 }, { "Sinnoh region", 30006 }, { "distant land", 30007 }, { "Unova region", 30009 },
                { "Kalos region", 30010 }, { "Pokémon Link", 30011 }, { "Pokémon GO", 30012 }, { "Alola region", 30015 },
                { "Poké Pelago", 30016 }, { "Pokémon HOME", 30018 }, { "lovely place", 40001 }, { "faraway place", 40002 },
                { "Pokémon movie", 40003 }, { "2019 Pokémon movie", 40005 }, { "2020 Pokémon movie", 40006 }, { "2021 Pokémon movie", 40007 },
                { "2022 Pokémon movie", 40008 }, { "2023 Pokémon movie", 40009 }, { "2024 Pokémon movie", 40010 }, { "Pokémon animated show", 40011 },
                { "Pokémon Center", 40012 }, { "Pokémon Center Tohoku", 40013 }, { "WCS", 40014 }, { "WCS 2019", 40016 },
                { "WCS 2020", 40017 }, { "WCS 2021", 40018 }, { "WCS 2022", 40019 }, { "WCS 2023", 40020 }, { "WCS 2024", 40021 },
                { "Worlds", 40022 }, { "2019 Worlds", 40024 }, { "2020 Worlds", 40025 }, { "2021 Worlds", 40026 }, { "2022 Worlds", 40027 },
                { "2023 Worlds", 40028 }, { "2024 Worlds", 40029 }, { "VGE", 40030 }, { "VGE 2019", 40032 }, { "VGE 2020", 40033 },
                { "VGE 2021", 40034 }, { "VGE 2022", 40035 }, { "VGE 2023", 40036 }, { "VGE 2024", 40037 }, { "Pokémon Event", 40038 },
                { "Battle Competition", 40039 }, { "Game Event", 40040 }, { "Pokémon Daisuki Club", 40041 }, { "Pokémon TV Program", 40042 },
                { "Concert", 40043 }, { "Online Present", 40044 }, { "PGL", 40045 }, { "2019 Pokémon event", 40047 }, { "2020 Pokémon event", 40048 },
                { "2021 Pokémon event", 40049 }, { "2022 Pokémon event", 40050 }, { "2023 Pokémon event", 40051 }, { "2024 Pokémon event", 40052 },
                { "Pokémon event", 40053 }, { "PokéPark", 40061 }, { "PokéPark 2019", 40063 }, { "PokéPark 2020", 40064 },
                { "PokéPark 2021", 40065 }, { "PokéPark 2022", 40066 }, { "PokéPark 2023", 40067 }, { "PokéPark 2024", 40068 },
                { "Event Site", 40069 }, { "GAME FREAK", 40070 }, { "Stadium", 40071 }, { "VGC event", 40072 }, { "VGC 2019", 40074 },
                { "VGC 2020", 40075 }, { "VGC 2021", 40076 }, { "VGC 2022", 40077 }, { "VGC 2023", 40078 }, { "VGC 2024", 40079 },
                { "Virtual Console Game", 40080 }, { "Pokémon Bank", 40082 }, { "Pokémon shop", 40083 }, { "Demo Version", 40084 },
                { "Poké Ball Plus", 40085 }, { "Stranger", 60001 }, { "Nursery Worker", 60002 }, { "Treasure Hunter", 60003 },
                { "Old Hot-Springs Visitor", 60004 }
            };
        }

        private static Dictionary<string, int> InitializePLALocations()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mystery Zone", 2 }, { "Faraway Place", 4 }, { "Jubilife Village", 6 }, { "Obsidian Fieldlands", 7 },
                { "Crimson Mirelands", 8 }, { "Cobalt Coastlands", 9 }, { "Coronet Highlands", 10 }, { "Alabaster Icelands", 11 },
                { "Ancient Retreat", 12 }, { "Aspiration Hill", 13 }, { "Horseshoe Plains", 14 }, { "Deertrack Path", 15 },
                { "Deertrack Heights", 16 }, { "Obsidian Falls", 17 }, { "Oreburrow Tunnel", 18 }, { "Nature's Pantry", 19 },
                { "Heartwood", 20 }, { "Grandtree Arena", 21 }, { "Grueling Grove", 22 }, { "Lake Verity", 23 },
                { "Sandgem Flats", 24 }, { "Tidewater Dam", 25 }, { "Floaro Gardens", 26 }, { "Ramanas Island", 27 },
                { "Fieldlands Camp", 28 }, { "Heights Camp", 29 }, { "Windswept Run", 30 }, { "Worn Bridge", 32 },
                { "Moss Rock", 33 }, { "Golden Lowlands", 34 }, { "Solaceon Ruins", 35 }, { "Gapejaw Bog", 36 },
                { "Sludge Mound", 37 }, { "Scarlet Bog", 38 }, { "Bolderoll Slope", 39 }, { "Diamond Settlement", 40 },
                { "Lake Valor", 41 }, { "Cloudpool Ridge", 42 }, { "Shrouded Ruins", 43 }, { "Holm of Trials", 45 },
                { "Droning Meadow", 46 }, { "Mirelands Camp", 47 }, { "Bogbound Camp", 48 }, { "Crossing Slope", 49 },
                { "Ginkgo Landing", 50 }, { "Deadwood Haunt", 51 }, { "Hideaway Bay", 52 }, { "Tombolo Walk", 53 },
                { "Veilstone Cape", 54 }, { "Seagrass Haven", 55 }, { "Firespit Island", 56 }, { "Iscan's home", 57 },
                { "Spring Path", 58 }, { "Lunker's Lair", 59 }, { "Islespy Shore", 60 }, { "Windbreak Stand", 61 },
                { "Tidal Passage", 63 }, { "Seaside Hollow", 64 }, { "Beachside Camp", 65 }, { "Coastlands Camp", 66 },
                { "Turnback Cave", 67 }, { "Lava Dome Sanctum", 68 }, { "Highlands Camp", 69 }, { "Heavenward Lookout", 70 },
                { "Wayward Wood", 71 }, { "Ancient Quarry", 72 }, { "Celestica Trail", 73 }, { "Lonely Spring", 74 },
                { "Fabled Spring", 75 }, { "Bolderoll Ravine", 76 }, { "Stonetooth Rows", 77 }, { "Primeval Grotto", 79 },
                { "Clamberclaw Cliffs", 80 }, { "Celestica Ruins", 81 }, { "Moonview Arena", 82 }, { "Summit Camp", 83 },
                { "Cloudcap Pass", 84 }, { "Spear Pillar", 85 }, { "Wayward Cave", 86 }, { "Snowfields Camp", 87 },
                { "Whiteout Valley", 88 }, { "Crevasse Passage", 89 }, { "Bonechill Wastes", 90 }, { "Avalugg's Legacy", 92 },
                { "Glacier Terrace", 93 }, { "Lake Acuity", 94 }, { "Snowpoint Temple", 95 }, { "Hibernal Cave", 96 },
                { "Icebound Falls", 97 }, { "Avalanche Slopes", 98 }, { "Arena's Approach", 99 }, { "Pearl Settlement", 100 },
                { "Heart's Crag", 101 }, { "Ice Column Chamber", 102 }, { "Icepeak Camp", 103 }, { "Verity Cavern", 104 },
                { "Valor Cavern", 105 }, { "Cottonsedge Prairie", 106 }, { "Mountain Camp", 107 }, { "Stone Portal", 108 },
                { "Temple of Sinnoh", 109 }, { "Ice Rock", 110 }, { "Acuity Cavern", 111 }, { "Icepeak Cavern", 112 },
                { "Secret Hollow", 113 }, { "Galaxy Hall", 114 }, { "General Store", 115 }, { "candy stand", 116 },
                { "Craftworks", 117 }, { "Ginkgo Guild Cart", 118 }, { "Trading Post", 119 }, { "Pastures", 120 },
                { "Clothier", 121 }, { "Hairdresser", 122 }, { "Photography Studio", 123 }, { "Farm", 124 },
                { "Training Grounds", 125 }, { "Folk Shrine", 126 }, { "Your Quarters", 127 }, { "Castaway Shore", 128 },
                { "Sand's Reach", 129 }, { "Sonorous Path", 130 }, { "Sacred Plaza", 131 }, { "Front Gate", 132 },
                { "Seaside Gate", 133 }, { "Canala Avenue", 134 }, { "Floaro Main Street", 135 }, { "Prelude Beach", 136 },
                { "Practice Field", 137 }, { "Unknown Location", 138 }, { "Brava Arena", 139 }, { "Molten Arena", 140 },
                { "Icepeak Arena", 141 }, { "Snowfall Hot Spring", 142 }, { "First Floor", 143 }, { "Second Floor", 144 },
                { "Third Floor", 145 }, { "Basement", 146 }, { "Ursa's Ring", 147 }, { "Diamond Heath", 148 },
                { "Aipom Hill", 149 }, { "Bathers' Lagoon", 150 }, { "Tranquility Cove", 151 }
            };
        }

        private static Dictionary<string, int> InitializeBDSPLocations()
        {
            // BDSP has extensive locations - including only commonly used ones for readability
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Jubilife City", 0 }, { "Canalave City", 24 }, { "Canalave Gym", 26 }, { "Oreburgh City", 37 },
                { "Oreburgh Gym", 39 }, { "Eterna City", 52 }, { "Eterna Gym", 54 }, { "Hearthome City", 72 },
                { "Hearthome Gym", 74 }, { "Pastoria City", 107 }, { "Pastoria Gym", 109 }, { "Veilstone City", 119 },
                { "Veilstone Gym", 120 }, { "Sunyshore City", 139 }, { "Sunyshore Gym", 141 }, { "Snowpoint City", 156 },
                { "Snowpoint Gym", 158 }, { "Pokémon League", 164 }, { "Fight Area", 183 }, { "Oreburgh Mine", 191 },
                { "Valley Windworks", 193 }, { "Eterna Forest", 195 }, { "Fuego Ironworks", 197 }, { "Mount Coronet", 199 },
                { "Spear Pillar", 212 }, { "Hall of Origin", 214 }, { "Great Marsh", 215 }, { "Solaceon Ruins", 221 },
                { "Victory Road", 240 }, { "Ramanas Park", 246 }, { "Amity Square", 247 }, { "Ravaged Path", 248 },
                { "Floaroma Meadow", 249 }, { "Oreburgh Gate", 251 }, { "Fullmoon Island", 253 }, { "Stark Mountain", 255 },
                { "Sendoff Spring", 259 }, { "Turnback Cave", 260 }, { "Flower Paradise", 281 }, { "Snowpoint Temple", 282 },
                { "Wayward Cave", 288 }, { "Ruin Maniac Cave", 290 }, { "Maniac Tunnel", 292 }, { "Trophy Garden", 293 },
                { "Iron Island", 294 }, { "Old Chateau", 302 }, { "Galactic HQ", 310 }, { "Lake Verity", 318 },
                { "Lake Valor", 321 }, { "Lake Acuity", 324 }, { "Newmoon Island", 327 }, { "Battle Park", 329 },
                { "Battle Tower", 334 }, { "Mystery Zone", 340 }, { "Verity Lakefront", 341 }, { "Valor Lakefront", 342 },
                { "Acuity Lakefront", 347 }, { "Spring Path", 348 }, { "Route 201", 350 }, { "Route 202", 351 },
                { "Route 203", 352 }, { "Route 204", 353 }, { "Route 205", 355 }, { "Route 206", 358 },
                { "Route 207", 360 }, { "Route 208", 361 }, { "Route 209", 363 }, { "Route 210", 369 },
                { "Route 211", 373 }, { "Route 212", 375 }, { "Route 213", 381 }, { "Route 214", 388 },
                { "Route 215", 390 }, { "Route 216", 391 }, { "Route 217", 393 }, { "Route 218", 396 },
                { "Route 219", 399 }, { "Route 221", 400 }, { "Route 222", 403 }, { "Route 224", 407 },
                { "Route 225", 408 }, { "Route 227", 410 }, { "Route 228", 412 }, { "Route 229", 416 },
                { "Twinleaf Town", 418 }, { "Sandgem Town", 425 }, { "Floaroma Town", 434 }, { "Solaceon Town", 443 },
                { "Celestic Town", 453 }, { "Survival Area", 462 }, { "Resort Area", 470 }, { "Route 220", 481 },
                { "Route 223", 482 }, { "Route 226", 483 }, { "Route 230", 485 }, { "Seabreak Path", 486 },
                { "Grand Underground", 505 }
            };
        }

        private static Dictionary<string, int> InitializeSVLocations()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mystery Zone", 2 }, { "Faraway place", 4 }, { "South Province (Area One)", 6 }, { "Mesagoza", 8 },
                { "Pokémon League", 10 }, { "South Province (Area Two)", 12 }, { "South Province (Area Four)", 14 },
                { "South Province (Area Six)", 16 }, { "South Province (Area Five)", 18 }, { "South Province (Area Three)", 20 },
                { "West Province (Area One)", 22 }, { "Asado Desert", 24 }, { "West Province (Area Two)", 26 },
                { "West Province (Area Three)", 28 }, { "Tagtree Thicket", 30 }, { "East Province (Area Three)", 32 },
                { "East Province (Area One)", 34 }, { "East Province (Area Two)", 36 }, { "Glaseado Mountain", 38 },
                { "Casseroya Lake", 40 }, { "North Province (Area Three)", 44 }, { "North Province (Area One)", 46 },
                { "North Province (Area Two)", 48 }, { "Great Crater of Paldea", 50 }, { "Zero Gate", 52 },
                { "South Paldean Sea", 56 }, { "West Paldean Sea", 58 }, { "East Paldean Sea", 60 }, { "North Paldean Sea", 62 },
                { "Inlet Grotto", 64 }, { "Alfornada Cavern", 67 }, { "Dalizapa Passage", 69 }, { "Poco Path", 70 },
                { "Cascarrafa", 76 }, { "Levincia", 78 }, { "Cabo Poco", 80 }, { "Los Platos", 82 },
                { "Cortondo", 84 }, { "Artazon", 86 }, { "Porto Marinada", 88 }, { "Medali", 90 },
                { "Zapapico", 92 }, { "Montenevera", 94 }, { "Alfornada", 96 }, { "Caph Squad's Base", 99 },
                { "Segin Squad's Base", 101 }, { "Ruchbah Squad's Base", 103 }, { "Schedar Squad's Base", 105 },
                { "Navi Squad's Base", 107 }, { "Socarrat Trail", 109 }, { "Area Zero", 110 },
                { "Research Station No. 1", 111 }, { "Research Station No. 2", 113 }, { "Research Station No. 3", 115 },
                { "Research Station No. 4", 117 }, { "Zero Lab", 118 }, { "Naranja Academy", 130 },
                { "Uva Academy", 131 }, { "Kitakami Road", 132 }, { "Mossui Town", 134 }, { "Apple Hills", 136 },
                { "Loyalty Plaza", 138 }, { "Reveler's Road", 140 }, { "Kitakami Hall", 142 }, { "Oni Mountain", 144 },
                { "Dreaded Den", 146 }, { "Oni's Maw", 148 }, { "Crystal Pool", 152 }, { "Wistful Fields", 156 },
                { "Mossfell Confluence", 158 }, { "Fellhorn Gorge", 160 }, { "Paradise Barrens", 162 }, { "Kitakami Wilds", 164 },
                { "Timeless Woods", 166 }, { "Infernal Pass", 168 }, { "Chilling Waterhead", 170 }, { "Blueberry Academy", 172 },
                { "Savanna Biome", 174 }, { "Coastal Biome", 176 }, { "Canyon Biome", 178 }, { "Polar Biome", 180 },
                { "Central Plaza", 182 }, { "Savanna Plaza", 184 }, { "Coastal Plaza", 186 }, { "Canyon Plaza", 188 },
                { "Polar Plaza", 190 }, { "Chargestone Cavern", 192 }, { "Torchlit Labyrinth", 194 },
                { "Area Zero Underdepths", 196 }, { "Terarium", 200 }, { "a distant land", 30007 },
                { "a picnic", 30023 }, { "a crystal cavern", 30024 }
            };
        }
    }
}
