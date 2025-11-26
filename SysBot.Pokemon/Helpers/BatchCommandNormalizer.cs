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
        // Value is a numeric value only for now
        private static string ProcessMetLocation(string val) =>
            $".MetLocation={val}"; // Hook into your met location resolver here

        // .HyperTrainFlags= → HyperTrain: or HyperTrainFlags:
        // Value is "true" or "false"
        private static string ProcessHyperTrainFlags(string val) =>
            TryParseBoolean(val, out var b)
                ? $".HyperTrainFlags={b}"
                : string.Empty;

        // .StatNature= → Stat Nature:
        // Value is a Nature enum name or "Random"
        // W.I.P.
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
    }
}
