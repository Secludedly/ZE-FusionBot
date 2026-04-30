using Discord;
using Discord.WebSocket;
using SysBot.Base;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Helper class for sending bot recovery notifications to Discord.
/// </summary>
public static class RecoveryNotificationHelper
{
    private static DiscordSocketClient? _client;
    private static ulong? _notificationChannelId;
    private static string _hubName = "Bot Hub";

    /// <summary>
    /// Initializes the recovery notification system with Discord client and channel.
    /// </summary>
    public static void Initialize(DiscordSocketClient client, ulong? notificationChannelId, string hubName)
    {
        _client = client;
        _notificationChannelId = notificationChannelId;
        _hubName = hubName;
    }

    /// <summary>
    /// Hooks up recovery events to Discord notifications.
    /// </summary>
    public static void HookRecoveryEvents<T>(BotRecoveryService<T> recoveryService) where T : class, IConsoleBotConfig
    {
        if (recoveryService == null || _client == null)
            return;

        recoveryService.BotCrashed += async (sender, e) => await OnBotCrashed(e);
        recoveryService.RecoveryAttempted += async (sender, e) => await OnRecoveryAttempted(e);
        recoveryService.RecoverySucceeded += async (sender, e) => await OnRecoverySucceeded(e);
        recoveryService.RecoveryFailed += async (sender, e) => await OnRecoveryFailed(e);
    }

    private static async Task OnBotCrashed(BotCrashEventArgs e)
    {
        var embed = new EmbedBuilder()
            .WithTitle("⚠️ Bot Crash Detected")
            .WithDescription($"**Bot**: {e.BotName}\n**Time**: {e.CrashTime:yyyy-MM-dd HH:mm:ss} UTC")
            .WithColor(Color.Orange)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .AddField("Status", "Attempting automatic recovery...", false)
            .WithFooter($"{_hubName} Recovery System")
            .Build();

        await SendNotificationAsync(embed);
    }

    private static async Task OnRecoveryAttempted(BotRecoveryEventArgs e)
    {
        if (!e.IsSuccess) // Only notify on attempts, not successes (handled separately)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🔄 Recovery Attempt")
                .WithDescription($"**Bot**: {e.BotName}\n**Attempt**: {e.AttemptNumber}")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter($"{_hubName} Recovery System")
                .Build();

            await SendNotificationAsync(embed);
        }
    }

    private static async Task OnRecoverySucceeded(BotRecoveryEventArgs e)
    {
        var embed = new EmbedBuilder()
            .WithTitle("✅ Bot Recovery Successful")
            .WithDescription($"**Bot**: {e.BotName}\n**Attempts**: {e.AttemptNumber}")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .AddField("Status", "Bot is now running normally", false)
            .WithFooter($"{_hubName} Recovery System")
            .Build();

        await SendNotificationAsync(embed);
    }

    private static async Task OnRecoveryFailed(BotRecoveryEventArgs e)
    {
        var embed = new EmbedBuilder()
            .WithTitle("❌ Bot Recovery Failed")
            .WithDescription($"**Bot**: {e.BotName}\n**Attempts**: {e.AttemptNumber}")
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .AddField("Reason", e.FailureReason ?? "Unknown error", false)
            .AddField("Action Required", "Manual intervention needed to restart this bot", false)
            .WithFooter($"{_hubName} Recovery System")
            .Build();

        await SendNotificationAsync(embed);
    }

    private static async Task SendNotificationAsync(Embed embed)
    {
        try
        {
            if (_client == null || !_notificationChannelId.HasValue)
                return;

            if (_client.GetChannel(_notificationChannelId.Value) is ISocketMessageChannel channel)
            {
                await channel.SendMessageAsync(embed: embed);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to send recovery notification to Discord: {ex.Message}", "RecoveryNotification");
        }
    }

    /// <summary>
    /// Sends a custom recovery notification.
    /// </summary>
    public static async Task SendCustomNotificationAsync(string title, string description, Color color)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter($"{_hubName} Recovery System")
            .Build();

        await SendNotificationAsync(embed);
    }

    /// <summary>
    /// Sends a recovery summary report.
    /// </summary>
    public static async Task SendRecoverySummaryAsync<T>(BotRunner<T> runner, BotRecoveryService<T> recoveryService) 
        where T : class, IConsoleBotConfig
    {
        var embedBuilder = new EmbedBuilder()
            .WithTitle("📊 Bot Recovery Summary")
            .WithColor(Color.Blue)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter($"{_hubName} Recovery System");

        foreach (var bot in runner.Bots)
        {
            var state = bot.GetRecoveryState();
            if (state != null && (state.ConsecutiveFailures > 0 || state.CrashHistory.Count > 0))
            {
                var status = bot.IsRunning ? "🟢 Running" : "🔴 Stopped";
                var fieldValue = $"Status: {status}\n" +
                                $"Crashes: {state.CrashHistory.Count}\n" +
                                $"Failed Attempts: {state.ConsecutiveFailures}";
                
                embedBuilder.AddField(bot.Bot.Connection.Name, fieldValue, true);
            }
        }

        if (embedBuilder.Fields.Count == 0)
        {
            embedBuilder.WithDescription("All bots are running normally with no recent crashes.");
        }

        await SendNotificationAsync(embedBuilder.Build());
    }
}