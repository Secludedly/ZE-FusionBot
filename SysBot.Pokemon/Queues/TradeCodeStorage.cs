using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PKHeX.Core;

namespace SysBot.Pokemon
{
    public class TradeCodeStorage
    {
        private const string FileName = "tradecodes.json";
        private Dictionary<ulong, TradeCodeDetails> _tradeCodeDetails;

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

        public int GetTradeCode(ulong trainerID)
        {
            LoadFromFile();

            if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
            {
                details.TradeCount++;
                SaveToFile();
                return details.Code;
            }

            var code = GenerateRandomTradeCode();
            _tradeCodeDetails[trainerID] = new TradeCodeDetails { Code = code, TradeCount = 1 };
            SaveToFile();
            return code;
        }

        private static int GenerateRandomTradeCode()
        {
            var settings = new TradeSettings();
            return settings.GetRandomTradeCode();
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
