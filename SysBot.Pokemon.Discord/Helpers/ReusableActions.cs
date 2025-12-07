using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class ReusableActions
{
    private static readonly string[] separator = [",", ", ", " "];

    public static async Task EchoAndReply(this ISocketMessageChannel channel, string msg)
    {
        // Announce it in the channel the command was entered only if it's not already an echo channel.
        EchoUtil.Echo(msg);
        if (!EchoModule.IsEchoChannel(channel))
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
    }

    public static RequestSignificance GetFavor(this IUser user)
    {
        var mgr = SysCordSettings.Manager;
        if (user.Id == mgr.Owner)
            return RequestSignificance.Owner;
        if (mgr.CanUseSudo(user.Id))
            return RequestSignificance.Favored;
        if (user is SocketGuildUser g)
            return mgr.GetSignificance(g.Roles.Select(z => z.Name));
        return RequestSignificance.None;
    }

    public static string GetFormattedShowdownText(PKM pkm)
    {
        // Start with the base Showdown text split into lines
        var lines = ShowdownParsing.GetShowdownText(pkm).Split('\n').ToList();

        // Add Egg info if needed
        if (pkm.IsEgg)
            lines.Add("\nPokémon is an egg");

        // Adjust shiny info
        if (pkm.IsShiny)
        {
            int shinyIndex = lines.FindIndex(x => x.Contains("Shiny: Yes"));
            if (shinyIndex >= 0)
            {
                lines[shinyIndex] = (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                    ? "Shiny: Square"
                    : "Shiny: Star";
            }
        }

        // Insert Ball info after Nature line
        int natureIndex = lines.FindIndex(l => l.Contains("Nature"));
        if (pkm.Ball > (int)Ball.None && natureIndex >= 0)
            lines.Insert(natureIndex + 1, $"Ball: {(Ball)pkm.Ball} Ball");

        // Insert OT, TID, SID, Gender, Language info immediately after Nature line (or at start if Nature not found)
        int insertIndex = natureIndex >= 0 ? natureIndex + 2 : 1; // +2 if Ball inserted
        var trainerInfo = new List<string>
    {
        $"OT: {pkm.OriginalTrainerName}",
        $"TID: {pkm.DisplayTID}",
        $"SID: {pkm.DisplaySID}",
        $"OTGender: {(Gender)pkm.OriginalTrainerGender}",
        $"Language: {(LanguageID)pkm.Language}",
        $".MetDate={pkm.MetDate:yyyy-MM-dd}",
        $".MetLocation={pkm.MetLocation}",
        $".MetLevel={pkm.MetLevel}",
        $".Version={(GameVersion)pkm.Version}",
        $".OriginalTrainerFriendship={pkm.OriginalTrainerFriendship}",
        $".HandlingTrainerFriendship={pkm.HandlingTrainerFriendship}"
    };
        lines.InsertRange(insertIndex, trainerInfo);

        // =============================
        // FIXED IV PLACEMENT LOGIC
        // =============================
        if (pkm.IVs is int[] pkmIVs && pkmIVs.Length == 6)
        {
            // Map PKHeX IV order (HP, Atk, Def, Spe, SpA, SpD) → Showdown (HP, Atk, Def, SpA, SpD, Spe)
            int hp = pkmIVs[0];
            int atk = pkmIVs[1];
            int def = pkmIVs[2];
            int spe = pkmIVs[3];
            int spa = pkmIVs[4];
            int spd = pkmIVs[5];

            string ivLine = $"IVs: {hp} HP / {atk} Atk / {def} Def / {spa} SpA / {spd} SpD / {spe} Spe";

            // Remove old IV line if exists
            int oldIVIndex = lines.FindIndex(l => l.StartsWith("IVs:"));
            if (oldIVIndex >= 0)
                lines.RemoveAt(oldIVIndex);

            // Determine placement
            int evIndex = lines.FindIndex(l => l.StartsWith("EVs:"));
            int ballLineIndex = lines.FindIndex(l => l.StartsWith("Ball:"));
            natureIndex = lines.FindIndex(l => l.Contains("Nature")); // reuse variable safely

            if (evIndex >= 0)
                lines.Insert(evIndex + 1, ivLine);
            else if (ballLineIndex >= 0)
                lines.Insert(ballLineIndex, ivLine);
            else if (natureIndex >= 0)
                lines.Insert(natureIndex, ivLine);
            else
                lines.Insert(1, ivLine);
        }

        // Clean up empty lines and join
        lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return Format.Code(string.Join("\n", lines).TrimEnd());
    }


    public static IReadOnlyList<string> GetListFromString(string str)
    {
        // Extract comma separated list
        return str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    public static async Task RepostPKMAsShowdownAsync(this ISocketMessageChannel channel, IAttachment att, SocketUserMessage userMessage)
    {
        if (!EntityDetection.IsSizePlausible(att.Size))
            return;
        var result = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!result.Success)
            return;

        var pkm = result.Data!;
        await channel.SendPKMAsShowdownSetAsync(pkm, userMessage).ConfigureAwait(false);
    }

    public static async Task SendPKMAsShowdownSetAsync(this ISocketMessageChannel channel, PKM pkm, SocketUserMessage userMessage)
    {
        var txt = GetFormattedShowdownText(pkm);
        bool canGmax = pkm is PK8 pk8 && pk8.CanGigantamax;
        var speciesImageUrl = TradeExtensions<PK9>.PokeImg(pkm, canGmax, false);

        var embed = new EmbedBuilder()
            .WithTitle("Pokémon Showdown Set")
            .WithDescription(txt)
            .WithColor(Color.Blue)
            .WithThumbnailUrl(speciesImageUrl)
            .Build();

        var botMessage = await channel.SendMessageAsync(embed: embed).ConfigureAwait(false); // Send the embed
        var warningMessage = await channel.SendMessageAsync("This message will self-destruct in 15 seconds. Please copy your data.").ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(20000).ConfigureAwait(false);
            await botMessage.DeleteAsync().ConfigureAwait(false);
            await warningMessage.DeleteAsync().ConfigureAwait(false);
        });
    }

    public static async Task SendPKMAsync(this IMessageChannel channel, PKM pkm, string msg = "")
    {
        // Create a unique filename for each Pokémon
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{uniqueId}_{PathUtil.CleanFileName(pkm.FileName)}";
        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // Write the file
            await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);

            // Send the file and WAIT for it to complete
            await channel.SendFileAsync(tmp, msg);

            // Add a small delay to ensure Discord processes each file separately
            await Task.Delay(700);
        }
        finally
        {
            // Make sure we attempt to delete the temp file even if an exception occurs
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting temporary file: {ex.Message}");
            }
        }
    }

    public static async Task SendPKMAsync(this IUser user, PKM pkm, string msg = "")
    {
        // Create a unique filename for each Pokémon
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{uniqueId}_{PathUtil.CleanFileName(pkm.FileName)}";
        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // Write the file
            await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);

            // Send the file and WAIT for it to complete
            await user.SendFileAsync(tmp, msg);

            // Add a small delay to ensure Discord processes each file separately
            await Task.Delay(700);
        }
        finally
        {
            // Make sure we attempt to delete the temp file even if an exception occurs
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting temporary file: {ex.Message}");
            }
        }
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
