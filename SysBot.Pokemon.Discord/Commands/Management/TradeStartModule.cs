using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public class TradeStartModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private class TradeStartAction(
        ulong ChannelId,
        Action<PokeRoutineExecutorBase, PokeTradeDetail<T>> messager,
        string channel)
        : ChannelAction<PokeRoutineExecutorBase, PokeTradeDetail<T>>(ChannelId, messager, channel);

    private static DiscordSocketClient? _discordClient;

    private static readonly Dictionary<ulong, TradeStartAction> Channels = [];

    // GLOBAL GUARDS
    private static bool _forwarderRegistered;
    private static readonly HashSet<int> _startedTrades = [];

    private static void Remove(TradeStartAction entry)
    {
        Channels.Remove(entry.ChannelID);
        SysCord<T>.Runner.Hub.Queues.Forwarders.Remove(entry.Action);
    }

#pragma warning disable RCS1158
    public static void RestoreTradeStarting(DiscordSocketClient discord)
    {
        _discordClient = discord;

        var cfg = SysCordSettings.Settings;
        foreach (var ch in cfg.TradeStartingChannels)
        {
            if (discord.GetChannel(ch.ID) is ISocketMessageChannel c)
                AddLogChannel(c, ch.ID);
        }

        LogUtil.LogInfo("Discord", "Added Trade Start Notification to Discord channel(s) on Bot startup.");
    }
#pragma warning restore RCS1158

    public static bool IsStartChannel(ulong cid) => Channels.ContainsKey(cid);

    [Command("startHere")]
    [Summary("Makes the bot log trade starts to the channel.")]
    [RequireSudo]
    public async Task AddLogAsync()
    {
        var c = Context.Channel;
        var cid = c.Id;

        if (Channels.ContainsKey(cid))
        {
            await ReplyAsync("Already logging here.").ConfigureAwait(false);
            return;
        }

        AddLogChannel(c, cid);

        SysCordSettings.Settings.TradeStartingChannels
            .AddIfNew([GetReference(Context.Channel)]);

        await ReplyAsync("Added Start Notification output to this channel!").ConfigureAwait(false);
    }

    private static void AddLogChannel(ISocketMessageChannel c, ulong cid)
    {
        async void Logger(PokeRoutineExecutorBase bot, PokeTradeDetail<T> detail)
        {
            if (detail.Type == PokeTradeType.Random)
                return;

            // prevent duplicate embeds per trade
            lock (_startedTrades)
            {
                if (_startedTrades.Contains(detail.ID))
                    return;

                _startedTrades.Add(detail.ID);
            }

#pragma warning disable CS8602
            var user = _discordClient?.GetUser(detail.Trainer.ID);
#pragma warning restore CS8602
            if (user == null)
                return;

            string speciesName = detail.TradeData != null
                ? GameInfo.Strings.Species[detail.TradeData.Species]
                : "";

            string ballImgUrl =
                "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-uhoherror.gif";

            if (detail.TradeData != null &&
                detail.Type is not (PokeTradeType.Clone or PokeTradeType.Dump or PokeTradeType.Seed or PokeTradeType.FixOT))
            {
                var ballName = GameInfo.GetStrings("en").balllist[detail.TradeData.Ball]
                    .Replace(" ", "")
                    .Replace("(LA)", "")
                    .ToLower();

                ballName = ballName == "pokéball"
                    ? "pokeball"
                    : (ballName.Contains("(la)") ? "la" + ballName : ballName);

                ballImgUrl =
                    $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/AltBallImg/28x28/{ballName}.png";
            }

            string tradeTitle = detail.IsMysteryEgg
                ? "Mystery Egg"
                : detail.Type switch
                {
                    PokeTradeType.Clone => "Cloned Pokémon",
                    PokeTradeType.Dump => "Pokémon Dump",
                    PokeTradeType.FixOT => "Cloned Pokémon (Fixing OT Info)",
                    PokeTradeType.Seed => "Cloned Pokémon (Special Request)",
                    _ => speciesName
                };

            string embedImageUrl = detail.IsMysteryEgg
                ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg3.png"
                : detail.Type switch
                {
                    PokeTradeType.Clone => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Cloning.png",
                    PokeTradeType.Dump => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Dumping.png",
                    PokeTradeType.FixOT => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/FixOTing.png",
                    PokeTradeType.Seed => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Seeding.png",
                    _ => TradeExtensions<T>.PokeImg(detail.TradeData!, false, true)
                };

            var (r, g, b) = await GetDominantColorAsync(embedImageUrl);

            string footerText =
                detail.Type is PokeTradeType.Clone or PokeTradeType.Dump or PokeTradeType.Seed or PokeTradeType.FixOT
                    ? "Starting trade..."
                    : $"Starting trade...\n{tradeTitle} is on its way!";

            string authorText;
            string? authorIconUrl;

            if (detail.IsHiddenTrade)
            {
                authorText = "Up Next: Hidden User";
                authorIconUrl = "https://i.imgur.com/pTqYqXP.gif";
            }
            else
            {
                authorText = $"Up Next: {user.Username}";
                authorIconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            }

            var embed = new EmbedBuilder()
                .WithColor(new DiscordColor(r, g, b))
                .WithThumbnailUrl(embedImageUrl)
                .WithAuthor(authorText, authorIconUrl)
                .WithDescription($"**Receiving**: {tradeTitle}\n**Trade ID**: {detail.ID}")
                .WithFooter($"{footerText}\u200B", ballImgUrl)
                .Build();

            await c.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }

        // REGISTER FORWARDER ONCE EVER
        if (!_forwarderRegistered)
        {
            SysCord<T>.Runner.Hub.Queues.Forwarders.Add(Logger);
            _forwarderRegistered = true;
        }

        Channels[cid] = new TradeStartAction(cid, Logger, c.Name);
    }

    private RemoteControlAccess GetReference(IChannel channel) => new()
    {
        ID = channel.Id,
        Name = channel.Name,
        Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-HH:mm:ss}",
    };

    public static async Task<(int R, int G, int B)> GetDominantColorAsync(string imagePath)
    {
        try
        {
            using var image = await LoadImageAsync(imagePath);
            var colorCount = new Dictionary<Color, int>();

            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.A < 128 || pixel.GetBrightness() > 0.9)
                        continue;

                    var key = Color.FromArgb(
                        pixel.R / 10 * 10,
                        pixel.G / 10 * 10,
                        pixel.B / 10 * 10);

                    colorCount[key] = colorCount.TryGetValue(key, out var v) ? v + 1 : 1;
                }

            if (colorCount.Count == 0)
                return (255, 255, 255);

            var dom = colorCount.MaxBy(k => k.Value).Key;
            return (dom.R, dom.G, dom.B);
        }
        catch
        {
            return (255, 255, 255);
        }
    }

    private static async Task<Bitmap> LoadImageAsync(string imagePath)
    {
        if (!imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new Bitmap(imagePath);

        using var http = new HttpClient();
        using var stream = await http.GetStreamAsync(imagePath);
        return new Bitmap(stream);
    }
}
