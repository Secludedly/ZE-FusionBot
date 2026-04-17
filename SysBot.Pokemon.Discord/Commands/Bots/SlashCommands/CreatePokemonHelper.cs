using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiscordColor = Discord.Color;
using static SysBot.Pokemon.Helpers.DetailedLegalityChecker;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

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
        string? ivs,
        string? evs,
        string specialFeature,
        Action<T>? postProcessing = null,
        string? nickname = null,
        string? abilitySlot = null,
        string? move1 = null,
        string? move2 = null,
        string? move3 = null,
        string? move4 = null,
        string? language = null
    ) where T : PKM, new()
    {
        // Parse the pokemon parameter (may be encoded as Species|FormIndex|ShowdownName)
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

        if (string.IsNullOrWhiteSpace(speciesInput))
        {
            await context.Interaction.FollowupAsync("❌ Pokemon parameter is empty! Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (!Enum.TryParse<Species>(speciesInput, true, out var species) || species <= 0)
        {
            await context.Interaction.FollowupAsync($"❌ Invalid Pokemon: **{speciesInput}**. Please use autocomplete to select a valid Pokemon.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        // Resolve ability name from slot before building the Showdown set
        string? resolvedAbilityName = ResolveAbilityName<T>(species, form, abilitySlot);

        var speciesName = showdownSpeciesName;
        var showdownBuilder = new StringBuilder();

        // Line 1: [Nickname (]Species[)] @ Item
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            showdownBuilder.AppendLine(!string.IsNullOrWhiteSpace(item)
                ? $"{nickname} ({speciesName}) @ {item}"
                : $"{nickname} ({speciesName})");
        }
        else
        {
            showdownBuilder.AppendLine(!string.IsNullOrWhiteSpace(item)
                ? $"{speciesName} @ {item}"
                : speciesName);
        }

        // Ability (if slot specified and resolved)
        if (!string.IsNullOrWhiteSpace(resolvedAbilityName))
            showdownBuilder.AppendLine($"Ability: {resolvedAbilityName}");

        // Nature — for ZA (PA9), skip random pick; ALM uses the legal encounter nature
        bool isZATarget = typeof(T) == typeof(PA9);
        string? finalNature = nature;
        if (string.IsNullOrWhiteSpace(finalNature) && !isZATarget)
        {
            string[] beneficialNatures = ["Adamant", "Jolly", "Timid", "Modest", "Careful", "Calm"];
            finalNature = beneficialNatures[new Random().Next(beneficialNatures.Length)];
        }

        if (!string.IsNullOrWhiteSpace(finalNature))
            showdownBuilder.AppendLine($"{finalNature} Nature");

        showdownBuilder.AppendLine($"Level: {level}");

        if (!string.IsNullOrWhiteSpace(ball))
            showdownBuilder.AppendLine($"Ball: {ball}");

        showdownBuilder.AppendLine(ParseIVString(ivs));

        if (!string.IsNullOrWhiteSpace(evs))
            showdownBuilder.AppendLine(ParseEVString(evs));

        if (shiny)
            showdownBuilder.AppendLine("Shiny: Yes");

        // Language line — extracted by LanguageHelper before ShowdownSet parsing
        if (!string.IsNullOrWhiteSpace(language))
            showdownBuilder.AppendLine($"Language: {language}");

        if (!string.IsNullOrWhiteSpace(specialFeature))
            showdownBuilder.AppendLine(specialFeature);

        // Moves come last in Showdown format
        foreach (var move in new[] { move1, move2, move3, move4 })
        {
            if (!string.IsNullOrWhiteSpace(move))
                showdownBuilder.AppendLine($"- {move}");
        }

        var showdownText = showdownBuilder.ToString();

        var processed = await Helpers<T>.ProcessShowdownSetAsync(showdownText, ignoreAutoOT: false).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(processed.Error) || processed.Pokemon == null)
        {
            await context.Interaction.FollowupAsync($"❌ {processed.Error ?? "Unknown error occurred during Pokemon generation."}", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        var pk = processed.Pokemon;
        var lgcode = processed.LgCode;

        // Apply custom IVs
        int[] requestedIVs = ParseIVValues(ivs);
        pk.IV_HP = requestedIVs[0];
        pk.IV_ATK = requestedIVs[1];
        pk.IV_DEF = requestedIVs[2];
        pk.IV_SPA = requestedIVs[3];
        pk.IV_SPD = requestedIVs[4];
        pk.IV_SPE = requestedIVs[5];

        if (pk is IHyperTrain ht)
        {
            ht.HT_HP = requestedIVs[0] < 31;
            ht.HT_ATK = requestedIVs[1] < 31;
            ht.HT_DEF = requestedIVs[2] < 31;
            ht.HT_SPA = requestedIVs[3] < 31;
            ht.HT_SPD = requestedIVs[4] < 31;
            ht.HT_SPE = requestedIVs[5] < 31;
        }

        // Set Nature — skip for ZA (PA9) where ProcessShowdownSetAsync handles mint logic
        if (pk is not PA9 && !string.IsNullOrWhiteSpace(finalNature) && Enum.TryParse<Nature>(finalNature, true, out var parsedNature))
        {
            pk.Nature = parsedNature;
            pk.StatNature = parsedNature;
        }

        pk.ResetPartyStats();
        pk.RefreshChecksum();

        if (pk.Form != form)
        {
            pk.Form = form;
            pk.ResetPartyStats();
            pk.RefreshChecksum();
        }

        postProcessing?.Invoke(pk);

        return await QueuePokemonForTradeAsync(context, pk, showdownSpeciesName, ignoreAutoOT: false, lgcode: lgcode).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the final legality check and queues a pre-built Pokemon for trade, sending all embeds.
    /// Used by all slash command modules that need to queue a Pokemon.
    /// </summary>
    public static async Task<bool> QueuePokemonForTradeAsync<T>(
        SocketInteractionContext context,
        T pk,
        string displayName,
        bool ignoreAutoOT = false,
        bool isMysteryEgg = false,
        List<Pictocodes>? lgcode = null
    ) where T : PKM, new()
    {
        var Info = SysCord<T>.Runner.Hub.Queues.Info;

        var commandPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
        if (!DetailedLegalityChecker.IsLegalWithDetailedReport(pk, displayName, commandPrefix, out string? legalityError))
        {
            await context.Interaction.FollowupAsync($"❌ **Illegal Pokemon Detected**\n\n{legalityError}", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        var code = Info.GetRandomTradeCode(context.User.Id);
        var userID = context.User.Id;

        var trainer_info = new PokeTradeTrainerInfo(context.User.Username, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer_info, code, context.User, 1, 1, false, lgcode: lgcode ?? []);

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int uniqueTradeID = (int)(timestamp & 0x7FFFFFFF);

        var detail = new PokeTradeDetail<T>(pk, trainer_info, notifier, PokeTradeType.Specific, code,
            false, null, 1, 1, false, false, uniqueTradeID, ignoreAutoOT: ignoreAutoOT);

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, context.User.Username, uniqueTradeID);

        var added = Info.AddToTradeQueue(trade, userID, false, false);

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

        await EmbedHelper.SendTradeCodeEmbedAsync(context.User, code).ConfigureAwait(false);

        // Build embed
        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(pk, context.User, false, false, false, false, false, false, 1, 1);

        DiscordColor embedColor;
        string embedImageUrl;

        if (isMysteryEgg)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg3.png";
            embedColor = DiscordColor.Gold;
        }
        else
        {
            (embedImageUrl, embedColor) = await QueueHelper<T>.PrepareEmbedDetails(pk).ConfigureAwait(false);
        }

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
        string footerText = $"Current Queue Position: {(position.Position == -1 ? 1 : position.Position)}";
        footerText += $"\nWait Estimate: {baseEta:F1} min(s) for trade.";
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
            await context.Interaction.FollowupAsync("✅ Pokemon added to queue! Check your DMs for the trade code.", ephemeral: true).ConfigureAwait(false);
            await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed).ConfigureAwait(false);
            await QueueHelper<T>.ScheduleFileDeletion(embedData.EmbedImageUrl, 0).ConfigureAwait(false);
        }
        else
        {
            await context.Interaction.FollowupAsync(embed: embed).ConfigureAwait(false);
        }

        return true;
    }

    private static string? ResolveAbilityName<T>(Species species, byte form, string? abilitySlot) where T : PKM, new()
    {
        if (string.IsNullOrWhiteSpace(abilitySlot) || species == Species.None)
            return null;

        try
        {
            var pi = GetPersonalInfo<T>(species, form);
            if (pi is not IPersonalAbility12 pa12) return null;

            int abilityID = abilitySlot.ToUpper() switch
            {
                "1" => pa12.Ability2,
                "H" when pa12 is IPersonalAbility12H paH => paH.AbilityH,
                _ => pa12.Ability1
            };

            if (abilityID <= 0 || abilityID >= GameInfo.Strings.Ability.Count)
                return null;

            var abilityName = GameInfo.Strings.Ability[abilityID];
            return string.IsNullOrWhiteSpace(abilityName) ? null : abilityName;
        }
        catch { return null; }
    }

    private static IPersonalInfo? GetPersonalInfo<T>(Species species, byte form) where T : PKM, new()
    {
        try
        {
            IPersonalTable table = typeof(T).Name switch
            {
                "PK9" => PersonalTable.SV,
                "PA9" => PersonalTable.ZA,
                "PK8" => PersonalTable.SWSH,
                "PB8" => PersonalTable.BDSP,
                "PA8" => PersonalTable.LA,
                "PB7" => PersonalTable.GG,
                _ => PersonalTable.SV
            };
            return table.GetFormEntry((ushort)species, form);
        }
        catch { return null; }
    }

    private static int[] ParseIVValues(string? ivs)
    {
        if (string.IsNullOrWhiteSpace(ivs))
            return [31, 31, 31, 31, 31, 31];

        if (ivs.Contains("HP") || ivs.Contains("Atk") || ivs.Contains("Def"))
        {
            int[] result = [31, 31, 31, 31, 31, 31];
            var parts = ivs.Replace("IVs:", "").Trim().Split('/');
            foreach (var part in parts)
            {
                var tokens = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2 && int.TryParse(tokens[0], out int value))
                {
                    value = Math.Clamp(value, 0, 31);
                    switch (tokens[1].ToUpperInvariant())
                    {
                        case "HP":  result[0] = value; break;
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

        var ivParts = ivs.Split('/');
        if (ivParts.Length == 6)
        {
            int[] ivValues = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(ivParts[i].Trim(), out ivValues[i]) || ivValues[i] < 0 || ivValues[i] > 31)
                    ivValues[i] = 31;
            }
            return ivValues;
        }

        return [31, 31, 31, 31, 31, 31];
    }

    private static string ParseIVString(string? ivs)
    {
        if (string.IsNullOrWhiteSpace(ivs))
            return "IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe";

        if (ivs.Contains("HP") || ivs.Contains("Atk") || ivs.Contains("Def"))
            return ivs.StartsWith("IVs:") ? ivs : $"IVs: {ivs}";

        var parts = ivs.Split('/');
        if (parts.Length == 6)
        {
            int[] ivValues = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out ivValues[i]) || ivValues[i] < 0 || ivValues[i] > 31)
                    ivValues[i] = 31;
            }
            return $"IVs: {ivValues[0]} HP / {ivValues[1]} Atk / {ivValues[2]} Def / {ivValues[3]} SpA / {ivValues[4]} SpD / {ivValues[5]} Spe";
        }

        return "IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe";
    }

    private static string ParseEVString(string? evs)
    {
        if (string.IsNullOrWhiteSpace(evs))
            return string.Empty;

        if (evs.Contains("HP") || evs.Contains("Atk") || evs.Contains("Def"))
            return evs.StartsWith("EVs:") ? evs : $"EVs: {evs}";

        var parts = evs.Split('/');
        if (parts.Length == 6)
        {
            int[] evValues = new int[6];
            int totalEVs = 0;

            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out evValues[i]) || evValues[i] < 0 || evValues[i] > 252)
                    evValues[i] = 0;
                totalEVs += evValues[i];
            }

            if (totalEVs > 510)
            {
                double scaleFactor = 510.0 / totalEVs;
                for (int i = 0; i < 6; i++)
                    evValues[i] = (int)(evValues[i] * scaleFactor);
            }

            return $"EVs: {evValues[0]} HP / {evValues[1]} Atk / {evValues[2]} Def / {evValues[3]} SpA / {evValues[4]} SpD / {evValues[5]} Spe";
        }

        return string.Empty;
    }
}
