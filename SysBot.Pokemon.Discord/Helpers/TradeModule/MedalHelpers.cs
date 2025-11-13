using Discord;
using Discord.WebSocket;
using System.Linq;

namespace SysBot.Pokemon.Discord;

public static class MedalHelpers
{
    public static int GetCurrentMilestone(int totalTrades)
    {
        int[] milestones = [700, 650, 600, 550, 500, 450, 400, 350, 300, 250, 200, 150, 100, 50, 1];
        return milestones.FirstOrDefault(m => totalTrades >= m, 0);
    }

    public static Embed CreateMedalsEmbed(SocketUser user, int milestone, int totalTrades)
    {
        string status = milestone switch
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

        string description = $"Total Trades: **{totalTrades}**\n**Current Status:** {status}";

        if (milestone > 0)
        {
            string imageUrl = $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/{milestone:D3}.png";
            return new EmbedBuilder()
                .WithTitle($"{user.Username}'s Trading Status")
                .WithColor(new Color(255, 215, 0))
                .WithDescription(description)
                .WithThumbnailUrl(imageUrl)
                .Build();
        }
        else
        {
            return new EmbedBuilder()
                .WithTitle($"{user.Username}'s Trading Status")
                .WithColor(new Color(255, 215, 0))
                .WithDescription(description)
                .Build();
        }
    }
}
