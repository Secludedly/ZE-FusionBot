using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class SlashMysteryModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("mystery-egg", "Receive a random shiny 6IV egg (SWSH/BDSP/SV only)")]
    public async Task MysteryEggAsync()
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var typeName = typeof(T).Name;
        if (typeName is not ("PK8" or "PB8" or "PK9"))
        {
            await RespondAsync("❌ Mystery Eggs are only available for SWSH, BDSP, and SV (current game does not support breeding).", ephemeral: true).ConfigureAwait(false);
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
            var egg = MysteryEggModule<T>.GenerateLegalMysteryEgg();
            if (egg == null)
            {
                await FollowupAsync("❌ Failed to generate a mystery egg. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await CreatePokemonHelper.QueuePokemonForTradeAsync(Context, egg, "Mystery Egg", isMysteryEgg: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(SlashMysteryModule<T>));
            await FollowupAsync("❌ An error occurred while generating the mystery egg. Please try again.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [SlashCommand("mystery-mon", "Receive a fully randomized mystery Pokemon")]
    public async Task MysteryMonAsync()
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
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
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var pk = MysteryMonModule<T>.GenerateMysteryMon(cancel.Token);
            if (pk == null)
            {
                await FollowupAsync("❌ Failed to generate a mystery Pokemon. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var speciesName = GameInfo.GetStrings("en").specieslist[pk.Species];
            await CreatePokemonHelper.QueuePokemonForTradeAsync(Context, pk, speciesName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(SlashMysteryModule<T>));
            await FollowupAsync("❌ An error occurred while generating the mystery Pokemon. Please try again.", ephemeral: true).ConfigureAwait(false);
        }
    }
}
