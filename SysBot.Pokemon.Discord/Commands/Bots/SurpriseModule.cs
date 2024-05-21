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
            await ReplyAsync("**Surprise!**");
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
                    var speciesList = GetBreedableSpecies(gameVersion, "en");

                    var randomIndex = new Random().Next(speciesList.Count);
                    ushort speciesId = speciesList[randomIndex];
                    var speciesName = GameInfo.GetStrings("en").specieslist[speciesId];

                    var showdownSet = new ShowdownSet(speciesName);
                    var template = AutoLegalityWrapper.GetTemplate(showdownSet);

                    var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                    var pkm = sav.GetLegal(template, out var result);

                    RandomizePokemon(pkm);

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


        // RANDOMIZE POKEMON STATS //
        private static void RandomizePokemon(PKM pk)
        {
            var random = new Random();

            // Shiny
            bool isShiny = random.Next(0, 100) < 50; // 50% chance of being shiny
            if (isShiny)
            {
                pk.SetShiny(); // make shiny
            }

            // Ability
            int abilityCount = 3; // how many abilities
            int selectedAbility = random.Next(abilityCount); // now randomize them
            pk.RefreshAbility(selectedAbility); // refresh if selected is good

            // Level
            pk.CurrentLevel = (byte)random.Next(1, 101); // randomized levels 1-100
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

        public static List<ushort> GetBreedableSpecies(GameVersion gameVersion, string language = "en")
        {
            var gameStrings = GameInfo.GetStrings(language);
            var availableSpeciesList = gameStrings.specieslist
                .Select((name, index) => (Name: name, Index: index))
                .Where(item => item.Name != string.Empty)
                .ToList();

            var breedableSpecies = new List<ushort>();
            var pt = GetPersonalTable(gameVersion);
            foreach (var species in availableSpeciesList)
            {
                var speciesId = (ushort)species.Index;
                var speciesName = species.Name;
                var pi = GetFormEntry(pt, speciesId, 0);
                if (IsBreedable(pi) && pi.EvoStage == 1)
                {
                    breedableSpecies.Add(speciesId);
                }
            }

            return breedableSpecies;
        }

        private static bool IsBreedable(PersonalInfo pi)
        {
            return pi.EggGroup1 != 0 || pi.EggGroup2 != 0;
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
