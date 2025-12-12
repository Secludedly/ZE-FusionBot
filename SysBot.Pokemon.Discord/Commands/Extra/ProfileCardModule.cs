using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.Pokemon;
using SysBot.Pokemon.Discord;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FusionBot.Modules
{
    public class ProfileCardModule : ModuleBase<SocketCommandContext>
    {
        private readonly TradeCodeStorage _tradeStorage = new();

        // ======================
        // MYINFO COMMAND
        // ======================
        [Command("myinfo")]
        [Alias("mi")]
        [Summary("Displays your FusionBot profile card.")]
        public async Task MyInfoAsync()
        {
            if (Context.User is not SocketGuildUser user)
            {
                await ReplyAsync("Can't show profile outside a guild!");
                return;
            }

            var tradeDetails = _tradeStorage.GetTradeDetails(user.Id);
            if (tradeDetails == null)
            {
                await ReplyAsync("You haven't traded yet, so no profile data exists!");
                return;
            }

            int totalTrades = _tradeStorage.GetTradeCount(user.Id);

            // Medal info
            int milestone = MedalHelpers.GetCurrentMilestone(totalTrades);
            string medalImageUrl = $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/{milestone:D3}.png";
            string medalTitle = milestone switch
            {
                1 => "Newbie Trainer",
                50 => "Novice Trainer",
                100 => "Pokémon Professor",
                150 => "Pokémon Specialist",
                200 => "Pokémon Champion",
                250 => "Pokémon Hero",
                300 => "Pokémon Elite",
                350 => "Pokémon Trader",
                400 => "Pokémon Sage",
                450 => "Pokémon Legend",
                500 => "Region Master",
                550 => "Trade Master",
                600 => "World Famous",
                650 => "Pokémon Master",
                700 => "Pokémon God",
                _ => "New Trainer"
            };

            // Embed color
            Color embedColor = MedalMilestoneToColor(milestone);

            // Level & gradient progress bar
            int level = (int)Math.Round(totalTrades / 700.0 * 100);
            double progressPct = totalTrades / 700.0;
            string progressBar = BuildGradientProgressBar(progressPct, embedColor);

            // Top role in current server
            string serverName = Context.Guild?.Name ?? "this server";
            var topRole = user.Roles.Where(r => !r.IsEveryone)
                                    .OrderByDescending(r => r.Position)
                                    .FirstOrDefault();
            string topRoleDisplay = topRole != null ? topRole.Mention : "No Roles";

            // Discord info
            string accountCreated = user.CreatedAt.ToString("MMM dd, yyyy");
            string serverJoin = user.JoinedAt?.ToString("MMM dd, yyyy") ?? "Unknown";
            int roleCount = user.Roles.Count(r => !r.IsEveryone);

            // Top role in current server with color flair
            var topRoleColor = user.Roles
                              .Where(r => !r.IsEveryone)
                              .OrderByDescending(r => r.Position)
                              .FirstOrDefault();

            string topRoleDisplayColor = topRoleColor != null ? topRoleColor.Mention : "No Roles";

            // Get a colored emoji representing the role color
//          string topRoleColorEmoji = topRole != null ? GetRoleColorEmoji(topRole.Color) : "⬜";

            // User quote
            string quote = tradeDetails.Quote ?? GenerateRandomQuote();

            // Calculate level and trades to next level
            int maxTrades = 700;
            int maxLevel = 100;
            int tradesPerLevel = maxTrades / maxLevel;
            int tradesToNextLevel = ((level + 1) * tradesPerLevel) - totalTrades;
            if (tradesToNextLevel < 0) tradesToNextLevel = 0; // cap at max level

            // Build embed
            var embed = new EmbedBuilder()
    .WithTitle($"{user.Username}'s Bot Profile Card!")
    .WithThumbnailUrl(user.GetAvatarUrl(size: 512) ?? user.GetDefaultAvatarUrl())
    .WithColor(embedColor)

    // ==============================
    // PERSONAL QUOTE
    // ==============================
    .AddField("─ **PERSONAL QUOTE**", $"\"*{quote}*\"", false)

    // ==============================
    // TRAINER INFO
    // ==============================
    .AddField("─ **TRAINER INFO**",
        $"**OT:** {tradeDetails.OT ?? "Unknown"}\n" +
        $"**TID:** {tradeDetails.TID}\n" +
        $"**SID:** {tradeDetails.SID}\n" +
        $"**Total Trades:** {totalTrades}",
        false)

    // ==============================
    // MILESTONE INFO
    // ==============================
    .AddField("─ **MILESTONE INFO**",
        $"**Current Title:** {medalTitle}\n" +
        $"**Current Milestone:** {milestone}",
        false)

    // ==============================
    // LEVEL PROGRESS
    // ==============================
    .AddField("─ **LEVEL PROGRESS**",
        $"🏆 **Current Level:** {level}   ⚡ **To Next Level:** {tradesToNextLevel}\n" +
        $"**Progress...**\n{progressBar} {Math.Round(progressPct * 100)}%",
        false)

    // ==============================
    // DISCORD INFO
    // ==============================
    .AddField("─ **DISCORD INFO**",
        $"**Account Created:** {accountCreated}\n" +
        $"**Joined {serverName}:** {serverJoin}\n" +
        $"**Roles Count:** {roleCount}\n" +
        $"**Top Role:** {topRoleDisplay}",
        false)

    // ==============================
    // CURRENT MEDAL
    // ==============================
    .AddField("✨───────**CURRENT MEDAL**───────✨", "\u200B", false)
    .WithImageUrl(medalImageUrl)

    .WithFooter(f => f.Text = "To set a personal quote, use the miq command");


            await ReplyAsync(embed: embed.Build());
        }

        // ======================
        // MYINFOQUOTE COMMAND
        // ======================
        [Command("myinfoquote")]
        [Alias("miq")]
        [Summary("Set your personal FusionBot profile quote.")]
        public async Task SetQuoteAsync([Remainder] string quote)
        {
            if (quote.Length > 50)
            {
                await ReplyAsync("Quote is too long! Max 50 characters.");
                return;
            }

            var details = _tradeStorage.GetTradeDetails(Context.User.Id);
            if (details == null)
            {
                await ReplyAsync("You haven't traded yet, so no profile exists to set a quote.");
                return;
            }

            _tradeStorage.UpdateTradeDetails(
            Context.User.Id,
            details.OT ?? "",
            details.TID,
            details.SID,
            quote
            );

            await ReplyAsync("Your profile quote has been updated!");
        }

        // ======================
        // Helper Methods
        // ======================
        private string BuildGradientProgressBar(double pct, Color medalColor)
        {
            int totalSegments = 20;
            int filled = (int)Math.Round(pct * totalSegments);
            int empty = totalSegments - filled;

            // gradient effect
            string greenBlock = "🟩";
            string medalBlock = "🟩"; // emoji to match medal if needed
            string grayBlock = "⬜";

            string bar = "";
            for (int i = 0; i < filled; i++)
            {
                // First half green, second half medal color
                if (i < filled / 2)
                    bar += greenBlock;
                else
                    bar += medalBlock;
            }
            bar += string.Concat(Enumerable.Repeat(grayBlock, empty));

            return bar;
        }

        // Helper method for mapping role color to a basic emoji
        private string GetRoleColorEmoji(Color roleColor)
        {
            // Use simple thresholding for primary colors
            if (roleColor.R > 200 && roleColor.G < 100 && roleColor.B < 100) return "🟥"; // Red
            if (roleColor.R < 100 && roleColor.G > 200 && roleColor.B < 100) return "🟩"; // Green
            if (roleColor.R < 100 && roleColor.G < 100 && roleColor.B > 200) return "🟦"; // Blue
            if (roleColor.R > 200 && roleColor.G > 200 && roleColor.B < 100) return "🟨"; // Yellow
            if (roleColor.R > 200 && roleColor.G < 100 && roleColor.B > 200) return "🟪"; // Purple
            if (roleColor.R < 100 && roleColor.G > 200 && roleColor.B > 200) return "🟦"; // Cyan-like
            if (roleColor.R > 200 && roleColor.G > 100 && roleColor.B < 100) return "🟧"; // Orange-like
            return "⬜"; // fallback for grey/black/unknown
        }

        private string GenerateRandomQuote()
        {
            string[] quotes = new[]
            {
        "I only trade shiny Pokémon… sometimes.",
        "Here for the fun, not the fame!",
        "Rare candy addict.",
        "I speak fluent Pikachu.",
        "I bribed this bot with cookies.",
        "If you see me trading, run.",
        "Trading since the beginning of time.",
        "Gotta catch 'em all… eventually."
    };
            Random r = new();
            return quotes[r.Next(quotes.Length)];
        }

        private Color MedalMilestoneToColor(int milestone)
        {
            return milestone switch
            {
                1 => new Color(169, 169, 169),     // Grey
                50 => new Color(205, 127, 50),     // Bronze
                100 => new Color(192, 192, 192),   // Silver
                150 => new Color(255, 215, 0),     // Gold
                200 => new Color(0, 255, 255),     // Cyan
                250 => new Color(0, 128, 0),       // Green
                300 => new Color(255, 105, 180),   // Hot Pink
                350 => new Color(138, 43, 226),    // BlueViolet
                400 => new Color(75, 0, 130),      // Indigo
                450 => new Color(255, 69, 0),      // OrangeRed
                500 => new Color(255, 140, 0),     // DarkOrange
                550 => new Color(0, 191, 255),     // DeepSkyBlue
                600 => new Color(124, 252, 0),     // LawnGreen
                650 => new Color(218, 112, 214),   // Orchid
                700 => new Color(255, 0, 0),       // Red
                _ => new Color(128, 128, 128)
            };
        }
    }
}
