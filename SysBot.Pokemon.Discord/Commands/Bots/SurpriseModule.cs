using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class SurprisePokemonModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

        [Command("surprise")]
        [Alias("random", "st", "randomize", "randomtrade", "rt")]
        [Summary("Trades a random Pokémon with perfect stats and shiny appearance.")]
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

        [Command("surprise")]
        [Alias("random", "st", "randomize", "randomtrade", "rt")]
        [Summary("Trades a random Pokémon with perfect stats and shiny appearance.")]
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
                T? pk = null;  // loop logic until legal set found
                bool isValid = false;

                while (!isValid)
                {

                    var gameVersion = GetGameVersion();
                    var speciesList = GetSpecies(gameVersion, "en");

                    var randomIndex = new Random().Next(speciesList.Count);
                    ushort speciesId = speciesList[randomIndex];
                    var speciesName = GameInfo.GetStrings("en").specieslist[speciesId];

                    var showdownSet = new ShowdownSet(speciesName);
                    var template = AutoLegalityWrapper.GetTemplate(showdownSet);

                    var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                    var pkm = sav.GetLegal(template, out var result);

                    RandomizePokemon(pkm, gameVersion);

                    pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

                    if (pkm is T generatedPk)
                    {
                        var la = new LegalityAnalysis(generatedPk);
                        if (la.Valid)
                        {
                            pk = generatedPk;
                            isValid = true;
                        }
                    }
                }

                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User).ConfigureAwait(false);

                if (Context.Message is IUserMessage userMessage)
                {
                    _ = DeleteMessageAfterDelay(userMessage, 2000);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(SurprisePokemonModule<T>));
                await ReplyAsync("An error occurred while processing the request.").ConfigureAwait(false);
            }
        }

        private static async Task DeleteMessageAfterDelay(IUserMessage message, int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            await message.DeleteAsync().ConfigureAwait(false);
        }

        /////////////////////////////////////////////////////////////////
        //////////////////// RANDOMIZE POKEMON STATS ////////////////////
        /////////////////////////////////////////////////////////////////

        private static void RandomizePokemon(PKM pk, GameVersion gameVersion)
        {
            var random = new Random();

            //--------------- Held Items --------------//
            List<int> heldItems = new List<int>
    {
        1, 236, 244, 286, 81, 217, 221, 248, 228, 230, 233, 281, 541, 234,
        265, 158, 241, 155, 269, 157, 287, 210, 645, 1606, 275, 223, 297,
        220, 270, 268, 1128, 50, 82, 84, 85, 1120, 109
    };
            if (gameVersion != GameVersion.PLA)
            {
                pk.HeldItem = heldItems[random.Next(heldItems.Count)];
            }

            //--------------- Fateful Encounter --------------//
            pk.FatefulEncounter = random.Next(0, 100) < 12; // 12% chance of being "True" for being an Event/Gift
            if (!pk.FatefulEncounter)
            {
                //--------------- Shiny --------------//
                bool isShiny = random.Next(0, 100) < 65; // 65% chance of being shiny
                if (isShiny)
                {
                    pk.SetShiny(); // Make shiny
                }

                //--------------- Level --------------//
                bool isLevel100 = random.Next(0, 100) < 5; // 5% chance of being Level 100 (Not really though, more like 20%)
                if (isLevel100)
                {
                    pk.CurrentLevel = 100; // Set to Level 100

                    // Set all IVs to 31 if generated Pokemon is Level 100
                    pk.IV_HP = 31;  // HP
                    pk.IV_ATK = 31; // Attack
                    pk.IV_DEF = 31; // Defense
                    pk.IV_SPA = 31; // Special Attack
                    pk.IV_SPD = 31; // Special Defense
                    pk.IV_SPE = 31; // Speed
                }
                else
                {
                    pk.CurrentLevel = (byte)random.Next(1, 100); // Randomize Levels from 1-99

                    //--------------- IVs --------------//
                    bool isPerfectIVs = random.Next(0, 100) < 15; // 15% chance of having perfect IVs if not Level 100
                    if (isPerfectIVs)
                    {
                        // Set all IVs to 31
                        pk.IV_HP = 31;
                        pk.IV_ATK = 31;
                        pk.IV_DEF = 31;
                        pk.IV_SPA = 31;
                        pk.IV_SPD = 31;
                        pk.IV_SPE = 31;
                    }
                    else
                    {
                        // Otherwise, set IVs randomly
                        pk.IV_HP = (byte)random.Next(0, 32);
                        pk.IV_ATK = (byte)random.Next(0, 32);
                        pk.IV_DEF = (byte)random.Next(0, 32);
                        pk.IV_SPA = (byte)random.Next(0, 32);
                        pk.IV_SPD = (byte)random.Next(0, 32);
                        pk.IV_SPE = (byte)random.Next(0, 32);
                    }
                }

                //--------------- EVs --------------//
                int totalEVs = 0; // Initialize total EV count
                for (int i = 0; i < 6; i++) // There are 6 EVs
                {
                    int ev = random.Next(0, 253); // Random EV values between 0 and 252
                    if (totalEVs + ev > 510) // Total EVs cannot exceed 510
                    {
                        ev = 510 - totalEVs; // Keep EVs within the 510 limit
                    }
                    // Do not let EVs exceed 252
                    if (ev > 252)
                    {
                        ev = 252;
                    }
                    // Register the random EVs to stats
                    switch (i)
                    {
                        case 0: // HP
                            pk.EV_HP = (byte)ev;
                            break;
                        case 1: // Attack
                            pk.EV_ATK = (byte)ev;
                            break;
                        case 2: // Defense
                            pk.EV_DEF = (byte)ev;
                            break;
                        case 3: // Special Attack
                            pk.EV_SPA = (byte)ev;
                            break;
                        case 4: // Special Defense
                            pk.EV_SPD = (byte)ev;
                            break;
                        case 5: // Speed
                            pk.EV_SPE = (byte)ev;
                            break;
                    }
                    totalEVs += ev; // Update the total EVs
                }

                //--------------- Ability --------------//
                var randomAbility = new Random(); // Register sending a random ability
                int abilityIndex = randomAbility.Next(0, 3); // Generate a random number between 0, 1, or 2
                byte abilityNumber = (byte)(abilityIndex * 2); // How the ability index assigns itself an ability number (0, 2, or 4)
                if (abilityIndex == 2) // If the ability index is 2...
                {
                    abilityNumber = 4; // ...Then set the ability number to 4
                }
                pk.AbilityNumber = abilityNumber; // Send the random ability number to the Pokemon


            }
        }

        private static GameVersion GetGameVersion()
        {
            if (typeof(T) == typeof(PK8))
                return GameVersion.SWSH;
            else if (typeof(T) == typeof(PB8))
                return GameVersion.BDSP;
            else if (typeof(T) == typeof(PA8))
                return GameVersion.PLA;
            else if (typeof(T) == typeof(PK9))
                return GameVersion.SV;
            else
                throw new ArgumentException("Unsupported game version.");
        }

        public static List<ushort> GetSpecies(GameVersion gameVersion, string language = "en")
        {
            var gameStrings = GameInfo.GetStrings(language);
            var availableSpeciesList = gameStrings.specieslist
                .Select((name, index) => (Name: name, Index: index))
                .Where(item => item.Name != string.Empty)
                .ToList();

            var speciesList = new List<ushort>();
            var pt = GetPersonalTable(gameVersion);
            foreach (var species in availableSpeciesList)
            {
                var speciesId = (ushort)species.Index;
                speciesList.Add(speciesId);
            }

            return speciesList;
        }

        private static PersonalInfo GetFormEntry(object personalTable, ushort species, byte form)
        {
            return personalTable switch
            {
                PersonalTable9SV pt => pt.GetFormEntry(species, form),
                PersonalTable8SWSH pt => pt.GetFormEntry(species, form),
                PersonalTable8LA pt => pt.GetFormEntry(species, form),
                PersonalTable8BDSP pt => pt.GetFormEntry(species, form),
                _ => throw new ArgumentException("Unsupported personal table type."),
            };
        }

        private static object GetPersonalTable(GameVersion gameVersion)
        {
            return gameVersion switch
            {
                GameVersion.SWSH => PersonalTable.SWSH,
                GameVersion.BDSP => PersonalTable.BDSP,
                GameVersion.PLA => PersonalTable.LA,
                GameVersion.SV => PersonalTable.SV,
                _ => throw new ArgumentException("Unsupported game version."),
            };
        }

        private async Task AddTradeToQueueAsync(int code, string trainerName, T? pk, RequestSignificance sig, SocketUser usr, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false)
        {
            var la = new LegalityAnalysis(pk);
            if (!la.Valid)
            {
                string responseMessage;
                string speciesName = GameInfo.GetStrings("en").specieslist[pk.Species];
                responseMessage = $"Use the command again!";

                var reply = await ReplyAsync(responseMessage).ConfigureAwait(false);
                await Task.Delay(6000);
                await reply.DeleteAsync().ConfigureAwait(false);
                return;
            }

            await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.Specific, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade).ConfigureAwait(false);
        }
    }
}
