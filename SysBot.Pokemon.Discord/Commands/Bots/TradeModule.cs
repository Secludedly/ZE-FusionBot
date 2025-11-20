using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static SysBot.Pokemon.TradeSettings.TradeSettingsCategory;
using System.Collections.Concurrent;

namespace SysBot.Pokemon.Discord;

[Summary("Queues new Link Code trades")]
public partial class TradeModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;
    private static string Prefix => SysCordSettings.Settings.CommandPrefix;

    #region Medal Achievement Command

    [Command("medals")]
    [Alias("ml")]
    [Summary("Shows your current trade count and medal status")]
    public async Task ShowMedalsCommand()
    {
        var tradeCodeStorage = new TradeCodeStorage();
        int totalTrades = tradeCodeStorage.GetTradeCount(Context.User.Id);

        if (totalTrades == 0)
        {
            await ReplyAsync($"{Context.User.Username}, you haven't made any trades yet.\nStart trading to earn your first medal!");
            return;
        }

        int currentMilestone = MedalHelpers.GetCurrentMilestone(totalTrades);
        var embed = MedalHelpers.CreateMedalsEmbed(Context.User, currentMilestone, totalTrades);
        await Context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    #endregion

    #region Trade Commands

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return ProcessTradeAsync(code, content);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
        => ProcessTradeAsync(code, content);

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you the provided Pokémon file.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsyncAttach([Summary("Trade Code")] int code, [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return ProcessTradeAttachmentAsync(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you the attached file.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        var sig = Context.User.GetFavor();

        await Task.Run(async () =>
        {
            await ProcessTradeAttachmentAsync(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you a Pokémon without showing trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return ProcessTradeAsync(code, content, isHiddenTrade: true);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you a Pokémon without showing trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
        => ProcessTradeAsync(code, content, isHiddenTrade: true);

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you the provided file without showing trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsyncAttach([Summary("Trade Code")] int code, [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return ProcessTradeAttachmentAsync(code, sig, Context.User, isHiddenTrade: true, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you the attached file without showing trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task HideTradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        var sig = Context.User.GetFavor();

        await ProcessTradeAttachmentAsync(code, sig, Context.User, isHiddenTrade: true, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Makes the bot trade the mentioned user the attached file.")]
    [RequireSudo]
    public async Task TradeAsyncAttachUser([Summary("Trade Code")] int code, [Remainder] string _)
    {
        if (Context.Message.MentionedUsers.Count > 1)
        {
            await ReplyAsync("Too many mentions. Queue one user at a time.").ConfigureAwait(false);
            return;
        }

        if (Context.Message.MentionedUsers.Count == 0)
        {
            await ReplyAsync("A user must be mentioned in order to do this.").ConfigureAwait(false);
            return;
        }

        var usr = Context.Message.MentionedUsers.ElementAt(0);
        var sig = usr.GetFavor();
        await ProcessTradeAttachmentAsync(code, sig, usr).ConfigureAwait(false);
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Makes the bot trade the mentioned user the attached file.")]
    [RequireSudo]
    public Task TradeAsyncAttachUser([Remainder] string _)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        return TradeAsyncAttachUser(code, _);
    }

    #endregion

    #region Special Trade Commands

    [Command("egg")]
    [Alias("Egg")]
    [Summary("Trades an egg generated from the provided Pokémon name.")]
    public async Task TradeEgg([Remainder] string egg)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID);
        await TradeEggAsync(code, egg).ConfigureAwait(false);
    }

    [Command("egg")]
    [Alias("Egg")]
    [Summary("Trades an egg generated from the provided Pokémon name.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeEggAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        content = BatchCommandNormalizer.NormalizeBatchCommands(content);
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var template = AutoLegalityWrapper.GetTemplate(set);

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

                // Generate the egg using ALM's GenerateEgg method
                var pkm = sav.GenerateEgg(template, out var result);

                if (result != LegalizationResult.Regenerated)
                {
                    var reason = result == LegalizationResult.Timeout
                        ? "Egg generation took too long and the bot timed out."
                        : "Failed to generate egg from the provided set.\nTry to remove possible illegal lines and try again.";
                    await Helpers<T>.ReplyAndDeleteAsync(Context, reason, 6);
                    return;
                }

                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk)
                {
                    await Helpers<T>.ReplyAndDeleteAsync(Context, "Oops! I wasn't able to create an egg for that.\nTry to remove possible illegal lines and try again", 6);
                    return;
                }

                var sig = Context.User.GetFavor();
                await Helpers<T>.AddTradeToQueueAsync(Context, code, Context.User.Username, pk, sig, Context.User).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                await Helpers<T>.ReplyAndDeleteAsync(Context, "An error occurred while processing the request.", 5);
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT()
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessFixOTAsync(code);
    }

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT([Summary("Trade Code")] int code)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        await ProcessFixOTAsync(code);
    }

    private async Task ProcessFixOTAsync(int code)
    {
        var trainerName = Context.User.Username;
        var sig = Context.User.GetFavor();
        var lgcode = Info.GetRandomLGTradeCode();

        await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, new T(),
            PokeRoutineType.FixOT, PokeTradeType.FixOT, Context.User, false, 1, 1, false, false, lgcode: lgcode).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
    public async Task DittoTrade([Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword,
        [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessDittoTradeAsync(code, keyword, language, nature);
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
    public async Task DittoTrade([Summary("Trade Code")] int code,
        [Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword,
        [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        await ProcessDittoTradeAsync(code, keyword, language, nature);
    }

    private async Task ProcessDittoTradeAsync(int code, string keyword, string language, string nature)
    {
        keyword = keyword.ToLower().Trim();

        if (!Enum.TryParse(language, true, out LanguageID lang))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context, $"Couldn't recognize language: {language}.", 5);
            return;
        }

        nature = nature.Trim()[..1].ToUpper() + nature.Trim()[1..].ToLower();
        var set = new ShowdownSet($"{keyword}(Ditto)\nLanguage: {lang}\nNature: {nature}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);

        if (pkm == null)
        {
            await ReplyAsync("Set took too long to legalize.");
            return;
        }

        TradeExtensions<T>.DittoTrade((T)pkm);
        var la = new LegalityAnalysis(pkm);

        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"Oops! {reason} Here's my best attempt for that Ditto!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();

        // Ad Name Check
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (TradeExtensions<T>.HasAdName(pk, out string ad))
            {
                var botName = string.IsNullOrEmpty(SysCordSettings.HubConfig.BotName);
                await Helpers<T>.ReplyAndDeleteAsync(Context, $"{Context.User.Username} just tried genning an Admon on {botName}\nEveryone laugh at them and start calling them stupid.", 15);
                return;
            }
        }

        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk,
            PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Makes the bot trade you a Pokémon holding the requested item.")]
    public async Task ItemTrade([Remainder] string item)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        var code = Info.GetRandomTradeCode(userID);
        await ProcessItemTradeAsync(code, item);
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Makes the bot trade you a Pokémon holding the requested item.")]
    public async Task ItemTrade([Summary("Trade Code")] int code, [Remainder] string item)
    {
        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }

        await ProcessItemTradeAsync(code, item);
    }

    private async Task ProcessItemTradeAsync(int code, string item)
    {
        Species species = Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies == Species.None
            ? Species.Diglett
            : Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies;

        var set = new ShowdownSet($"{SpeciesName.GetSpeciesNameGeneration((ushort)species, 2, 8)} @ {item.Trim()}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);

        if (pkm == null)
        {
            await ReplyAsync("Set took too long to legalize.");
            return;
        }

        pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

        if (pkm.HeldItem == 0)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context, $"{Context.User.Username}, the item you entered wasn't recognized.", 5);
            return;
        }

        var la = new LegalityAnalysis(pkm);
        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"{reason}\nHere's my best attempt for that {species}!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();
        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk,
            PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 2);
    }

    // Dictionaries for the TextTrade command's pending trades and queue status
    private static readonly ConcurrentDictionary<ulong, List<string>> _pendingTextTrades = new();
    private static readonly ConcurrentDictionary<ulong, bool> _usersInQueue = new();
    private static readonly ConcurrentDictionary<ulong, bool> _batchQueueMessageSent = new();

    [Command("textTrade")]
    [Alias("tt", "text")]
    [Summary("Upload a .txt or .csv file of Showdown sets, then select which Pokémon to trade.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TextTradeAsync([Remainder] string args = "")
    {
        await ProcessTextTradeBatchAsync(Context.User.Id, (SocketUser)Context.User, args);
    }

    private async Task ProcessTextTradeBatchAsync(ulong userId, SocketUser user, string args)
    {
        // Load trade limits
        int configLimit = SysCord<T>.Runner.Config.Trade.TradeConfiguration.MaxPkmsPerTrade;

        // Absolute hard limit of 6 set in Maximum Pokémon per Trade setting
        int hardLimit = Math.Min(configLimit, 6);

        // Prevent invalid request limits
        if (hardLimit < 1)
            hardLimit = 1;

        if (_usersInQueue.ContainsKey(userId))
        {
            await ReplyAsync("You already have an existing trade in the queue. Please wait until it is finished processing.");
            return;
        }

        if (Context.Message is IUserMessage existingMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(existingMessage, null, 6);
        }

        // ===== JOB 1: File Upload =====
        if (Context.Message.Attachments.Count > 0 && string.IsNullOrWhiteSpace(args))
        {
            var file = Context.Message.Attachments.First();
            if (!file.Filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                !file.Filename.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                !file.Filename.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) &&
                !file.Filename.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) &&
                !file.Filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyAsync("Only `.txt`, `.csv`, `.rtf`, `.docx`, and `.pdf` files are supported for TextTrade.");
                return;
            }

            // Supports sets being separated by "---" or by double new lines
            var data = await new HttpClient().GetStringAsync(file.Url);
            var rawBlocks = Regex.Split(data, @"(?:---|\r?\n\s*\r?\n)+")
                 .Select(b => b.Trim())
                 .Where(b => !string.IsNullOrWhiteSpace(b))
                 .ToList();

            // Get valid species names (string form)
            var validSpecies = Enum.GetNames(typeof(Species))
                .Where(n => n != "None")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var blocks = new List<string>();

            foreach (var block in rawBlocks)
            {
                var firstLine = block.Split('\n')[0].Trim();
                string candidate;

                // Check for nickname format: Nickname(Species)
                var nicknameMatch = Regex.Match(firstLine, @"\((?<species>[^\)]+)\)");
                if (nicknameMatch.Success)
                {
                    candidate = nicknameMatch.Groups["species"].Value.Trim();
                }
                else
                {
                    // No nickname, take the part before @
                    candidate = firstLine.Contains("@")
                        ? firstLine.Split('@')[0].Trim()
                        : firstLine;
                }

                // Remove gender markers like (M) or (F)
                candidate = Regex.Replace(candidate, @"\s*\(M\)|\s*\(F\)", "", RegexOptions.IgnoreCase).Trim();

                // Special handling: Egg formats
                if (candidate.Contains("Egg", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(block);
                    continue;
                }

                // Only accept if it's an actual Pokémon species
                if (validSpecies.Contains(candidate))
                    blocks.Add(block);
            }

            if (blocks.Count == 0)
            {
                await ReplyAsync("No valid Pokémon sets found in the uploaded file.");
                return;
            }

            if (Context.Message is IUserMessage detectionMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(detectionMessage, null, 6);
            }

            _pendingTextTrades[userId] = blocks;

            // Initial embed for the user to select from
            var embed = new EmbedBuilder()
                .WithTitle("📄 Text Trade Detected!")
                .WithDescription($"Detected **{blocks.Count}** Pokémon sets from **{file.Filename}**")
                .WithColor(Color.Blue);

            for (int i = 0; i < blocks.Count; i++)
            {
                var firstLine = blocks[i].Split('\n')[0];
                var species = firstLine.Split('@')[0].Trim();

                string icons = "";

                // ✨ Shiny check
                if (blocks[i].IndexOf("Shiny: Yes", StringComparison.OrdinalIgnoreCase) >= 0)
                    icons += "✨ ";

                // 🚩 Level check
                var levelMatch = Regex.Match(blocks[i], @"Level:\s*(\d+)", RegexOptions.IgnoreCase);
                if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int lvl))
                {
                    if (lvl < 5 || lvl > 100)
                        icons += "🚩 ";
                }

                // ⚪ Item check (missing @ on first line)
                if (!firstLine.Contains("@"))
                    icons += "⚪ ";

                // 🧾 OT/TID/SID check
                if (blocks[i].Contains("OT:", StringComparison.OrdinalIgnoreCase) ||
                    blocks[i].Contains("TID:", StringComparison.OrdinalIgnoreCase) ||
                    blocks[i].Contains("SID:", StringComparison.OrdinalIgnoreCase))
                    icons += "🧾 ";

                // 🥚 Egg check
                if (firstLine.Contains("Egg", StringComparison.OrdinalIgnoreCase))
                    icons += "🥚 ";

                embed.AddField(
                    $"{i + 1}. {species} {icons}",
                    $"Use `{Prefix}tt {i + 1}` to trade this Pokémon\nUse `{Prefix}tv {i + 1}` to view this Pokémon set",
                    false
                );
            }

            // Footer msg
            embed.AddField(
                "Multiple Pokémon",
                $"Use `{Prefix}tt 1 2 3 etc.`, to trade **no more than {hardLimit} Pokémon**",
                false
            );
            embed.WithFooter("✨ = Shiny | 🚩 = Fishy | ⚪ = No Held Item | 🧾 = Has OT/TID/SID | 🥚 = Egg\n⏳ Make a selection within 60s or the TextTrade is canceled automatically.");
            var detectionEmbedMessage = await ReplyAsync(embed: embed.Build());
            _ = DeleteMessagesAfterDelayAsync(null, detectionEmbedMessage, 60);

            _ = Task.Run(async () =>
            {
                await Task.Delay(80000);
                if (_pendingTextTrades.TryRemove(userId, out _))
                    await ReplyAsync($"⌛ {user.Mention}, your TextTrade request expired after 80 seconds.");
            });
            return;
        }

        // ===== JOB 2: Selection =====
        if (!_pendingTextTrades.TryGetValue(userId, out var sets))
        {
            await ReplyAsync("You haven’t uploaded a file yet or it expired. Attach a text-based file first.");
            return;
        }

        var selections = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => int.TryParse(t, out int idx) ? idx : 0)
                             .Where(idx => idx > 0 && idx <= sets.Count)
                             .ToList();

        if (selections.Count == 0)
        {
            await ReplyAsync($"Invalid selection. Use `{Prefix}tt 1` or `{Prefix}tt 1 2` (max 6 Pokémon).");
            return;
        }

        if (selections.Count > 6)
        {
            await ReplyAsync("You can only trade up to 6 Pokémon at a time.");
            return;
        }

        // Mark the user as in queue
        _usersInQueue[userId] = true;

        // Generate a unique batch trade code and piggyback off the batch trade logic
        int batchTradeCode = Info.GetRandomTradeCode(userId);

        // Send the batch DM exactly once without spamming DMs
        if (!_batchQueueMessageSent.ContainsKey(userId))
        {
            _batchQueueMessageSent[userId] = true;
            await EmbedHelper.SendTradeCodeEmbedAsync(user, batchTradeCode);
        }

        // Queue all selected Pokémon and treat like a batch
        _ = Task.Run(async () =>
        {
            try
            {
                int tradeNumber = 1;
                foreach (var idx in selections)
                {
                    // Use ReusableActions to clean and normalize each block
                    // Use BatchNormalizer to ensure consistent command formatting with custom class
                    string showdownBlock = sets[idx - 1];
                    showdownBlock = ReusableActions.StripCodeBlock(showdownBlock);
                    showdownBlock = BatchNormalizer.NormalizeBatchCommands(showdownBlock);

                    await ProcessSingleTextTradeAsync(showdownBlock, batchTradeCode, tradeNumber, selections.Count, user);

                    tradeNumber++;
                    await Task.Delay(1000); // delay to avoid spamming
                }
            }
            finally
            {
                // Cleanup for queue, pending trades, and batch message flag
                _pendingTextTrades.TryRemove(userId, out _);
                _usersInQueue.TryRemove(userId, out _);
                _batchQueueMessageSent.TryRemove(userId, out _);
            }
        });
    }

    // Process a single Pokémon trade based on the TextTrade batch method
    private async Task ProcessSingleTextTradeAsync(string tradeContent, int batchTradeCode, int tradeNumber, int totalTrades, SocketUser user)
    {
        // Pre-checks content and ignores AutoOT if OT/TID/SID present
        tradeContent = ReusableActions.StripCodeBlock(tradeContent);
        bool ignoreAutoOT = tradeContent.Contains("OT:") || tradeContent.Contains("TID:") || tradeContent.Contains("SID:");

        // Showdown parsing logic to get the set
        if (!ShowdownParsing.TryParseAnyLanguage(tradeContent, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            await ReplyAsync($"{user.Mention}, could not parse the Pokémon set. Skipping trade.");
            return;
        }

        // Determine final language and legal template
        byte finalLanguage = LanguageHelper.GetFinalLanguage(tradeContent, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, TradeExtensions<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            await ReplyAsync($"{user.Mention}, invalid lines found:\n{string.Join("\n", set.InvalidLines)}");
            return;
        }

        // Generate PKM via ALM
        PKM? pkm = null;
        string result = "Unknown";

        await Task.Run(() =>
        {
            try
            {
                // Use language-specific sav to get the legal PKM
                var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
                pkm = sav.GetLegal(template, out result);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(ProcessSingleTextTradeAsync));
            }
        });

        if (pkm == null)
        {
            await EmbedHelper.SendTradeCanceledEmbedAsync(user, $"Failed to generate Pokémon: {result}");
            return;
        }

        // Egg & Held Item fixes
        if (pkm.HeldItem == 0 && !pkm.IsEgg)
            pkm.HeldItem = (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem;

        // Spam and/or Admon check
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck && TradeExtensions<T>.HasAdName((T)pkm, out string ad))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context, $"{user.Mention}, detected invalid Adname in Pokémon or Trainer name.", 5);
            return;
        }

        // If LG, utilize LG code method
        var lgCode = Info.GetRandomLGTradeCode();

        // Add to queue
        await Helpers<T>.AddTradeToQueueAsync(
            Context,
            batchTradeCode,
            Context.User.Username,
            (T)pkm,
            Context.User.GetFavor(),
            Context.User,
            isBatchTrade: true,
            batchTradeNumber: tradeNumber,
            totalBatchTrades: totalTrades,
            isHiddenTrade: false,
            isMysteryEgg: false,
            lgcode: lgCode,
            tradeType: PokeTradeType.Batch,
            ignoreAutoOT: ignoreAutoOT,
            setEdited: false,
            isNonNative: false
        ).ConfigureAwait(false);
    }

    #endregion

    #region List Commands

    [Command("textView")]
    [Alias("tv")]
    [Summary("View a specific Pokémon set from your pending TextTrade file by number.")]
    public async Task TextViewAsync([Remainder] string args = "")
    {
        ulong userId = Context.User.Id;

        if (!_pendingTextTrades.TryGetValue(userId, out var sets))
        {
            await ReplyAsync($"{Context.User.Mention}, you don’t have an active TextTrade file loaded. Upload one first with `{Prefix}tt`.");
            return;
        }

        if (string.IsNullOrWhiteSpace(args) || !int.TryParse(args, out int idx) || idx <= 0 || idx > sets.Count)
        {
            await ReplyAsync($"Invalid set number. Use `{Prefix}tv 1` through `{Prefix}tv {sets.Count}`.");
            return;
        }

        var showdownBlock = ReusableActions.StripCodeBlock(sets[idx - 1].Trim());

        // Build an embed with the full showdown set
        var embed = new EmbedBuilder()
            .WithTitle($"👀 Viewing Set #{idx}")
            .WithDescription($"```text\n{showdownBlock}\n```")
            .WithFooter($"Use {Prefix}tt {idx} to trade this Pokémon.")
            .WithColor(Color.DarkPurple);

        var sentEmbed = await ReplyAsync(embed: embed.Build());
        _ = DeleteMessagesAfterDelayAsync(null, sentEmbed, 60);
    }

    [Command("tradeList")]
    [Alias("tl")]
    [Summary("Prints the users in the trade queues.")]
    [RequireSudo]
    public async Task GetTradeListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("fixOTList")]
    [Alias("fl", "fq")]
    [Summary("Prints the users in the FixOT queue.")]
    [RequireSudo]
    public async Task GetFixListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.FixOT);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("listevents")]
    [Alias("le")]
    [Summary("Lists available event files, filtered by a specific letter or substring, and sends the list via DM.")]
    public Task ListEventsAsync([Remainder] string args = "")
        => ListHelpers<T>.HandleListCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder,
            "events",
            "er",
            args
        );

    [Command("battlereadylist")]
    [Alias("brl")]
    [Summary("Lists available battle-ready files, filtered by a specific letter or substring, and sends the list via DM.")]
    public Task BattleReadyListAsync([Remainder] string args = "")
        => ListHelpers<T>.HandleListCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder,
            "battle-ready files",
            "brr",
            args
        );

    #endregion

    #region Request Commands

    [Command("eventrequest")]
    [Alias("er")]
    [Summary("Downloads event attachments from the specified EventsFolder and adds to trade queue.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task EventRequestAsync(int index)
        => ListHelpers<T>.HandleRequestCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder,
            index,
            "event",
            "le"
        );

    [Command("battlereadyrequest")]
    [Alias("brr", "br")]
    [Summary("Downloads battle-ready attachments from the specified folder and adds to trade queue.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task BattleReadyRequestAsync(int index)
        => ListHelpers<T>.HandleRequestCommandAsync(
            Context,
            SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder,
            index,
            "battle-ready file",
            "brl"
        );

    #endregion

    #region Batch Trades

    [Command("batchTrade")]
    [Alias("bt")]
    [Summary("Makes the bot trade multiple Pokémon from the provided list, up to a maximum of 4 trades.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task BatchTradeAsync([Summary("List of Showdown Sets separated by '---'")][Remainder] string content)
    {
        var tradeConfig = SysCord<T>.Runner.Config.Trade.TradeConfiguration;

        // Check if batch trades are allowed
        if (!tradeConfig.AllowBatchTrades)
        {
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"Batch trades are currently disabled by the bot administrator, @{app.Owner}.", 6);
            return;
        }

        var userID = Context.User.Id;
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
            return;
        }
        content = ReusableActions.StripCodeBlock(content);
        var trades = BatchHelpers<T>.ParseBatchTradeContent(content);

        // Use configured max trades per batch, default to 4 if less than 1
        int maxTradesAllowed = tradeConfig.MaxPkmsPerTrade > 0 ? tradeConfig.MaxPkmsPerTrade : 4;

        if (trades.Count > maxTradesAllowed)
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                $"You can only process up to {maxTradesAllowed} trades at a time.\nPlease reduce the number of trades in your batch.", 5);
            return;
        }

        var processingMessage = await Context.Channel.SendMessageAsync($"{Context.User.Mention} Processing your batch trade with {trades.Count} Pokémon...");

        _ = Task.Run(async () =>
        {
            try
            {
                var batchPokemonList = new List<T>();
                var errors = new List<BatchTradeError>();
                for (int i = 0; i < trades.Count; i++)
                {
                    var (pk, error, set, legalizationHint) = await BatchHelpers<T>.ProcessSingleTradeForBatch(trades[i]);
                    if (pk != null)
                    {
                        batchPokemonList.Add(pk);
                    }
                    else
                    {
                        var speciesName = set != null && set.Species > 0
                            ? GameInfo.Strings.Species[set.Species]
                            : "Unknown";
                        errors.Add(new BatchTradeError
                        {
                            TradeNumber = i + 1,
                            SpeciesName = speciesName,
                            ErrorMessage = error ?? "Unknown error",
                            LegalizationHint = legalizationHint,
                            ShowdownSet = set != null ? string.Join("\n", set.GetSetLines()) : trades[i]
                        });
                    }
                }

                await processingMessage.DeleteAsync();

                if (errors.Count > 0)
                {
                    await BatchHelpers<T>.SendBatchErrorEmbedAsync(Context, errors, trades.Count);
                    return;
                }
                if (batchPokemonList.Count > 0)
                {
                    var batchTradeCode = Info.GetRandomTradeCode(userID);
                    await BatchHelpers<T>.ProcessBatchContainer(Context, batchPokemonList, batchTradeCode, trades.Count);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    await processingMessage.DeleteAsync();
                }
                catch { }

                await Context.Channel.SendMessageAsync($"{Context.User.Mention} An error occurred while processing your batch trade. Please try again.");
                Base.LogUtil.LogError($"Batch trade processing error: {ex.Message}", nameof(BatchTradeAsync));
            }
        });

        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, 6);
    }

    [Command("batchtradezip")]
    [Alias("btz", "batchzip", "ziptrade")]
    [Summary("Upload a .zip containing .pk* files to trade multiple Pokémon at once.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task BatchTradeZipAsync()
    {
        var user = Context.User;
        ulong userId = user.Id;

        if (Context.Message.Attachments.Count == 0)
        {
            await ReplyAsync($"{user.Mention}, attach a `.zip` file containing `.pk*` Pokémon files.");
            return;
        }

        var zip = Context.Message.Attachments.First();

        if (!zip.Filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ReplyAsync($"{user.Mention}, only `.zip` files are accepted.");
            return;
        }

        // Prevent duplicate queueing
        if (_usersInQueue.ContainsKey(userId))
        {
            await ReplyAsync($"{user.Mention}, you already have trades processing. Wait your damn turn.");
            return;
        }

        _usersInQueue[userId] = true;

        if (Context.Message is IUserMessage zmsg)
            _ = DeleteMessagesAfterDelayAsync(zmsg, null, 5);

        // Download ZIP data
        byte[] zipBytes;
        using (var http = new HttpClient())
            zipBytes = await http.GetByteArrayAsync(zip.Url);

        // Parse inside Task.Run like PokeBot to avoid blocking Discord thread
        _ = Task.Run(async () =>
        {
            try
            {
                List<PKM> pkms = new();

                using (var ms = new MemoryStream(zipBytes))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length == 0)
                            continue;

                        if (!entry.Name.EndsWith(".pk", StringComparison.OrdinalIgnoreCase) &&
                            !entry.Name.EndsWith(".pk7", StringComparison.OrdinalIgnoreCase) &&
                            !entry.Name.EndsWith(".pk8", StringComparison.OrdinalIgnoreCase) &&
                            !entry.Name.EndsWith(".pa9", StringComparison.OrdinalIgnoreCase) &&
                            !entry.Name.EndsWith(".pk9", StringComparison.OrdinalIgnoreCase))
                            continue; // skip trash

                        using var es = entry.Open();
                        using var msEntry = new MemoryStream();
                        await es.CopyToAsync(msEntry);
                        var data = msEntry.ToArray();

                        // Convert raw bytes into PKM (PokeBot-style)
                        var pkm = EntityFormat.GetFromBytes(data);

                        if (pkm == null || pkm.Species <= 0)
                            continue; // junk file

                        // Integrates Max Pokémon per Trade limits
                        pkms.Add(pkm);
                        int configLimit = SysCord<T>.Runner.Config.Trade.TradeConfiguration.MaxPkmsPerTrade;

                        // Enforced absolute hard cap (never allow more than 6)
                        int hardLimit = Math.Min(configLimit, 6);

                        // Enforce lower bounds sanity
                        if (hardLimit < 1)
                            hardLimit = 1;

                        // Check the PKM count against limits
                        if (pkms.Count > hardLimit)
                        {
                            await ReplyAsync($"{user.Mention}, your ZIP contains **{pkms.Count} Pokémon**, but the limit is **{hardLimit}**.");
                            return;
                        }
                    }
                }

                if (pkms.Count == 0)
                {
                    await ReplyAsync($"{user.Mention}, that ZIP had no valid PKM files. The hell you upload?");
                    return;
                }

                // Generate batch trade code
                int batchTradeCode = Info.GetRandomTradeCode(userId);

                // Send to user like PokeBot
                await EmbedHelper.SendTradeCodeEmbedAsync(user, batchTradeCode);

                int tradeNumber = 1;
                int total = pkms.Count;

                foreach (var pkm in pkms)
                {
                    try
                    {
                        // All ZIP trades should honor default held item
                        if (pkm.HeldItem == 0 && !pkm.IsEgg)
                            pkm.HeldItem = (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem;

                        // Language handling (ZIP doesn’t include language info)
                        byte lang = (byte)Info.Hub.Config.Legality.GenerateLanguage;

                        await Helpers<T>.AddTradeToQueueAsync(
                            Context,
                            batchTradeCode,
                            user.Username,
                            (T)pkm,
                            user.GetFavor(),
                            user,
                            isBatchTrade: true,
                            batchTradeNumber: tradeNumber,
                            totalBatchTrades: total,
                            isHiddenTrade: false,
                            isMysteryEgg: false,
                            tradeType: PokeTradeType.Batch,
                            ignoreAutoOT: false,
                            setEdited: false,
                            isNonNative: false
                        ).ConfigureAwait(false);

                        tradeNumber++;
                        await Task.Delay(750);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogSafe(ex, nameof(BatchTradeZipAsync));
                    }
                }

                await ReplyAsync($"{user.Mention}, your ZIP trade batch (**{total} Pokémon**) has been queued.");
            }
            finally
            {
                _usersInQueue.TryRemove(userId, out _);
            }
        });
    }


    #endregion

    #region Private Helper Methods

    private async Task ProcessTradeAsync(int code, string content, bool isHiddenTrade = false)
    {
        var userID = Context.User.Id;

        // Prevent duplicate queued trades for the same user
        if (!await Helpers<T>.EnsureUserNotInQueueAsync(userID))
        {
            await Helpers<T>.ReplyAndDeleteAsync(Context,
                "You already have an existing trade in the queue that cannot be cleared.\nPlease wait until it is processed.", 5);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Parse the Showdown set and detect manual trainer overrides
                var set = new ShowdownSet(content);
                var ignoreAutoOT = content.Contains("OT:") || content.Contains("TID:") || content.Contains("SID:");

                // Legacy helper: returns the processed pokemon + extra info (errors, lgcode, etc.)
                var processed = await Helpers<T>.ProcessShowdownSetAsync(content, ignoreAutoOT);
                if (processed.Pokemon == null)
                {
                    await Helpers<T>.SendTradeErrorEmbedAsync(Context, processed);
                    return;
                }

                // Get AutoLegality trainer/save info and template
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var template = AutoLegalityWrapper.GetTemplate(set);

                // Generate the legal pokemon (ALM)
                var pkm = sav.GetLegal(template, out var result);
                if (pkm == null)
                {
                    await Helpers<T>.ReplyAndDeleteAsync(Context, $"Failed to generate Pokémon from your set.\nTry to use `{Prefix}convert` instead, or try removing some information.", 8);
                    return;
                }

                // ***** FORCE NATURE (ZA) *****
                // Ensure you have a ZANatureHelper implementation placed in SysBot.Pokemon.Helpers namespace.
                // This will reroll PID until PID%25 == requested nature and keep IV/EVs consistent.
                ZANatureHelper.ForceNatureZA(pkm, set.Nature, set.Shiny);

                // Convert to the bot's runtime type (T)
                var pk = EntityConverter.ConvertToType(pkm, typeof(T), out _) as T;
                if (pk == null)
                {
                    await Helpers<T>.ReplyAndDeleteAsync(Context, "Failed to convert Pokémon to correct type.", 5);
                    return;
                }
                // Raw message ad check BEFORE generating PKM
                if (TradeExtensions<T>.ContainsAdText(content, out var domain))
                {
                    var botName = string.IsNullOrEmpty(SysCordSettings.HubConfig.BotName)
                        ? "the bot"
                        : SysCordSettings.HubConfig.BotName;
                    var trainerMention = Context.User.Mention;
                    await Helpers<T>.ReplyAndDeleteAsync(Context,
                        $"{trainerMention} just tried to run an ad in my fuckin’ bot, {botName}.\n" +
                        $"Point and laugh at them for being a bitch ass failure!", 15);
                    return;
                }


                // Final safety refresh
                pk.RefreshChecksum();

                // Queue the trade
                var sig = Context.User.GetFavor();
                await Helpers<T>.AddTradeToQueueAsync(
                    Context,
                    code,
                    Context.User.Username,
                    pk,
                    sig,
                    Context.User,
                    isHiddenTrade: isHiddenTrade,
                    lgcode: processed.LgCode,
                    ignoreAutoOT: ignoreAutoOT,
                    isNonNative: processed.IsNonNative
                );
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                await Helpers<T>.ReplyAndDeleteAsync(Context, $"An unexpected problem happened with this Showdown Set.\nTry to use `{Prefix}convert` instead, or try removing some information.", 8);
            }
        });

        // Auto-delete original message (as before)
        if (Context.Message is IUserMessage userMessage)
            _ = Helpers<T>.DeleteMessagesAfterDelayAsync(userMessage, null, isHiddenTrade ? 0 : 2);
    }

    private async Task ProcessTradeAttachmentAsync(int code, RequestSignificance sig, SocketUser user, bool isHiddenTrade = false, bool ignoreAutoOT = false)
    {
        var pk = await Helpers<T>.ProcessTradeAttachmentAsync(Context);
        if (pk == null)
            return;

        await Helpers<T>.AddTradeToQueueAsync(Context, code, user.Username, pk, sig, user,
            isHiddenTrade: isHiddenTrade, ignoreAutoOT: ignoreAutoOT);
    }

    private static async Task DeleteMessagesAfterDelayAsync(IMessage botMsg, IMessage userMsg, int delaySec)
    {
        await Task.Delay(delaySec * 1000);
        try { await botMsg.DeleteAsync(); } catch { }
        try { await userMsg.DeleteAsync(); } catch { }
    }

    #endregion
}
