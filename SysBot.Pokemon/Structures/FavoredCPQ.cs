using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SysBot.Pokemon;

/// <summary>
/// Interface for favoritism settings used by the FavoredCPQ.
/// </summary>
public interface IFavoredCPQSetting
{
    /// <summary>
    /// Whether favoritism is enabled.
    /// </summary>
    bool EnableFavoritism { get; }

    /// <summary>
    /// Percentage of regular users that priority users can skip (0-100).
    /// </summary>
    int SkipPercentage { get; }

    /// <summary>
    /// Minimum number of regular users that must be processed before any priority user can skip ahead.
    /// </summary>
    int MinimumRegularUsersFirst { get; }
}

/// <summary>
/// Allows Enqueue requests to have favored requests inserted ahead of a percentage of unfavored requests.
/// </summary>
public sealed class FavoredCPQ<TKey, TValue> : ConcurrentPriorityQueue<TKey, TValue> where TKey : IComparable<TKey> where TValue : IEquatable<TValue>, IFavoredEntry
{
    public FavoredCPQ(IFavoredCPQSetting settings) => Settings = settings;

    public FavoredCPQ(IEnumerable<KeyValuePair<TKey, TValue>> collection, IFavoredCPQSetting settings) : base(collection) => Settings = settings;

    public IFavoredCPQSetting Settings { get; set; }

    public void Add(TKey priority, TValue value)
    {
        if (!Settings.EnableFavoritism || !value.IsFavored)
        {
            Enqueue(priority, value);
            return;
        }

        lock (_syncLock)
        {
            var q = Queue;
            var items = q.Items;
            int start = items.FindIndex(z => z.Key.Equals(priority));
            if (start < 0) // nobody with this priority in the queue
            {
                // Call directly into the methods since we already reserved the lock
                q.Insert(priority, value);
                return;
            }

            int count = 0;
            int favored = 0;
            int max = items.Count;
            int pos = start;
            while (pos != max)
            {
                var entry = items[pos];
                if (!entry.Key.Equals(priority))
                    break;

                count++;
                if (entry.Value.IsFavored)
                    ++favored;
                ++pos;
            }

            int insertPosition = start + favored + GetInsertPosition(count, favored, Settings);
            if (insertPosition >= items.Count)
                insertPosition = items.Count;

            // Call directly into the methods since we already reserved the lock
            var kvp = new KeyValuePair<TKey, TValue>(priority, value);
            items.Insert(insertPosition, kvp);
        }
    }

    public List<KeyValuePair<TKey, TValue>> GetSnapshot()
    {
        lock (_syncLock)
        {
            var items = Queue.Items;
            return [.. items];
        }
    }

    /// <summary>
    /// Calculates how many regular users a priority user should skip.
    /// Uses percentage-based calculation with minimum protection.
    /// </summary>
    /// <param name="total">Total users at this priority level</param>
    /// <param name="favored">Number of favored users already at this priority level</param>
    /// <param name="s">Settings containing skip percentage and minimum protection</param>
    /// <returns>Number of regular users to skip</returns>
    private static int GetInsertPosition(int total, int favored, IFavoredCPQSetting s)
    {
        int unfavored = total - favored;

        // Calculate insert position: percentage NOT skipped (users remaining ahead)
        // E.g., 70% skip means 30% remain ahead
        int skipCount = (int)Math.Ceiling(unfavored * ((100 - s.SkipPercentage) / 100.0));

        // Ensure non-negative
        skipCount = Math.Max(0, skipCount);

        // Apply minimum protection: if enough regular users are waiting,
        // ensure at least MinimumRegularUsersFirst are processed before this priority user
        if (unfavored >= s.MinimumRegularUsersFirst)
            return Math.Max(s.MinimumRegularUsersFirst, skipCount);

        return skipCount;
    }
}

/// <summary>
/// Interface for entries that can be marked as favored.
/// </summary>
public interface IFavoredEntry
{
    bool IsFavored { get; }
}
