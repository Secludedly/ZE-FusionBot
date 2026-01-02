using Discord;
using Discord.Net;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class EmbedHelper
{
    // Rate limiter for DM operations to prevent "opening DMs too fast" errors
    private static readonly SemaphoreSlim _dmRateLimiter = new(1, 1);
    private static readonly ConcurrentDictionary<ulong, IDMChannel> _dmChannels = new();
    private static DateTime _lastDmTime = DateTime.MinValue;
    private const int MinDmDelayMs = 2000; // Minimum 2 seconds between DMs

    private static async Task<IDMChannel?> GetOrCreateDMAsync(IUser user)
    {
        try
        {
            if (_dmChannels.TryGetValue(user.Id, out var channel))
                return channel;

            // Enforce minimum delay before creating a new DM channel to respect Discord rate limits
            var timeSinceLastDm = DateTime.Now - _lastDmTime;
            if (timeSinceLastDm.TotalMilliseconds < MinDmDelayMs)
            {
                var remainingDelay = MinDmDelayMs - (int)timeSinceLastDm.TotalMilliseconds;
                await Task.Delay(remainingDelay).ConfigureAwait(false);
            }

            var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
            _dmChannels[user.Id] = dm;
            _lastDmTime = DateTime.Now;
            return dm;
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast when creating DM channel for user {user.Username} ({user.Id}). Waiting 5 seconds...", "GetOrCreateDMAsync");
            await Task.Delay(5000).ConfigureAwait(false);

            // Try one more time after the delay
            try
            {
                var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
                _dmChannels[user.Id] = dm;
                _lastDmTime = DateTime.Now;
                return dm;
            }
            catch (Exception retryEx)
            {
                LogUtil.LogError($"Failed to create DM channel after retry: {retryEx.Message}", "GetOrCreateDMAsync");
                return null;
            }
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client is disposed. Cannot create DM channel.", "GetOrCreateDMAsync");
            return null;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to create DM channel: {ex.Message}", "GetOrCreateDMAsync");
            return null;
        }
    }

    public static async Task SendNotificationEmbedAsync(IUser user, string message)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping notification.", "SendNotificationEmbedAsync");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Notice...")
                .WithDescription(message)
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-legalityerror.gif")
                .WithColor(Color.Red)
                .Build();

            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);
            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending notification embed.", "SendNotificationEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendNotificationEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending notification embed: {ex.Message}", "SendNotificationEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendTradeCanceledEmbedAsync(IUser user, string reason)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade canceled message.", "SendTradeCanceledEmbedAsync");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Trade Canceled...")
                .WithDescription($"Your trade was canceled.\n**Reason**: {reason}")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-uhoherror.gif")
                .WithColor(Color.Red)
                .Build();

            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);
            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade canceled embed.", "SendTradeCanceledEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeCanceledEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade canceled embed: {ex.Message}", "SendTradeCanceledEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendTradeCodeEmbedAsync(IUser user, int code)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade code message.", "SendTradeCodeEmbedAsync");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Here's your Link Trade Code!")
                .WithDescription($"# {code:0000 0000}\n*I'll notify you when your trade starts!*")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-tradecode.gif")
                .WithColor(Color.Gold)
                .Build();

            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);
            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade code embed.", "SendTradeCodeEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeCodeEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade code embed: {ex.Message}", "SendTradeCodeEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
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
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade initializing message.", "SendTradeInitializingEmbedAsync");
                return;
            }

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
            await dm.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade initializing embed.", "SendTradeInitializingEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeInitializingEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade initializing embed: {ex.Message}", "SendTradeInitializingEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, string? message = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
            {
                LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping trade searching message.", "SendTradeSearchingEmbedAsync");
                return;
            }

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
            await dm.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
            _lastDmTime = DateTime.Now;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client disposed when sending trade searching embed.", "SendTradeSearchingEmbedAsync");
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast! User: {user.Username} ({user.Id})", "SendTradeSearchingEmbedAsync");
            _dmChannels.TryRemove(user.Id, out _);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error sending trade searching embed: {ex.Message}", "SendTradeSearchingEmbedAsync");
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }
}
