using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using SysBot.Pokemon.Discord.Helpers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Discord.GatewayIntents;
using static SysBot.Pokemon.DiscordSettings;
using Discord.Net;

namespace SysBot.Pokemon.Discord;

public static class SysCordSettings
{
    public static PokeTradeHubConfig HubConfig { get; internal set; } = default!;

    public static DiscordManager Manager { get; internal set; } = default!;

    public static DiscordSettings Settings => Manager.Config;
}

public sealed partial class SysCord<T> where T : PKM, new()
{
    public readonly PokeTradeHub<T> Hub;
    private readonly ProgramConfig _config;
    private readonly Dictionary<ulong, ulong> _announcementMessageIds = [];
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commands;
    private readonly HashSet<ITradeBot> _connectedBots = [];
    private readonly object _botConnectionLock = new object();

    private readonly IServiceProvider _services;

    private readonly HashSet<string> _validCommands =
    [
     "BatchTrade", "Batchtrade", "batchTrade", "batchtradezip", "battlereadylist", "battlereadyrequest", "brl", "brr",
        "BT", "bt", "BTZ", "btz", "C", "c", "CLONE", "Clone", "clone", "CONVERT", "Convert", "convert", "D", "d", "deleteTradeCode",
        "Ditto", "ditto", "dittoTrade", "dittotrade", "dt", "DTC", "dtc", "DUMP", "Dump", "dump", "Egg", "egg", "er", "eventrequest",
        "f", "fix", "FixOT", "fixOT", "fixot", "Hello", "hello", "Help", "help", "Hi", "hi", "Hidetrade", "hideTrade", "hidetrade",
        "HT", "ht", "INFO", "info", "it", "Item", "item", "itemTrade", "joke", "Lc", "LC", "LCV", "lcv", "le", "LE", "Legalize", "legalize",
        "listevents", "Me", "me", "MysteryEgg", "mysteryegg", "PokePaste", "pokepaste", "PP", "pp", "QC", "Qc", "qc", "QS", "Qs", "qs",
        "queueClear", "queueclear", "queueStatus", "Random", "random", "RandomTeam", "randomteam", "rt", "SEED", "Seed", "seed",
        "specialrequestpokemon", "srp", "st", "status", "SURPRISE", "Surprise", "surprise", "surprisetrade", "T", "t", "tc", "TRADE",
        "Trade", "trade", "ts", "mm", "mysterymon", "Mysterymon", "MysteryMon", "homeready", "hrr", "hr", "MM", "HRR", "TV", "tv", "TT", "tt",
        "texttrade", "TextTrade", "Texttrade", "remotestart", "RemoteStart", "Remotestart", "startremote", "StartRemote", "Startremote"
    ];

    private readonly DiscordManager Manager;
    private readonly SemaphoreSlim _reconnectSemaphore = new(1, 1);
    private CancellationTokenSource? _reconnectCts;

    public SysCord(PokeBotRunner<T> runner, ProgramConfig config)
    {
        Runner = runner;
        Hub = runner.Hub;
        Manager = new DiscordManager(Hub.Config.Discord);
        _config = config;

        foreach (var bot in runner.Hub.Bots.ToArray())
        {
            if (bot is ITradeBot tradeBot)
            {
                tradeBot.ConnectionSuccess += async (sender, e) =>
                {
                    bool shouldHandleStart = false;

                    lock (_botConnectionLock)
                    {
                        _connectedBots.Add(tradeBot);
                        if (_connectedBots.Count == 1)
                        {
                            // First bot connected, handle start outside lock
                            shouldHandleStart = true;
                        }
                    }

                    if (shouldHandleStart)
                    {
                        await HandleBotStart();
                    }
                };

                tradeBot.ConnectionError += async (sender, ex) =>
                {
                    bool shouldHandleStop = false;

                    lock (_botConnectionLock)
                    {
                        _connectedBots.Remove(tradeBot);
                        if (_connectedBots.Count == 0)
                        {
                            // All bots disconnected, handle stop outside lock
                            shouldHandleStop = true;
                        }
                    }

                    if (shouldHandleStop)
                    {
                        await HandleBotStop();
                    }
                };
            }
        }

        SysCordSettings.Manager = Manager;
        SysCordSettings.HubConfig = Hub.Config;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // How much logging do you want to see?
            LogLevel = LogSeverity.Info,
            GatewayIntents = Guilds | GuildMessages | DirectMessages | GuildMembers | GuildPresences | MessageContent,

            // If you or another service needs to do anything with messages
            // (ex. checking Reactions, checking the content of edited/deleted messages),
            // you must set the MessageCacheSize. You may adjust the number as needed.
            //MessageCacheSize = 50,
        });

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = Guilds | GuildMessages | DirectMessages | GuildMembers | GuildPresences | MessageContent,
        });

        // ===== DM Relay Setup =====
        ulong forwardTargetId = 0;
        if (!string.IsNullOrWhiteSpace(Hub.Config.Discord.UserDMsToBotForwarder))
        {
            if (!ulong.TryParse(Hub.Config.Discord.UserDMsToBotForwarder, out forwardTargetId))
            {
                LogUtil.LogInfo("SysCord", $"Invalid UserDMsToBotForwarder ID: {Hub.Config.Discord.UserDMsToBotForwarder}");
            }
        }

        if (forwardTargetId != 0)
        {
            _ = new DMRelayService(_client, forwardTargetId);
            LogUtil.LogInfo("SysCord", $"DM relay active -> forwarding bot DMs to {forwardTargetId}");
        }


        _commands = new CommandService(new CommandServiceConfig
        {
            // Again, log level:
            LogLevel = LogSeverity.Info,

            DefaultRunMode = RunMode.Async,

            // There's a few more properties you can set,
            // for example, case-insensitive commands.
            CaseSensitiveCommands = false,
        });

        // Subscribe the logging handler to both the client and the CommandService.
        _client.Log += Log;
        _commands.Log += Log;

        // Setup your DI container.
        _services = ConfigureServices();

        _client.PresenceUpdated += Client_PresenceUpdated;

        _client.Disconnected += (exception) =>
        {
            LogUtil.LogText($"Discord connection lost. Reason: {exception?.Message ?? "Unknown"}");
            Task.Run(() => ReconnectAsync());
            return Task.CompletedTask;
        };
    }

    public static PokeBotRunner<T> Runner { get; private set; } = default!;

    // Track loading of Echo/Logging channels, so they aren't loaded multiple times.
    private bool MessageChannelsLoaded { get; set; }

    private async Task ReconnectAsync()
    {
        // Prevent multiple concurrent reconnection attempts
        if (!await _reconnectSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            LogUtil.LogText("Client is already attempting to reconnect.");
            return;
        }

        try
        {
            // Cancel any previous reconnection attempt
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            var cancellationToken = _reconnectCts.Token;

            const int maxRetries = 5;
            const int delayBetweenRetries = 5000; // 5 seconds
            const int initialDelay = 10000; // 10 seconds

            // Initial delay to allow Discord's automatic reconnection
            await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < maxRetries; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LogUtil.LogText("Reconnection attempt cancelled.");
                    return;
                }

                try
                {
                    if (_client.ConnectionState == ConnectionState.Connected)
                    {
                        LogUtil.LogText("Client reconnected automatically.");
                        return; // Already reconnected
                    }

                    // Check if the client is in the process of reconnecting
                    if (_client.ConnectionState == ConnectionState.Connecting)
                    {
                        LogUtil.LogText("Waiting for automatic reconnection...");
                        await Task.Delay(delayBetweenRetries, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await _client.StartAsync().ConfigureAwait(false);
                    LogUtil.LogText("Reconnected successfully.");
                    return;
                }
                catch (Exception ex)
                {
                    LogUtil.LogText($"Reconnection attempt {i + 1} failed: {ex.Message}");
                    if (i < maxRetries - 1)
                        await Task.Delay(delayBetweenRetries, cancellationToken).ConfigureAwait(false);
                }
            }

            // If all attempts to reconnect fail, stop and restart the bot
            LogUtil.LogText("Failed to reconnect after maximum attempts. Restarting the bot...");

            try
            {
                // Stop the bot cleanly
                if (_client.ConnectionState != ConnectionState.Disconnected)
                {
                    await _client.StopAsync().ConfigureAwait(false);
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                }

                // Restart the bot
                await _client.StartAsync().ConfigureAwait(false);
                LogUtil.LogText("Bot restarted successfully.");
            }
            catch (Exception ex)
            {
                LogUtil.LogText($"Failed to restart bot: {ex.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            LogUtil.LogText("Reconnection cancelled.");
        }
        catch (Exception ex)
        {
            LogUtil.LogText($"Unexpected error in ReconnectAsync: {ex.Message}");
        }
        finally
        {
            _reconnectSemaphore.Release();
        }
    }

    public async Task AnnounceBotStatus(string status, EmbedColorOption color)
    {
        if (!SysCordSettings.Settings.BotEmbedStatus)
            return;

        // Check if client is connected before attempting to announce
        if (_client == null || _client.ConnectionState != ConnectionState.Connected)
        {
            LogUtil.LogInfo("SysCord", "Cannot announce bot status: Discord client is not connected");
            return;
        }

        var botName = string.IsNullOrEmpty(SysCordSettings.HubConfig.BotName) ? "SysBot" : SysCordSettings.HubConfig.BotName;
        var fullStatusMessage = $"**Status**: {botName} is {status}!";
        var thumbnailUrl = status == "Online"
            ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/botgo.png"
            : "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/botstop.png";

        var embed = new EmbedBuilder()
            .WithTitle("Bot Status Report")
            .WithDescription(fullStatusMessage)
            .WithColor(EmbedColorConverter.ToDiscordColor(color))
            .WithThumbnailUrl(thumbnailUrl)
            .WithTimestamp(DateTimeOffset.Now)
            .Build();

        foreach (var channelId in SysCordSettings.Manager.WhitelistedChannels.List.Select(channel => channel.ID))
        {
            try
            {
                // Check connection state before each channel operation
                if (_client.ConnectionState != ConnectionState.Connected)
                {
                    LogUtil.LogInfo("SysCord", "Discord client disconnected during status announcement, aborting");
                    return;
                }

                ITextChannel? textChannel = _client.GetChannel(channelId) as ITextChannel;
                if (textChannel == null)
                {
                    var restChannel = await _client.Rest.GetChannelAsync(channelId);
                    textChannel = restChannel as ITextChannel;
                }

                if (textChannel != null)
                {
                    if (_announcementMessageIds.TryGetValue(channelId, out ulong messageId))
                    {
                        try
                        {
                            await textChannel.DeleteMessageAsync(messageId);
                        }
                        catch { }
                    }
                    var message = await textChannel.SendMessageAsync(embed: embed);
                    _announcementMessageIds[channelId] = message.Id;

                    if (SysCordSettings.Settings.ChannelStatus)
                    {
                        try
                        {
                            var emoji = status == "Online"
                                ? SysCordSettings.Settings.OnlineEmoji
                                : SysCordSettings.Settings.OfflineEmoji;
                            var currentName = textChannel.Name;
                            var updatedChannelName = $"{emoji}{TrimStatusEmoji(currentName)}";

                            if (currentName != updatedChannelName)
                            {
                                await textChannel.ModifyAsync(x => x.Name = updatedChannelName);
                            }
                        }
                        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                        {
                            LogUtil.LogInfo("SysCord", $"Cannot update channel name for {channelId}: Missing Manage Channel permission");
                        }
                        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.RequestEntityTooLarge)
                        {
                            LogUtil.LogInfo("SysCord", $"Cannot update channel name for {channelId}: Rate limited");
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogInfo("SysCord", $"Failed to update channel name for {channelId}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    LogUtil.LogInfo("SysCord", $"Channel {channelId} is not a text channel or could not be found");
                }
            }
            catch (ObjectDisposedException)
            {
                LogUtil.LogInfo("SysCord", "Discord client was disposed during status announcement, aborting");
                return;
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo("SysCord", $"AnnounceBotStatus: Exception in channel {channelId}: {ex.Message}");
            }
        }
    }

    public async Task HandleBotStart()
    {
        try
        {
            await AnnounceBotStatus("Online", EmbedColorOption.Green);
        }
        catch (Exception ex)
        {
            LogUtil.LogText($"HandleBotStart: Exception when announcing bot start: {ex.Message}");
        }
    }

    public async Task HandleBotStop()
    {
        try
        {
            await AnnounceBotStatus("Offline", EmbedColorOption.Red);
        }
        catch (Exception ex)
        {
            LogUtil.LogText($"HandleBotStop: Exception when announcing bot stop: {ex.Message}");
        }
    }

    private void InitializeRecoveryNotifications()
    {
        if (!Hub.Config.Recovery.EnableRecovery)
            return;

        // Get the recovery service from the runner
        var recoveryService = Runner.GetRecoveryService();
        if (recoveryService == null)
            return;

        // Determine the notification channel
        ulong? notificationChannelId = null;
        if (Manager.WhitelistedChannels.List.Count > 0)
        {
            // Use the first whitelisted channel for notifications
            notificationChannelId = Manager.WhitelistedChannels.List[0].ID;
        }

        // Initialize the recovery notification helper
        var hubName = string.IsNullOrEmpty(Hub.Config.BotName) ? "SysBot" : Hub.Config.BotName;
        RecoveryNotificationHelper.Initialize(_client, notificationChannelId, hubName);
        
        // Hook up the recovery events
        RecoveryNotificationHelper.HookRecoveryEvents(recoveryService);
        
        LogUtil.LogInfo("Recovery notifications initialized for Discord", "Recovery");
    }

    public async Task InitCommands()
    {
        var assembly = Assembly.GetExecutingAssembly();

        await _commands.AddModulesAsync(assembly, _services).ConfigureAwait(false);
        foreach (var t in assembly.DefinedTypes.Where(z => z.IsSubclassOf(typeof(ModuleBase<SocketCommandContext>)) && z.IsGenericType))
        {
            var genModule = t.MakeGenericType(typeof(T));
            await _commands.AddModuleAsync(genModule, _services).ConfigureAwait(false);
        }
        var modules = _commands.Modules.ToList();

        var blacklist = Hub.Config.Discord.ModuleBlacklist
            .Replace("Module", "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(z => z.Trim()).ToList();

        foreach (var module in modules)
        {
            var name = module.Name;
            name = name.Replace("Module", "");
            var gen = name.IndexOf('`');
            if (gen != -1)
                name = name[..gen];
            if (blacklist.Any(z => z.Equals(name, StringComparison.OrdinalIgnoreCase)))
                await _commands.RemoveModuleAsync(module).ConfigureAwait(false);
        }

        // Subscribe a handler to see if a message invokes a command.
        _client.Ready += LoadLoggingAndEcho;
        _client.MessageReceived += HandleMessageAsync;
    }

    public async Task MainAsync(string apiToken, CancellationToken token)
    {
        // Centralize the logic for commands into a separate method.
        await InitCommands().ConfigureAwait(false);

        // Login and connect.
        await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);

        var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
        Manager.Owner = app.Owner.Id;

        // Initialize recovery notifications if recovery is enabled
        InitializeRecoveryNotifications();
        try
        {
            // Wait infinitely so your bot actually stays connected.
            await MonitorStatusAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Handle the cancellation and perform cleanup tasks
            LogUtil.LogText("MainAsync: Bot is disconnecting due to cancellation...");
            await AnnounceBotStatus("Offline", EmbedColorOption.Red);
            LogUtil.LogText("MainAsync: Cleanup tasks completed.");
        }
        finally
        {
            // Cancel any ongoing reconnection attempts
            _reconnectCts?.Cancel();

            // Disconnect the bot
            await _client.StopAsync();

            // Dispose resources
            _reconnectCts?.Dispose();
            _reconnectSemaphore?.Dispose();
            _client?.Dispose();
        }
    }

    // If any services require the client, or the CommandService, or something else you keep on hand,
    // pass them as parameters into this method as needed.
    // If this method is getting pretty long, you can separate it out into another file using partials.
    private static ServiceProvider ConfigureServices()
    {
        var map = new ServiceCollection();//.AddSingleton(new SomeServiceClass());

        // When all your required services are in the collection, build the container.
        // Tip: There's an overload taking in a 'validateScopes' bool to make sure
        // you haven't made any mistakes in your dependency graph.
        return map.BuildServiceProvider();
    }

    // Example of a logging handler. This can be reused by add-ons
    // that ask for a Func<LogMessage, Task>.

    private static ConsoleColor GetTextColor(LogSeverity sv) => sv switch
    {
        LogSeverity.Critical => ConsoleColor.Red,
        LogSeverity.Error => ConsoleColor.Red,

        LogSeverity.Warning => ConsoleColor.Yellow,
        LogSeverity.Info => ConsoleColor.White,

        LogSeverity.Verbose => ConsoleColor.DarkGray,
        LogSeverity.Debug => ConsoleColor.DarkGray,
        _ => Console.ForegroundColor,
    };

    private static Task Log(LogMessage msg)
    {
        var text = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
        Console.ForegroundColor = GetTextColor(msg.Severity);
        Console.WriteLine($"{DateTime.Now,-19} {text}");
        Console.ResetColor();

        LogUtil.LogText($"SysCord: {text}");

        return Task.CompletedTask;
    }

    private static async Task RespondToThanksMessage(SocketUserMessage msg)
    {
        var channel = msg.Channel;
        await channel.TriggerTypingAsync();
        await Task.Delay(500).ConfigureAwait(false);

        var responses = new List<string>
        {
        "It is an honor for you to be in my presence.",
        "You good, homie.",
        "Always here to help people like you, even if you *are* funny looking.",
        "It's your pleasure and my burden.",
        "It was a little annoying, but I liked you enough, so yay you, I guess...",
        "You should really be showing appreciation to your parents instead.",
        "Yes... thank me, you bootlicker! :)",
        "Not a problem, you weak and meager human! :D",
        "If you were *truly* appreciative, you'd pay me in dance. Now dance, monkey!",
        "No hablo Espanol or something...",
        "Did you really just show me appreciation? Lol, I'm a bot, dummy. I don't care.",
        "No problem. Now give me your dog for the sacrifice."
    };

        var randomResponse = responses[new Random().Next(responses.Count)];
        var finalResponse = $"{randomResponse}";

        await msg.Channel.SendMessageAsync(finalResponse).ConfigureAwait(false);
    }

    private static string TrimStatusEmoji(string channelName)
    {
        var onlineEmoji = SysCordSettings.Settings.OnlineEmoji;
        var offlineEmoji = SysCordSettings.Settings.OfflineEmoji;

        if (channelName.StartsWith(onlineEmoji))
        {
            return channelName[onlineEmoji.Length..].Trim();
        }

        if (channelName.StartsWith(offlineEmoji))
        {
            return channelName[offlineEmoji.Length..].Trim();
        }

        return channelName.Trim();
    }

    private Task Client_PresenceUpdated(SocketUser user, SocketPresence before, SocketPresence after)
    {
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(SocketMessage arg)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (arg is not SocketUserMessage msg)
                return;

            if (msg.Channel is SocketGuildChannel guildChannel)
            {
                if (Manager.BlacklistedServers.Contains(guildChannel.Guild.Id))
                {
                    await guildChannel.Guild.LeaveAsync();
                    return;
                }
            }

            if (msg.Author.Id == _client.CurrentUser.Id || msg.Author.IsBot)
                return;

            string thanksText = msg.Content.ToLower();
            if (SysCordSettings.Settings.ReplyToThanks &&
                (thanksText.Contains("thank") ||
                (thanksText.Contains("arigato") ||
                (thanksText.Contains("amazing") ||
                (thanksText.Contains("incredible") ||
                (thanksText.Contains("i love you") ||
                (thanksText.Contains("awesome") ||
                (thanksText.Contains("thanx")
                ))))))))
            {
                await SysCord<T>.RespondToThanksMessage(msg).ConfigureAwait(false);
                return;
            }

            char[] allowedPrefixes = new[]
            {
    '$', '!', '.', '=', '%', '~', '-', '+', ',', '/', '?', '*', '^',
    '<', '>', '"', '`', '4', ';', ':'
};

            var correctPrefix = SysCordSettings.Settings.CommandPrefix;
            bool allowAnyPrefix = SysCordSettings.HubConfig.Discord.AllowAnyPrefix;
            string content = msg.Content;
            int argPos = 0;

            // --- STRICT MODE (AllowAnyPrefix = false) ---

            if (!allowAnyPrefix)
            {
                // If message doesn't start with ANY allowed prefix → it's just normal chat
                if (content.Length == 0 || !allowedPrefixes.Contains(content[0]))
                    return;

                // Now we know it STARTS with a prefix-like symbol.
                // If it's NOT the correct prefix → show the error.
                if (!content.StartsWith(correctPrefix))
                {
                    await SafeSendMessageAsync(msg.Channel,
                        $"Incorrect prefix! The correct prefix is `{correctPrefix}`");
                    return;
                }

                // Valid strict prefix
                argPos = correctPrefix.Length;
            }
            else
            {
                // AllowAnyPrefix = true → accept ANY allowed prefix OR the correct one.

                if (content.Length > 0 && allowedPrefixes.Contains(content[0]))
                {
                    argPos = 1;
                }
                else if (content.StartsWith(correctPrefix))
                {
                    argPos = correctPrefix.Length;
                }
                else
                {
                    // normal chatting → ignore
                    return;
                }
            }

            // --- HANDLE COMMAND ---
            var context = new SocketCommandContext(_client, msg);
            var handled = await TryHandleCommandAsync(msg, context, argPos);
            if (handled)
                return;


            if (msg.Attachments.Count > 0)
            {
                await TryHandleAttachmentAsync(msg).ConfigureAwait(false);
            }
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions) // Missing Permissions
        {
            await Log(new LogMessage(LogSeverity.Warning, "Command", $"Missing permissions to handle a message in channel {arg.Channel.Name}")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Log(new LogMessage(LogSeverity.Error, "Command", $"Unhandled exception in HandleMessageAsync: {ex.Message}", ex)).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 1000) // Log if processing takes more than 1 second
            {
                await Log(new LogMessage(LogSeverity.Warning, "Gateway",
                    $"A MessageReceived handler is blocking the gateway task. " +
                    $"Method: HandleMessageAsync, Execution Time: {stopwatch.ElapsedMilliseconds}ms, " +
                    $"Message Content: {arg.Content[..Math.Min(arg.Content.Length, 100)]}...")).ConfigureAwait(false);
            }
        }
    }

    private async Task LoadLoggingAndEcho()
    {
        if (MessageChannelsLoaded)
            return;

        // Restore Echoes
        EchoModule.RestoreChannels(_client, Hub.Config.Discord);

        // Subscribe to queue status changes
        QueueMonitor<T>.OnQueueStatusChanged = async (isFull, currentCount, maxCount) =>
        {
            await EchoModule.SendQueueStatusEmbedAsync(isFull, currentCount, maxCount).ConfigureAwait(false);
        };

        // Restore Logging
        LogModule.RestoreLogging(_client, Hub.Config.Discord);
        TradeStartModule<T>.RestoreTradeStarting(_client);

        // Don't let it load more than once in case of Discord hiccups.
        await Log(new LogMessage(LogSeverity.Info, "LoadLoggingAndEcho()", "Logging and Echo channels loaded!")).ConfigureAwait(false);
        MessageChannelsLoaded = true;

        var game = Hub.Config.Discord.BotGameStatus;
        if (!string.IsNullOrWhiteSpace(game))
            await _client.SetGameAsync(game).ConfigureAwait(false);
    }

    private async Task MonitorStatusAsync(CancellationToken token)
    {
        const int Interval = 20; // seconds

        // Check datetime for update
        UserStatus state = UserStatus.Idle;
        while (!token.IsCancellationRequested)
        {
            var time = DateTime.Now;
            var lastLogged = LogUtil.LastLogged;
            if (Hub.Config.Discord.BotColorStatusTradeOnly)
            {
                var recent = Hub.Bots.ToArray()
                    .Where(z => z.Config.InitialRoutine.IsTradeBot())
                    .MaxBy(z => z.LastTime);
                lastLogged = recent?.LastTime ?? time;
            }
            var delta = time - lastLogged;
            var gap = TimeSpan.FromSeconds(Interval) - delta;

            bool noQueue = !Hub.Queues.Info.GetCanQueue();
            if (gap <= TimeSpan.Zero)
            {
                var idle = noQueue ? UserStatus.DoNotDisturb : UserStatus.Idle;
                if (idle != state)
                {
                    state = idle;
                    await _client.SetStatusAsync(state).ConfigureAwait(false);
                }
                await Task.Delay(2_000, token).ConfigureAwait(false);
                continue;
            }

            var active = noQueue ? UserStatus.DoNotDisturb : UserStatus.Online;
            if (active != state)
            {
                state = active;
                await _client.SetStatusAsync(state).ConfigureAwait(false);
            }
            await Task.Delay(gap, token).ConfigureAwait(false);
        }
    }


    private async Task TryHandleAttachmentAsync(SocketMessage msg)
    {
        var mgr = Manager;
        var cfg = mgr.Config;
        if (cfg.ConvertPKMToShowdownSet && (cfg.ConvertPKMReplyAnyChannel || mgr.CanUseCommandChannel(msg.Channel.Id)))
        {
            if (msg is SocketUserMessage userMessage)
            {
                foreach (var att in msg.Attachments)
                    await msg.Channel.RepostPKMAsShowdownAsync(att, userMessage).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, SocketCommandContext context, int pos)
    {
        try
        {
            var AbuseSettings = Hub.Config.TradeAbuse;
            // Check if the user is in the bannedIDs list
            if (msg.Author is SocketGuildUser user && AbuseSettings.BannedIDs.List.Any(z => z.ID == user.Id))
            {
                await SysCord<T>.SafeSendMessageAsync(msg.Channel, "You are banned from using this bot.").ConfigureAwait(false);
                return true;
            }

            var mgr = Manager;
            if (!mgr.CanUseCommandUser(msg.Author.Id))
            {
                await SysCord<T>.SafeSendMessageAsync(msg.Channel, "You are not permitted to use this command.").ConfigureAwait(false);
                return true;
            }

            if (!mgr.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != mgr.Owner)
            {
                if (Hub.Config.Discord.ReplyCannotUseCommandInChannel)
                    await SysCord<T>.SafeSendMessageAsync(msg.Channel, "You can't use that command here.").ConfigureAwait(false);
                return true;
            }

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Executing command from {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);

            var result = await _commands.ExecuteAsync(context, pos, _services).ConfigureAwait(false);

            if (result.Error == CommandError.UnknownCommand)
                return false;

            if (!result.IsSuccess)
                await SafeSendMessageAsync(msg.Channel, result.ErrorReason).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            await Log(new LogMessage(LogSeverity.Error, "Command", $"Error executing command: {ex.Message}", ex)).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task SafeSendMessageAsync(IMessageChannel channel, string message)
    {
        try
        {
            await channel.SendMessageAsync(message).ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions) // Missing Permissions
        {
            await Log(new LogMessage(LogSeverity.Warning, "Command", $"Missing permissions to send message in channel {channel.Name}")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Log(new LogMessage(LogSeverity.Error, "Command", $"Error sending message: {ex.Message}", ex)).ConfigureAwait(false);
        }
    }
}
