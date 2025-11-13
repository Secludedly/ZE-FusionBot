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
            // First, try to find the trade in UsersInQueue (more reliable for recently added trades)
            var tradeEntry = UsersInQueue.FirstOrDefault(z => z.UserID == uid && z.UniqueTradeID == uniqueTradeID);
            
            // Try to find the trade in the queue system
            var allTrades = Hub.Queues.AllQueues.SelectMany(q => q.Queue.Select(x => x.Value)).ToList();
            var index = allTrades.FindIndex(z => z.Trainer.ID == uid && z.UniqueTradeID == uniqueTradeID);
            
            if (index >= 0)
            {
                // Trade found in queue - use queue-based position calculation
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

                // Use tradeEntry if available, otherwise create new one
                var resultEntry = tradeEntry ?? new TradeEntry<T>(entry, uid, type, entry.Trainer.TrainerName, uniqueTradeID);
                return new QueueCheckResult<T>(true, resultEntry, actualIndex, totalInQueue, entry.BatchTradeNumber, entry.TotalBatchTrades);
            }
            else if (tradeEntry != null)
            {
                // Trade found in UsersInQueue but not in queue yet - calculate position based on UsersInQueue order
                var userIndex = UsersInQueue.FindIndex(z => z.UserID == uid && z.UniqueTradeID == uniqueTradeID);
                
                // Count trades ahead in UsersInQueue
                int totalTradesAhead = 0;
                for (int i = 0; i < userIndex; i++)
                {
                    var entry = UsersInQueue[i];
                    if (entry.Trade.TotalBatchTrades > 1 && entry.Trade.BatchTrades != null)
                        totalTradesAhead += entry.Trade.BatchTrades.Count;
                    else
                        totalTradesAhead += 1;
                }

                // Count processing trades
                int processingCount = UsersInQueue.Count(z => z.Trade.IsProcessing);

                var actualIndex = totalTradesAhead + 1 + processingCount;
                var totalInQueue = UsersInQueue.Sum(entry =>
                {
                    if (entry.Trade.TotalBatchTrades > 1 && entry.Trade.BatchTrades != null)
                        return entry.Trade.BatchTrades.Count;
                    return 1;
                }) + processingCount;

                return new QueueCheckResult<T>(true, tradeEntry, actualIndex, totalInQueue, tradeEntry.Trade.BatchTradeNumber, tradeEntry.Trade.TotalBatchTrades);
            }
            
            // Trade not found anywhere
            return QueueCheckResult<T>.None;
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
            LogUtil.LogInfo(nameof(TradeQueueInfo<T>), $"Removing {detail.Trade.Trainer.TrainerName}");
            return UsersInQueue.Remove(detail);
        }
    }

    public QueueResultAdd AddToTradeQueue(TradeEntry<T> trade, ulong userID, bool allowMultiple = false, bool sudo = false)
    {
        lock (_sync)
        {
            // Check if user is already in queue (sudo users bypass this check entirely)
            if (!sudo)
            {
                // Get ALL existing entries for this user (not just the first one)
                var existingEntries = UsersInQueue.Where(z => z.UserID == userID).ToList();

                if (existingEntries.Count > 0)
                {
                    // For regular trades (allowMultiple = false), ALWAYS block duplicate entries
                    // This prevents users from joining the queue multiple times
                    if (!allowMultiple)
                    {
                        LogUtil.LogInfo(nameof(TradeQueueInfo<T>),
                            $"Blocked duplicate queue entry: User {userID} already has {existingEntries.Count} entry(s) in queue");
                        return QueueResultAdd.AlreadyInQueue;
                    }

                    // For batch trades (allowMultiple = true), only allow if same UniqueTradeID
                    // This allows multiple Pokemon from the same batch, but prevents starting a new batch
                    if (existingEntries.Any(e => e.UniqueTradeID != trade.Trade.UniqueTradeID))
                    {
                        LogUtil.LogInfo(nameof(TradeQueueInfo<T>),
                            $"Blocked different batch: User {userID} trying to queue UniqueTradeID {trade.Trade.UniqueTradeID} " +
                            $"but already has UniqueTradeID {existingEntries.First().UniqueTradeID}");
                        return QueueResultAdd.AlreadyInQueue;
                    }
                }
            }

            // Check if queue is full (unless user is sudo)
            if (!sudo && UsersInQueue.Count >= Hub.Config.Queues.MaxQueueCount)
                return QueueResultAdd.QueueFull;

            // PLZA blocked item validation - check if Pokemon has blocked held item
            // This central check ensures NO bypass is possible from any entry point
            if (NonTradableItemsPLZA.IsPLZAMode(Hub))
            {
                // Check the main pokemon
                if (NonTradableItemsPLZA.IsBlocked(trade.Trade.TradeData))
                {
                    var held = trade.Trade.TradeData.HeldItem;
                    var itemName = held > 0 ? PKHeX.Core.GameInfo.GetStrings("en").Item[held] : "(none)";
                    LogUtil.LogInfo(nameof(TradeQueueInfo<T>),
                        $"Blocked trade for user {userID}: held item '{itemName}' is not allowed in PLZA");
                    return QueueResultAdd.NotAllowedItem;
                }

                // For batch trades, also check all pokemon in the batch
                if (trade.Trade.BatchTrades != null && trade.Trade.BatchTrades.Count > 0)
                {
                    for (int i = 0; i < trade.Trade.BatchTrades.Count; i++)
                    {
                        if (NonTradableItemsPLZA.IsBlocked(trade.Trade.BatchTrades[i]))
                        {
                            var held = trade.Trade.BatchTrades[i].HeldItem;
                            var itemName = held > 0 ? PKHeX.Core.GameInfo.GetStrings("en").Item[held] : "(none)";
                            var speciesName = GameInfo.Strings.Species[trade.Trade.BatchTrades[i].Species];
                            LogUtil.LogInfo(nameof(TradeQueueInfo<T>),
                                $"Blocked batch trade for user {userID}: Pokemon #{i + 1} ({speciesName}) has held item '{itemName}' which is not allowed in PLZA");
                            return QueueResultAdd.NotAllowedItem;
                        }
                    }
                }
            }

            if (Hub.Config.Legality.ResetHOMETracker && trade.Trade.TradeData is IHomeTrack t)
                t.Tracker = 0;

            // Sudo users get Tier1 (highest priority)
            // Both favored and regular users get TierFree - favoritism is handled by queue positioning logic
            var priority = sudo ? PokeTradePriorities.Tier1 : PokeTradePriorities.TierFree;

            var queue = Hub.Queues.GetQueue(trade.Type);

            queue.Enqueue(trade.Trade, priority);
            UsersInQueue.Add(trade);

            trade.Trade.Notifier.OnFinish = _ => Remove(trade);
            return QueueResultAdd.Added;
        }
    }

    public int GetRandomTradeCode(ulong trainerID)
    {
        if (Hub.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            return _tradeCodeStorage.GetTradeCode(trainerID);
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

    // Genpkm method
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
}
