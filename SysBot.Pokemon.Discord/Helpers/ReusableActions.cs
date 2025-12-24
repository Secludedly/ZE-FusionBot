using Discord;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class ReusableActions
{
    private static readonly string[] separator = [",", ", ", " "];

    // Global rate limiter for Discord DM sends to prevent opening DMs too fast
    private static readonly SemaphoreSlim _dmRateLimiter = new(1, 1);
    private static readonly ConcurrentDictionary<ulong, IDMChannel> _dmChannels = new();
    private static DateTime _lastDmTime = DateTime.MinValue;
    private const int MinDmDelayMs = 2000; // Minimum 2 seconds between DMs to respect Discord rate limits


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
        $".MetLevel={pkm.MetLevel}",
        $".Version={(GameVersion)pkm.Version}",
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

    private static async Task<IDMChannel?> GetOrCreateDMAsync(IUser user)
    {
        try
        {
            if (_dmChannels.TryGetValue(user.Id, out var channel))
                return channel;

            var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
            _dmChannels[user.Id] = dm;
            return dm;
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client is disposed. Cannot create DM channel.", "GetOrCreateDMAsync");
            return null;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to create DM channel: {ex.Message}", "GetOrCreateDMAsync");
            return null;
        }
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

            // Retry logic for handling transient errors
            const int maxRetries = 5;
            int retryCount = 0;
            int delayMs = 1000;

            while (retryCount < maxRetries)
            {
                try
                {
                    // Send the file and WAIT for it to complete
                    await channel.SendFileAsync(tmp, msg);

                    // Add a small delay to ensure Discord processes each file separately
                    await Task.Delay(500);
                    break; // Success, exit retry loop
                }
                catch (ObjectDisposedException)
                {
                    LogUtil.LogError("Discord client is disposed. Cannot send file.", "SendPKMAsync");
                    return;
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    LogUtil.LogInfo($"Discord rate limit encountered, retrying in {delayMs}ms (attempt {retryCount + 1}/{maxRetries})", "SendPKMAsync");
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    retryCount++;
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        LogUtil.LogError($"Failed to send file to channel after {maxRetries} attempts: {ex.Message}", "SendPKMAsync");
                        throw;
                    }

                    LogUtil.LogInfo($"Discord server error encountered, retrying in {delayMs}ms (attempt {retryCount}/{maxRetries})", "SendPKMAsync");

                    await Task.Delay(delayMs);
                    delayMs *= 2; // Exponential backoff
                }
            }
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
                LogUtil.LogError($"Error deleting temporary file: {ex.Message}", "SendPKMAsync");
            }
        }
    }

    public static async Task SendPKMAsync(this IUser user, PKM pkm, string msg = "")
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{uniqueId}_{PathUtil.CleanFileName(pkm.FileName)}";
        var tmpPath = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            await File.WriteAllBytesAsync(tmpPath, pkm.DecryptedPartyData).ConfigureAwait(false);

            // Ensure we don't open DMs too fast - enforce minimum delay between DMs
            await _dmRateLimiter.WaitAsync().ConfigureAwait(false);

            try
            {
                // Enforce minimum delay between DM operations
                var timeSinceLastDm = DateTime.Now - _lastDmTime;
                if (timeSinceLastDm.TotalMilliseconds < MinDmDelayMs)
                {
                    var remainingDelay = MinDmDelayMs - (int)timeSinceLastDm.TotalMilliseconds;
                    await Task.Delay(remainingDelay).ConfigureAwait(false);
                }

                var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
                if (dm == null)
                {
                    LogUtil.LogError($"Could not create DM channel for user {user.Username} ({user.Id}). Skipping send.", "SendPKMAsync");
                    return;
                }

                const int maxRetries = 3;
                int delayMs = 2500; // Increased from 1500ms

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await dm.SendFileAsync(tmpPath, msg).ConfigureAwait(false);
                        _lastDmTime = DateTime.Now; // Update last DM time on success
                        await Task.Delay(750).ConfigureAwait(false); // Increased from 500ms for safer pacing
                        break; // success
                    }
                    catch (ObjectDisposedException)
                    {
                        LogUtil.LogError("Discord client is disposed. Cannot send DM.", "SendPKMAsync");
                        return;
                    }
                    catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
                    {
                        // ❌ 40003 = Opening DMs too fast
                        LogUtil.LogError($"Opening DMs too fast! Waiting 5 seconds before retry. User: {user.Username} ({user.Id})", "SendPKMAsync");

                        // Remove cached DM channel to force recreation with proper delay
                        _dmChannels.TryRemove(user.Id, out _);

                        if (attempt < maxRetries)
                        {
                            await Task.Delay(5000).ConfigureAwait(false); // Wait 5 seconds for error 40003
                            continue;
                        }
                        break; // Give up after max retries
                    }
                    catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                    {
                        // User has DMs disabled or bot is blocked
                        LogUtil.LogError($"Cannot send messages to user {user.Username} ({user.Id}). DMs may be disabled.", "SendPKMAsync");
                        break;
                    }
                    catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        if (attempt == maxRetries)
                        {
                            LogUtil.LogError($"Rate limited when DMing {user.Username} after {maxRetries} attempts", "SendPKMAsync");
                            break;
                        }

                        LogUtil.LogInfo($"Rate limited sending DM to {user.Username}, waiting {delayMs}ms (attempt {attempt}/{maxRetries})", "SendPKMAsync");
                        await Task.Delay(delayMs).ConfigureAwait(false);
                        delayMs *= 2; // Exponential backoff
                    }
                    catch (HttpException ex)
                    {
                        if (attempt == maxRetries)
                        {
                            LogUtil.LogError($"Failed to DM {user.Username} after {maxRetries} attempts: {ex.Message}", "SendPKMAsync");
                            throw;
                        }

                        LogUtil.LogInfo($"Discord error sending DM to {user.Username}, retrying in {delayMs}ms (attempt {attempt}/{maxRetries})", "SendPKMAsync");
                        await Task.Delay(delayMs).ConfigureAwait(false);
                        delayMs *= 2;
                    }
                }
            }
            finally
            {
                _dmRateLimiter.Release();
            }
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
