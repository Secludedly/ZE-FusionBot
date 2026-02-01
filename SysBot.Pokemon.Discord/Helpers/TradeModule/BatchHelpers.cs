using Discord;
using Discord.Commands;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class BatchHelpers<T> where T : PKM, new()
{
    public static List<string> ParseBatchTradeContent(string content)
    {
        var delimiters = new[] { "---", "—-" };
        return [.. content.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Select(trade => trade.Trim())];
    }

    public static async Task<(T? Pokemon, string? Error, ShowdownSet? Set, string? LegalizationHint)>
        ProcessSingleTradeForBatch(string tradeContent)
    {
        tradeContent = ReusableActions.StripCodeBlock(tradeContent);
        tradeContent = BatchCommandNormalizer.NormalizeBatchCommands(tradeContent);

        // Parse hypertraining preferences before processing
        var userHTPreferences = AutoLegalityExtensionsDiscord.ParseHyperTrainingCommandsPublic(tradeContent);

        var result = await Helpers<T>.ProcessShowdownSetAsync(tradeContent);

        if (result.Pokemon != null)
        {
            var pk = result.Pokemon;
            var set = result.ShowdownSet;

            // Apply the same post-processing that single trades use
            if (set != null)
            {
                // Get trainer info and template for IVEnforcer
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var template = AutoLegalityWrapper.GetTemplate(set);

                // --------------------------------------------------
                // Forced encounter Nature override
                // Same logic as ProcessTradeAsync lines 1362-1414
                // --------------------------------------------------
                if (pk.Version == GameVersion.ZA && !pk.FatefulEncounter)
                {
                    // Check if user explicitly set a different StatNature via batch command (.StatNature=)
                    bool hasExplicitStatNature = pk.StatNature != pk.Nature;
                    Nature explicitStatNature = hasExplicitStatNature ? pk.StatNature : Nature.Random;

                    // Store user's requested nature for stat nature (minting)
                    Nature userRequestedNature = set.Nature;

                    // Check for special Nature handling (e.g., Toxtricity)
                    if (ForcedEncounterEnforcer.HasSpecialNatureHandling(pk, out var randomLegalNature))
                    {
                        // Toxtricity-like Pokemon: Only certain Natures are legal as actual Natures
                        if (userRequestedNature != Nature.Random && !ForcedEncounterEnforcer.IsNatureLegal(pk, userRequestedNature))
                        {
                            // User requested an illegal Nature - mint it as Stat Nature
                            pk.Nature = randomLegalNature;
                            pk.StatNature = hasExplicitStatNature ? explicitStatNature : userRequestedNature;
                            LogUtil.LogInfo(
                                $"{(Species)pk.Species}: Requested Nature {userRequestedNature} is illegal as actual Nature. Using random legal Nature {randomLegalNature} (actual) with {(hasExplicitStatNature ? explicitStatNature : userRequestedNature)} (stat nature/minted) - batch trade",
                                nameof(BatchHelpers<T>));
                            pk.RefreshChecksum();
                        }
                        else if (userRequestedNature == Nature.Random)
                        {
                            // No specific Nature requested, use random legal Nature
                            pk.Nature = randomLegalNature;
                            pk.StatNature = hasExplicitStatNature ? explicitStatNature : randomLegalNature;
                            LogUtil.LogInfo(
                                $"{(Species)pk.Species}: Using random legal Nature {randomLegalNature} (special Nature handling - batch trade)",
                                nameof(BatchHelpers<T>));
                            pk.RefreshChecksum();
                        }
                        else
                        {
                            // User requested a legal Nature
                            pk.Nature = userRequestedNature;
                            pk.StatNature = hasExplicitStatNature ? explicitStatNature : userRequestedNature;
                            LogUtil.LogInfo(
                                $"{(Species)pk.Species}: Using requested legal Nature {userRequestedNature} (special Nature handling - batch trade)",
                                nameof(BatchHelpers<T>));
                            pk.RefreshChecksum();
                        }
                    }
                    else if (ForcedEncounterEnforcer.TryGetForcedNature(pk, out var forcedNature))
                    {
                        if (pk.Nature != forcedNature)
                        {
                            pk.Nature = forcedNature;

                            // Priority for Stat Nature:
                            // 1.If user explicitly set Stat Nature via batch command(.StatNature = / Stat Nature:), use that.
                            // 2.However, if a user requested a different nature than a forced one, mint it(use requested as Stat Nature).
                            // 3.Otherwise, use forced nature as Stat Nature.
                            if (hasExplicitStatNature)
                            {
                                pk.StatNature = explicitStatNature;
                                LogUtil.LogInfo(
                                    $"{(Species)pk.Species}: Nature forced to {forcedNature} with explicit StatNature {explicitStatNature} (static encounter - batch trade)",
                                    nameof(BatchHelpers<T>));
                            }
                            else if (userRequestedNature != Nature.Random && userRequestedNature != forcedNature)
                            {
                                pk.StatNature = userRequestedNature;
                                LogUtil.LogInfo(
                                    $"{(Species)pk.Species}: Nature minted from {forcedNature} (actual) to {userRequestedNature} (stat nature) due to static encounter (batch trade)",
                                    nameof(BatchHelpers<T>));
                            }
                            else
                            {
                                pk.StatNature = forcedNature;
                                LogUtil.LogInfo(
                                    $"{(Species)pk.Species}: User-requested Nature overridden to {forcedNature} (forced encounter rule - batch trade)",
                                    nameof(BatchHelpers<T>));
                            }

                            pk.RefreshChecksum();
                        }
                    }
                }

                // -----------------------------
                // Apply Nature enforcement for non-ZA games
                // For non-ZA games, manually set Nature if it doesn't match
                // -----------------------------
                if (pk.Version != GameVersion.ZA)
                {
                    // For non-ZA games, check if the generated Nature matches the requested Nature
                    // If not, manually set it (no PID rerolling needed for non-ZA)
                    if (set.Nature != Nature.Random && pk.Nature != set.Nature)
                    {
                        // Check if user explicitly set Stat Nature
                        bool hasExplicitStatNature = pk.StatNature != pk.Nature;
                        Nature explicitStatNature = hasExplicitStatNature ? pk.StatNature : Nature.Random;

                        pk.Nature = set.Nature;

                        // Preserve explicit Stat Nature if user set it, otherwise match the nature
                        if (hasExplicitStatNature)
                        {
                            pk.StatNature = explicitStatNature;
                        }
                        else
                        {
                            pk.StatNature = set.Nature;
                        }

                        pk.RefreshChecksum();
                    }
                }

                // -----------------------------
                // Apply IVs, HyperTraining, Nature, Shiny for ZA games
                // Same logic as ProcessTradeAsync lines 1407-1424
                // -----------------------------
                if (pk.Version == GameVersion.ZA)
                {
                    // Capture explicit StatNature BEFORE IVEnforcer runs
                    bool hasExplicitStatNature = pk.StatNature != pk.Nature;
                    Nature explicitStatNature = hasExplicitStatNature ? pk.StatNature : Nature.Random;

                    // Always pass full IV array if present; otherwise default logic applies inside IVEnforcer
                    int[] requestedIVs =
                        set.IVs != null && set.IVs.Count() == 6
                            ? set.IVs.ToArray()
                            : Array.Empty<int>();

                    IVEnforcer.ApplyRequestedIVsAndForceNature(
                        pk,
                        requestedIVs,
                        set.Nature,
                        set.Shiny,
                        sav,
                        template,
                        userHTPreferences
                    );

                    // Verify that Nature was set correctly after IVEnforcer
                    // Safety check in case IVEnforcer didn't apply the Nature for some reason
                    if (set.Nature != Nature.Random && pk.Nature != set.Nature)
                    {
                        LogUtil.LogInfo(
                            $"{(Species)pk.Species}: IVEnforcer did not apply requested Nature {set.Nature}, current Nature is {pk.Nature}. Forcing Nature manually.",
                            nameof(BatchHelpers<T>));

                        // Fallback: Use NatureEnforcer directly for non-forced-encounter Pokemon
                        if (!ForcedEncounterEnforcer.TryGetForcedNature(pk, out _))
                        {
                            NatureEnforcer.ForceNature(pk, set.Nature, set.Shiny);
                        }
                    }

                    // Restore explicit Stat Nature if user set it
                    // IVEnforcer should preserve it, but double-check as a safety measure
                    if (hasExplicitStatNature && pk.StatNature != explicitStatNature)
                    {
                        LogUtil.LogInfo(
                            $"{(Species)pk.Species}: Restoring explicit StatNature {explicitStatNature} (was changed to {pk.StatNature})",
                            nameof(BatchHelpers<T>));
                        pk.StatNature = explicitStatNature;
                        pk.RefreshChecksum();
                    }
                }
            }

            return (pk, null, result.ShowdownSet, null);
        }

        return (null, result.Error, result.ShowdownSet, result.LegalizationHint);
    }

    public static async Task SendBatchErrorEmbedAsync(SocketCommandContext context, List<BatchTradeError> errors, int totalTrades)
    {
        var embed = new EmbedBuilder()
            .WithTitle("❌ Batch Trade Validation Failed")
            .WithColor(Color.Red)
            .WithDescription($"{errors.Count} out of {totalTrades} Pokémon could not be processed.")
            .WithFooter("Please fix the invalid sets and try again.");

        foreach (var error in errors)
        {
            var fieldValue = $"**Error:** {error.ErrorMessage}";
            if (!string.IsNullOrEmpty(error.LegalizationHint))
            {
                fieldValue += $"\n💡 **Hint:** {error.LegalizationHint}";
            }

            if (!string.IsNullOrEmpty(error.ShowdownSet))
            {
                var lines = error.ShowdownSet.Split('\n').Take(2);
                fieldValue += $"\n**Set:** {string.Join(" | ", lines)}...";
            }

            if (fieldValue.Length > 1024)
            {
                fieldValue = fieldValue[..1021] + "...";
            }

            embed.AddField($"Trade #{error.TradeNumber} - {error.SpeciesName}", fieldValue);
        }

        var replyMessage = await context.Channel.SendMessageAsync(embed: embed.Build());
        _ = Helpers<T>.DeleteMessagesAfterDelayAsync(replyMessage, context.Message, 20);
    }

    public static async Task ProcessBatchContainer(SocketCommandContext context, List<T> batchPokemonList,
        int batchTradeCode, int totalTrades)
    {
        var sig = context.User.GetFavor();
        var firstPokemon = batchPokemonList[0];

        await QueueHelper<T>.AddBatchContainerToQueueAsync(context, batchTradeCode, context.User.Username,
            firstPokemon, batchPokemonList, sig, context.User, totalTrades).ConfigureAwait(false);
    }

    public static string BuildDetailedBatchErrorMessage(List<BatchTradeError> errors, int totalTrades)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Batch Trade Validation Failed**");
        sb.AppendLine($"❌ {errors.Count} out of {totalTrades} Pokémon could not be processed.\n");

        foreach (var error in errors)
        {
            sb.AppendLine($"**Trade #{error.TradeNumber} - {error.SpeciesName}**");
            sb.AppendLine($"Error: {error.ErrorMessage}");

            if (!string.IsNullOrEmpty(error.LegalizationHint))
            {
                sb.AppendLine($"💡 Hint: {error.LegalizationHint}");
            }

            if (!string.IsNullOrEmpty(error.ShowdownSet))
            {
                var lines = error.ShowdownSet.Split('\n').Take(3);
                sb.AppendLine($"Set Preview: {string.Join(" | ", lines)}...");
            }

            sb.AppendLine();
        }

        sb.AppendLine("**Please fix the invalid sets and try again.**");
        return sb.ToString();
    }
}
