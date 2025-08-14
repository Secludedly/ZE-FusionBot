using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.Pokemon
{
    public record MilestoneData(string Description, string ImageUrl);

    /// <summary>
    /// Single source of truth for milestone thresholds, text, and images.
    /// Also builds/sends embeds with null-safety (no more NREs).
    /// </summary>
    public class MilestoneService
    {
        // Keep all milestone content in ONE place.
        private readonly SortedDictionary<int, MilestoneData> _milestones = new()
        {
            { 1,   new("Congratulations on your first trade!\n**Status:** Newbie Trainer.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/001.png") },
            { 50,  new("You've reached 50 trades!\n**Status:** Novice Trainer.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/050.png") },
            { 100, new("You've reached 100 trades!\n**Status:** Pokémon Professor.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/100.png") },
            { 150, new("You've reached 150 trades!\n**Status:** Pokémon Specialist.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/150.png") },
            { 200, new("You've reached 200 trades!\n**Status:** Pokémon Champion.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/200.png") },
            { 250, new("You've reached 250 trades!\n**Status:** Pokémon Hero.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/250.png") },
            { 300, new("You've reached 300 trades!\n**Status:** Pokémon Elite.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/300.png") },
            { 350, new("You've reached 350 trades!\n**Status:** Pokémon Trader.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/350.png") },
            { 400, new("You've reached 400 trades!\n**Status:** Pokémon Sage.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/400.png") },
            { 450, new("You've reached 450 trades!\n**Status:** Pokémon Legend.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/450.png") },
            { 500, new("You've reached 500 trades!\n**Status:** Region Master.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/500.png") },
            { 550, new("You've reached 550 trades!\n**Status:** Trade Master.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/550.png") },
            { 600, new("You've reached 600 trades!\n**Status:** World Famous.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/600.png") },
            { 650, new("You've reached 650 trades!\n**Status:** Pokémon Master.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/650.png") },
            { 700, new("You've reached 700 trades!\n**Status:** Pokémon God.",
                       "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/700.png") },
        };

        public bool IsMilestone(int tradeCount) => _milestones.ContainsKey(tradeCount);

        public MilestoneData? GetMilestone(int tradeCount) =>
            _milestones.TryGetValue(tradeCount, out var m) ? m : null;

        /// <summary>
        /// Returns a sorted list of thresholds the user has already reached (1, 50, 100, ...).
        /// </summary>
        public List<int> GetEarnedMilestones(int tradeCount)
        {
            var earned = new List<int>();
            foreach (var kvp in _milestones)
                if (tradeCount >= kvp.Key)
                    earned.Add(kvp.Key);
            return earned;
        }

        public Embed BuildMedalEmbed(SocketUser user, int tradeCount)
        {
            var m = GetMilestone(tradeCount);
            var desc = m?.Description ?? $"Congratulations on reaching {tradeCount} trades! Keep it going!";
            var imageUrl = m?.ImageUrl ?? "";

            return new EmbedBuilder()
                .WithTitle($"{user.Username}'s Milestone Medal")
                .WithColor(Color.Gold)
                .WithDescription(desc)
                .WithThumbnailUrl(imageUrl)
                .Build();
        }

        /// <summary>
        /// Null-safe, Task-returning sender. Use fire-and-forget as: _ = SendMilestoneEmbedAsync(...).
        /// </summary>
        public async Task SendMilestoneEmbedAsync(ISocketMessageChannel? channel, SocketUser? user, int tradeCount)
        {
            if (channel == null || user == null)
            {
                LogUtil.LogInfo(nameof(MilestoneService),
                    $"Skipped milestone {tradeCount}: channel or user is null.");
                return;
            }

            var m = GetMilestone(tradeCount);
            if (m == null)
                return;

            var embed = new EmbedBuilder()
                .WithTitle($"🎉 Congratulations, {user.Username}! 🎉")
                .WithColor(Color.Gold)
                .WithImageUrl(m.ImageUrl)
                .WithDescription(tradeCount == 1
                    ? "Congratulations on your very first trade!\nCollect medals by trading with the bot!\nEvery 50 trades is a new medal!\nHow many can you collect?\nSee your current medals with **ml**."
                    : $"You’ve completed {tradeCount} trades!\n*Keep up the great work!*")
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }
}
