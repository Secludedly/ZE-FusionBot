using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers; // Make sure to add this for LanguageHelper
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon
{
    public class PokeTradeLogNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
    {
        private int BatchTradeNumber { get; set; } = 1;
        private int TotalBatchTrades { get; set; } = 1;
        public Action<PokeRoutineExecutor<T>>? OnFinish { get; set; }

        public Task SendInitialQueueUpdate()
        {
            return Task.CompletedTask;
        }

        public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
        {
            BatchTradeNumber = currentBatchNumber;

            if (TotalBatchTrades > 1)
            {
                string speciesName = LanguageHelper.GetLocalizedSpeciesLog(currentPokemon);
                LogUtil.LogInfo($"Batch trade progress: {currentBatchNumber}/{TotalBatchTrades} - {speciesName}", "BatchTracker");
            }
        }

        public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        {
            var batchInfo = info.TotalBatchTrades > 1 ? $"[Batch trade starting - {info.TotalBatchTrades} total] " : "";
            string speciesName = LanguageHelper.GetLocalizedSpeciesLog(info.TradeData);
            LogUtil.LogInfo($"{batchInfo}Starting trade loop for {info.Trainer.TrainerName}, sending {speciesName}", routine.Connection.Label);
        }

        public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        {
            var batchInfo = info.TotalBatchTrades > 1 ? $"[Trade {BatchTradeNumber}/{info.TotalBatchTrades}] " : "";
            string speciesName = LanguageHelper.GetLocalizedSpeciesLog(info.TradeData);
            LogUtil.LogInfo($"{batchInfo}Searching for trade with {info.Trainer.TrainerName}, sending {speciesName}", routine.Connection.Label);
        }

        public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
        {
            LogUtil.LogInfo($"Canceling trade with {info.Trainer.TrainerName}, because {msg}.", routine.Connection.Label);
            OnFinish?.Invoke(routine);
        }

        public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
        {
            var ledyname = string.Empty;
            if (info.Trainer.TrainerName == "Random Distribution" && result.IsNicknamed)
                ledyname = $" ({result.Nickname})";

            var batchInfo = info.TotalBatchTrades > 1 ? $"[Trade {BatchTradeNumber}/{info.TotalBatchTrades}] " : "";
            string requestedSpecies = LanguageHelper.GetLocalizedSpeciesLog(info.TradeData);
            string receivedSpecies = LanguageHelper.GetLocalizedSpeciesLog(result);
            LogUtil.LogInfo($"{batchInfo}Finished trading {info.Trainer.TrainerName} {requestedSpecies} for {receivedSpecies}{ledyname}", routine.Connection.Label);

            if (info.TotalBatchTrades <= 1 || BatchTradeNumber == info.TotalBatchTrades)
            {
                OnFinish?.Invoke(routine);
            }
        }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
        {
            if (info.TotalBatchTrades > 1)
            {
                TotalBatchTrades = info.TotalBatchTrades;
                message = $"[Trade {BatchTradeNumber}/{TotalBatchTrades}] {message}";
            }
            LogUtil.LogInfo(message, routine.Connection.Label);
        }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
        {
            var msg = message.Summary;
            if (message.Details.Count > 0)
                msg += ", " + string.Join(", ", message.Details.Select(z => $"{z.Heading}: {z.Detail}"));

            if (info.TotalBatchTrades > 1)
            {
                TotalBatchTrades = info.TotalBatchTrades;
                msg = $"[Trade {BatchTradeNumber}/{TotalBatchTrades}] {msg}";
            }
            LogUtil.LogInfo(msg, routine.Connection.Label);
        }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
        {
            var batchInfo = info.TotalBatchTrades > 1 ? $"[Trade {BatchTradeNumber}/{info.TotalBatchTrades}] " : "";
            string speciesName = LanguageHelper.GetLocalizedSpeciesLog(result);
            LogUtil.LogInfo($"{batchInfo}Notifying {info.Trainer.TrainerName} about their {speciesName}", routine.Connection.Label);
            LogUtil.LogInfo($"{batchInfo}{message}", routine.Connection.Label);
        }
    }
}
