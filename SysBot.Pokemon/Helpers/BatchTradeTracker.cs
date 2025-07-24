using PKHeX.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Helpers
{
    public class BatchTradeTracker<T> where T : PKM, new()
    {
        private static readonly ConcurrentDictionary<Type, object> _instances = new();
        private static readonly object _instanceLock = new();

        private readonly ConcurrentDictionary<(ulong TrainerId, int UniqueTradeID), string> _activeBatches = new();
        private readonly TimeSpan _tradeTimeout = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<(ulong TrainerId, int UniqueTradeID), DateTime> _lastTradeTime = new();
        private readonly ConcurrentDictionary<ulong, List<T>> _receivedPokemon = new();
        private readonly object _claimLock = new();

        private BatchTradeTracker() { }

        public static BatchTradeTracker<T> Instance
        {
            get
            {
                var type = typeof(T);
                if (_instances.TryGetValue(type, out var instance))
                    return (BatchTradeTracker<T>)instance;

                lock (_instanceLock)
                {
                    if (_instances.TryGetValue(type, out instance))
                        return (BatchTradeTracker<T>)instance;

                    var newInstance = new BatchTradeTracker<T>();
                    _instances[type] = newInstance;
                    return newInstance;
                }
            }
        }

        public bool CanProcessBatchTrade(PokeTradeDetail<T> trade)
        {
            if (trade.TotalBatchTrades <= 1)
                return true;

            CleanupStaleEntries();
            return true; // Always true since we handle one batch container at a time
        }

        public bool TryClaimBatchTrade(PokeTradeDetail<T> trade, string botName)
        {
            if (trade.TotalBatchTrades <= 1)
                return true;

            var key = (trade.Trainer.ID, trade.UniqueTradeID);

            lock (_claimLock)
            {
                CleanupStaleEntries();

                if (_activeBatches.TryGetValue(key, out var existingBot))
                {
                    _lastTradeTime[key] = DateTime.Now;
                    return botName == existingBot;
                }

                if (_activeBatches.TryAdd(key, botName))
                {
                    _lastTradeTime[key] = DateTime.Now;
                    return true;
                }

                return false;
            }
        }

        public void CompleteBatchTrade(PokeTradeDetail<T> trade)
        {
            if (trade.TotalBatchTrades <= 1)
                return;

            var key = (trade.Trainer.ID, trade.UniqueTradeID);

            // Since we process the entire batch as one unit, we can remove it immediately
            _activeBatches.TryRemove(key, out _);
            _lastTradeTime.TryRemove(key, out _);
        }

        public void ReleaseBatch(ulong trainerId, int uniqueTradeId)
        {
            var key = (trainerId, uniqueTradeId);
            _activeBatches.TryRemove(key, out _);
            _lastTradeTime.TryRemove(key, out _);
        }

        private void CleanupStaleEntries()
        {
            var now = DateTime.Now;
            var staleKeys = _lastTradeTime
                .Where(x => now - x.Value > _tradeTimeout)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _activeBatches.TryRemove(key, out _);
                _lastTradeTime.TryRemove(key, out _);
            }
        }

        public void ClearReceivedPokemon(ulong trainerId)
        {
            _receivedPokemon.TryRemove(trainerId, out _);
        }

        public void AddReceivedPokemon(ulong trainerId, T pokemon)
        {
            if (!_receivedPokemon.ContainsKey(trainerId))
            {
                var newList = new List<T>();
                _receivedPokemon.TryAdd(trainerId, newList);
            }

            if (_receivedPokemon.TryGetValue(trainerId, out var list))
            {
                lock (list)
                {
                    list.Add(pokemon);
                }
            }
        }

        public List<T> GetReceivedPokemon(ulong trainerId)
        {
            if (_receivedPokemon.TryGetValue(trainerId, out var list))
            {
                lock (list)
                {
                    return [.. list];
                }
            }
            return [];
        }
    }
}
