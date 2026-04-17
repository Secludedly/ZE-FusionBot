using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class SlashQueueModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("queue-status", "Check your current position in the trade queue")]
    public async Task QueueStatusAsync()
    {
        var Info = SysCord<T>.Runner.Hub.Queues.Info;
        var userID = Context.User.Id;

        if (!Info.IsUserInQueue(userID))
        {
            await RespondAsync("You are not currently in the trade queue.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Find the user's first queued trade to get position
        var queued = Info.GetIsUserQueued(x => x.UserID == userID);
        TradeEntry<T>? trade = null;
        foreach (var t in queued)
        {
            trade = t;
            break;
        }

        if (trade == null)
        {
            await RespondAsync("You are not currently in the trade queue.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var position = Info.CheckPosition(userID, trade.UniqueTradeID, trade.Type);
        int pos = position.Position == -1 ? 1 : position.Position;
        var botct = Info.Hub.Bots.Count;
        var eta = pos > botct ? Info.Hub.Config.Queues.EstimateDelay(pos, botct) : 0;

        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithTitle("Your Queue Status")
            .AddField("Position", $"#{pos}", inline: true)
            .AddField("Estimated Wait", $"{eta:F1} min(s)", inline: true)
            .AddField("Active Bots", botct.ToString(), inline: true)
            .WithFooter($"ZE FusionBot {TradeBot.Version}")
            .Build();

        await RespondAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("queue-clear", "Remove yourself from the trade queue")]
    public async Task QueueClearAsync()
    {
        var Info = SysCord<T>.Runner.Hub.Queues.Info;
        var userID = Context.User.Id;

        var result = Info.ClearTrade(userID);

        string message = result switch
        {
            QueueResultRemove.Removed => "✅ Removed your pending trades from the queue.",
            QueueResultRemove.CurrentlyProcessing => "❌ Your trade is currently being processed and cannot be removed.",
            QueueResultRemove.CurrentlyProcessingRemoved => "✅ Removed pending trades from queue (one trade is currently processing and was left).",
            QueueResultRemove.NotInQueue => "You are not currently in the queue.",
            _ => "Unknown result."
        };

        await RespondAsync(message, ephemeral: true).ConfigureAwait(false);
    }
}
