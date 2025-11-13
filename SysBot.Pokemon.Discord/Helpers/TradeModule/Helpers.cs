using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
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
        content = ReusableActions.StripCodeBlock(content);
        bool isEgg = TradeExtensions<T>.IsEggCheck(content);

        if (!ShowdownParsing.TryParseAnyLanguage(content, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = "Unable to parse Showdown set. Could not identify the Pokémon species.",
                ShowdownSet = set
            });
        }

        byte finalLanguage = LanguageHelper.GetFinalLanguage(
            content, set,
            (byte)Info.Hub.Config.Legality.GenerateLanguage,
            TradeExtensions<T>.DetectShowdownLanguage
        );

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Filter out batch commands (.) and filters (~) from invalid lines - these are handled by ALM
        var actualInvalidLines = set.InvalidLines.Where(line =>
        {
            var text = line.Value?.Trim();
            return !string.IsNullOrEmpty(text) && !text.StartsWith('.') && !text.StartsWith('~');
        }).ToList();

        if (actualInvalidLines.Count != 0)
        {
            return Task.FromResult(new ProcessedPokemonResult<T>
            {
                Error = $"Unable to parse Showdown Set:\n{string.Join("\n", actualInvalidLines.Select(l => l.Value))}",
                ShowdownSet = set
            });
        }

        var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);

        PKM pkm;
        string result;

        // Generate egg or normal pokemon based on isEgg flag
        if (isEgg)
        {
            // Use ALM's GenerateEgg method for eggs
            pkm = sav.GenerateEgg(template, out var eggResult);
            result = eggResult.ToString();
        }
        else
        {
            // Use normal generation for non-eggs
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

        pk.Language = finalLanguage;

        if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
            _ = pk.ClearNickname();

        pk.ResetPartyStats();
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

    public static async Task AddTradeToQueueAsync(SocketCommandContext context, int code, string trainerName, T? pk, RequestSignificance sig,
        SocketUser usr, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1,
        bool isHiddenTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null,
        PokeTradeType tradeType = PokeTradeType.Specific, bool ignoreAutoOT = false, bool setEdited = false,
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
