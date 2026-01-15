using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static SysBot.Pokemon.TradeSettings.TradeSettingsCategory;

namespace SysBot.Pokemon.Discord;

public static class Helpers<T> where T : PKM, new()
{
    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    public static Task<bool> EnsureUserNotInQueueAsync(ulong userID, int deleteDelay = 2)
    {
        if (!Info.IsUserInQueue(userID))
            return Task.FromResult(true);

        var existingTrades = Info.GetIsUserQueued(x => x.UserID == userID);
        foreach (var trade in existingTrades)
        {
            trade.Trade.IsProcessing = false;
        }

        var clearResult = Info.ClearTrade(userID);
        if (clearResult == QueueResultRemove.CurrentlyProcessing || clearResult == QueueResultRemove.NotInQueue)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public static async Task ReplyAndDeleteAsync(SocketCommandContext context, string message, int delaySeconds, IMessage? messageToDelete = null)
    {
        try
        {
            var sentMessage = await context.Channel.SendMessageAsync(message).ConfigureAwait(false);

            // Check if message deletion is enabled in settings
            if (!Info.Hub.Config.Discord.MessageDeletionEnabled)
                return;

            // Use configured delay from settings instead of hardcoded value
            var configuredDelay = Info.Hub.Config.Discord.ErrorMessageDeleteDelaySeconds;

            // Determine which user message to delete based on settings
            IMessage? userMessageToDelete = null;
            if (Info.Hub.Config.Discord.DeleteUserCommandMessages)
            {
                userMessageToDelete = messageToDelete ?? context.Message;
            }

            _ = DeleteMessagesAfterDelayAsync(sentMessage, userMessageToDelete, configuredDelay);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    public static async Task DeleteMessagesAfterDelayAsync(IMessage? sentMessage, IMessage? messageToDelete, int delaySeconds)
    {
        try
        {
            // Check if message deletion is enabled in settings
            if (!Info.Hub.Config.Discord.MessageDeletionEnabled)
                return;

            // Use configured delay from settings
            var configuredDelay = Info.Hub.Config.Discord.ErrorMessageDeleteDelaySeconds;
            await Task.Delay(configuredDelay * 1000);

            var tasks = new List<Task>();

            // Check if sentMessage is a bot message or user message
            // In some places, user messages are passed as the first parameter
            if (sentMessage != null)
            {
                // If it's a user message and DeleteUserCommandMessages is false, skip it
                if (sentMessage is IUserMessage userMsg && userMsg.Author.IsBot == false)
                {
                    if (Info.Hub.Config.Discord.DeleteUserCommandMessages)
                        tasks.Add(TryDeleteMessageAsync(sentMessage));
                }
                else
                {
                    // It's a bot message, always delete it
                    tasks.Add(TryDeleteMessageAsync(sentMessage));
                }
            }

            // Only delete user message if setting is enabled
            if (messageToDelete != null && Info.Hub.Config.Discord.DeleteUserCommandMessages)
                tasks.Add(TryDeleteMessageAsync(messageToDelete));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    private static async Task TryDeleteMessageAsync(IMessage message)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            // Ignore Unknown Message exception
        }
    }

    public static Task<ProcessedPokemonResult<T>> ProcessShowdownSetAsync(string content, bool ignoreAutoOT = false)
    {
        bool isEgg = TradeExtensions<T>.IsEggCheck(content);

        // CRITICAL FIX: Extract language BEFORE parsing ShowdownSet
        // If we let PKHeX see "Language: German", it includes it in the template
        // and ALM fails to find encounters with that language
        byte finalLanguage = LanguageHelper.GetFinalLanguage(
            content, null,
            (byte)Info.Hub.Config.Legality.GenerateLanguage,
            TradeExtensions<T>.DetectShowdownLanguage
        );

        // Remove Language: line from content before parsing
        // This prevents PKHeX from including it in the template
        var contentLines = content.Split('\n');
        var filteredLines = contentLines.Where(line =>
            !line.TrimStart().StartsWith("Language:", StringComparison.OrdinalIgnoreCase)
        ).ToArray();
        var contentWithoutLanguage = string.Join('\n', filteredLines);

        // Now parse the ShowdownSet without the Language line
        if (!ShowdownParsing.TryParseAnyLanguage(contentWithoutLanguage, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Unable to parse Showdown set. Could not identify the Pokémon species.",
                ShowdownSet = set
            });
        }

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Filter out batch commands (.) and filters (~) from invalid lines - these are handled by ALM
        // Also filter out custom fields like Language: and Alpha: which are ALM-specific
        var actualInvalidLines = set.InvalidLines.Where(line =>
        {
            var text = line.Value?.Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            // Skip batch commands and filters
            if (text.StartsWith('.') || text.StartsWith('~'))
                return false;

            // Skip custom ALM fields
            if (text.StartsWith("Language:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Alpha:", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }).ToList();

        if (actualInvalidLines.Count != 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"Unable to parse Showdown Set:\n{string.Join("\n", actualInvalidLines.Select(l => l.Value))}",
                ShowdownSet = set
            });
        }

        // DON'T use language-specific trainer! It causes encounter errors.
        // Generate with normal trainer (English), then set language after generation.
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

        // Fix for Asian languages: Truncate OT to 6 characters max
        // Japanese, Korean, ChineseS, ChineseT only support 6 character OT names
        if (finalLanguage == (byte)LanguageID.Japanese ||
            finalLanguage == (byte)LanguageID.Korean ||
            finalLanguage == (byte)LanguageID.ChineseS ||
            finalLanguage == (byte)LanguageID.ChineseT)
        {
            if (sav.OT.Length > 6)
            {
                // Create a new trainer with truncated OT
                var truncatedOT = sav.OT.Substring(0, 6);
                sav = new SimpleTrainerInfo(sav.Version)
                {
                    OT = truncatedOT,
                    TID16 = sav.TID16,
                    SID16 = sav.SID16,
                    Language = sav.Language,
                    Generation = sav.Generation
                };
            }
        }

        PKM pkm;
        string result;

        // Generate egg or normal pokemon based on isEgg flag
        if (isEgg)
        {
            // Create a proper RegenTemplate from the ShowdownSet
            var regenTemplate = new RegenTemplate(set);

            // Generate egg using ALM
            pkm = sav.GenerateEgg(regenTemplate, out var eggResult);
            result = eggResult.ToString();
        }
        else
        {
            // Use normal template for regular Pokémon
            pkm = sav.GetLegal(template, out result);
        }

        if (pkm == null)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Set took too long to legalize.",
                ShowdownSet = set
            });
        }

        var spec = GameInfo.Strings.Species[template.Species];

        // Apply standard item logic only for non-eggs
        if (!isEgg)
        {
            ApplyStandardItemLogic(pkm);
        }

        // Generate LGPE code if needed
        List<Pictocodes>? lgcode = null;
        if (pkm is PB7)
        {
            lgcode = GenerateRandomPictocodes(3);
            if (pkm.Species == (int)Species.Mew && pkm.IsShiny)
            {
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = "Mew can **not** be Shiny in LGPE. PoGo Mew does not transfer and Pokeball Plus Mew is shiny locked.",
                    ShowdownSet = set
                });
            }
        }

        var la = new LegalityAnalysis(pkm);
        if (pkm is not T pk || !la.Valid)
        {
            var reason = GetFailureReason(result, spec);
            var hint = result == "Failed" ? GetLegalizationHint(template, sav, pkm, spec) : null;
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = reason,
                LegalizationHint = hint,
                ShowdownSet = set
            });
        }

        // Final preparation
        PrepareForTrade(pk, set, finalLanguage);

        // Check for spam names
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (TradeExtensions<T>.HasAdName(pk, out string ad))
            {
                return Task.FromResult(new ProcessedPokemonResult<T>
                {
                    Error = "Detected Adname in the Pokémon's name or trainer name, which is not allowed.",
                    ShowdownSet = set
                });
            }
        }
    
        // For SWSH (PK8), GO Pokemon can have AutoOT applied, so don't mark them as non-native
        la = new LegalityAnalysis(pk);
        var isNonNative = la.EncounterOriginal.Context != pk.Context || (pk.GO && pk is not PK8);

        return Task.FromResult(new ProcessedPokemonResult<T>
        {
            Pokemon = pk,
            ShowdownSet = set,
            LgCode = lgcode,
            IsNonNative = isNonNative
        });
    }

    public static void ApplyStandardItemLogic(PKM pkm)
    {
        pkm.HeldItem = pkm switch
        {
            PA8 => (int)HeldItem.None,
            _ when pkm.HeldItem == 0 && !pkm.IsEgg => (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem,
            _ => pkm.HeldItem
        };
    }

    public static void PrepareForTrade(T pk, ShowdownSet set, byte finalLanguage)
    {
        // Only set EggMetDate for hatched Pokemon, not for unhatched eggs
        if (pk.WasEgg && !pk.IsEgg)
            pk.EggMetDate = pk.MetDate;

        // Validate language is supported for this game version
        // SpanishL (11) isn't supported in some games, fall back to Spanish (7)
        var validatedLanguage = ValidateLanguageForGame(pk, finalLanguage);
        pk.Language = validatedLanguage;

        // CRITICAL: Asian languages only support 6-character OT names
        // Replace English OT with Asian characters for Asian languages
        if (validatedLanguage == (int)LanguageID.Japanese ||
            validatedLanguage == (int)LanguageID.Korean ||
            validatedLanguage == (int)LanguageID.ChineseS ||
            validatedLanguage == (int)LanguageID.ChineseT)
        {
            if (pk.OriginalTrainerName.Length > 6)
            {
                // Use proper Asian characters instead of truncating English text
                var asianOT = "王犬米";

                // Properly set OT and clear trash bytes
                pk.OriginalTrainerName = asianOT;

                // Clear OT trash bytes to ensure legality
                // Get the OT as bytes, properly sized for the format
                Span<byte> trash = stackalloc byte[pk.TrashCharCountTrainer * 2];
                int length = pk.SetString(trash, asianOT.AsSpan(), pk.TrashCharCountTrainer, StringConverterOption.ClearZero);
                pk.OriginalTrainerTrash.Clear();
                trash[..length].CopyTo(pk.OriginalTrainerTrash);

                // Refresh checksum after modifying OT
                pk.RefreshChecksum();
            }
        }

        if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
            _ = pk.ClearNickname();

        pk.ResetPartyStats();
    }

    private static int ValidateLanguageForGame(PKM pk, byte requestedLanguage)
    {
        // SpanishL (11) support varies by game
        if (requestedLanguage == (byte)LanguageID.SpanishL)
        {
            // Check if this game supports SpanishL
            bool supportsSpanishL = pk switch
            {
                PB7 => false,  // Let's Go - does not support SpanishL properly
                PK8 => false,  // Sword/Shield - does not support SpanishL properly
                PB8 => false, // BDSP - does not support SpanishL properly
                PA8 => false, // Legends Arceus - does not support SpanishL properly
                PK9 => false,  // Scarlet/Violet - does not support SpanishL properly
                PA9 => true,  // Legends Z-A - Supports SpanishL
                _ => false
            };

            if (!supportsSpanishL)
            {
                // Fall back to Spanish (7) if SpanishL is used in any game other than Legends Z-A
                return (int)LanguageID.Spanish;
            }
        }

        return requestedLanguage;
    }

    public static string GetFailureReason(string result, string speciesName)
    {
        return result switch
        {
            "Timeout" => $"That {speciesName} set took too long to generate.",
            "VersionMismatch" => "Request refused: PKHeX and Auto-Legality Mod version mismatch.",
            _ => $"I wasn't able to create a {speciesName} from that set."
        };
    }

    public static string GetLegalizationHint(IBattleTemplate template, ITrainerInfo sav, PKM pkm, string speciesName)
    {
        var hint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
        if (hint.Contains("Requested shiny value (ShinyType."))
        {
            hint = $"{speciesName} **cannot** be shiny. Please try again.";
        }
        return hint;
    }

    public static async Task SendTradeErrorEmbedAsync(SocketCommandContext context, ProcessedPokemonResult<T> result)
    {
        var spec = result.ShowdownSet != null && result.ShowdownSet.Species > 0
            ? GameInfo.Strings.Species[result.ShowdownSet.Species]
            : "Unknown";

        var embedBuilder = new EmbedBuilder()
            .WithTitle("Trade Creation Failed.")
            .WithColor(Color.Red)
            .AddField("Status", $"Failed to create {spec}.")
            .AddField("Reason", result.Error ?? "Unknown error");

        if (!string.IsNullOrEmpty(result.LegalizationHint))
        {
            _ = embedBuilder.AddField("Hint", result.LegalizationHint);
        }

        string userMention = context.User.Mention;
        string messageContent = $"{userMention}, here's the report for your request:";
        var message = await context.Channel.SendMessageAsync(text: messageContent, embed: embedBuilder.Build()).ConfigureAwait(false);
        _ = DeleteMessagesAfterDelayAsync(message, context.Message, 30);
    }

    /// <summary>
    /// Sends a detailed trade error log to configured Full Trade Error Log channels.
    /// </summary>
    public static async Task SendFullTradeErrorLogAsync(SocketCommandContext context, string errorReason, string userRequest, int tradeCode, string? legalizationHint = null)
    {
        var cfg = SysCordSettings.Settings.FullTradeErrorLogChannels;
        if (cfg.List.Count == 0)
            return;

        var user = context.User;
        var guild = (context.Channel as IGuildChannel)?.Guild;
        var channel = context.Channel;

        string serverName = guild?.Name ?? "Direct Message";
        string channelName = channel is IGuildChannel guildChannel ? $"#{guildChannel.Name}" : "DM";
        string channelId = channel.Id.ToString();

        // Get game version from PKM type
        string gameVersion = typeof(T).Name switch
        {
            "PA9" => "ZA",
            "PK9" => "SV",
            "PA8" => "LA",
            "PB8" => "BDSP",
            "PK8" => "SWSH",
            "PB7" => "LGPE",
            _ => "Unknown"
        };

        // Truncate user request if it's too long for embed field (Discord limit is 1024 characters per field)
        string truncatedRequest = userRequest.Length > 950
            ? userRequest.Substring(0, 947) + "..."
            : userRequest;

        var embedBuilder = new EmbedBuilder()
            .WithTitle("**DETAILED TRADE ERROR LOGS**")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .AddField("**Connected User**", $"{user.Username} ({user.Id})", inline: false)
            .AddField("**Link Trade Code**", tradeCode.ToString("0000 0000"), inline: false)
            .AddField("**Server of Request**", serverName, inline: false)
            .AddField("**Channel of Request**", $"{channelName} ({channelId})", inline: false)
            .AddField("**Game Version of Bot**", gameVersion, inline: false)
            .AddField("**Reason for Error**", errorReason, inline: false);

        // Add legalization hint if available
        if (!string.IsNullOrEmpty(legalizationHint))
        {
            embedBuilder.AddField("**Hint**", legalizationHint, inline: false);
        }

        // Check if we should include Known Trainer Details
        var hub = SysCord<T>.Runner.Hub;
        bool storeTradeCodesEnabled = hub.Config.Trade.TradeConfiguration.StoreTradeCodes;

        if (storeTradeCodesEnabled)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            var tradeDetails = tradeCodeStorage.GetTradeDetails(user.Id);

            if (tradeDetails != null && !string.IsNullOrEmpty(tradeDetails.OT))
            {
                string trainerDetails = $"**OT:** {tradeDetails.OT}\n**TID:** {tradeDetails.TID}\n**SID:** {tradeDetails.SID}";
                embedBuilder.AddField("**Known Trainer Details**", trainerDetails, inline: false);
            }
        }

        embedBuilder.AddField("**User's Request**", $"```\n{truncatedRequest}\n```", inline: false);

        var embed = embedBuilder.Build();

        // Send to all configured Full Trade Error Log channels
        foreach (var logChannel in cfg)
        {
            try
            {
                if (context.Client.GetChannel(logChannel.ID) is ISocketMessageChannel msgChannel)
                {
                    await msgChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to send Full Trade Error Log to channel {logChannel.ID}: {ex.Message}", nameof(Helpers<T>));
            }
        }
    }

    /// <summary>
    /// Sends a detailed batch trade error log to configured Full Trade Error Log channels.
    /// </summary>
    public static async Task SendFullBatchTradeErrorLogAsync(SocketCommandContext context, List<BatchTradeError> errors, int tradeCode, int totalTrades)
    {
        var cfg = SysCordSettings.Settings.FullTradeErrorLogChannels;
        if (cfg.List.Count == 0)
            return;

        var user = context.User;
        var guild = (context.Channel as IGuildChannel)?.Guild;
        var channel = context.Channel;

        string serverName = guild?.Name ?? "Direct Message";
        string channelName = channel is IGuildChannel guildChannel ? $"#{guildChannel.Name}" : "DM";
        string channelId = channel.Id.ToString();

        // Get game version from PKM type
        string gameVersion = typeof(T).Name switch
        {
            "PA9" => "ZA",
            "PK9" => "SV",
            "PA8" => "LA",
            "PB8" => "BDSP",
            "PK8" => "SWSH",
            "PB7" => "LGPE",
            _ => "Unknown"
        };

        // Build error summary
        var errorSummary = new System.Text.StringBuilder();
        errorSummary.AppendLine($"**{errors.Count} out of {totalTrades} Pokémon failed:**\n");

        foreach (var error in errors.Take(5)) // Limit to first 5 errors to avoid embed size limits
        {
            errorSummary.AppendLine($"**Trade #{error.TradeNumber}** - {error.SpeciesName}");
            errorSummary.AppendLine($"Error: {error.ErrorMessage}");
            if (!string.IsNullOrEmpty(error.LegalizationHint))
            {
                errorSummary.AppendLine($"Hint: {error.LegalizationHint}");
            }
            errorSummary.AppendLine();
        }

        if (errors.Count > 5)
        {
            errorSummary.AppendLine($"... and {errors.Count - 5} more errors.");
        }

        var embedBuilder = new EmbedBuilder()
            .WithTitle("**DETAILED BATCH TRADE ERROR LOGS**")
            .WithColor(Color.Gold)
            .WithCurrentTimestamp()
            .AddField("**Connected User**", $"{user.Username} ({user.Id})", inline: false)
            .AddField("**Link Trade Code**", tradeCode.ToString("0000 0000"), inline: false)
            .AddField("**Server of Request**", serverName, inline: false)
            .AddField("**Channel of Request**", $"{channelName} ({channelId})", inline: false)
            .AddField("**Game Version of Bot**", gameVersion, inline: false)
            .AddField("**Reason for Error**", $"Batch trade validation failed: {errors.Count}/{totalTrades} Pokémon invalid", inline: false);

        // Check if we should include Known Trainer Details
        var hub = SysCord<T>.Runner.Hub;
        bool storeTradeCodesEnabled = hub.Config.Trade.TradeConfiguration.StoreTradeCodes;

        if (storeTradeCodesEnabled)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            var tradeDetails = tradeCodeStorage.GetTradeDetails(user.Id);

            if (tradeDetails != null && !string.IsNullOrEmpty(tradeDetails.OT))
            {
                string trainerDetails = $"**OT:** {tradeDetails.OT}\n**TID:** {tradeDetails.TID}\n**SID:** {tradeDetails.SID}";
                embedBuilder.AddField("**Known Trainer Details**", trainerDetails, inline: false);
            }
        }

        embedBuilder.AddField("**Error Details**", errorSummary.ToString(), inline: false);

        var embed = embedBuilder.Build();

        // Send to all configured Full Trade Error Log channels
        foreach (var logChannel in cfg)
        {
            try
            {
                if (context.Client.GetChannel(logChannel.ID) is ISocketMessageChannel msgChannel)
                {
                    await msgChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to send Full Batch Trade Error Log to channel {logChannel.ID}: {ex.Message}", nameof(Helpers<T>));
            }
        }
    }

    public static T? GetRequest(Download<PKM> dl)
    {
        if (!dl.Success)
            return null;
        return dl.Data switch
        {
            null => null,
            T pk => pk,
            _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
        };
    }

    public static List<Pictocodes> GenerateRandomPictocodes(int count)
    {
        Random rnd = new();
        List<Pictocodes> randomPictocodes = [];
        Array pictocodeValues = Enum.GetValues<Pictocodes>();

        for (int i = 0; i < count; i++)
        {
            Pictocodes randomPictocode = (Pictocodes)pictocodeValues.GetValue(rnd.Next(pictocodeValues.Length))!;
            randomPictocodes.Add(randomPictocode);
        }

        return randomPictocodes;
    }

    public static async Task<T?> ProcessTradeAttachmentAsync(SocketCommandContext context)
    {
        var attachment = context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            _ = await context.Channel.SendMessageAsync("No attachment provided!").ConfigureAwait(false);
            return null;
        }

        var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
        var pk = GetRequest(att);

        if (pk == null)
        {
            _ = await context.Channel.SendMessageAsync("Attachment provided is not compatible with this module!").ConfigureAwait(false);
            return null;
        }

        return pk;
    }

    public static (string filter, int page) ParseListArguments(string args)
    {
        string filter = "";
        int page = 1;
        var parts = args.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        return (filter, page);
    }

    public static async Task AddTradeToQueueAsync(
        SocketCommandContext context,
        int code,
        string trainerName,
        T? pk,
        RequestSignificance sig,
        SocketUser usr,
        bool isBatchTrade = false,
        int batchTradeNumber = 1,
        int totalBatchTrades = 1,
        bool isHiddenTrade = false,
        bool isMysteryEgg = false,
        List<Pictocodes>? lgcode = null,
        PokeTradeType tradeType = PokeTradeType.Specific,
        bool ignoreAutoOT = false, bool setEdited = false,
        bool isNonNative = false)
    {
        lgcode ??= GenerateRandomPictocodes(3);

        if (pk is not null && !pk.CanBeTraded())
        {
            var reply = await context.Channel.SendMessageAsync("Provided Pokémon content is blocked from trading!").ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }

        // Block non-tradable items using PKHeX's ItemRestrictions
        if (pk is not null && TradeExtensions<T>.IsItemBlocked(pk))
        {
            var itemName = pk.HeldItem > 0 ? GameInfo.GetStrings("en").Item[pk.HeldItem] : "(none)";
            var reply = await context.Channel.SendMessageAsync($"Trade blocked: The held item '{itemName}' cannot be traded.").ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }


        var la = new LegalityAnalysis(pk!);

        if (!la.Valid)
        {
            string responseMessage;
            if (pk?.IsEgg == true)
            {
                string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
                responseMessage = $"Invalid Showdown Set for the {speciesName} egg. Please review your information and try again.\n\nLegality Report:\n```\n{la.Report()}\n```";
            }
            else
            {
                string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
                responseMessage = $"{speciesName} attachment is not legal, and cannot be traded!\n\nLegality Report:\n```\n{la.Report()}\n```";
            }
            var reply = await context.Channel.SendMessageAsync(responseMessage).ConfigureAwait(false);
            await Task.Delay(6000);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }

        if (Info.Hub.Config.Legality.DisallowNonNatives && isNonNative)
        {
            string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
            _ = await context.Channel.SendMessageAsync($"This **{speciesName}** is not native to this game, and cannot be traded! Trade with the correct bot, then trade to HOME.").ConfigureAwait(false);
            return;
        }

        if (Info.Hub.Config.Legality.DisallowTracked && pk is IHomeTrack { HasTracker: true })
        {
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            _ = await context.Channel.SendMessageAsync($"This {speciesName} file is tracked by HOME, and cannot be traded!").ConfigureAwait(false);
            return;
        }

        // Handle past gen file requests
        if (!la.Valid)
        {
            if (la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
            {
                var clone = (T)pk!.Clone();
                clone.HandlingTrainerName = pk.OriginalTrainerName;
                clone.HandlingTrainerGender = pk.OriginalTrainerGender;
                if (clone is PK8 or PA8 or PB8 or PK9)
                    ((dynamic)clone).HandlingTrainerLanguage = (byte)pk.Language;
                clone.CurrentHandler = 1;
                la = new LegalityAnalysis(clone);
                if (la.Valid) pk = clone;
            }
        }

        await QueueHelper<T>.AddToQueueAsync(context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade,
            tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg,
            lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }
}
