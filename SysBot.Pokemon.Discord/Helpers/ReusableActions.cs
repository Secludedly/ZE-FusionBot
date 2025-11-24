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
        // Start from PKHeX's showdown output
        var lines = ShowdownParsing.GetShowdownText(pkm)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // --- Ensure IVs exist in the output (create if missing) ---
        bool hasIVLine = lines.Any(l => l.StartsWith("IVs:"));
        if (!hasIVLine && pkm.IVs is int[] ivs && ivs.Length == 6)
        {
            lines.Add($"IVs: {ivs[0]} HP / {ivs[1]} Atk / {ivs[2]} Def / {ivs[3]} SpA / {ivs[4]} SpD / {ivs[5]} Spe");
        }

        // --- Alpha: insert under Ability if applicable ---
        if (pkm is IAlpha alpha && alpha.IsAlpha)
        {
            int abilityIndex = lines.FindIndex(z => z.StartsWith("Ability:"));
            if (abilityIndex >= 0)
                lines.Insert(abilityIndex + 1, "Alpha: Yes");
            else
                lines.Add("Alpha: Yes");
        }

        // --- Shiny formatting (Square/Star only for SWSH) ---
        if (pkm.IsShiny)
        {
            int shinyIndex = lines.FindIndex(x => x.StartsWith("Shiny:"));

            // If SWSH → use Square/Star
            if (pkm.Version == GameVersion.SW || pkm.Version == GameVersion.SH)
            {
                string shinyText = (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                    ? "Shiny: Square"
                    : "Shiny: Star";

                if (shinyIndex >= 0)
                    lines[shinyIndex] = shinyText;
                else
                    lines.Add(shinyText);
            }
            else
            {
                // Every other game → simply "Shiny: Yes"
                if (shinyIndex >= 0)
                    lines[shinyIndex] = "Shiny: Yes";
                else
                    lines.Add("Shiny: Yes");
            }
        }

        // --- Egg line (if egg) ---
        if (pkm.IsEgg)
        {
            // Keep a single egg notice; add at end
            if (!lines.Any(l => l.Contains("Pokémon is an egg")))
                lines.Add("Pokémon is an egg");
        }

        // --- Ball insertion (before Nature if present) ---
        if (pkm.Ball > (int)Ball.None)
        {
            int natureIndex = lines.FindIndex(z => z.Contains("Nature"));
            var ballLine = $"Ball: {(Ball)pkm.Ball} Ball";
            if (natureIndex >= 0)
                lines.Insert(natureIndex, ballLine);
            else if (!lines.Contains(ballLine))
                lines.Add(ballLine);
        }

        // --- Full-mode extra info ---
        if (!simple)
        {
            int insertIndex = lines.FindIndex(z => z.Contains("Nature")) + 1;
            if (insertIndex <= 0) insertIndex = 1;

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
            $".HandlingTrainerFriendship={pkm.HandlingTrainerFriendship}"
        };

            // Height/Weight if available
            if (pkm is IScaledSize scaled)
            {
                extraInfo.Add($".HeightScalar={scaled.HeightScalar}");
                extraInfo.Add($".WeightScalar={scaled.WeightScalar}");
            }

            // Scale info
            if (pkm is PK9 pk9)
                extraInfo.Add($".Scale={pk9.Scale}");
            else if (pkm is PA9 pa9)
                extraInfo.Add($".Scale={pa9.Scale}");

            lines.InsertRange(insertIndex, extraInfo);

            // Move IVs to below EVs if both exist
            int evIndex = lines.FindIndex(x => x.StartsWith("EVs:"));
            int ivIndex = lines.FindIndex(x => x.StartsWith("IVs:"));
            if (evIndex >= 0 && ivIndex >= 0 && ivIndex != evIndex + 1)
            {
                var ivLine = lines[ivIndex];
                lines.RemoveAt(ivIndex);
                // If removing shifted indices, ensure insert location remains valid
                evIndex = lines.FindIndex(x => x.StartsWith("EVs:"));
                if (evIndex >= 0)
                    lines.Insert(evIndex + 1, ivLine);
                else
                    lines.Add(ivLine);
            }
        }

        // Final formatted code block
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
