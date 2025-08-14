using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Discord.WebSocket;

namespace SysBot.Pokemon
{
    public class TradeCodeStorage
    {
        private const string FileName = "tradecodes.json";
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private Dictionary<ulong, TradeCodeDetails>? _tradeCodeDetails;
        private readonly MilestoneService _milestones;

        public TradeCodeStorage()
            : this(new MilestoneService())
        {
        }

        public TradeCodeStorage(MilestoneService milestones)
        {
            _milestones = milestones ?? new MilestoneService();
            LoadFromFile();
        }

        public bool DeleteTradeCode(ulong trainerID)
        {
            LoadFromFile();
            if (_tradeCodeDetails != null && _tradeCodeDetails.Remove(trainerID))
            {
                SaveToFile();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the user's trade code, increments trade count,
        /// persists, and triggers milestone message if applicable.
        /// </summary>
        public int GetTradeCode(ulong trainerID, ISocketMessageChannel channel, SocketUser user)
        {
            LoadFromFile();

            if (_tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.TradeCount++;
                SaveToFile();

                // Milestone check for this count
                TriggerMilestoneIfAny(details.TradeCount, channel, user);
                return details.Code;
            }

            // New user: create code + first milestone (1)
            var code = GenerateRandomTradeCode();
            _tradeCodeDetails ??= new Dictionary<ulong, TradeCodeDetails>();
            _tradeCodeDetails[trainerID] = new TradeCodeDetails
            {
                Code = code,
                TradeCount = 1
            };
            SaveToFile();

            TriggerMilestoneIfAny(1, channel, user);
            return code;
        }

        public int GetTradeCount(ulong trainerID)
        {
            LoadFromFile();
            return _tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details)
                ? details.TradeCount
                : 0;
        }

        public TradeCodeDetails? GetTradeDetails(ulong trainerID)
        {
            LoadFromFile();
            return _tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details)
                ? details
                : null;
        }

        public void UpdateTradeDetails(ulong trainerID, string ot, int tid, int sid)
        {
            LoadFromFile();
            if (_tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.OT = ot;
                details.TID = tid;
                details.SID = sid;
                SaveToFile();
            }
        }

        public bool UpdateTradeCode(ulong trainerID, int newCode)
        {
            LoadFromFile();
            if (_tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.Code = newCode;
                SaveToFile();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Convenience for the medals command: returns the earned milestone counts
        /// (e.g., [1, 50, 100, ...]) for this trainer.
        /// </summary>
        public List<int> GetEarnedMilestoneCounts(ulong trainerID)
        {
            LoadFromFile();
            if (_tradeCodeDetails != null && _tradeCodeDetails.TryGetValue(trainerID, out var details))
                return _milestones.GetEarnedMilestones(details.TradeCount);

            return new List<int>();
        }

        private static int GenerateRandomTradeCode()
        {
            var settings = new TradeSettings();
            return settings.GetRandomTradeCode();
        }

        private void TriggerMilestoneIfAny(int tradeCount, ISocketMessageChannel channel, SocketUser user)
        {
            if (_milestones.IsMilestone(tradeCount))
            {
                // Fire-and-forget so we don't block message handling
                _ = _milestones.SendMilestoneEmbedAsync(channel, user, tradeCount);
            }
        }

        private void LoadFromFile()
        {
            try
            {
                if (File.Exists(FileName))
                {
                    string json = File.ReadAllText(FileName);
                    _tradeCodeDetails = JsonSerializer.Deserialize<Dictionary<ulong, TradeCodeDetails>>(json, SerializerOptions)
                                       ?? new Dictionary<ulong, TradeCodeDetails>();
                }
                else
                {
                    _tradeCodeDetails = new Dictionary<ulong, TradeCodeDetails>();
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(nameof(TradeCodeStorage), $"Error loading trade codes file: {ex.Message}");
                _tradeCodeDetails ??= new Dictionary<ulong, TradeCodeDetails>();
            }
        }

        private void SaveToFile()
        {
            try
            {
                string json = JsonSerializer.Serialize(_tradeCodeDetails, SerializerOptions);
                File.WriteAllText(FileName, json);
            }
            catch (IOException ex)
            {
                LogUtil.LogInfo(nameof(TradeCodeStorage), $"Error saving trade codes to file: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                LogUtil.LogInfo(nameof(TradeCodeStorage), $"Access denied while saving trade codes to file: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(nameof(TradeCodeStorage), $"An error occurred while saving trade codes to file: {ex.Message}");
            }
        }

        public class TradeCodeDetails
        {
            public int Code { get; set; }
            public string? OT { get; set; }
            public int SID { get; set; }
            public int TID { get; set; }
            public int TradeCount { get; set; }
        }
    }
}
