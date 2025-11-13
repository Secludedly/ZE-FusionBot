using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base;

/// <summary>
/// Service that monitors bot health and automatically attempts to recover crashed bots.
/// </summary>
public sealed class BotRecoveryService<T> : IDisposable where T : class, IConsoleBotConfig
{
    private readonly BotRunner<T> _runner;
    private readonly RecoveryConfiguration _config;
    private readonly ConcurrentDictionary<string, BotRecoveryState> _recoveryStates = new();
    private readonly PeriodicTimer _periodicTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private readonly Task _monitorTask;
    private bool _isDisposed;

    public event EventHandler<BotRecoveryEventArgs>? RecoveryAttempted;
    public event EventHandler<BotRecoveryEventArgs>? RecoverySucceeded;
    public event EventHandler<BotRecoveryEventArgs>? RecoveryFailed;
    public event EventHandler<BotCrashEventArgs>? BotCrashed;

    public BotRecoveryService(BotRunner<T> runner, RecoveryConfiguration config)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ValidateConfiguration(_config);
        
        // Use PeriodicTimer for better async support
        _periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _monitorTask = MonitorBotsAsync(_cancellationTokenSource.Token);
    }

    private static void ValidateConfiguration(RecoveryConfiguration config)
    {
        if (config.MaxRecoveryAttempts < 1)
            throw new ArgumentException("MaxRecoveryAttempts must be at least 1", nameof(config));
        if (config.InitialRecoveryDelaySeconds < 0)
            throw new ArgumentException("InitialRecoveryDelaySeconds cannot be negative", nameof(config));
        if (config.BackoffMultiplier < 1.0)
            throw new ArgumentException("BackoffMultiplier must be at least 1.0", nameof(config));
    }

    /// <summary>
    /// Registers a bot for crash monitoring and recovery.
    /// </summary>
    public void RegisterBot(BotSource<T> bot)
    {
        var state = new BotRecoveryState
        {
            BotName = bot.Bot.Connection.Name,
            LastStartTime = DateTime.UtcNow,
            IsIntentionallyStopped = false
        };
        
        _recoveryStates.AddOrUpdate(bot.Bot.Connection.Name, state, (_, __) => state);
    }

    /// <summary>
    /// Marks a bot as intentionally stopped to prevent recovery.
    /// </summary>
    public void MarkIntentionallyStopped(string botName)
    {
        if (_recoveryStates.TryGetValue(botName, out var state))
        {
            state.IsIntentionallyStopped = true;
        }
    }

    /// <summary>
    /// Clears the intentionally stopped flag for a bot.
    /// </summary>
    public void ClearIntentionallyStopped(string botName)
    {
        if (_recoveryStates.TryGetValue(botName, out var state))
        {
            state.IsIntentionallyStopped = false;
        }
    }

    private async Task MonitorBotsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                
                if (!_config.EnableRecovery || _isDisposed)
                    continue;

                await MonitorAndRecoverBots(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error in bot recovery monitor: {ex.Message}", "Recovery");
            }
        }
    }

    private async Task MonitorAndRecoverBots(CancellationToken cancellationToken)
    {
        var botsToRecover = new List<(BotSource<T> bot, BotRecoveryState state)>();

        await _recoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var bot in _runner.Bots)
            {
                var botName = bot.Bot.Connection.Name;
                if (!_recoveryStates.TryGetValue(botName, out var state))
                    continue;

                // Check if bot has crashed or stopped
                if (!bot.IsRunning && !bot.IsStopping && !state.IsRecovering)
                {
                    // Check if we should attempt recovery
                    if (ShouldAttemptRecovery(bot, state))
                    {
                        botsToRecover.Add((bot, state));
                        state.IsRecovering = true;
                    }
                }
                else if (bot.IsRunning && state.ConsecutiveFailures > 0)
                {
                    // Bot is running, check if it's been stable long enough to reset attempts
                    var uptime = DateTime.UtcNow - state.LastStartTime;
                    if (uptime.TotalSeconds >= _config.MinimumStableUptimeSeconds)
                    {
                        state.ConsecutiveFailures = 0;
                        LogUtil.LogInfo("Recovery", $"Bot {botName} has been stable for {uptime.TotalMinutes:F1} minutes. Resetting recovery attempts.");
                    }
                }
            }
        }
        finally
        {
            _recoveryLock.Release();
        }

        // Attempt recovery for crashed bots
        foreach (var (bot, state) in botsToRecover)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            await AttemptRecovery(bot, state, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ShouldAttemptRecovery(BotSource<T> bot, BotRecoveryState state)
    {
        var botName = bot.Bot.Connection.Name;

        // Don't recover if intentionally stopped and config doesn't allow it
        if (state.IsIntentionallyStopped && !_config.RecoverIntentionalStops)
        {
            return false;
        }

        // Check if we've exceeded max attempts
        if (state.ConsecutiveFailures >= _config.MaxRecoveryAttempts)
        {
            LogUtil.LogError($"Bot {botName} has exceeded maximum recovery attempts ({_config.MaxRecoveryAttempts})", "Recovery");
            return false;
        }

        // Clean up old crash history
        state.RemoveOldCrashes(crash => 
            (DateTime.UtcNow - crash).TotalMinutes > _config.CrashHistoryWindowMinutes);

        // Check crash frequency
        if (state.CrashHistory.Count >= _config.MaxCrashesInWindow)
        {
            LogUtil.LogError($"Bot {botName} has crashed {state.CrashHistory.Count} times in the last {_config.CrashHistoryWindowMinutes} minutes. Disabling recovery.", "Recovery");
            return false;
        }

        // Check cooldown period
        if (state.LastRecoveryAttempt.HasValue)
        {
            var timeSinceLastAttempt = DateTime.UtcNow - state.LastRecoveryAttempt.Value;
            var requiredDelay = CalculateBackoffDelay(state.ConsecutiveFailures);
            
            if (timeSinceLastAttempt.TotalSeconds < requiredDelay)
            {
                return false;
            }
        }

        return true;
    }

    private double CalculateBackoffDelay(int attemptNumber)
    {
        var delay = _config.InitialRecoveryDelaySeconds * Math.Pow(_config.BackoffMultiplier, attemptNumber);
        return Math.Min(delay, _config.MaxRecoveryDelaySeconds);
    }

    private async Task AttemptRecovery(BotSource<T> bot, BotRecoveryState state, CancellationToken cancellationToken)
    {
        var botName = bot.Bot.Connection.Name;
        state.LastRecoveryAttempt = DateTime.UtcNow;
        state.ConsecutiveFailures++;
        state.AddCrashTime(DateTime.UtcNow);

        try
        {
            // Notify about crash
            BotCrashed?.Invoke(this, new BotCrashEventArgs 
            { 
                BotName = botName, 
                CrashTime = DateTime.UtcNow,
                AttemptNumber = state.ConsecutiveFailures 
            });

            LogUtil.LogInfo($"Attempting recovery for bot {botName} (attempt {state.ConsecutiveFailures}/{_config.MaxRecoveryAttempts})", "Recovery");

            if (_config.NotifyOnRecoveryAttempt)
            {
                RecoveryAttempted?.Invoke(this, new BotRecoveryEventArgs 
                { 
                    BotName = botName, 
                    AttemptNumber = state.ConsecutiveFailures,
                    IsSuccess = false 
                });
            }

            // Wait for backoff delay
            var delay = CalculateBackoffDelay(state.ConsecutiveFailures - 1);
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);

            // Attempt to restart the bot
            await Task.Run(() =>
            {
                try
                {
                    bot.Start();
                    state.LastStartTime = DateTime.UtcNow;
                    state.IsIntentionallyStopped = false;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to start bot {botName}: {ex.Message}", "Recovery");
                    throw;
                }
            }).ConfigureAwait(false);

            // Wait a bit to see if the bot stays running
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

            if (bot.IsRunning)
            {
                LogUtil.LogInfo($"Successfully recovered bot {botName}", "Recovery");
                RecoverySucceeded?.Invoke(this, new BotRecoveryEventArgs 
                { 
                    BotName = botName, 
                    AttemptNumber = state.ConsecutiveFailures,
                    IsSuccess = true 
                });
            }
            else
            {
                throw new Exception("Bot stopped immediately after restart");
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Recovery failed for bot {botName}: {ex.Message}", "Recovery");
            
            if (state.ConsecutiveFailures >= _config.MaxRecoveryAttempts && _config.NotifyOnRecoveryFailure)
            {
                RecoveryFailed?.Invoke(this, new BotRecoveryEventArgs 
                { 
                    BotName = botName, 
                    AttemptNumber = state.ConsecutiveFailures,
                    IsSuccess = false,
                    FailureReason = ex.Message
                });
            }
        }
        finally
        {
            state.IsRecovering = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        
        // Cancel monitoring task
        _cancellationTokenSource.Cancel();
        
        try
        {
            // Wait for monitor task to complete
            _monitorTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Task was cancelled, this is expected
        }
        
        _periodicTimer.Dispose();
        _cancellationTokenSource.Dispose();
        _recoveryLock.Dispose();
    }

    /// <summary>
    /// Gets the current recovery state for a bot.
    /// </summary>
    public BotRecoveryState? GetRecoveryState(string botName)
    {
        return _recoveryStates.TryGetValue(botName, out var state) ? state : null;
    }

    /// <summary>
    /// Resets the recovery state for a bot.
    /// </summary>
    public void ResetRecoveryState(string botName)
    {
        if (_recoveryStates.TryGetValue(botName, out var state))
        {
            state.ConsecutiveFailures = 0;
            state.ClearCrashHistory();
            state.LastRecoveryAttempt = null;
            state.IsRecovering = false;
        }
    }
    
    /// <summary>
    /// Enables the recovery service.
    /// </summary>
    public void EnableRecovery()
    {
        _config.EnableRecovery = true;
        LogUtil.LogInfo("Bot recovery service enabled", "Recovery");
    }
    
    /// <summary>
    /// Disables the recovery service.
    /// </summary>
    public void DisableRecovery()
    {
        _config.EnableRecovery = false;
        LogUtil.LogInfo("Bot recovery service disabled", "Recovery");
    }
}

/// <summary>
/// Configuration for the recovery service (simplified version of RecoverySettings).
/// </summary>
public class RecoveryConfiguration
{
    public bool EnableRecovery { get; set; } = true;
    public int MaxRecoveryAttempts { get; set; } = 3;
    public int InitialRecoveryDelaySeconds { get; set; } = 5;
    public int MaxRecoveryDelaySeconds { get; set; } = 300;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int CrashHistoryWindowMinutes { get; set; } = 60;
    public int MaxCrashesInWindow { get; set; } = 5;
    public bool RecoverIntentionalStops { get; set; } = false;
    public int MinimumStableUptimeSeconds { get; set; } = 600;
    public bool NotifyOnRecoveryAttempt { get; set; } = true;
    public bool NotifyOnRecoveryFailure { get; set; } = true;
}

/// <summary>
/// Tracks the recovery state of an individual bot.
/// </summary>
public class BotRecoveryState
{
    private readonly ConcurrentBag<DateTime> _crashHistory = new();
    private int _consecutiveFailures;
    private bool _isRecovering;
    
    public string BotName { get; init; } = string.Empty;
    
    public int ConsecutiveFailures 
    { 
        get => _consecutiveFailures;
        set => Interlocked.Exchange(ref _consecutiveFailures, value);
    }
    
    public IReadOnlyCollection<DateTime> CrashHistory => _crashHistory;
    public DateTime? LastRecoveryAttempt { get; set; }
    public DateTime LastStartTime { get; set; }
    public bool IsIntentionallyStopped { get; set; }
    
    public bool IsRecovering 
    { 
        get => _isRecovering;
        set => _isRecovering = value;
    }
    
    public void AddCrashTime(DateTime crashTime)
    {
        _crashHistory.Add(crashTime);
    }
    
    public void ClearCrashHistory()
    {
        _crashHistory.Clear();
    }
    
    public int RemoveOldCrashes(Func<DateTime, bool> predicate)
    {
        var current = _crashHistory.ToList();
        var toKeep = current.Where(crash => !predicate(crash)).ToList();
        
        if (current.Count == toKeep.Count)
            return 0;
            
        _crashHistory.Clear();
        foreach (var crash in toKeep)
            _crashHistory.Add(crash);
            
        return current.Count - toKeep.Count;
    }
}

/// <summary>
/// Event arguments for bot recovery events.
/// </summary>
public class BotRecoveryEventArgs : EventArgs
{
    public string BotName { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public bool IsSuccess { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Event arguments for bot crash events.
/// </summary>
public class BotCrashEventArgs : EventArgs
{
    public string BotName { get; set; } = string.Empty;
    public DateTime CrashTime { get; set; }
    public int AttemptNumber { get; set; }
}