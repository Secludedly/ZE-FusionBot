using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Base;

/// <summary>
/// Enhanced BotRunner that supports automatic recovery of crashed bots.
/// </summary>
public class RecoverableBotRunner<T> : BotRunner<T> where T : class, IConsoleBotConfig
{
    private BotRecoveryService<T>? _recoveryService;
    private RecoveryConfiguration? _recoveryConfig;

    /// <summary>
    /// Gets the recovery service for this runner.
    /// </summary>
    public BotRecoveryService<T>? RecoveryService => _recoveryService;

    /// <summary>
    /// Initializes the recovery service with the given configuration.
    /// </summary>
    public void InitializeRecovery(RecoveryConfiguration config)
    {
        if (_recoveryService != null)
        {
            _recoveryService.Dispose();
        }

        _recoveryConfig = config;
        _recoveryService = new BotRecoveryService<T>(this, config);
        
        // Subscribe to recovery events for logging
        _recoveryService.BotCrashed += OnBotCrashed;
        _recoveryService.RecoveryAttempted += OnRecoveryAttempted;
        _recoveryService.RecoverySucceeded += OnRecoverySucceeded;
        _recoveryService.RecoveryFailed += OnRecoveryFailed;
        
        LogUtil.LogInfo("Bot recovery service initialized", "Recovery");
    }

    /// <summary>
    /// Adds a bot with recovery support.
    /// </summary>
    public override void Add(RoutineExecutor<T> bot)
    {
        if (Bots.Any(z => z.Bot.Connection.Equals(bot.Connection)))
            throw new ArgumentException($"{nameof(bot.Connection)} has already been added.");
        
        // Create a recoverable bot source if recovery is enabled
        if (_recoveryService != null)
        {
            var recoverableBot = new RecoverableBotSource<T>(bot, _recoveryService);
            Bots.Add(recoverableBot);
        }
        else
        {
            // Fallback to regular bot source
            Bots.Add(new BotSource<T>(bot));
        }
    }

    /// <summary>
    /// Converts existing bots to recoverable if recovery is enabled later.
    /// </summary>
    public void ConvertToRecoverable()
    {
        if (_recoveryService == null)
            return;

        var regularBots = Bots.Where(b => !(b is RecoverableBotSource<T>)).ToList();
        
        foreach (var bot in regularBots)
        {
            var index = Bots.IndexOf(bot);
            var recoverableBot = new RecoverableBotSource<T>(bot.Bot, _recoveryService);
            
            // Preserve state
            if (bot.IsRunning)
            {
                bot.Stop();
                Bots[index] = recoverableBot;
                recoverableBot.Start();
            }
            else
            {
                Bots[index] = recoverableBot;
            }
        }
    }

    public override void StopAll()
    {
        // Mark all bots as intentionally stopped before stopping
        foreach (var bot in Bots)
        {
            if (bot is RecoverableBotSource<T> recoverableBot)
            {
                _recoveryService?.MarkIntentionallyStopped(recoverableBot.Bot.Connection.Name);
            }
        }
        
        base.StopAll();
    }

    /// <summary>
    /// Disposes of the recovery service.
    /// </summary>
    public void DisposeRecovery()
    {
        _recoveryService?.Dispose();
        _recoveryService = null;
    }

    // Event handlers for recovery events
    private void OnBotCrashed(object? sender, BotCrashEventArgs e)
    {
        LogUtil.LogError($"Bot {e.BotName} crashed at {e.CrashTime:yyyy-MM-dd HH:mm:ss}", "Recovery");
    }

    private void OnRecoveryAttempted(object? sender, BotRecoveryEventArgs e)
    {
        LogUtil.LogInfo($"Attempting recovery for {e.BotName} (attempt {e.AttemptNumber})", "Recovery");
    }

    private void OnRecoverySucceeded(object? sender, BotRecoveryEventArgs e)
    {
        LogUtil.LogInfo($"Successfully recovered {e.BotName} after {e.AttemptNumber} attempt(s)", "Recovery");
    }

    private void OnRecoveryFailed(object? sender, BotRecoveryEventArgs e)
    {
        LogUtil.LogError($"Failed to recover {e.BotName} after {e.AttemptNumber} attempts. Reason: {e.FailureReason}", "Recovery");
    }

    /// <summary>
    /// Gets recovery statistics for all bots.
    /// </summary>
    public Dictionary<string, BotRecoveryState?> GetRecoveryStatistics()
    {
        var stats = new Dictionary<string, BotRecoveryState?>();
        
        foreach (var bot in Bots)
        {
            var state = bot.GetRecoveryState();
            stats[bot.Bot.Connection.Name] = state;
        }
        
        return stats;
    }

    /// <summary>
    /// Resets recovery state for a specific bot.
    /// </summary>
    public void ResetBotRecovery(string botName)
    {
        _recoveryService?.ResetRecoveryState(botName);
    }

    /// <summary>
    /// Temporarily disables recovery for maintenance or debugging.
    /// </summary>
    public void DisableRecovery()
    {
        if (_recoveryConfig != null)
        {
            _recoveryConfig.EnableRecovery = false;
        }
    }

    /// <summary>
    /// Re-enables recovery after maintenance.
    /// </summary>
    public void EnableRecovery()
    {
        if (_recoveryConfig != null)
        {
            _recoveryConfig.EnableRecovery = true;
        }
    }
    
    /// <summary>
    /// Gets the recovery service instance.
    /// </summary>
    public BotRecoveryService<T>? GetRecoveryService() => _recoveryService;
}