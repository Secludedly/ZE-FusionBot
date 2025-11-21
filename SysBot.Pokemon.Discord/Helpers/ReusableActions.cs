using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

    public static string GetFormattedShowdownText(PKM pkm, bool simple = false)
    {
        var newShowdown = new List<string>();
        var showdown = ShowdownParsing.GetShowdownText(pkm);
        foreach (var line in showdown.Split('\n'))
            newShowdown.Add(line);

        if (pkm.IsEgg)
            newShowdown.Add("\nPokémon is an egg");

        if (pkm.Ball > (int)Ball.None)
        {
            int natureIndex = newShowdown.FindIndex(z => z.Contains("Nature"));
            if (natureIndex >= 0)
                newShowdown.Insert(natureIndex, $"Ball: {(Ball)pkm.Ball} Ball");
        }

        if (pkm.IsShiny)
        {
            int index = newShowdown.FindIndex(x => x.Contains("Shiny: Yes"));
            if (index >= 0)
                newShowdown[index] = (pkm.ShinyXor == 0 || pkm.FatefulEncounter) ? "Shiny: Square\r" : "Shiny: Star\r";
        }

        int insertIndex = newShowdown.FindIndex(z => z.Contains("Nature")) + 1;
        if (insertIndex <= 0) insertIndex = 1;

        if (!simple)
        {
            // Full mode extra info
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

            // Add Scale info for concrete modes
            if (pkm is PK9 pk9)
                extraInfo.Add($".Scale={pk9.Scale}");
            else if (pkm is PA9 pa9)
                extraInfo.Add($".Scale={pa9.Scale}");

            newShowdown.InsertRange(insertIndex, extraInfo);

            // Force IVs under EVs
            int evIndex = newShowdown.FindIndex(x => x.StartsWith("EVs:"));
            int ivIndex = newShowdown.FindIndex(x => x.StartsWith("IVs:"));
            if (evIndex >= 0 && ivIndex >= 0 && ivIndex != evIndex + 1)
            {
                var ivLine = newShowdown[ivIndex];
                newShowdown.RemoveAt(ivIndex);
                newShowdown.Insert(evIndex + 1, ivLine);
            }
        }

        return Format.Code(string.Join("\n", newShowdown).TrimEnd());
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
        string speciesSafe = SafeFileName(GameInfo.GetStrings("en").Species[pkm.Species]);
        string shinySafe = SafeFileName(pkm.IsShiny ? (pkm.ShinyXor == 0 || pkm.FatefulEncounter ? "Square Shiny" : "Shiny") : "");
        string langSafe = SafeFileName(((LanguageID)pkm.Language).ToString());
        string otSafe = SafeFileName(pkm.OriginalTrainerName);
        string tidSafe = pkm.DisplayTID.ToString();
        string sidSafe = pkm.DisplaySID.ToString();
        string hashSafe = (pkm.EncryptionConstant != 0 ? pkm.EncryptionConstant : pkm.PID).ToString("X8");

        var fileName = $"{speciesSafe} - {shinySafe} - {langSafe} - {otSafe} - {tidSafe} - {sidSafe} - {hashSafe}.pk{pkm.Format}";

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
        string speciesSafe = SafeFileName(GameInfo.GetStrings("en").Species[pkm.Species]);
        string shinySafe = SafeFileName(pkm.IsShiny ? (pkm.ShinyXor == 0 || pkm.FatefulEncounter ? "Square Shiny" : "Shiny") : "");
        string langSafe = SafeFileName(((LanguageID)pkm.Language).ToString());
        string otSafe = SafeFileName(pkm.OriginalTrainerName);
        string tidSafe = pkm.DisplayTID.ToString();
        string sidSafe = pkm.DisplaySID.ToString();
        string hashSafe = (pkm.EncryptionConstant != 0 ? pkm.EncryptionConstant : pkm.PID).ToString("X8");

        var fileName = $"{speciesSafe} - {shinySafe} - {langSafe} - {otSafe} - {tidSafe} - {sidSafe} - {hashSafe}.pk{pkm.Format}";

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

    public static string SafeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Pokemon";

        // Remove invalid filename chars
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(c => !invalidChars.Contains(c)).ToArray());

        // Replace multiple spaces with single space, trim
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Optional: replace other risky symbols
        var riskySymbols = new[] { '#', '$', '%', '&', '{', '}', '@', '<', '>', '*', '?', '/', '\\', '+', '`', '|', '=' };
        foreach (var r in riskySymbols)
            cleaned = cleaned.Replace(r, '-');

        // Trim trailing dots
        cleaned = cleaned.TrimEnd('.');

        return cleaned.Length == 0 ? "Pokemon" : cleaned;
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
