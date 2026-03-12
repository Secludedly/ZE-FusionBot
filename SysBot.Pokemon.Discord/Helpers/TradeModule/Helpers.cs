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
using System.IO;
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

    public static Task<ProcessedPokemonResult<T>> ProcessShowdownSetAsync(string content, bool ignoreAutoOT = false, IEnumerable<string>? userRoles = null)
    {
        content = ReusableActions.StripCodeBlock(content);
        bool isEgg = TradeExtensions<T>.IsEggCheck(content);

        // Check role-based permissions for Batch Commands and Trainer Data Override
        bool canUseBatchCommands = true;
        bool canOverrideTrainerData = true;

        if (userRoles != null && SysCordSettings.Manager != null)
        {
            canUseBatchCommands = SysCordSettings.Manager.GetHasRoleAccess(nameof(DiscordManager.RolesUseBatchCommands), userRoles);
            canOverrideTrainerData = SysCordSettings.Manager.GetHasRoleAccess(nameof(DiscordManager.RolesAutoOT), userRoles);
        }

        // IMPORTANT: Remove trainer data overrides FIRST, then other batch commands
        // If user doesn't have permission for Trainer Data Override, remove them from content
        if (!canOverrideTrainerData && ContainsTrainerDataOverride(content))
        {
            content = RemoveTrainerDataOverrides(content);
        }

        // If user doesn't have permission for Batch Commands, remove NON-TRAINER batch commands from content
        if (!canUseBatchCommands && ContainsBatchCommands(content))
        {
            content = RemoveNonTrainerBatchCommands(content);
        }

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

        // ============================================================================
        // DITTO METLOCATION FIX
        // ============================================================================
        // Fix Ditto MetLocation for game version compatibility
        // ALM may select encounters from different games (e.g., SV location for SWSH trade)
        // This ensures Ditto has a valid MetLocation for the target game
        // Only apply the fix if Ditto is currently invalid to avoid overriding correct locations
        // ============================================================================
        if (pkm.Species == 132) // Species 132 = Ditto
        {
            var initialDittoLA = new LegalityAnalysis(pkm);
            if (!initialDittoLA.Valid)
            {
                // Ditto is invalid, try to fix MetLocation
                pkm.MetLocation = pkm switch
                {
                    PB8 => 400,  // BDSP: Grand Underground
                    PK9 => 28,   // SV: South Province (Area Three)
                    _ => 162,    // PK8 (SWSH): Route 5 / Wild Area
                };

                // Revalidate after fixing MetLocation and apply trash bytes fix
                var dittoLA = new LegalityAnalysis(pkm);
                pkm = (T)TradeExtensions<T>.TrashBytes(pkm, dittoLA); // CRITICAL: Assign result back!
            }
            else
            {
                // Ditto is already valid, just apply trash bytes without changing MetLocation
                pkm = (T)TradeExtensions<T>.TrashBytes(pkm, initialDittoLA);
            }
        }
        // ============================================================================
        // END OF DITTO METLOCATION FIX
        // ============================================================================

        var spec = GameInfo.Strings.Species[template.Species];

        // Apply standard item logic only for non-eggs
        if (!isEgg)
        {
            ApplyStandardItemLogic(pkm);
        }

        // Align language and nickname before legality check to avoid false invalids
        if (pkm is T pkBeforeCheck)
        {
            pkBeforeCheck.Language = finalLanguage;
            if (string.IsNullOrEmpty(set.Nickname))
            {
                // Force default species nickname for the target language
                var laNick = new LegalityAnalysis(pkBeforeCheck);
                pkBeforeCheck.SetDefaultNickname(laNick);
                pkBeforeCheck.IsNicknamed = false;
            }
        }

        // ============================================================================
        // MAX LAIR POKEMON MOVE POPULATION BUG WORKAROUND
        // ============================================================================
        // PKHeX.Core.dll (as of 01-22-2026, commit fe32739) has a bug where Max Lair
        // Pokemon from SWSH Crown Tundra do not get moves automatically populated
        // during legalization, causing them to be marked as illegal.
        //
        // This workaround manually populates moves for Max Lair encounters after
        // generation but before validation.
        // ============================================================================
        if (pkm is PK8 pk8 && !isEgg)
        {
            const int MaxLairLocationID = 244; // Max Lair in Crown Tundra
            bool hasNoMoves = pk8.Move1 == 0 && pk8.Move2 == 0 && pk8.Move3 == 0 && pk8.Move4 == 0;
            bool isFromMaxLair = pk8.MetLocation == MaxLairLocationID;

            if (hasNoMoves && isFromMaxLair)
            {
                // Populate moves using PKHeX (not ALM)
                pk8.SetSuggestedMoves();
                pk8.HealPP();
                pk8.RefreshChecksum();
            }
        }
        // ============================================================================
        // END OF MAX LAIR FIX
        // ============================================================================

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

        // Auto-fix language-related nickname mismatches for sets without a nickname
        if (!la.Valid && string.IsNullOrEmpty(set.Nickname))
        {
            if (la.Results.Any(r => r.Identifier is CheckIdentifier.Nickname))
            {
                // Clear nickname and re-validate; prevents false invalid when trainer language != set language
                _ = pkm.ClearNickname();
                la = new LegalityAnalysis(pkm);
            }
        }

        // Handle past gen file requests (PK8, PA8, PB8, PK9) - fix BEFORE returning error
        if (!la.Valid && pkm is T && la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
        {
            var clone = (T)(object)pkm.Clone();
            clone.HandlingTrainerName = pkm.OriginalTrainerName;
            clone.HandlingTrainerGender = pkm.OriginalTrainerGender;
            if (clone is PK8 or PA8 or PB8 or PK9)
                ((dynamic)clone).HandlingTrainerLanguage = (byte)pkm.Language;
            clone.CurrentHandler = 1;
            var laClone = new LegalityAnalysis(clone);
            if (laClone.Valid)
            {
                pkm = clone;
                la = laClone;
            }
        }

        // ============================================================================
        // MAX LAIR SHINY FALLBACK
        // ============================================================================
        // If a shiny PK8 is still invalid and not already at Max Lair, retry with
        // MetLocation=244. Many SWSH legendaries and Ultra Beasts are shiny-eligible
        // only via Dynamax Adventures (Max Lair). ALM sometimes generates them at
        // the correct location but without moves, or fails to set the shiny PID.
        // ============================================================================
        if (!la.Valid && pkm is PK8 pk8Retry && set.Shiny && pk8Retry.MetLocation != 244)
        {
            var pk8RetryClone = (PK8)pk8Retry.Clone();
            pk8RetryClone.MetLocation = 244;
            pk8RetryClone.SetSuggestedMoves();
            pk8RetryClone.HealPP();
            pk8RetryClone.RefreshChecksum();
            var laRetry = new LegalityAnalysis(pk8RetryClone);
            if (laRetry.Valid)
            {
                pkm = pk8RetryClone;
                la = laRetry;
            }
        }
        // Also retry if already at Max Lair but still invalid (wrong moves)
        else if (!la.Valid && pkm is PK8 pk8RetryLair && set.Shiny && pk8RetryLair.MetLocation == 244)
        {
            pk8RetryLair.SetSuggestedMoves();
            pk8RetryLair.HealPP();
            pk8RetryLair.RefreshChecksum();
            la = new LegalityAnalysis(pk8RetryLair);
        }
        // ============================================================================
        // END OF MAX LAIR SHINY FALLBACK
        // ============================================================================

        // ============================================================================
        // WC8 COMPETITION EVENT FIX — Direct WC8.ConvertToPKM
        // ============================================================================
        // For shiny PK8 from event MetLocations (>= 40000), ALM's generation fails
        // because competition WC8 events have a specific EC/PID generation algorithm
        // that our manual Xoroshiro fix can't reproduce correctly.
        // Instead: load the matching WC8 file from the MGDB and call ConvertToPKM
        // directly — this uses PKHeX's own verified generation logic.
        // ============================================================================
        if (!la.Valid && pkm is PK8 pk8WC && pk8WC.MetLocation >= 40000 && pk8WC.IsShiny)
        {
            var mgdbPath = Info.Hub.Config.Legality.MGDBPath;
            if (Directory.Exists(mgdbPath))
            {
                var wc8Files = Directory.GetFiles(mgdbPath, "*.wc8", SearchOption.AllDirectories);
                foreach (var wc8File in wc8Files)
                {
                    try
                    {
                        var wc8 = new WC8(File.ReadAllBytes(wc8File));
                        if (wc8.Species != pk8WC.Species || wc8.Form != pk8WC.Form)
                            continue;
                        if (wc8.IsShiny == false)
                            continue;

                        var directPkm = wc8.ConvertToPKM(sav);
                        if (directPkm is not T directT)
                            continue;

                        var laWC8 = new LegalityAnalysis(directPkm);
                        LogUtil.LogInfo($"WC8 ConvertToPKM: file={Path.GetFileName(wc8File)} valid={laWC8.Valid} fateful={directPkm.FatefulEncounter} shiny={directPkm.IsShiny}", "Legality");
                        if (laWC8.Valid)
                        {
                            pkm = directPkm;
                            la = laWC8;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogInfo($"WC8 ConvertToPKM error: {ex.Message}", "Legality");
                    }
                }
            }
        }
        // ============================================================================
        // END OF WC8 EVENT FIX
        // ============================================================================


        if (pkm is not T pk || !la.Valid)
        {
            // Diagnostic: log specific legality failure reasons
            if (pkm != null && !la.Valid)
            {
                var failReasons = string.Join(", ", la.Results
                    .Where(r => !r.Valid)
                    .Select(r => $"{r.Identifier}"));
                LogUtil.LogInfo($"TradeModule legality fail: species={pkm.Species} form={pkm.Form} loc={pkm.MetLocation} ot='{pkm.OriginalTrainerName}' shiny={pkm.IsShiny} shinyXor={pkm.ShinyXor} result='{result}' | {failReasons}", "Legality");
            }
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

        // Auto-fix nickname-only issues on attachments by clearing nickname and re-validating
        if (!la.Valid && la.Results.Any(r => r.Identifier is CheckIdentifier.Nickname))
        {
            var clone = (T)pk!.Clone();
            _ = clone.ClearNickname();
            var laNick = new LegalityAnalysis(clone);
            if (laNick.Valid)
            {
                pk = clone;
                la = laNick;
            }
        }

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

        // Past gen file fix is now handled in ProcessShowdownSetAsync before this point

        // Check if user has permission to use AutoOT (ALWAYS check, regardless of ignoreAutoOT value)
        if (SysCordSettings.Manager != null)
        {
            if (context.User is SocketGuildUser gUser)
            {
                var roles = gUser.Roles.Select(z => z.Name);
                bool hasAutoOTRole = SysCordSettings.Manager.GetHasRoleAccess(nameof(DiscordManager.RolesAutoOT), roles);
                if (!hasAutoOTRole)
                {
                    // User doesn't have AutoOT permission, force ignoreAutoOT to true
                    ignoreAutoOT = true;
                }
            }
        }

        await QueueHelper<T>.AddToQueueAsync(context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade,
            tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg,
            lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }

    public static bool ContainsBatchCommands(string content)
    {
        // Check for ANY batch command patterns (not trainer-specific)
        return content.Contains('.') &&
               (content.Contains('=') || content.Contains("++") || content.Contains("--"));
    }

    public static bool ContainsTrainerDataOverride(string content)
    {
        // Check if the original content contains trainer data overrides (Showdown style OR Batch commands)
        return content.Contains("OT:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("TID:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("SID:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("OTGender:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".OriginalTrainerName=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".TrainerTID7=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".TrainerID7=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".DisplayTID=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".TrainerSID7=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".DisplaySID=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".OriginalTrainerGender=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".TID16=", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(".SID16=", StringComparison.OrdinalIgnoreCase);
    }

    public static string RemoveBatchCommands(string content)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            // Remove ALL batch command lines (lines starting with .)
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('.'))
            {
                filteredLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, filteredLines);
    }

    public static string RemoveNonTrainerBatchCommands(string content)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // If it's a batch command line (starts with .)
            if (trimmed.StartsWith('.'))
            {
                // Keep ONLY trainer-related batch commands, remove everything else
                if (trimmed.StartsWith(".OriginalTrainerName=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".TrainerTID7=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".TrainerID7=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".DisplayTID=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".TrainerSID7=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".DisplaySID=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".OriginalTrainerGender=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".TID16=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(".SID16=", StringComparison.OrdinalIgnoreCase))
                {
                    // This is a trainer-related command, keep it
                    filteredLines.Add(line);
                }
                // else: skip this line (it's a non-trainer batch command)
            }
            else
            {
                // Not a batch command, keep it
                filteredLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, filteredLines);
    }

    public static string RemoveTrainerDataOverrides(string content)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Remove lines that contain trainer data overrides (both Showdown style and Batch commands)
            if (!(trimmed.StartsWith("OT:", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith("TID:", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith("SID:", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith("OTGender:", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".OriginalTrainerName=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".TrainerTID7=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".TrainerID7=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".DisplayTID=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".TrainerSID7=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".DisplaySID=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".OriginalTrainerGender=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".TID16=", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith(".SID16=", StringComparison.OrdinalIgnoreCase)))
            {
                filteredLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, filteredLines);
    }
}
