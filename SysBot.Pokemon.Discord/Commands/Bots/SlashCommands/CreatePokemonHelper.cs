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
            await context.Interaction.FollowupAsync($"ƒ?O Pokemon parameter is empty! Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Parse Pokemon species
        if (!Enum.TryParse<Species>(speciesInput, true, out var species) || species <= 0)
        {
            await context.Interaction.FollowupAsync($"ƒ?O Invalid Pokemon: **{speciesInput}**. Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Build Showdown format string
        var speciesName = showdownSpeciesName;
        var showdownBuilder = new StringBuilder();

        // Line 1: Pokemon @ Item
        if (!string.IsNullOrWhiteSpace(item))
            showdownBuilder.AppendLine($"{speciesName} @ {item}");
        else
            showdownBuilder.AppendLine(speciesName);

        // Line 2: Nature - Use provided nature or pick a random beneficial one
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

        // IMPORTANT: Use proper Showdown IVs format
        showdownBuilder.AppendLine("IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe");

        if (shiny)
            showdownBuilder.AppendLine("Shiny: Yes");

        // Add special feature
        if (!string.IsNullOrWhiteSpace(specialFeature))
            showdownBuilder.AppendLine(specialFeature);

        var showdownText = showdownBuilder.ToString();

        // No role restrictions - AutoOT is enabled for everyone
        bool ignoreAutoOT = false;

        // Process showdown set
        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownText, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
        {
            var errorMsg = processed.Error ?? "Unknown error occurred during Pokemon generation.";
            await context.Interaction.FollowupAsync($"❌ {errorMsg}", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        var pk = processed.Pokemon;

        // CRITICAL: Set IVs and Nature AFTER legalization, as ALM may override them
        // Set all IVs to 31 (perfect)
        pk.IV_HP = 31;
        pk.IV_ATK = 31;
        pk.IV_DEF = 31;
        pk.IV_SPA = 31;
        pk.IV_SPD = 31;
        pk.IV_SPE = 31;

        // CRITICAL: Clear ALL Hyper Training flags since we have perfect IVs
        // Having HT flags with perfect IVs makes the Pokemon illegal
        if (pk is IHyperTrain ht)
        {
            ht.HT_HP = false;
            ht.HT_ATK = false;
            ht.HT_DEF = false;
            ht.HT_SPA = false;
            ht.HT_SPD = false;
            ht.HT_SPE = false;
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

        // Apply post-processing (for Gigantamax, TeraType, etc.)
        postProcessing?.Invoke(pk);

        // Generate trade code
        var code = Info.GetRandomTradeCode(context.User.Id);
        var sig = RequestSignificance.None;
        var userID = context.User.Id;

        // Create trade detail
        var trainer_info = new PokeTradeTrainerInfo(context.User.Username, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer_info, code, context.User, 1, 1, false, lgcode: processed.LgCode ?? []);

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
            await context.Interaction.FollowupAsync("ƒ?O You are already in the queue!", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (added == QueueResultAdd.QueueFull)
        {
            await context.Interaction.FollowupAsync("ƒ?O The queue is currently full. Please try again later.", ephemeral: true).ConfigureAwait(false);
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
}
