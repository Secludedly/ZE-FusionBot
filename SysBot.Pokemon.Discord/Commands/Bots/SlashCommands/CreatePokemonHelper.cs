using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiscordColor = Discord.Color;
using static SysBot.Pokemon.Helpers.DetailedLegalityChecker;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Helper class containing common logic for all CreatePokemon commands
/// </summary>
public static class CreatePokemonHelper
{
    public static async Task<bool> ExecuteCreatePokemonAsync<T>(
        SocketInteractionContext context,
        string pokemon,
        bool shiny,
        string? item,
        string? ball,
        int level,
        string? nature,
        string? ivs,     // Optional: "31/31/31/31/31/31" or "31 HP / 31 Atk / ..." format
        string? evs,     // Optional: "252/252/4/0/0/0" or "252 HP / 252 Atk / ..." format
        string specialFeature, // "Alpha: Yes", "Gigantamax", etc.
        Action<T>? postProcessing = null) where T : PKM, new()
    {
        var Info = SysCord<T>.Runner.Hub.Queues.Info;

        // Pokemon parameter can encode form data as Species|FormIndex|ShowdownName
        var speciesInput = pokemon;
        byte form = 0;
        string showdownSpeciesName = pokemon;

        if (!string.IsNullOrWhiteSpace(pokemon) && pokemon.Contains('|'))
        {
            var parts = pokemon.Split('|');
            speciesInput = parts.ElementAtOrDefault(0) ?? pokemon;
            if (byte.TryParse(parts.ElementAtOrDefault(1), out var parsedForm))
                form = parsedForm;
            showdownSpeciesName = parts.ElementAtOrDefault(2) ?? speciesInput;
        }

        // Validate pokemon parameter
        if (string.IsNullOrWhiteSpace(speciesInput))
        {
            await context.Interaction.FollowupAsync($"❌ Pokemon parameter is empty! Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Parse Pokemon species
        if (!Enum.TryParse<Species>(speciesInput, true, out var species) || species <= 0)
        {
            await context.Interaction.FollowupAsync($"❌ Invalid Pokemon: **{speciesInput}**. Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Build Showdown format string
        // For ZA Pokemon, IVEnforcer will handle encounters in ForcedEncounterEnforcer class
        var speciesName = showdownSpeciesName;
        var showdownBuilder = new StringBuilder();

        // Line 1: Pokemon @ Item
        if (!string.IsNullOrWhiteSpace(item))
            showdownBuilder.AppendLine($"{speciesName} @ {item}");
        else
            showdownBuilder.AppendLine(speciesName);

        // Line 2: Nature - Use provided or pick a random beneficial one
        string finalNature = nature;
        if (string.IsNullOrWhiteSpace(finalNature))
        {
            // Pick a random beneficial nature if none specified
            string[] beneficialNatures = ["Adamant", "Jolly", "Timid", "Modest", "Careful", "Calm"];
            var random = new Random();
            finalNature = beneficialNatures[random.Next(beneficialNatures.Length)];
        }
        showdownBuilder.AppendLine($"{finalNature} Nature");

        // Stats and other properties
        showdownBuilder.AppendLine($"Level: {level}");

        if (!string.IsNullOrWhiteSpace(ball))
            showdownBuilder.AppendLine($"Ball: {ball}");

        // IVs - Parse and validate custom IVs if provided
        string ivString = ParseIVString(ivs);
        showdownBuilder.AppendLine(ivString);

        // EVs - Parse and apply custom EVs if provided
        if (!string.IsNullOrWhiteSpace(evs))
        {
            string evString = ParseEVString(evs);
            showdownBuilder.AppendLine(evString);
        }

        // Shiny
        if (shiny)
            showdownBuilder.AppendLine("Shiny: Yes");

        // Add special feature
        if (!string.IsNullOrWhiteSpace(specialFeature))
            showdownBuilder.AppendLine(specialFeature);

        var showdownText = showdownBuilder.ToString();

        // Process through normal ALM pipeline
        // For encounters in ForcedEncounterEnforcer class, we'll apply the forced attributes AFTER ALM succeeds
        bool ignoreAutoOT = false; // AutoOT enabled for everyone in slash commands
        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownText, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
        {
            var errorMsg = processed.Error ?? "Unknown error occurred during Pokemon generation.";
            await context.Interaction.FollowupAsync($"❌ {errorMsg}", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        var pk = processed.Pokemon;
        var lgcode = processed.LgCode;

        // ===================================================================
        // Use IVEnforcer for ZA Pokemon (same as text commands)
        // This handles encounters in ForcedEncounterEnforcer classes automatically
        // ===================================================================
        if (pk.Version == GameVersion.ZA)
        {
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var set = new ShowdownSet(showdownText);

            // Parse the IVs from the showdown set
            int[] requestedIVs = set.IVs != null && set.IVs.Count() == 6
                ? set.IVs.ToArray()
                : new[] { 31, 31, 31, 31, 31, 31 }; // Default to perfect IVs

            // Use IVEnforcer which handles encounters in ForcedEncounterEnforcer class correctly
            var template = AutoLegalityWrapper.GetTemplate(set);
            IVEnforcer.ApplyRequestedIVsAndForceNature(
                pk,
                requestedIVs,
                set.Nature,
                set.Shiny,
                sav,
                template,
                userHTPreferences: null
            );
        }
        else
        {
            // Non-ZA Pokemon: Apply custom IVs from user input
            // Parse IVs from the ivs parameter (supports "31/31/31/31/31/31" or Showdown format)
            int[] requestedIVs = ParseIVValues(ivs);

            pk.IV_HP = requestedIVs[0];
            pk.IV_ATK = requestedIVs[1];
            pk.IV_DEF = requestedIVs[2];
            pk.IV_SPA = requestedIVs[3];
            pk.IV_SPD = requestedIVs[4];
            pk.IV_SPE = requestedIVs[5];

            // Clear Hyper Training flags for stats that are already 31, set for those that aren't
            if (pk is IHyperTrain ht)
            {
                ht.HT_HP = requestedIVs[0] < 31;
                ht.HT_ATK = requestedIVs[1] < 31;
                ht.HT_DEF = requestedIVs[2] < 31;
                ht.HT_SPA = requestedIVs[3] < 31;
                ht.HT_SPD = requestedIVs[4] < 31;
                ht.HT_SPE = requestedIVs[5] < 31;
            }

            // Set Nature - use provided or the random one we picked earlier
            if (Enum.TryParse<Nature>(finalNature, true, out var parsedNature))
            {
                pk.Nature = parsedNature;
                pk.StatNature = parsedNature;
            }

            // Refresh stats after IV/Nature changes
            pk.ResetPartyStats();
            pk.RefreshChecksum();

            // Apply requested form if one was specified
            if (form > 0 && pk.Form != form)
            {
                pk.Form = form;
                pk.ResetPartyStats();
                pk.RefreshChecksum();
            }
        }

        // Apply post-processing (for Gigantamax, TeraType, etc.)
        postProcessing?.Invoke(pk);

        // ===================================================================
        // Final legality check BEFORE adding to queue
        // Prevents illegal Pokemon from being injected into the game
        // ===================================================================
        var commandPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
        string displayName = showdownSpeciesName;
        if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError))
        {
            // Pokemon is illegal so we send detailed error message to user
            await context.Interaction.FollowupAsync($"❌ **Illegal Pokemon Detected**\n\n{legalityError}", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Generate trade code
        var code = Info.GetRandomTradeCode(context.User.Id);
        var sig = RequestSignificance.None;
        var userID = context.User.Id;

        // Create trade detail
        var trainer_info = new PokeTradeTrainerInfo(context.User.Username, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer_info, code, context.User, 1, 1, false, lgcode: lgcode ?? []);

        // Generate unique trade ID
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);

        var detail = new PokeTradeDetail<T>(pk, trainer_info, notifier, PokeTradeType.Specific, code,
            sig == RequestSignificance.Favored, null, 1, 1, false, false, uniqueTradeID, ignoreAutoOT: ignoreAutoOT);

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, context.User.Username, uniqueTradeID);

        // Add to queue
        var added = Info.AddToTradeQueue(trade, userID, false, sig == RequestSignificance.Owner);

        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Interaction.FollowupAsync("❌ You are already in the queue!", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (added == QueueResultAdd.QueueFull)
        {
            await context.Interaction.FollowupAsync("❌ The queue is currently full. Please try again later.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Send trade code embed via DM
        await EmbedHelper.SendTradeCodeEmbedAsync(context.User, code).ConfigureAwait(false);

        // Build channel embed
        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
            pk, context.User, false, false, false, false, false, false, 1, 1
        );

        (string embedImageUrl, DiscordColor embedColor) = await QueueHelper<T>.PrepareEmbedDetails(pk).ConfigureAwait(false);
        embedData.EmbedImageUrl = embedImageUrl;

        embedData.HeldItemUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
        {
            string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
            embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
        }

        embedData.IsLocalFile = System.IO.File.Exists(embedData.EmbedImageUrl);

        var position = Info.CheckPosition(userID, uniqueTradeID, PokeRoutineType.LinkTrade);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
        var etaMessage = $"Wait Estimate: {baseEta:F1} min(s) for trade.";
        string footerText = $"Current Queue Position: {(position.Position == -1 ? 1 : position.Position)}";
        footerText += $"\n{etaMessage}";
        footerText += $"\nZE FusionBot {TradeBot.Version}";

        var embedBuilder = new EmbedBuilder()
            .WithColor(embedColor)
            .WithImageUrl(embedData.IsLocalFile ? $"attachment://{System.IO.Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
            .WithFooter(footerText)
            .WithAuthor(new EmbedAuthorBuilder()
                .WithName(embedData.AuthorName)
                .WithIconUrl(context.User.GetAvatarUrl() ?? context.User.GetDefaultAvatarUrl())
                .WithUrl("https://hideoutpk.de"));

        DetailsExtractor<T>.AddAdditionalText(embedBuilder);
        DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, context.User.Mention, pk);
        DetailsExtractor<T>.AddThumbnails(embedBuilder, false, false, embedData.HeldItemUrl);

        var embed = embedBuilder.Build();

        if (embedData.IsLocalFile)
        {
            // First, dismiss the "thinking..." message with an empty followup
            await context.Interaction.FollowupAsync("✅ Pokemon added to queue! Check your DMs for the trade code.", ephemeral: true).ConfigureAwait(false);
            // Then send the embed with the file in the channel
            await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed).ConfigureAwait(false);
            await QueueHelper<T>.ScheduleFileDeletion(embedData.EmbedImageUrl, 0).ConfigureAwait(false);
        }
        else
        {
            await context.Interaction.FollowupAsync(embed: embed).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Parses IV string input and returns the IV values as an int array [HP, Atk, Def, SpA, SpD, Spe].
    /// Supports formats: "31/31/31/31/31/31" or "31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe"
    /// </summary>
    private static int[] ParseIVValues(string? ivs)
    {
        // Default to perfect IVs if none provided
        if (string.IsNullOrWhiteSpace(ivs))
            return [31, 31, 31, 31, 31, 31];

        // Try to parse Showdown format first: "31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe"
        if (ivs.Contains("HP") || ivs.Contains("Atk") || ivs.Contains("Def"))
        {
            int[] result = [31, 31, 31, 31, 31, 31];
            var cleanIvs = ivs.Replace("IVs:", "").Trim();
            var parts = cleanIvs.Split('/');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2 && int.TryParse(tokens[0], out int value))
                {
                    value = Math.Clamp(value, 0, 31);
                    var stat = tokens[1].ToUpperInvariant();
                    switch (stat)
                    {
                        case "HP": result[0] = value; break;
                        case "ATK": result[1] = value; break;
                        case "DEF": result[2] = value; break;
                        case "SPA": result[3] = value; break;
                        case "SPD": result[4] = value; break;
                        case "SPE": result[5] = value; break;
                    }
                }
            }
            return result;
        }

        // Parse slash-separated format: "31/31/31/31/31/31"
        var ivParts = ivs.Split('/');
        if (ivParts.Length == 6)
        {
            int[] ivValues = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(ivParts[i].Trim(), out ivValues[i]) || ivValues[i] < 0 || ivValues[i] > 31)
                {
                    ivValues[i] = 31; // Default to 31 if invalid
                }
            }
            return ivValues;
        }

        // Invalid format, default to perfect IVs
        return [31, 31, 31, 31, 31, 31];
    }

    /// <summary>
    /// Parses IV string input from user and returns Showdown format.
    /// Supports formats: "31/31/31/31/31/31" or "31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe"
    /// </summary>
    private static string ParseIVString(string? ivs)
    {
        // Default to perfect IVs if none provided
        if (string.IsNullOrWhiteSpace(ivs))
            return "IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe";

        // If already in showdown format, return as-is (with IVs: prefix if missing)
        if (ivs.Contains("HP") || ivs.Contains("Atk") || ivs.Contains("Def"))
        {
            return ivs.StartsWith("IVs:") ? ivs : $"IVs: {ivs}";
        }

        // Parse slash-separated format: "31/31/31/31/31/31"
        var parts = ivs.Split('/');
        if (parts.Length == 6)
        {
            // Validate each IV value (0-31)
            int[] ivValues = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out ivValues[i]) || ivValues[i] < 0 || ivValues[i] > 31)
                {
                    // Invalid IV, default to 31
                    ivValues[i] = 31;
                }
            }

            return $"IVs: {ivValues[0]} HP / {ivValues[1]} Atk / {ivValues[2]} Def / {ivValues[3]} SpA / {ivValues[4]} SpD / {ivValues[5]} Spe";
        }

        // Invalid format, default to perfect IVs
        return "IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe";
    }

    /// <summary>
    /// Parses EV string input from user and returns Showdown format.
    /// Supports formats: "252/252/4/0/0/0" or "252 HP / 252 Atk / 4 Def / 0 SpA / 0 SpD / 0 Spe"
    /// </summary>
    private static string ParseEVString(string? evs)
    {
        if (string.IsNullOrWhiteSpace(evs))
            return string.Empty;

        // If already in showdown format, return as-is (with EVs: prefix if missing)
        if (evs.Contains("HP") || evs.Contains("Atk") || evs.Contains("Def"))
        {
            return evs.StartsWith("EVs:") ? evs : $"EVs: {evs}";
        }

        // Parse slash-separated format: "252/252/4/0/0/0"
        var parts = evs.Split('/');
        if (parts.Length == 6)
        {
            // Validate each EV value (0-252, total max 510)
            int[] evValues = new int[6];
            int totalEVs = 0;

            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out evValues[i]) || evValues[i] < 0 || evValues[i] > 252)
                {
                    evValues[i] = 0;
                }
                totalEVs += evValues[i];
            }

            // If total EVs exceed 510, scale them down proportionally
            if (totalEVs > 510)
            {
                double scaleFactor = 510.0 / totalEVs;
                for (int i = 0; i < 6; i++)
                {
                    evValues[i] = (int)(evValues[i] * scaleFactor);
                }
            }

            return $"EVs: {evValues[0]} HP / {evValues[1]} Atk / {evValues[2]} Def / {evValues[3]} SpA / {evValues[4]} SpD / {evValues[5]} Spe";
        }

        // Invalid format, return empty (no EVs)
        return string.Empty;
    }
}
