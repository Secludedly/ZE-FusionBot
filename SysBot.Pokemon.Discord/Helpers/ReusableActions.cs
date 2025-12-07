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

namespace SysBot.Pokemon.Discord
{
    public static class ReusableActions
    {
        private static readonly string[] separator = new[] { ",", ", ", " " };

        public static async Task EchoAndReply(this ISocketMessageChannel channel, string msg)
        {
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
            // Start with PKHeX's default showdown text
            var lines = ShowdownParsing.GetShowdownText(pkm)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            // --- Alpha formatting ---
            if (pkm is IAlpha alpha && alpha.IsAlpha)
            {
                int abilityIndex = lines.FindIndex(l => l.StartsWith("Ability:"));
                if (abilityIndex >= 0)
                    lines.Insert(abilityIndex + 1, "Alpha: Yes");
                else
                    lines.Add("Alpha: Yes");
            }

            // --- Shiny formatting ---
            if (pkm.IsShiny)
            {
                int shinyIndex = lines.FindIndex(l => l.StartsWith("Shiny:"));

                if (pkm.Version is GameVersion.SW or GameVersion.SH)
                {
                    string shiny = (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                        ? "Shiny: Square"
                        : "Shiny: Star";

                    if (shinyIndex >= 0) lines[shinyIndex] = shiny;
                    else lines.Add(shiny);
                }
                else
                {
                    if (shinyIndex >= 0) lines[shinyIndex] = "Shiny: Yes";
                    else lines.Add("Shiny: Yes");
                }
            }

            // --- Egg line ---
            if (pkm.IsEgg && !lines.Any(l => l.Contains("Pokémon is an egg")))
            {
                lines.Add("Pokémon is an egg");
            }

            // --- Ball line (insert before Nature) ---
            if (pkm.Ball > (int)Ball.None)
            {
                var ballLine = $"Ball: {(Ball)pkm.Ball} Ball";
                int natureIndex = lines.FindIndex(l => l.Contains("Nature"));

                if (natureIndex >= 0)
                    lines.Insert(natureIndex, ballLine);
                else if (!lines.Contains(ballLine))
                    lines.Add(ballLine);
            }

            // --- Extra info block (only in full mode) ---
            if (!simple)
            {
                int insertIndex = lines.FindIndex(l => l.Contains("Nature")) + 1;
                if (insertIndex <= 0) insertIndex = 1;

                var extra = new List<string>
                {
                    $"OT: {pkm.OriginalTrainerName}",
                    $"TID: {pkm.DisplayTID}",
                    $"SID: {pkm.DisplaySID}",
                    $"OTGender: {(Gender)pkm.OriginalTrainerGender}",
                    $"Language: {(LanguageID)pkm.Language}",
                    $".MetDate={pkm.MetDate:yyyy-MM-dd}",
                    $".MetLocation={pkm.MetLocation}",
                    $".MetLevel={pkm.MetLevel}",
                    $"~=Version={(GameVersion)pkm.Version}",
                    $".OriginalTrainerFriendship={pkm.OriginalTrainerFriendship}",
                    $".HandlingTrainerFriendship={pkm.HandlingTrainerFriendship}"
                };

                if (pkm is PK9 pk9)
                    extra.Add($".Scale={pk9.Scale}");
                else if (pkm is PA9 pa9)
                    extra.Add($".Scale={pa9.Scale}");

                lines.InsertRange(insertIndex, extra);
            }

            // =====================================================================
            // >>> FIXED IV PLACEMENT LOGIC <<<
            // =====================================================================
            if (pkm.IVs is int[] pkmIVs && pkmIVs.Length == 6)
            {
                // Map PKHeX IVs (HP, Atk, Def, Spe, SpA, SpD) → Showdown (HP, Atk, Def, SpA, SpD, Spe)
                int hp = pkmIVs[0];
                int atk = pkmIVs[1];
                int def = pkmIVs[2];
                int spe = pkmIVs[3];
                int spa = pkmIVs[4];
                int spd = pkmIVs[5];

                string ivLine = $"IVs: {hp} HP / {atk} Atk / {def} Def / {spa} SpA / {spd} SpD / {spe} Spe";

                // Remove any existing IV line
                int oldIVIndex = lines.FindIndex(l => l.StartsWith("IVs:"));
                if (oldIVIndex >= 0) lines.RemoveAt(oldIVIndex);

                // Insert IV line in proper place
                int evIndex = lines.FindIndex(l => l.StartsWith("EVs:"));
                int ballIndex = lines.FindIndex(l => l.StartsWith("Ball:"));
                int natureIndex = lines.FindIndex(l => l.Contains("Nature"));

                if (evIndex >= 0)
                    lines.Insert(evIndex + 1, ivLine);
                else if (ballIndex >= 0)
                    lines.Insert(ballIndex, ivLine);
                else if (natureIndex >= 0)
                    lines.Insert(natureIndex, ivLine);
                else
                    lines.Insert(1, ivLine);
            }

            // Remove empty lines
            lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            return $"```\n{string.Join("\n", lines)}\n```";
        }

        public static IReadOnlyList<string> GetListFromString(string str) =>
            str.Split(separator, StringSplitOptions.RemoveEmptyEntries);

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

            var botMessage = await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
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
            string tmp = Path.Combine(Path.GetTempPath(), GenerateSafeFileName(pkm));
            try
            {
                await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);
                await channel.SendFileAsync(tmp, msg);
                await Task.Delay(700);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        public static async Task SendPKMAsync(this IUser user, PKM pkm, string msg = "")
        {
            string tmp = Path.Combine(Path.GetTempPath(), GenerateSafeFileName(pkm));
            try
            {
                await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);
                await user.SendFileAsync(tmp, msg);
                await Task.Delay(700);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        private static string GenerateSafeFileName(PKM pkm)
        {
            string speciesSafe = SafeFileName(GameInfo.GetStrings("en").Species[pkm.Species]);
            string shinySafe = SafeFileName(pkm.IsShiny ? (pkm.ShinyXor == 0 || pkm.FatefulEncounter ? "Square Shiny" : "Shiny") : "");
            string langSafe = SafeFileName(((LanguageID)pkm.Language).ToString());
            string otSafe = SafeFileName(pkm.OriginalTrainerName);
            string tidSafe = pkm.DisplayTID.ToString();
            string sidSafe = pkm.DisplaySID.ToString();
            string hashSafe = (pkm.EncryptionConstant != 0 ? pkm.EncryptionConstant : pkm.PID).ToString("X8");

            return $"{speciesSafe} - {shinySafe} - {langSafe} - {otSafe} - {tidSafe} - {sidSafe} - {hashSafe}.pk{pkm.Format}";
        }

        public static string SafeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Pokemon";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(input.Where(c => !invalidChars.Contains(c)).ToArray());
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

            var riskySymbols = new[] { '#', '$', '%', '&', '{', '}', '@', '<', '>', '*', '?', '/', '\\', '+', '`', '|', '=' };
            foreach (var r in riskySymbols)
                cleaned = cleaned.Replace(r, '-');

            return cleaned.TrimEnd('.') != "" ? cleaned.TrimEnd('.') : "Pokemon";
        }

        public static string StripCodeBlock(string str) =>
            str.Replace("`\n", "").Replace("\n`", "").Replace("`", "").Trim();
    }
}
