namespace SysBot.Base;

/// <summary>
/// Standardized log identity constants to ensure consistent folder structure.
/// Only use these constants for logging - no arbitrary strings!
/// </summary>
public static class LogIdentity
{
    /// <summary>
    /// System-level operations: startup, shutdown, recovery, configuration, etc.
    /// All system logs go to logs/System/
    /// </summary>
    public const string System = "System";

    /// <summary>
    /// For bot-specific operations, use the bot's Connection.Label (IP or USB identifier)
    /// Examples: "192.168.0.106", "USB-1"
    /// These create per-bot folders: logs/192.168.0.106/
    /// </summary>
    public static string Bot(string connectionLabel) => connectionLabel;
}
