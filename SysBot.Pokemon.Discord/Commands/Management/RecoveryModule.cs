using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class RecoveryModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static IPokeBotRunner? Runner => SysCord<T>.Runner;

    [Command("recovery")]
    [Alias("recover")]
    [Summary("Shows the recovery status of all bots.")]
    [RequireSudo]
    public async Task ShowRecoveryStatusAsync()
    {
        if (Runner == null)
        {
            await ReplyAsync("Bot runner is not initialized.").ConfigureAwait(false);
            return;
        }

        if (Runner is not PokeBotRunner<T> runner)
        {
            await ReplyAsync("Recovery service is not available for this bot runner type.").ConfigureAwait(false);
            return;
        }
        
        var recoveryService = runner.GetRecoveryService();
        
        if (recoveryService == null)
        {
            await ReplyAsync("Recovery service is not enabled.").ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Bot Recovery Status")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.Now);

        var hasRecoveryData = false;
        foreach (var bot in Runner.Bots)
        {
            var state = bot.GetRecoveryState();
            if (state != null && (state.ConsecutiveFailures > 0 || state.CrashHistory.Count > 0))
            {
                hasRecoveryData = true;
                var status = bot.IsRunning ? "🟢 Running" : "🔴 Stopped";
                if (state.IsRecovering)
                    status = "🟠 Recovering";

                var fieldValue = $"Status: {status}\n" +
                                $"Crashes: {state.CrashHistory.Count}\n" +
                                $"Failed Attempts: {state.ConsecutiveFailures}";
                
                if (state.LastRecoveryAttempt.HasValue)
                {
                    fieldValue += $"\nLast Recovery: {state.LastRecoveryAttempt.Value:HH:mm:ss}";
                }
                
                embed.AddField(bot.Bot.Connection.Name, fieldValue, true);
            }
        }

        if (!hasRecoveryData)
        {
            embed.WithDescription("All bots are running normally with no recovery history.");
        }

        await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("recoveryReset")]
    [Alias("resetRecovery")]
    [Summary("Resets the recovery state for a specific bot.")]
    [RequireSudo]
    public async Task ResetRecoveryAsync([Remainder] string botName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botName);
        
        if (Runner == null)
        {
            await ReplyAsync("Bot runner is not initialized.").ConfigureAwait(false);
            return;
        }

        if (Runner is not PokeBotRunner<T> runner)
        {
            await ReplyAsync("Recovery service is not available for this bot runner type.").ConfigureAwait(false);
            return;
        }
        
        var recoveryService = runner.GetRecoveryService();
        
        if (recoveryService == null)
        {
            await ReplyAsync("Recovery service is not enabled.").ConfigureAwait(false);
            return;
        }

        var bot = Runner.Bots.FirstOrDefault(b => b.Bot.Connection.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
        if (bot == null)
        {
            await ReplyAsync($"Bot '{botName}' not found.").ConfigureAwait(false);
            return;
        }

        recoveryService.ResetRecoveryState(bot.Bot.Connection.Name);
        await ReplyAsync($"Recovery state for bot '{bot.Bot.Connection.Name}' has been reset.").ConfigureAwait(false);
    }

    [Command("recoveryToggle")]
    [Alias("toggleRecovery")]
    [Summary("Enables or disables the recovery system.")]
    [RequireSudo]
    public async Task ToggleRecoveryAsync()
    {
        if (Runner == null)
        {
            await ReplyAsync("Bot runner is not initialized.").ConfigureAwait(false);
            return;
        }

        if (Runner is not PokeBotRunner<T> runner)
        {
            await ReplyAsync("Recovery service is not available for this bot runner type.").ConfigureAwait(false);
            return;
        }
        
        var config = Runner.Config.Recovery;
        config.EnableRecovery = !config.EnableRecovery;

        var status = config.EnableRecovery ? "enabled" : "disabled";
        await ReplyAsync($"Recovery system has been {status}.").ConfigureAwait(false);
        
        // Update the recovery service state
        if (config.EnableRecovery)
            runner.RecoveryService?.EnableRecovery();
        else
            runner.RecoveryService?.DisableRecovery();
    }

    [Command("recoveryConfig")]
    [Alias("recoveryCfg")]
    [Summary("Shows the current recovery configuration.")]
    [RequireSudo]
    public async Task ShowRecoveryConfigAsync()
    {
        if (Runner == null)
        {
            await ReplyAsync("Bot runner is not initialized.").ConfigureAwait(false);
            return;
        }

        var config = Runner.Config.Recovery;
        
        var embed = new EmbedBuilder()
            .WithTitle("Recovery Configuration")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.Now)
            .AddField("Enabled", config.EnableRecovery ? "✅ Yes" : "❌ No", true)
            .AddField("Max Attempts", config.MaxRecoveryAttempts, true)
            .AddField("Initial Delay", $"{config.InitialRecoveryDelaySeconds}s", true)
            .AddField("Max Delay", $"{config.MaxRecoveryDelaySeconds}s", true)
            .AddField("Backoff Multiplier", $"{config.BackoffMultiplier}x", true)
            .AddField("Crash Window", $"{config.CrashHistoryWindowMinutes} min", true)
            .AddField("Max Crashes/Window", config.MaxCrashesInWindow, true)
            .AddField("Recover Intentional", config.RecoverIntentionalStops ? "✅" : "❌", true)
            .AddField("Stable Uptime", $"{config.MinimumStableUptimeSeconds}s", true);

        await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
    }
}