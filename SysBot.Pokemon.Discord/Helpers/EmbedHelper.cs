using Discord;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class EmbedHelper
{
    public static async Task SendNotificationEmbedAsync(IUser user, string message)
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("Notice...")
                .WithDescription(message)
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-legalityerror.gif")
                .WithColor(Color.Red)
                .Build();

            await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending notification embed.", "SendNotificationEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending notification embed: {ex.Message}", "SendNotificationEmbedAsync");
        }
    }

    public static async Task SendTradeCanceledEmbedAsync(IUser user, string reason)
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("Trade Canceled...")
                .WithDescription($"Your trade was canceled.\n**Reason**: {reason}")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-uhoherror.gif")
                .WithColor(Color.Red)
                .Build();

            await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade canceled embed.", "SendTradeCanceledEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade canceled embed: {ex.Message}", "SendTradeCanceledEmbedAsync");
        }
    }

    public static async Task SendTradeCodeEmbedAsync(IUser user, int code)
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("Here's your Link Trade Code!")
                .WithDescription($"# {code:0000 0000}\n*I'll notify you when your trade starts!*")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-tradecode.gif")
                .WithColor(Color.Gold)
                .Build();

            await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade code embed.", "SendTradeCodeEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade code embed: {ex.Message}", "SendTradeCodeEmbedAsync");
        }
    }

    public static async Task SendTradeFinishedEmbedAsync<T>(IUser user, string message, T pk, bool isMysteryEgg)
        where T : PKM, new()
    {
        try
        {
            string thumbnailUrl;

            if (isMysteryEgg)
            {
                thumbnailUrl = "https://raw.githubusercontent.com/hexbyt3/sprites/main/mysteryegg3.png";
            }
            else
            {
                thumbnailUrl = TradeExtensions<T>.PokeImg(pk, false, true, null);
            }

            var embed = new EmbedBuilder()
                .WithTitle("Trade Completed!")
                .WithDescription(message)
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl(thumbnailUrl)
                .WithColor(Color.Teal)
                .Build();

            await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade finished embed.", "SendTradeFinishedEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade finished embed: {ex.Message}", "SendTradeFinishedEmbedAsync");
        }
    }

    public static async Task SendTradeInitializingEmbedAsync(IUser user, string speciesName, int code, bool isMysteryEgg, string? message = null)
    {
        try
        {
            if (isMysteryEgg)
            {
                speciesName = "**Mystery Egg**";
            }

            var embed = new EmbedBuilder()
                .WithTitle("Loading the Trade Menu...")
                .WithDescription($"**Pokemon**: {speciesName}\n**Trade Code**: {code:0000 0000}")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-initializingbot.gif")
                .WithColor(Color.Green);

            if (!string.IsNullOrEmpty(message))
            {
                embed.WithDescription($"{embed.Description}\n\n{message}");
            }

            var builtEmbed = embed.Build();
            await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade initializing embed.", "SendTradeInitializingEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade initializing embed: {ex.Message}", "SendTradeInitializingEmbedAsync");
        }
    }

    public static async Task SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, string? message = null)
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle($"Now Searching...")
                .WithDescription($"**Waiting For**: {trainerName}\n**My IGN**: {inGameName}\n\n**Insert your Trade Code!**")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-nowsearching.gif")
                .WithColor(Color.DarkGreen);

            if (!string.IsNullOrEmpty(message))
            {
                embed.WithDescription($"{embed.Description}\n\n{message}");
            }

            var builtEmbed = embed.Build();
            await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade searching embed.", "SendTradeSearchingEmbedAsync");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade searching embed: {ex.Message}", "SendTradeSearchingEmbedAsync");
        }
    }
}
