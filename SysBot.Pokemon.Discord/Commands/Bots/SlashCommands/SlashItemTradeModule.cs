using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class SlashItemTradeModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("item-trade", "Receive a Pokemon holding your requested item")]
    public async Task ItemTradeAsync(
        [Summary("item", "The item you want the Pokemon to hold")]
        [Autocomplete(typeof(ItemAutocompleteHandler))]
        string item
    )
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // LGPE and PLA do not support held items
        var typeName = typeof(T).Name;
        if (typeName is "PB7" or "PA8")
        {
            await RespondAsync("❌ Item trades are not available for this game (held items not supported).", ephemeral: true).ConfigureAwait(false);
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
            var hub = SysCord<T>.Runner.Hub;
            Species species = hub.Config.Trade.TradeConfiguration.ItemTradeSpecies == Species.None
                ? Species.Diglett
                : hub.Config.Trade.TradeConfiguration.ItemTradeSpecies;

            var speciesName = SpeciesName.GetSpeciesNameGeneration((ushort)species, 2, 8);
            var set = new ShowdownSet($"{speciesName} @ {item.Trim()}");
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GetLegal(template, out _);

            if (pkm == null)
            {
                await FollowupAsync("❌ Failed to generate the Pokemon. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

            if (pkm.HeldItem == 0)
            {
                await FollowupAsync($"❌ The item **{item}** wasn't recognized. Please choose a valid item from autocomplete.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var la = new LegalityAnalysis(pkm);
            if (pkm is not T pk || !la.Valid)
            {
                await FollowupAsync("❌ Generated Pokemon failed legality check. Please try a different item.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pk.ResetPartyStats();
            await CreatePokemonHelper.QueuePokemonForTradeAsync(Context, pk, $"{speciesName} @ {item}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(SlashItemTradeModule<T>));
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
