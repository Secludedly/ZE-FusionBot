using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PKHeX.Core;
using Discord;
using System.Threading.Channels;
using Discord.WebSocket;

namespace SysBot.Pokemon
{
    public class TradeCodeStorage
    {
        private const string FileName = "tradecodes.json";
        private Dictionary<ulong, TradeCodeDetails> _tradeCodeDetails;
        private readonly Dictionary<int, string> _milestoneImages = new()
        {
            { 1, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/001.png" },
            { 50, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/050.png" },
            { 100, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/100.png" },
            { 150, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/150.png" },
            { 200, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/200.png" },
            { 250, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/250.png" },
            { 300, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/300.png" },
            { 350, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/350.png" },
            { 400, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/400.png" },
            { 450, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/450.png" },
            { 500, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/500.png" },
            { 550, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/550.png" },
            { 600, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/600.png" },
            { 650, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/650.png" },
            { 700, "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/700.png" },
            // Add more milestone images...
        };

        public TradeCodeStorage()
        {
            LoadFromFile();
        }

        public bool AddOrUpdateTradeCode(ulong userID, int tradeCode, string? ot, int tid, int sid, GameVersion gameVersion)
        {
            LoadFromFile();

            if (_tradeCodeDetails.ContainsKey(userID))
            {
                // Update existing entry
                var details = _tradeCodeDetails[userID];
                details.Code = tradeCode;
                details.OT = ot ?? details.OT;
                details.TID = tid;
                details.SID = sid;
            }
            else
            {
                // Add new entry
                _tradeCodeDetails[userID] = new TradeCodeDetails
                {
                    Code = tradeCode,
                    OT = ot,
                    TID = tid,
                    SID = sid,
                    TradeCount = 0,
                };
            }

            SaveToFile();
            return true;
        }

        public int GetTradeCode(ulong trainerID, ISocketMessageChannel channel, SocketUser user)
        {
            LoadFromFile();

            if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.TradeCount++;
                SaveToFile();

                // Check if the new trade count is a milestone
                CheckTradeMilestone(details.TradeCount, channel, user);
                return details.Code;
            }

            var code = GenerateRandomTradeCode();
            _tradeCodeDetails[trainerID] = new TradeCodeDetails { Code = code, TradeCount = 1 };
            SaveToFile();

            // Check if the new user hits the first milestone
            CheckTradeMilestone(1, channel, user);
            return code;
        }

        private static int GenerateRandomTradeCode()
        {
            var settings = new TradeSettings();
            return settings.GetRandomTradeCode();
        }

        private void CheckTradeMilestone(int tradeCount, ISocketMessageChannel channel, SocketUser user)
        {
            if (_milestoneImages.ContainsKey(tradeCount))
            {
                SendMilestoneEmbed(tradeCount, channel, user);
            }
        }

        private async void SendMilestoneEmbed(int tradeCount, ISocketMessageChannel channel, SocketUser user)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"🎉 Congratulations, {user.Username}! 🎉")
                .WithColor(Color.Gold)
                .WithImageUrl(_milestoneImages[tradeCount]);

            if (tradeCount == 1)
            {
                embedBuilder.WithDescription("Congratulations on your very first trade!\nCollect medals by trading with the bot!\nEvery 50 trades is a new medal!\nHow many can you collect?");
            }
            else
            {
                embedBuilder.WithDescription($"You’ve completed {tradeCount} trades!\n*Keep up the great work!*");
            }

            var embed = embedBuilder.Build();
            await channel.SendMessageAsync(embed: embed);
        }


        private void LoadFromFile()
        {
            if (File.Exists(FileName))
            {
                string json = File.ReadAllText(FileName);
                _tradeCodeDetails = JsonConvert.DeserializeObject<Dictionary<ulong, TradeCodeDetails>>(json)
                                    ?? new Dictionary<ulong, TradeCodeDetails>();
            }
            else
            {
                _tradeCodeDetails = new Dictionary<ulong, TradeCodeDetails>();
            }
        }

        private void SaveToFile()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_tradeCodeDetails, Formatting.Indented);
                File.WriteAllText(FileName, json);
            }
            catch (IOException ex)
            {
                LogUtil.LogInfo("TradeCodeStorage", $"Error saving trade codes to file: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                LogUtil.LogInfo("TradeCodeStorage", $"Access denied while saving trade codes to file: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo("TradeCodeStorage", $"An error occurred while saving trade codes to file: {ex.Message}");
            }
        }

        public bool DeleteTradeCode(ulong trainerID)
        {
            LoadFromFile();

            if (_tradeCodeDetails.Remove(trainerID))
            {
                SaveToFile();
                return true;
            }
            return false;
        }

        public int GetTradeCount(ulong trainerID)
        {
            LoadFromFile();
            return _tradeCodeDetails.TryGetValue(trainerID, out var details) ? details.TradeCount : 0;
        }

        public TradeCodeDetails? GetTradeDetails(ulong trainerID)
        {
            LoadFromFile();
            return _tradeCodeDetails.TryGetValue(trainerID, out var details) ? details : null;
        }

        public void UpdateTradeDetails(ulong trainerID, string? ot, int tid, int sid)
        {
            LoadFromFile();

            if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.OT = ot ?? details.OT;
                details.TID = tid;
                details.SID = sid;
                SaveToFile();
            }
        }

        public class TradeCodeDetails
        {
            public int Code { get; set; }
            public string? OT { get; set; }
            public int TID { get; set; }
            public int SID { get; set; }
            public int TradeCount { get; set; }
        }
    }
}
