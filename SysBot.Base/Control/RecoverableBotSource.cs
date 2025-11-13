using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base;

/// <summary>
/// Enhanced BotSource that integrates with the recovery service for automatic crash recovery.
/// </summary>
public class RecoverableBotSource<T> : BotSource<T>, IDisposable where T : class, IConsoleBotConfig
{
    private readonly BotRecoveryService<T>? _recoveryService;
    private volatile bool _isIntentionallyStopped;
    private DateTime _lastCrashTime;
    private Exception? _lastCrashException;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private bool _disposed;

    public RecoverableBotSource(RoutineExecutor<T> bot, BotRecoveryService<T>? recoveryService = null) : base(bot)
    {
        _recoveryService = recoveryService;
        _recoveryService?.RegisterBot(this);
    }

    /// <summary>
    /// Gets whether the bot crashed (stopped unintentionally).
    /// </summary>
    public bool HasCrashed => !IsRunning && !_isIntentionallyStopped && _lastCrashException != null;

    /// <summary>
    /// Gets the last crash exception if any.
    /// </summary>
    public Exception? LastCrashException => _lastCrashException;

    /// <summary>
    /// Gets the time of the last crash.
    /// </summary>
    public DateTime LastCrashTime => _lastCrashTime;

    public new void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RecoverableBotSource<T>));
        
        _isIntentionallyStopped = false;
        _lastCrashException = null;
        
        // Cancel any existing monitoring
        StopMonitoring();
        
        // Clear the intentionally stopped flag in recovery service
        _recoveryService?.ClearIntentionallyStopped(Bot.Connection.Name);
        
        base.Start();
        
        // Start monitoring bot execution
        StartMonitoring();
    }

    public new void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RecoverableBotSource<T>));
        
        _isIntentionallyStopped = true;
        
        // Mark as intentionally stopped in recovery service
        _recoveryService?.MarkIntentionallyStopped(Bot.Connection.Name);
        
        StopMonitoring();
        base.Stop();
    }

    public new void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RecoverableBotSource<T>));
        
        _isIntentionallyStopped = true;
        _recoveryService?.MarkIntentionallyStopped(Bot.Connection.Name);
        
        base.Pause();
    }

    public new void RebootAndStop()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RecoverableBotSource<T>));
        
        _isIntentionallyStopped = true;
        _recoveryService?.MarkIntentionallyStopped(Bot.Connection.Name);
        
        StopMonitoring();
        base.RebootAndStop();
    }

    private void StartMonitoring()
    {
        _monitoringCts = new CancellationTokenSource();
        _monitoringTask = MonitorBotExecutionAsync(_monitoringCts.Token);
    }
    
    private void StopMonitoring()
    {
        _monitoringCts?.Cancel();
        try
        {
            _monitoringTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Task was cancelled, this is expected
        }
        finally
        {
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
        }
    }
    
    private async Task MonitorBotExecutionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait a bit to ensure the bot has actually started
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            
            while (IsRunning && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            
            // Bot has stopped, check if it was intentional
            if (!_isIntentionallyStopped && !IsStopping && !cancellationToken.IsCancellationRequested)
            {
                _lastCrashTime = DateTime.UtcNow;
                LogUtil.LogError($"Bot {Bot.Connection.Name} has crashed unexpectedly!", "Recovery");
                
                // The recovery service will handle the restart if enabled
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when monitoring is stopped
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error in bot monitoring for {Bot.Connection.Name}: {ex.Message}", "Recovery");
        }
    }

    /// <summary>
    /// Reports a crash with the given exception.
    /// </summary>
    internal void ReportCrash(Exception exception)
    {
        _lastCrashException = exception;
        _lastCrashTime = DateTime.UtcNow;
        _isIntentionallyStopped = false;
    }

    /// <summary>
    /// Gets the recovery state from the recovery service.
    /// </summary>
    public BotRecoveryState? GetRecoveryState()
    {
        return _recoveryService?.GetRecoveryState(Bot.Connection.Name);
    }

    /// <summary>
    /// Resets the recovery state for this bot.
    /// </summary>
    public void ResetRecoveryState()
    {
        _recoveryService?.ResetRecoveryState(Bot.Connection.Name);
        _lastCrashException = null;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
            
        if (disposing)
        {
            StopMonitoring();
        }
        
        _disposed = true;
    }
}

/// <summary>
/// Extension methods for BotSource to support recovery features.
/// </summary>
public static class BotSourceExtensions
{
    /// <summary>
    /// Converts a regular BotSource to a RecoverableBotSource.
    /// </summary>
    public static RecoverableBotSource<T> ToRecoverable<T>(this BotSource<T> source, BotRecoveryService<T>? recoveryService = null) 
        where T : class, IConsoleBotConfig
    {
        return new RecoverableBotSource<T>(source.Bot, recoveryService);
    }
    
    /// <summary>
    /// Checks if the bot source is recoverable.
    /// </summary>
    public static bool IsRecoverable<T>(this BotSource<T> source) where T : class, IConsoleBotConfig
    {
        return source is RecoverableBotSource<T>;
    }
    
    /// <summary>
    /// Gets the recovery state if this is a recoverable bot source.
    /// </summary>
    public static BotRecoveryState? GetRecoveryState<T>(this BotSource<T> source) where T : class, IConsoleBotConfig
    {
        return (source as RecoverableBotSource<T>)?.GetRecoveryState();
    }
}