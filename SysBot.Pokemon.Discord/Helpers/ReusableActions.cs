using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
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
    public static async Task SendPKMAsync(this IMessageChannel channel, PKM pkm, string msg = "")
    {
        string shinyMark = "";
        if (pkm.IsShiny)
            shinyMark = (pkm.ShinyXor == 0 || pkm.FatefulEncounter) ? "Square Shiny" : "Shiny";

        var species = GameInfo.GetStrings("en").Species[pkm.Species];
        var languageName = ((LanguageID)pkm.Language).ToString();
        var ot = pkm.OriginalTrainerName;
        var tid = pkm.DisplayTID;
        var sid = pkm.DisplaySID;
        var hash = (pkm.EncryptionConstant != 0 ? pkm.EncryptionConstant : pkm.PID).ToString("X8");

        // Final file name format
        var fileName = $"{species} - {shinyMark} - {languageName} - {ot} - {tid} - {sid} - {hash}.pk{pkm.Format}";
        fileName = SafeFileName(fileName);


        // Temp path
        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // Write the file
            await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);

            // Send the file
            await channel.SendFileAsync(tmp, msg);

            // Give Discord a little breathing room
            await Task.Delay(700);
        }
        finally
        {
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
        string shinyMark = "";
        if (pkm.IsShiny)
            shinyMark = (pkm.ShinyXor == 0 || pkm.FatefulEncounter) ? "Square Shiny" : "Shiny";

        var species = GameInfo.GetStrings("en").Species[pkm.Species];
        var languageName = ((LanguageID)pkm.Language).ToString();
        var ot = pkm.OriginalTrainerName;
        var tid = pkm.DisplayTID;
        var sid = pkm.DisplaySID;
        var hash = (pkm.EncryptionConstant != 0 ? pkm.EncryptionConstant : pkm.PID).ToString("X8");

        var fileName = $"{species} - {shinyMark} - {languageName} - {ot} - {tid} - {sid} - {hash}.pk{pkm.Format}";
        fileName = SafeFileName(fileName);


        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);
            await user.SendFileAsync(tmp, msg);
            await Task.Delay(700);
        }
        finally
        {
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

    public static string SafeFileName(string input)
    {
        // Only remove forbidden Windows chars
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(c => !invalidChars.Contains(c)).ToArray());
        return cleaned.Trim().TrimEnd('.');
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

    public static async Task EchoAndReply(this ISocketMessageChannel channel, string msg)
    {
        // Announce it in the channel the command was entered only if it's not already an echo channel.
        EchoUtil.Echo(msg);
        if (!EchoModule.IsEchoChannel(channel))
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
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
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(20000).ConfigureAwait(false);
            await botMessage.DeleteAsync().ConfigureAwait(false);
        });
    }

    public static string GetFormattedShowdownText(PKM pkm)
    {
        var newShowdown = new List<string>();
        var showdown = ShowdownParsing.GetShowdownText(pkm);
        foreach (var line in showdown.Split('\n'))
            newShowdown.Add(line);

        if (pkm.IsEgg)
            newShowdown.Add("\nPokémon is an egg");

        if (pkm.Ball > (int)Ball.None)
            newShowdown.Insert(newShowdown.FindIndex(z => z.Contains("Nature")), $"Ball: {(Ball)pkm.Ball} Ball");

        if (pkm.IsShiny)
        {
            var index = newShowdown.FindIndex(x => x.Contains("Shiny: Yes"));
            if (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                newShowdown[index] = "Shiny: Square\r";
            else
                newShowdown[index] = "Shiny: Star\r";
        }

        // Find the index of the line that contains "Nature"
        int natureIndex = newShowdown.FindIndex(line => line.Contains("Nature"));

        // If found, insert your OT/TID/etc after it. Otherwise, append to the end.
        int insertIndex = natureIndex >= 0 ? natureIndex + 1 : newShowdown.Count;

        // Build all the extra info in one list
        var extraInfo = new List<string>
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
        $".HandlingTrainerFriendship={pkm.HandlingTrainerFriendship}",
    };

        // Add Height/Weight if available
        if (pkm is IScaledSize scaled)
        {
            extraInfo.Add($".HeightScalar={scaled.HeightScalar}");
            extraInfo.Add($".WeightScalar={scaled.WeightScalar}");
        }

        // Add Scale if SV
        if (pkm is PK9 pk9)
        {
            extraInfo.Add($".Scale={pk9.Scale}");
        }

        // Insert everything at once
        newShowdown.InsertRange(insertIndex, extraInfo);

        return Format.Code(string.Join("\n", newShowdown).TrimEnd());
    }


    private static readonly string[] separator = [",", ", ", " "];

    public static IReadOnlyList<string> GetListFromString(string str)
    {
        // Extract comma separated list
        return str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
