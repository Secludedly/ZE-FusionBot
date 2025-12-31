using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Discord.Helpers.TradeModule;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class AutoLegalityExtensionsDiscord
{
    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, ITrainerInfo sav, ShowdownSet set, Dictionary<string, bool>? userHTPreferences = null)
    {
        if (set.Species <= 0)
        {
            await channel.SendMessageAsync(
                "Oops! I wasn't able to interpret your message! If you intended to convert something, please double check what you're pasting!"
            ).ConfigureAwait(false);
            return;
        }

        try
        {
            // Check if this is an egg request based on nickname
            bool isEggRequest = set.Nickname.Equals("egg", StringComparison.CurrentCultureIgnoreCase)
                                && Breeding.CanHatchAsEgg(set.Species);

            PKM pkm;
            string result;

            if (isEggRequest)
            {
                // Wrap the ShowdownSet directly in a RegenTemplate
                var regenTemplate = new RegenTemplate(set);

                // Generate egg using ALM
                pkm = sav.GenerateEgg(regenTemplate, out var eggResult);
                result = eggResult.ToString();
            }
            else
            {
                // Generate normally
                var template = AutoLegalityWrapper.GetTemplate(set);
                pkm = sav.GetLegal(template, out result);

                if (pkm == null)
                {
                    await channel.SendMessageAsync("Failed to generate Pokémon from your set.").ConfigureAwait(false);
                    return;
                }

                // -----------------------------
                // Enforce requested IVs, Nature, and Shiny (Z-A only)
                // -----------------------------
                // IVEnforcer and NatureEnforcer are only for Z-A (PA9) Pokemon
                // For other games, the normal legalization process already handles nature/shiny
                if (pkm is PA9)
                {
                    if (set.IVs != null && set.IVs.Count() == 6)
                    {
                        IVEnforcer.ApplyRequestedIVsAndForceNature(
                            pkm,
                            set.IVs.ToArray(),
                            set.Nature,
                            set.Shiny,
                            sav,
                            template,
                            userHTPreferences
                        );
                    }
                    else
                    {
                        // Even if no IVs requested, enforce nature/shiny only for Z-A
                        NatureEnforcer.ForceNature(pkm, set.Nature, set.Shiny);
                    }
                }

                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[set.Species];

                if (!la.Valid)
                {
                    var reason = result switch
                    {
                        "Timeout" => $"That {spec} set took too long to generate.",
                        "VersionMismatch" => "Request refused: PKHeX and Auto-Legality Mod version mismatch.",
                        _ => $"I wasn't able to create a {spec} from that set."
                    };

                    var imsg = $"Oops! {reason}";
                    if (result == "Failed")
                        imsg += $"\n{AutoLegalityWrapper.GetLegalizationHint(set, sav, pkm)}";

                    await channel.SendMessageAsync(imsg).ConfigureAwait(false);
                    return;
                }

                var msg = $"Here's your ({result}) legalized PKM & Showdown Set for {spec} ({la.EncounterOriginal.Name})!";
                await channel.SendPKMAsync(pkm, msg + $"\n{ReusableActions.GetFormattedShowdownText(pkm)}").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(AutoLegalityExtensionsDiscord));
            var msg = $"Oops! An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```\nError: {ex.Message}";
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
        }
    }

    public static Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, string content, byte gen)
    {
        content = BatchCommandNormalizer.NormalizeBatchCommands(content);
        var userHTPreferences = ParseHyperTrainingCommandsPublic(content);
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo(gen);
        return channel.ReplyWithLegalizedSetAsync(sav, set, userHTPreferences);
    }

    public static Task ReplyWithLegalizedSetAsync<T>(this ISocketMessageChannel channel, string content) where T : PKM, new()
    {
        content = BatchCommandNormalizer.NormalizeBatchCommands(content);
        var userHTPreferences = ParseHyperTrainingCommandsPublic(content);
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        return channel.ReplyWithLegalizedSetAsync(sav, set, userHTPreferences);
    }

    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, IAttachment att)
    {
        var download = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!download.Success)
        {
            await channel.SendMessageAsync(download.ErrorMessage).ConfigureAwait(false);
            return;
        }

        var pkm = download.Data!;
        if (new LegalityAnalysis(pkm).Valid)
        {
            await channel.SendMessageAsync($"{download.SanitizedFileName}: Already legal.").ConfigureAwait(false);
            return;
        }

        var legal = pkm.LegalizePokemon();
        if (!new LegalityAnalysis(legal).Valid)
        {
            await channel.SendMessageAsync($"{download.SanitizedFileName}: Unable to legalize.").ConfigureAwait(false);
            return;
        }

        legal.RefreshChecksum();

        var msg = $"Here's your legalized PKM for {download.SanitizedFileName}!\n{ReusableActions.GetFormattedShowdownText(legal)}";
        await channel.SendPKMAsync(legal, msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if the normalized content contains hypertrain-related batch commands.
    /// Returns a dictionary of which stats were specified and their values.
    /// If null, no HT commands were specified.
    /// If dictionary contains "ALL" key with value 0, HyperTrainFlags=0 was specified (no HT at all).
    /// </summary>
    public static Dictionary<string, bool>? ParseHyperTrainingCommandsPublic(string content)
    {
        var htFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Check for .HyperTrainFlags=0 which means disable all hypertraining
        if (content.Contains(".HyperTrainFlags=0", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["ALL"] = false;
            return htFlags;
        }

        // Check for individual HT flags
        if (content.Contains(".HT_HP=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["HP"] = !content.Contains(".HT_HP=False", StringComparison.OrdinalIgnoreCase);
        }
        if (content.Contains(".HT_ATK=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["ATK"] = !content.Contains(".HT_ATK=False", StringComparison.OrdinalIgnoreCase);
        }
        if (content.Contains(".HT_DEF=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["DEF"] = !content.Contains(".HT_DEF=False", StringComparison.OrdinalIgnoreCase);
        }
        if (content.Contains(".HT_SPA=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["SPA"] = !content.Contains(".HT_SPA=False", StringComparison.OrdinalIgnoreCase);
        }
        if (content.Contains(".HT_SPD=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["SPD"] = !content.Contains(".HT_SPD=False", StringComparison.OrdinalIgnoreCase);
        }
        if (content.Contains(".HT_SPE=", StringComparison.OrdinalIgnoreCase))
        {
            htFlags["SPE"] = !content.Contains(".HT_SPE=False", StringComparison.OrdinalIgnoreCase);
        }

        return htFlags.Count > 0 ? htFlags : null;
    }
}
