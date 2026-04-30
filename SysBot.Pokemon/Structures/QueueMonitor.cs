using PKHeX.Core;
using SysBot.Base;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public class QueueMonitor<T>(PokeTradeHub<T> Hub)
    where T : PKM, new()
{
    // Action to be called when queue status changes: (isFull, currentCount, maxCount)
    public static Action<bool, int, int>? OnQueueStatusChanged { get; set; }
    public async Task MonitorOpenQueue(CancellationToken token)
    {
        var queues = Hub.Queues.Info;
        var settings = Hub.Config.Queues;
        float secWaited = 0;

        const int sleepDelay = 0_500;
        const float sleepSeconds = sleepDelay / 1000f;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(sleepDelay, token).ConfigureAwait(false);
            var mode = settings.QueueToggleMode;
            if (!UpdateCanQueue(mode, settings, queues, secWaited))
            {
                secWaited += sleepSeconds;
                continue;
            }

            // Queue setting has been updated. Echo out that things have changed.
            secWaited = 0;
            var state = queues.GetCanQueue()
                ? "Users are now able to join the trade queue."
                : "Changed queue settings: **Users CANNOT join the queue until it is turned back on.**";
            EchoUtil.Echo(state);
        }
    }

    private static bool CheckInterval(QueueSettings settings, TradeQueueInfo<T> queues, float secWaited)
    {
        if (settings.CanQueue)
        {
            if (secWaited >= settings.IntervalOpenFor)
                queues.ToggleQueue();
            else
                return false;
        }
        else
        {
            if (secWaited >= settings.IntervalCloseFor)
                queues.ToggleQueue();
            else
                return false;
        }

        return true;
    }

    private bool CheckThreshold(QueueSettings settings, TradeQueueInfo<T> queues)
    {
        if (settings.CanQueue)
        {
            if (queues.Count >= settings.ThresholdLock)
            {
                queues.ToggleQueue();
                // Notify that queue is now full/closed
                if (settings.NotifyOnQueueClose)
                    OnQueueStatusChanged?.Invoke(true, queues.Count, settings.MaxQueueCount);
            }
            else
                return false;
        }
        else
        {
            if (queues.Count <= settings.ThresholdUnlock)
            {
                queues.ToggleQueue();
                // Notify that queue is now open
                if (settings.NotifyOnQueueClose)
                    OnQueueStatusChanged?.Invoke(false, queues.Count, settings.MaxQueueCount);
            }
            else
                return false;
        }

        return true;
    }

    private bool UpdateCanQueue(QueueOpening mode, QueueSettings settings, TradeQueueInfo<T> queues, float secWaited)
    {
        return mode switch
        {
            QueueOpening.Threshold => CheckThreshold(settings, queues),
            QueueOpening.Interval => CheckInterval(settings, queues, secWaited),
            _ => false,
        };
    }
}
