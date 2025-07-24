using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon;

/// <summary>
/// Contains a queue of users to be processed.
/// </summary>
/// <typeparam name="T">Type of data to be transmitted to the users</typeparam>
public sealed record TradeQueueInfo<T>(PokeTradeHub<T> Hub)
    where T : PKM, new()
{
    private readonly object _sync = new();
    private readonly List<TradeEntry<T>> UsersInQueue = [];
    public readonly PokeTradeHub<T> Hub = Hub;
    private readonly TradeCodeStorage _tradeCodeStorage = new();

    public bool IsUserInQueue(ulong userId)
    {
        lock (_sync)
        {
            return UsersInQueue.Any(entry => entry.UserID == userId);
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
                return UsersInQueue.Count;
        }
    }

    public bool ToggleQueue() => Hub.Config.Queues.CanQueue ^= true;

    public bool GetCanQueue()
    {
        if (!Hub.Config.Queues.CanQueue)
            return false;
        lock (_sync)
            return UsersInQueue.Count < Hub.Config.Queues.MaxQueueCount && Hub.TradeBotsReady;
    }

    public TradeEntry<T>? GetDetail(ulong uid)
    {
        lock (_sync)
            return UsersInQueue.Find(z => z.UserID == uid);
    }

    public QueueCheckResult<T> CheckPosition(ulong uid, int uniqueTradeID, PokeRoutineType type = 0)
    {
        lock (_sync)
        {
            var allTrades = Hub.Queues.AllQueues.SelectMany(q => q.Queue.Select(x => x.Value)).ToList();

            // Check if the user is in the queue
            var index = allTrades.FindIndex(z => z.Trainer.ID == uid && z.UniqueTradeID == uniqueTradeID);
            if (index < 0)
                return QueueCheckResult<T>.None;

            var entry = allTrades[index];

            // Count total trades accounting for batch trades
            int totalTradesAhead = 0;
            int processingCount = 0;
            for (int i = 0; i < allTrades.Count; i++)
            {
                var trade = allTrades[i];

                // Count processing trades
                if (trade.IsProcessing)
                {
                    processingCount++;
                    continue;
                }

                // For trades ahead of us, count their actual trade count
                if (i < index)
                {
                    if (trade.TotalBatchTrades > 1 && trade.BatchTrades != null)
                        totalTradesAhead += trade.BatchTrades.Count;
                    else
                        totalTradesAhead += 1;
                }
            }

            // Calculate actual position
            var actualIndex = totalTradesAhead + 1 + processingCount;

            // Calculate total trades in queue
            var totalInQueue = allTrades.Sum(trade =>
            {
                if (trade.TotalBatchTrades > 1 && trade.BatchTrades != null)
                    return trade.BatchTrades.Count;
                return 1;
            }) + processingCount;

            return new QueueCheckResult<T>(true, new TradeEntry<T>(entry, uid, type, entry.Trainer.TrainerName, uniqueTradeID), actualIndex, totalInQueue);
        }
    }

    public string GetPositionString(ulong uid, int uniqueTradeID, PokeRoutineType type = PokeRoutineType.Idle)
    {
        var check = CheckPosition(uid, uniqueTradeID, type);
        return check.GetMessage();
    }

    public string GetTradeList(PokeRoutineType t)
    {
        lock (_sync)
        {
            var queue = Hub.Queues.GetQueue(t);
            if (queue.Count == 0)
                return "Nobody in queue.";
            return queue.Summary();
        }
    }

    public void ClearAllQueues()
    {
        lock (_sync)
        {
            Hub.Queues.ClearAll();
            UsersInQueue.Clear();
        }
    }

    public void CleanStuckTrades()
    {
        lock (_sync)
        {
            var stuckTrades = UsersInQueue.Where(x => x.Trade.IsProcessing).ToList();
            foreach (var trade in stuckTrades)
            {
                trade.Trade.IsProcessing = false;
                Remove(trade);
                // Also release batch tracker if it's a batch trade
                if (trade.Trade.TotalBatchTrades > 1)
                {
                    var tracker = BatchTradeTracker<T>.Instance;
                    tracker.ReleaseBatch(trade.UserID, trade.Trade.UniqueTradeID);
                }
            }
        }
    }

    public QueueResultRemove ClearTrade(string userName)
    {
        var details = GetIsUserQueued(z => z.Username == userName);
        return ClearTrade(details);
    }

    public QueueResultRemove ClearTrade(ulong userID)
    {
        var details = GetIsUserQueued(z => z.UserID == userID);
        return ClearTrade(details);
    }

    private QueueResultRemove ClearTrade(ICollection<TradeEntry<T>> details)
    {
        if (details.Count == 0)
            return QueueResultRemove.NotInQueue;

        bool removedAll = true;
        bool currentlyProcessing = false;
        bool removedPending = false;

        foreach (var detail in details.ToList())
        {
            if (detail.Trade.IsProcessing)
            {
                currentlyProcessing = true;
                if (!Hub.Config.Queues.CanDequeueIfProcessing)
                {
                    removedAll = false;
                    detail.Trade.IsCanceled = true;
                    continue;
                }
            }

            if (RemoveTradeEntry(detail))
                removedPending = true;
        }

        if (!removedAll && currentlyProcessing && !removedPending)
            return QueueResultRemove.CurrentlyProcessing;

        if (currentlyProcessing && removedPending)
            return QueueResultRemove.CurrentlyProcessingRemoved;

        if (removedPending)
            return QueueResultRemove.Removed;

        return QueueResultRemove.NotInQueue;
    }

    private bool RemoveTradeEntry(TradeEntry<T> entry)
    {
        if (Remove(entry))
        {
            var queue = Hub.Queues.GetQueue(entry.Type);
            var tradeDetail = queue.Queue.FirstOrDefault(x => x.Value.Equals(entry.Trade));
            if (tradeDetail.Value != null)
            {
                if (queue.Remove(tradeDetail.Value) > 0)
                    return true;
            }
        }

        return false;
    }

    public IEnumerable<string> GetUserList(string fmt)
    {
        lock (_sync)
        {
            return UsersInQueue.Select(z => string.Format(fmt, z.Trade.ID, z.Trade.Code, z.Trade.Type, z.Username, (Species)z.Trade.TradeData.Species));
        }
    }

    public IEnumerable<ulong> GetUserIdList(int count)
    {
        lock (_sync)
        {
            return UsersInQueue.Take(count).Select(z => z.Trade.Trainer.ID);
        }
    }

    public IList<TradeEntry<T>> GetIsUserQueued(Func<TradeEntry<T>, bool> match)
    {
        lock (_sync)
        {
            return UsersInQueue.Where(match).ToArray();
        }
    }

    public bool Remove(TradeEntry<T> detail)
    {
        lock (_sync)
        {
            LogUtil.LogInfo($"Removing {detail.Trade.Trainer.TrainerName}", nameof(TradeQueueInfo<T>));
            return UsersInQueue.Remove(detail);
        }
    }

    public QueueResultAdd AddToTradeQueue(TradeEntry<T> trade, ulong userID, bool allowMultiple = false, bool sudo = false)
    {
        lock (_sync)
        {
            if (UsersInQueue.Any(z => z.UserID == userID) && !allowMultiple && !sudo)
                return QueueResultAdd.AlreadyInQueue;

            if (Hub.Config.Legality.ResetHOMETracker && trade.Trade.TradeData is IHomeTrack t)
                t.Tracker = 0;

            var priority = sudo ? PokeTradePriorities.Tier1 :
                           trade.Trade.IsFavored ? PokeTradePriorities.Tier2 :
                           PokeTradePriorities.TierFree;
            var queue = Hub.Queues.GetQueue(trade.Type);

            queue.Enqueue(trade.Trade, priority);
            UsersInQueue.Add(trade);

            trade.Trade.Notifier.OnFinish = _ => Remove(trade);
            return QueueResultAdd.Added;
        }
    }

    public int GetRandomTradeCode(ulong trainerID, ISocketMessageChannel channel, SocketUser user)
    {
        if (Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            return _tradeCodeStorage.GetTradeCode(trainerID, channel, user);
        }
        else
        {
            return Hub.Config.Trade.GetRandomTradeCode();
        }
    }

    public List<Pictocodes> GetRandomLGTradeCode()
    {
        var code = new List<Pictocodes>();
        for (int i = 0; i <= 2; i++)
        {
            code.Add((Pictocodes)Util.Rand.Next(10));
            code.Add(Pictocodes.Pikachu);

        }
        return code;
    }

    public int UserCount(Func<TradeEntry<T>, bool> func)
    {
        lock (_sync)
            return UsersInQueue.Count(func);
    }

    public (int effectiveCount, int processingBatchTrades) GetQueueStats()
    {
        lock (_sync)
        {
            var allTrades = Hub.Queues.AllQueues.SelectMany(q => q.Queue.Select(x => x.Value)).ToList();

            // Count effective queue size (batch trades count as their full size)
            int effectiveCount = allTrades
            .GroupBy(t => new { t.Trainer.ID, t.UniqueTradeID })
            .Sum(g => g.First().TotalBatchTrades > 1 ? g.First().TotalBatchTrades : g.Count());

            // Count how many batch trades are currently processing
            int processingBatchTrades = allTrades
            .Where(t => t.IsProcessing && t.TotalBatchTrades > 1)
            .GroupBy(t => new { t.Trainer.ID, t.UniqueTradeID })
            .Count();
            return (effectiveCount, processingBatchTrades);
        }
    }

    public int GetRandomTradeCode(int trainerID)
    {
        ulong id = (ulong)trainerID;
        return GetRandomTradeCode(id, null, null); // You can safely ignore `null` here if logging isn't mandatory
    }
}
