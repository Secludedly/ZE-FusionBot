using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class SlashDittoModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("ditto", "Request a Ditto with specific stats and language for breeding")]
    public async Task DittoAsync(
        [Summary("stats", "Which IVs should be set to 0 (used in nickname to drive stat selection)")]
        [Choice("6IV - All Stats Perfect", "6IV")]
        [Choice("0 Atk - For Special Sweepers", "ATK")]
        [Choice("0 Atk + 0 SpA + 0 SpD - Competitive", "ATK/SPA/SPE")]
        string stats,

        [Summary("language", "Ditto language - use Japanese/Korean for Masuda Method")]
        [Choice("English", "English")]
        [Choice("Japanese", "Japanese")]
        [Choice("French", "French")]
        [Choice("German", "German")]
        [Choice("Italian", "Italian")]
        [Choice("Spanish", "Spanish")]
        [Choice("Korean", "Korean")]
        [Choice("Chinese (Simplified)", "ChineseS")]
        [Choice("Chinese (Traditional)", "ChineseT")]
        string language = "English",

        [Summary("nature", "Ditto nature (optional)")]
        [Autocomplete(typeof(NatureAutocompleteHandler))]
        string? nature = null
    )
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (typeof(T).Name == "PA8")
        {
            await RespondAsync("❌ Ditto trades are not available for Legends: Arceus.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var Info = SysCord<T>.Runner.Hub.Queues.Info;
        if (Info.IsUserInQueue(Context.User.Id))
        {
            await RespondAsync("❌ You are already in the queue! Please wait until your current trade is processed.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: false).ConfigureAwait(false);

        try
        {
            var natureLine = string.IsNullOrWhiteSpace(nature) ? string.Empty : $"\nNature: {nature}";
            var setString = $"{stats}(Ditto)\nLanguage: {language}{natureLine}";

            var set = new ShowdownSet(setString);
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GetLegal(template, out _);

            if (pkm == null)
            {
                await FollowupAsync("❌ Failed to generate the Ditto. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pkm = TradeExtensions<T>.DittoTrade((T)pkm);

            var la = new LegalityAnalysis(pkm);
            if (pkm is not T pk || !la.Valid)
            {
                await FollowupAsync("❌ Generated Ditto failed legality check. Please try a different combination.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pk.ResetPartyStats();
            var displayName = $"Ditto ({language}, {stats})";
            await CreatePokemonHelper.QueuePokemonForTradeAsync(Context, pk, displayName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(SlashDittoModule<T>));
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
