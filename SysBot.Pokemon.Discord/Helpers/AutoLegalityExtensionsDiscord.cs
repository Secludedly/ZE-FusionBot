using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class AutoLegalityExtensionsDiscord
{
    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, ITrainerInfo sav, ShowdownSet set)
    {
        if (set.Species <= 0)
        {
            await channel.SendMessageAsync("Oops! I wasn't able to interpret your message! If you intended to convert something, please double check what you're pasting!").ConfigureAwait(false);
            return;
        }

        try
        {
            var template = AutoLegalityWrapper.GetTemplate(set);

            // Check if this is an egg request based on nickname
            bool isEggRequest = set.Nickname.Equals("egg", StringComparison.CurrentCultureIgnoreCase) && Breeding.CanHatchAsEgg(set.Species);

            PKM pkm;
            string result;
            if (isEggRequest)
            {
                // Generate as egg using ALM's GenerateEgg method
                pkm = sav.GenerateEgg(template, out var eggResult);
                result = eggResult.ToString();
            }
            else
            {
                // Generate normally
                pkm = sav.GetLegal(template, out result);
            }

            var la = new LegalityAnalysis(pkm);
            var spec = GameInfo.Strings.Species[template.Species];
            if (!la.Valid)
            {
                var reason = result == "Timeout" ? $"That {spec} set took too long to generate." : result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." : $"I wasn't able to create a {spec} from that set.";
                var imsg = $"Oops! {reason}";
                if (result == "Failed")
                    imsg += $"\n{AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm)}";
                await channel.SendMessageAsync(imsg).ConfigureAwait(false);
                return;
            }
            var msg = $"Here's your ({result}) legalized PKM & Showdown Set for {spec} ({la.EncounterOriginal.Name})!";
            await channel.SendPKMAsync(pkm, msg + $"\n{ReusableActions.GetFormattedShowdownText(pkm)}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(AutoLegalityExtensionsDiscord));
            var msg = $"Oops! An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
        }
    }

    public static Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, string content, byte gen)
    {
        content = BatchCommandNormalizer.NormalizeBatchCommands(content);
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo(gen);
        return channel.ReplyWithLegalizedSetAsync(sav, set);
    }

    public static Task ReplyWithLegalizedSetAsync<T>(this ISocketMessageChannel channel, string content) where T : PKM, new()
    {
        content = BatchCommandNormalizer.NormalizeBatchCommands(content);
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        return channel.ReplyWithLegalizedSetAsync(sav, set);
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
}
