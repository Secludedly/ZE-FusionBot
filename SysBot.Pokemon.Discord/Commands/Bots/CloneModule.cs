using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

[Summary("Queues new Clone trades")]
public class CloneModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    [Command("clone")]
    [Alias("c")]
    [Summary("Clones the Pokémon you show via Link Trade.")]
    [RequireQueueRole(nameof(DiscordManager.RolesClone))]
    public async Task CloneAsync(int code)
    {
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            await SafeReplyAsync("You already have an existing trade in the queue. Please wait until it is processed.");
            return;
        }

        var sig = Context.User.GetFavor();
        var lgcode = Info.GetRandomLGTradeCode();

        _ = QueueHelper<T>.AddToQueueAsync(
            Context,
            code,
            Context.User.Username,
            sig,
            new T(),
            PokeRoutineType.Clone,
            PokeTradeType.Clone,
            Context.User,
            false, 1, 1, false, false, lgcode
        );

        var confirmationMessage = await SafeReplyAsync("Processing your clone request...");

        _ = SafeDeleteMessagesAsync(Context.Message as IUserMessage, confirmationMessage, 2000);
    }

    [Command("clone")]
    [Alias("c")]
    [Summary("Clones the Pokémon you show via Link Trade.")]
    [RequireQueueRole(nameof(DiscordManager.RolesClone))]
    public async Task CloneAsync([Remainder] string code)
    {
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            await SafeReplyAsync("You already have an existing trade in the queue. Please wait until it is processed.");
            return;
        }

        int tradeCode = Util.ToInt32(code);
        var sig = Context.User.GetFavor();
        var lgcode = Info.GetRandomLGTradeCode();

        _ = QueueHelper<T>.AddToQueueAsync(
            Context,
            tradeCode == 0 ? Info.GetRandomTradeCode(userID, Context.Channel, Context.User) : tradeCode,
            Context.User.Username,
            sig,
            new T(),
            PokeRoutineType.Clone,
            PokeTradeType.Clone,
            Context.User,
            false, 1, 1, false, false, lgcode
        );

        var confirmationMessage = await SafeReplyAsync("Processing your clone request...");
        _ = SafeDeleteMessagesAsync(Context.Message as IUserMessage, confirmationMessage, 2000);
    }

    [Command("clone")]
    [Alias("c")]
    [Summary("Clones the Pokémon you show via Link Trade.")]
    [RequireQueueRole(nameof(DiscordManager.RolesClone))]
    public Task CloneAsync()
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        return CloneAsync(code);
    }

    [Command("cloneList")]
    [Alias("cl", "cq")]
    [Summary("Prints the users in the Clone queue.")]
    [RequireSudo]
    public async Task GetListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.Clone);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });

        await SafeReplyAsync("These are the users who are currently waiting:", embed: embed.Build());
    }

    #region Helpers

    private async Task<IUserMessage?> SafeReplyAsync(string text, Embed? embed = null)
    {
        try
        {
            return await ReplyAsync(text, embed: embed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(CloneModule<T>));
            return null;
        }
    }

    private async Task SafeDeleteMessagesAsync(IUserMessage? original, IUserMessage? confirmation, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs).ConfigureAwait(false);

            if (original != null)
            {
                try { await original.DeleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogUtil.LogSafe(ex, nameof(CloneModule<T>)); }
            }

            if (confirmation != null)
            {
                try { await confirmation.DeleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogUtil.LogSafe(ex, nameof(CloneModule<T>)); }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(CloneModule<T>));
        }
    }

    #endregion
}
