using System.ComponentModel;

namespace SysBot.Pokemon;

/// <summary>
/// Configuration settings for automatic bot recovery after crashes or cancellation token stops.
/// </summary>
public class RecoverySettings
{
    private const string Recovery = nameof(Recovery);

    [Category(Recovery), Description("Enables automatic recovery attempts for crashed or stopped bots.")]
    public bool EnableRecovery { get; set; } = true;

    [Category(Recovery), Description("Maximum number of consecutive recovery attempts before giving up on a bot.")]
    public int MaxRecoveryAttempts { get; set; } = 3;

    [Category(Recovery), Description("Initial delay in seconds before attempting to restart a crashed bot.")]
    public int InitialRecoveryDelaySeconds { get; set; } = 5;

    [Category(Recovery), Description("Maximum delay in seconds between recovery attempts (for exponential backoff).")]
    public int MaxRecoveryDelaySeconds { get; set; } = 300; // 5 minutes

    [Category(Recovery), Description("Multiplier for exponential backoff (e.g., 2.0 doubles the delay each time).")]
    public double BackoffMultiplier { get; set; } = 2.0;

    [Category(Recovery), Description("Time window in minutes to track crash history. Crashes outside this window are not counted.")]
    public int CrashHistoryWindowMinutes { get; set; } = 60; // 1 hour

    [Category(Recovery), Description("Maximum number of crashes allowed within the history window before permanent shutdown.")]
    public int MaxCrashesInWindow { get; set; } = 5;

    [Category(Recovery), Description("Enables recovery for bots that were intentionally stopped (useful for network disconnections).")]
    public bool RecoverIntentionalStops { get; set; } = false;

    [Category(Recovery), Description("Delay in seconds to wait after a successful recovery before resetting the attempt counter.")]
    public int SuccessfulRecoveryResetDelaySeconds { get; set; } = 300; // 5 minutes

    [Category(Recovery), Description("Send notifications when a bot crashes and recovery is attempted.")]
    public bool NotifyOnRecoveryAttempt { get; set; } = true;

    [Category(Recovery), Description("Send notifications when a bot fails to recover after all attempts.")]
    public bool NotifyOnRecoveryFailure { get; set; } = true;

    [Category(Recovery), Description("Minimum uptime in seconds before a bot is considered stable (resets recovery attempts).")]
    public int MinimumStableUptimeSeconds { get; set; } = 600; // 10 minutes

    public override string ToString() => "Bot Recovery Settings";
}