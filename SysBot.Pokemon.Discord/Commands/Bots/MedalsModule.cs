using Discord.Commands;
using Discord.WebSocket;
using SysBot.Pokemon;
using System.Threading.Tasks;
using System.Collections.Generic;

public class MedalsModule : ModuleBase<SocketCommandContext>
{
    // You can inject these if you have DI set up. For now, keep it simple.
    private readonly TradeCodeStorage _tradeCodeStorage = new();
    private readonly MilestoneService _milestones = new();

    [Command("medals")]
    [Alias("ml")]
    public async Task ShowMedalsCommand()
    {
        // Get total trade count and resolve earned thresholds
        int tradeCount = _tradeCodeStorage.GetTradeCount(Context.User.Id);
        List<int> earned = _milestones.GetEarnedMilestones(tradeCount);

        if (earned.Count == 0)
        {
            await ReplyAsync($"{Context.User.Username}, you haven't earned any medals yet. Start trading to earn your first one!");
            return;
        }

        // Send each medal as a nice embed (1/sec like before)
        foreach (var threshold in earned)
        {
            var embed = _milestones.BuildMedalEmbed(Context.User, threshold);
            await ReplyAsync(embed: embed);
            await Task.Delay(1000);
        }
    }
}
