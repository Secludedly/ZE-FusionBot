using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace SysBot.Pokemon.Discord.Modules
{
    public class HOMEReadyModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static T? GetRequest(Download<PKM> dl)
        {
            if (!dl.Success)
                return null;
            return dl.Data switch
            {
                null => null,
                T pk => pk,
                _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
            };
        }

        private string HOMEFolder => SysCord<T>.Runner.Config.Folder.HOMEReadyPKMFolder;

        private string Prefix => SysCord<T>.Runner.Config.Discord.CommandPrefix;

        // ============================================================================
        //  INSTRUCTIONS
        // ============================================================================
        [Command("homeready")]
        [Alias("hr")]
        [Summary("Displays instructions on how to use the HOME-Ready module.")]
        private async Task HomeReadyInstructionsAsync()
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            // Using your modern embed style
            async Task<IUserMessage> SendBreak(string title, string description)
            {
                var embed = new EmbedBuilder()
                    .WithTitle(title)
                    .WithDescription(description)
                    .WithColor(Color.Blue)
                    .WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");

                return await ReplyAsync(embed: embed.Build());
            }

            var m0 = await SendBreak(
                "------- HOME-READY MODULE INSTRUCTIONS -------",
                "Everything you need to know for the HOME-Ready commands."
            );

            var m1 = await SendBreak(
                $"GET LIST — `{Prefix}hrl <Pokemon>`",
                $"- Searches the entire HOME-Ready module.\n**Example:** `{Prefix}hrl Mewtwo`"
            );

            var m2 = await SendBreak(
                $"CHANGE PAGES — `{Prefix}hrl <page>`",
                $"- Switch between pages, with or without filters.\n**Example:** `{Prefix}hrl 5 Charmander`"
            );

            var m3 = await SendBreak(
                $"TRADE A FILE — `{Prefix}hrr <number>`",
                $"- Trades the Pokémon by its number in the list.\n**Example:** `{Prefix}hrr 682`"
            );

            _ = Task.Run(async () =>
            {
                await Task.Delay(60_000);
                try
                {
                    await m0.DeleteAsync();
                    await m1.DeleteAsync();
                    await m2.DeleteAsync();
                    await m3.DeleteAsync();
                }
                catch { }
            });
        }

        // ============================================================================
        //  REQUEST
        // ============================================================================
        [Command("homereadyrequest")]
        [Alias("hrr")]
        [Summary("Downloads a HOME-ready PKM and queues it for trade.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        private async Task HOMEReadyRequestAsync(int index)
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.").ConfigureAwait(false);
                return;
            }

            var userID = Context.User.Id;
            if (SysCord<T>.Runner.Hub.Queues.Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You're already in a trade queue. Finish that one first.").ConfigureAwait(false);
                return;
            }

            try
            {
                var files = Directory.GetFiles(HOMEFolder)
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    await ReplyAsync("No HOME-ready PKM files found.").ConfigureAwait(false);
                    return;
                }

                if (index < 1 || index > files.Count)
                {
                    await ReplyAsync("Invalid entry number. Choose a valid file.").ConfigureAwait(false);
                    return;
                }

                var filePath = files[index - 1];
                var data = await File.ReadAllBytesAsync(filePath);
                var entity = EntityFormat.GetFromBytes(data);

                if (entity == null)
                {
                    await ReplyAsync("Could not convert the file to a valid PKM.").ConfigureAwait(false);
                    return;
                }

                var download = new Download<PKM>
                {
                    Data = entity,
                    Success = true
                };

                var pk = GetRequest(download);
                if (pk == null && entity is PKM rawPkm)
                {
                    pk = EntityConverter.ConvertToType(rawPkm, typeof(T), out _) as T;
                }

                if (pk == null)
                {
                    await ReplyAsync("Failed to convert the HOME-ready file to your trade format.").ConfigureAwait(false);
                    return;
                }

                var code = SysCord<T>.Runner.Hub.Queues.Info.GetRandomTradeCode(userID);
                var lgcode = SysCord<T>.Runner.Hub.Queues.Info.GetRandomLGTradeCode();
                var sig = Context.User.GetFavor();

                await ReplyAsync($"**HOME-Ready Pokémon added to the queue.**").ConfigureAwait(false);

                await Helpers<T>.AddTradeToQueueAsync(
                    context: Context,
                    code: code,
                    trainerName: Context.User.Username,
                    pk: pk,
                    sig: sig,
                    usr: Context.User,
                    isBatchTrade: false,
                    batchTradeNumber: 1,
                    totalBatchTrades: 1,
                    isHiddenTrade: false,
                    isMysteryEgg: false,
                    lgcode: lgcode ?? SysCord<T>.Runner.Hub.Queues.Info.GetRandomLGTradeCode(),
                    tradeType: PokeTradeType.Specific,
                    ignoreAutoOT: false,
                    setEdited: false,
                    isNonNative: false
                );
            }
            catch (Exception ex)
            {
                await ReplyAsync($"**Error:** {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Context.Message is IUserMessage msg)
                        await msg.DeleteAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }


        // ============================================================================
        //  LIST
        // ============================================================================
        [Command("homereadylist")]
        [Alias("hrl")]
        [Summary("Lists available HOME-Ready files with filtering + pagination.")]
        private async Task HOMEListAsync([Remainder] string args = "")
        {
            if (string.IsNullOrWhiteSpace(HOMEFolder))
            {
                await ReplyAsync("This bot does not have the HOME-Ready module configured.");
                return;
            }

            const int itemsPerPage = 10;

            var files = Directory.GetFiles(HOMEFolder)
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();

            if (files.Count == 0)
            {
                await ReplyAsync("No HOME-ready files found.");
                return;
            }

            // Parse filter + page
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string filter = "";
            int page = 1;

            if (parts.Length > 0)
            {
                if (int.TryParse(parts.Last(), out int parsedPage))
                {
                    page = parsedPage;
                    filter = string.Join(" ", parts.Take(parts.Length - 1));
                }
                else
                {
                    filter = string.Join(" ", parts);
                }
            }

            var filtered = files
                .Where(f => string.IsNullOrWhiteSpace(filter) ||
                            f.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count == 0)
            {
                await ReplyAsync($"No HOME-ready files found for '{filter}'.");
                return;
            }

            var pageCount = (int)Math.Ceiling(filtered.Count / (double)itemsPerPage);
            page = Math.Clamp(page, 1, pageCount);

            var pageItems = filtered
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();

            var embed = new EmbedBuilder()
                .WithTitle($"HOME-Ready Files — '{filter}'")
                .WithDescription($"Page **{page}** of **{pageCount}**")
                .WithColor(Color.Blue);

            // Map file extensions to game names
            var extensionToGame = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                 { ".pb7", "LGPE" },
                 { ".pk8", "SWSH" },
                 { ".pb8", "BDSP" },
                 { ".pa8", "PLA" },
                 { ".pa9", "PLZA" },
                 { ".pk9", "SV" }
            };

            foreach (var item in pageItems)
            {
                var index = files.IndexOf(item) + 1;

                // Get the file extension, trim whitespace, and uppercase it
                var ext = Path.GetExtension(item)?.Trim().ToUpperInvariant() ?? "";

                // Lookup in dictionary
                string game = extensionToGame.TryGetValue(ext, out var g) ? g : "Unknown";

                // Add embed field
                embed.AddField(
                    $"{index}. {item}",
                    $"Use `{Prefix}hrr {index}` to request this Pokémon.\nThis file is for **{game}**."
                );
            }

            var embedMsg = await ReplyAsync(embed: embed.Build());

            await Task.Delay(20_000);

            try
            {
                await embedMsg.DeleteAsync();
            }
            catch { }
        }
    }
}
