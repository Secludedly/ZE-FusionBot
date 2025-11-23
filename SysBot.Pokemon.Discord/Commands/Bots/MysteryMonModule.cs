using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace SysBot.Pokemon.Discord
{
    public class MysteryMonModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;
        private static readonly Random rng = new(); // Global RNG for consistent randomness
        private static readonly HashSet<ushort> BannedForms = new() // Pokemon with alternate forms that may cause legality issues
        {
            643, // Reshiram
            644, // Zekrom
            646, // Kyurem
            716, // Xerneas
            718, // Zygarde
            791, // Solgaleo
            792, // Lunala
            800, // Necrozma
            801, // Magearna
            888, // Zacian
            889, // Zamazenta
            898, // Calyrex
        };

        // Commands for trading random Pokémon with completely random attributes and stats
        [Command("mysterymon")]
        [Alias("mm", "mystery", "surprise")]
        [Summary("Trades a random Pokémon with completely random attributes and stats.")]
        public async Task TradeRandomPokemonAsync()
        {
            var userID = Context.User.Id;
            if (Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You already have an existing trade in the queue. Please wait until it is processed.").ConfigureAwait(false);
                return;
            }
            var code = Info.GetRandomTradeCode(userID);
            await Task.Run(async () =>
            {
                await TradeRandomPokemonAsync(code).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // Commands for trading random Pokémon with completely random attributes and stats (with a specific trade code)
        [Command("mysterymon")]
        [Alias("mm", "mystery", "surprise")]
        [Summary("Trades a random Pokémon with completely random attributes and stats.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeRandomPokemonAsync([Summary("Trade Code")] int code)
        {
            var userID = Context.User.Id;
            if (Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You already have an existing trade in the queue. Please wait until it is processed.").ConfigureAwait(false);
                return;
            }

            try
            {
                T? pk = null;
                bool isValid = false;

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15)); // 15-second timeout
                var token = cts.Token;

                while (!isValid && !token.IsCancellationRequested)
                {
                    try
                    {
                        var gameVersion = GetGameVersion();
                        var speciesList = GetSpecies(gameVersion, "en");

                        if (speciesList.Count == 0)
                            break; // Safety check

                        ushort speciesId = speciesList[rng.Next(speciesList.Count)];

                        if (BannedForms.Contains(speciesId))
                            continue;

                        var speciesName = GameInfo.GetStrings("en").specieslist[speciesId];

                        var showdownSet = new ShowdownSet(speciesName);
                        var template = AutoLegalityWrapper.GetTemplate(showdownSet);

                        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                        var pkmTemp = sav.GetLegal(template, out _);

                        RandomizePokemon(pkmTemp, gameVersion);

                        pkmTemp = EntityConverter.ConvertToType(pkmTemp, typeof(T), out _) ?? pkmTemp;

                        if (pkmTemp is T generatedPk)
                        {
                            var la = new LegalityAnalysis(generatedPk);
                            if (la.Valid)
                            {
                                pk = generatedPk;
                                isValid = true;

                                // Optional logging
                                var ivs = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
                                var evs = $"{pk.EV_HP}/{pk.EV_ATK}/{pk.EV_DEF}/{pk.EV_SPA}/{pk.EV_SPD}/{pk.EV_SPE}";
                                var moves = string.Join(", ", pk.Moves.Select(m => GameInfo.GetStrings("en").movelist[m]));
                                Console.WriteLine($"[MysteryMon] {speciesName} | Lv {pk.CurrentLevel} | Shiny: {pk.IsShiny} | IVs: {ivs} | EVs: {evs} | Ability #: {pk.AbilityNumber} | Item: {pk.HeldItem} | Moves: {moves}");
                            }
                        }
                    }
                    catch
                    {
                        // Swallow errors and continue trying until timeout
                    }
                }

                if (pk != null)
                {
                    var sig = Context.User.GetFavor();
                    await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User).ConfigureAwait(false);

                    if (Context.Message is IUserMessage userMessage)
                        _ = DeleteMessageAfterDelay(userMessage, 2000);
                }
                else
                {
                    await ReplyAsync("Sorry, I couldn't find a legal Mystery Mon for you. Try again!").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(MysteryMonModule<T>));
                await ReplyAsync("An error occurred while processing the request.").ConfigureAwait(false);
            }
        }

        private static async Task DeleteMessageAfterDelay(IUserMessage message, int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }



        /////////////////////////////////////////////////////////////////
        //////////////////// RANDOMIZE POKEMON INFO /////////////////////
        /////////////////////////////////////////////////////////////////

        private static void RandomizePokemon(PKM pk, GameVersion gameVersion) // Method to call and set other randomizations below
        {
            var random = new Random();

            //--------------- Held Items --------------//
            var heldItems = GetHeldItemPool(gameVersion);
            if (heldItems.Count > 0)
            {
                pk.HeldItem = heldItems[rng.Next(heldItems.Count)];
            }
            else
            {
                pk.HeldItem = 0; // Set to 0 or "None" if PLA or empty pool
            }

            // --------------- Fateful Encounter --------------- //
            pk.FatefulEncounter = random.Next(100) < 6; // 6% chance to be event/gift

            // --------------- Shiny --------------- //
            if (!pk.FatefulEncounter && random.Next(100) < 54) // 54% chance shiny event/gift
                pk.SetShiny();

            // --------------- Level --------------- //
            bool isLevel100 = random.Next(100) < 10;
            pk.CurrentLevel = isLevel100 ? (byte)100 : (byte)random.Next(1, 100);

            // --------------- IVs --------------- //
            if (isLevel100 || random.Next(100) < 30) // 30% chance for perfect IVs
            {
                pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
            }
            else
            {
                pk.IV_HP = (byte)random.Next(32);
                pk.IV_ATK = (byte)random.Next(32);
                pk.IV_DEF = (byte)random.Next(32);
                pk.IV_SPA = (byte)random.Next(32);
                pk.IV_SPD = (byte)random.Next(32);
                pk.IV_SPE = (byte)random.Next(32);
            }

            // --------------- EVs --------------- //
            int totalEVs = 0;
            for (int i = 0; i < 6; i++)
            {
                int ev = random.Next(0, 253);
                if (totalEVs + ev > 510) ev = 510 - totalEVs;
                if (ev > 252) ev = 252;

                switch (i)
                {
                    case 0: pk.EV_HP = (byte)ev; break;
                    case 1: pk.EV_ATK = (byte)ev; break;
                    case 2: pk.EV_DEF = (byte)ev; break;
                    case 3: pk.EV_SPA = (byte)ev; break;
                    case 4: pk.EV_SPD = (byte)ev; break;
                    case 5: pk.EV_SPE = (byte)ev; break;
                }
                totalEVs += ev;
            }

            // --------------- Ability --------------- //
            int abilityCount = pk.PersonalInfo.AbilityCount;
            if (abilityCount > 0)
            {
                int abilityIndex = random.Next(abilityCount);
                pk.RefreshAbility(abilityIndex);
            }

            // --------------- Tera Type for SV --------------- //
            if (pk is PK9 pk9)
            {
                var personal = pk9.PersonalInfo;
                int type1 = personal.Type1;
                int type2 = personal.Type2;

                var typePool = Enumerable.Range(0, 18)
                    .Where(t => t != type1 && t != type2)
                    .ToList();

                int newTeraTypeIndex = typePool[random.Next(typePool.Count)];
                pk9.SetTeraType((MoveType)newTeraTypeIndex);
            }
        }

        //--------------- Held Item Pools --------------//
        private static List<int> GetHeldItemPool(GameVersion gameVersion)
        {
            return gameVersion switch
            {
                GameVersion.ZA => ZA_HeldItems,          // Use the ZA list
                GameVersion.PLA => new List<int>(),      // PLA has no held items
                _ => DefaultHeldItemPool(),              // Fallback to generic list
            };
        }

        private static List<int> DefaultHeldItemPool()
        {
            return new List<int>()
            {
        1, 236, 244, 1120, 286, 217, 328, 221, 248, 255,
        228, 229, 230, 275, 233, 281, 234, 265, 269, 245,
        538, 645, 223, 287, 297, 220, 270, 290, 294, 241,
        268, 50, 55, 47, 48, 49, 51, 54, 158, 210, 155,
        157, 619, 620, 82, 84, 85, 81, 80, 83, 107,
        108, 109

            };
        }

        private static readonly List<int> ZA_HeldItems = new()
        {

            23, 24, 27, 29, 33, 1, 150, 152, 155, 158,
            184, 185, 45, 46, 47, 48, 49, 50, 51, 52, 80,
            81, 82, 83, 84, 85, 107, 108, 109, 214, 217,
            218, 221, 222, 230, 231, 232, 233, 234, 236,
            237, 238, 241, 242, 248, 249, 245, 253, 266,
            267, 268, 270, 540, 565, 566, 567, 568, 569,
            570, 639, 640, 646, 647, 849, 1128, 1231, 1232,
            1233, 1234, 1235, 1236, 1237, 1238, 1239, 1240,
            1241, 1242, 1243, 1244, 1245, 1246, 1247, 1248,
            1249, 1250, 1251, 1582, 2558, 581

        };

        //--------------- Supported Game Versions --------------//
        private static GameVersion GetGameVersion()
        {
            return typeof(T) switch
            {
                Type t when t == typeof(PK8) => GameVersion.SWSH,
                Type t when t == typeof(PB8) => GameVersion.BDSP,
                Type t when t == typeof(PA8) => GameVersion.PLA,
                Type t when t == typeof(PK9) => GameVersion.SV,
                Type t when t == typeof(PA9) => GameVersion.ZA,
                _ => throw new ArgumentException("Unsupported game version.")
            };
        }

        //--------------- Get Species List --------------//
        public static List<ushort> GetSpecies(GameVersion gameVersion, string language = "en")
        {
            var gameStrings = GameInfo.GetStrings(language);
            var pt = GetPersonalTable(gameVersion);
            return gameStrings.specieslist
                .Select((name, index) => (name, index))
                .Where(x => !string.IsNullOrWhiteSpace(x.name) && !BannedForms.Contains((ushort)x.index))
                .Select(x => (ushort)x.index)
                .ToList();
        }

        //--------------- Get Personal Table --------------//
        private static object GetPersonalTable(GameVersion gameVersion) => gameVersion switch
        {
            GameVersion.SWSH => PersonalTable.SWSH,
            GameVersion.BDSP => PersonalTable.BDSP,
            GameVersion.PLA => PersonalTable.LA,
            GameVersion.SV => PersonalTable.SV,
            GameVersion.ZA => PersonalTable.ZA,
            _ => throw new ArgumentException("Unsupported personal table type.")
        };

        /////////////////////////////////////////////////////////////////
        ///// Add this mess of a Pokemon to the queue for processing ////
        /////////////////////////////////////////////////////////////////

        private async Task AddTradeToQueueAsync(int code, string trainerName, T? pk, RequestSignificance sig, SocketUser usr, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false)
        {
            var la = new LegalityAnalysis(pk);
            if (!la.Valid) return; // Should never happen with retry logic

            await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.Specific, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade).ConfigureAwait(false);
        }
    }
}
