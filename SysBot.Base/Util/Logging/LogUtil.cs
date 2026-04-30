using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SysBot.Base;

/// <summary>
/// Logic wrapper to handle logging (via NLog).
/// Supports both master log (all bots) and per-bot log files for better organization.
/// </summary>
public static class LogUtil
{
    // hook in here if you want to forward the message elsewhere
    public static readonly List<ILogForwarder> Forwarders = [];

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Cache of per-bot loggers to avoid recreating them
    private static readonly ConcurrentDictionary<string, Logger> BotLoggers = new();

    // Buffer for early bot logs before trainer identification
    // Key: Connection IP/USB identifier, Value: List of buffered log entries
    private static readonly ConcurrentDictionary<string, List<BufferedLogEntry>> LogBuffer = new();

    private static readonly string WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;

    private record BufferedLogEntry(LogLevel Level, string Message, DateTime Timestamp);

    static LogUtil()
    {
        if (!LogConfig.LoggingEnabled)
            return;

        var config = new LoggingConfiguration();
        Directory.CreateDirectory("logs");

        // Master log file (all bots combined) - only if enabled
        if (LogConfig.EnableMasterLog)
        {
            var masterLogFile = new FileTarget("masterlog")
            {
                FileName = Path.Combine(WorkingDirectory, "logs", "SysBotLog.txt"),
                ConcurrentWrites = true,

                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Date,
                ArchiveFileName = Path.Combine(WorkingDirectory, "logs", "SysBotLog.{#}.txt"),
                ArchiveDateFormat = "yyyy-MM-dd",
                ArchiveAboveSize = LogConfig.MaxLogFileSize,
                MaxArchiveFiles = LogConfig.MaxArchiveFiles,
                Encoding = Encoding.Unicode,
                WriteBom = true,
                Layout = "${date:format=MM-dd-yyyy h\\:mm\\:ss.ffff tt}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, masterLogFile);
        }

        LogManager.Configuration = config;
    }

    public static DateTime LastLogged { get; private set; } = DateTime.Now;

    /// <summary>
    /// Gets or creates a per-bot logger for the specified bot identity
    /// </summary>
    /// <param name="identity">Bot identifier (e.g., "USB-1", "192.168.1.100")</param>
    /// <returns>Logger instance for the bot</returns>
    private static Logger GetOrCreateBotLogger(string identity)
    {
        if (!LogConfig.EnablePerBotLogging || !LogConfig.LoggingEnabled)
            return Logger;

        return BotLoggers.GetOrAdd(identity, botName =>
        {
            // Sanitize bot name for file system
            var safeBotName = SanitizeBotName(botName);
            var botLogDir = Path.Combine(WorkingDirectory, "logs", safeBotName);
            Directory.CreateDirectory(botLogDir);

            // Create a unique logger name to avoid conflicts
            var loggerName = $"BotLogger_{safeBotName}";
            var botLogger = LogManager.GetLogger(loggerName);

            // Configure per-bot log target
            var config = LogManager.Configuration ?? new LoggingConfiguration();

            var fileName = LogConfig.IncludeTimestampInFilename
                ? $"SysBotLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt"
                : "SysBotLog.txt";

            var botLogTarget = new FileTarget($"botlog_{safeBotName}")
            {
                FileName = Path.Combine(botLogDir, fileName),
                ConcurrentWrites = true,

                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Date,
                ArchiveFileName = Path.Combine(botLogDir, "SysBotLog.{#}.txt"),
                ArchiveDateFormat = "yyyy-MM-dd",
                ArchiveAboveSize = LogConfig.MaxLogFileSize,
                MaxArchiveFiles = LogConfig.MaxArchiveFiles,
                Encoding = Encoding.Unicode,
                WriteBom = true,
                Layout = "${date:format=MM-dd-yyyy h\\:mm\\:ss.ffff tt}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            config.AddTarget(botLogTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, botLogTarget, loggerName);

            LogManager.Configuration = config;

            return botLogger;
        });
    }

    /// <summary>
    /// Sanitizes bot name for use in file paths
    /// Creates folders like: logs/HeXbyt3-483256/, logs/A-Z-734959/, logs/System/
    /// </summary>
    private static string SanitizeBotName(string botName)
    {
        if (string.IsNullOrWhiteSpace(botName))
            return "UnknownBot";

        // Check if this is a system component and should be consolidated
        if (LogConfig.ConsolidateSystemLogs)
        {
            foreach (var systemIdentity in LogConfig.SystemIdentities)
            {
                if (botName.Equals(systemIdentity, StringComparison.OrdinalIgnoreCase) ||
                    botName.StartsWith(systemIdentity + " ", StringComparison.OrdinalIgnoreCase) ||
                    botName.StartsWith(systemIdentity + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return "System";
                }
            }
        }

        // Keep the full identifier (e.g., "HeXbyt3-483256", "USB-1")
        // Just sanitize invalid file system characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", botName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

        // Remove any trailing/leading whitespace or underscores
        sanitized = sanitized.Trim('_', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? "UnknownBot" : sanitized;
    }

    /// <summary>
    /// Checks if an identity is a trainer identifier (Name-XXXXXX format)
    /// </summary>
    private static bool IsTrainerIdentifier(string identity)
    {
        return identity.Contains('-') && System.Text.RegularExpressions.Regex.IsMatch(identity, @"-\d{6}$");
    }

    /// <summary>
    /// Checks if identity should skip per-bot logging (system-wide services)
    /// </summary>
    private static bool IsGlobalIdentity(string identity)
    {
        return LogConfig.SystemIdentities.Any(prefix => identity.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                                                         identity.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Flushes buffered logs from early identifier (IP/USB) to trainer folder
    /// </summary>
    public static void FlushBufferedLogs(string earlyIdentifier, string trainerIdentifier)
    {
        if (LogBuffer.TryRemove(earlyIdentifier, out var bufferedLogs))
        {
            var botLogger = GetOrCreateBotLogger(trainerIdentifier);
            foreach (var entry in bufferedLogs)
            {
                botLogger.Log(entry.Level, entry.Message);
            }
        }
    }

    public static void LogError(string identity, string message)
    {
        // Log to master log
        if (LogConfig.EnableMasterLog)
            Logger.Log(LogLevel.Error, $"{identity} {message}");

        // Handle per-bot logging
        if (LogConfig.EnablePerBotLogging && !IsGlobalIdentity(identity))
        {
            if (IsTrainerIdentifier(identity))
            {
                // Identified bot - log directly to trainer folder
                var botLogger = GetOrCreateBotLogger(identity);
                botLogger.Log(LogLevel.Error, message);
            }
            else
            {
                // Early bot identifier (IP/USB) - buffer for later
                LogBuffer.GetOrAdd(identity, _ => new List<BufferedLogEntry>())
                    .Add(new BufferedLogEntry(LogLevel.Error, message, DateTime.Now));
            }
        }

        // Forward to external listeners (Discord, etc.)
        foreach (var fwd in Forwarders)
        {
            try
            {
                fwd.Forward(message, identity);
            }
            catch { }
        }
    }

    public static void LogInfo(string identity, string message)
    {
        // Log to master log
        if (LogConfig.EnableMasterLog)
            Logger.Log(LogLevel.Info, $"{identity} {message}");

        // Handle per-bot logging
        if (LogConfig.EnablePerBotLogging && !IsGlobalIdentity(identity))
        {
            if (IsTrainerIdentifier(identity))
            {
                // Identified bot - log directly to trainer folder
                var botLogger = GetOrCreateBotLogger(identity);
                botLogger.Log(LogLevel.Info, message);
            }
            else
            {
                // Early bot identifier (IP/USB) - buffer for later
                LogBuffer.GetOrAdd(identity, _ => new List<BufferedLogEntry>())
                    .Add(new BufferedLogEntry(LogLevel.Info, message, DateTime.Now));
            }
        }

        // Forward to external listeners (Discord, etc.)
        foreach (var fwd in Forwarders)
        {
            try
            {
                fwd.Forward(message, identity);
            }
            catch { }
        }
    }

    public static void LogGeneric(string message, string identity)
    {
        Logger.Log(NLog.LogLevel.Info, $"{identity} {message}");
        Log(message, identity);
    }

    public static void LogSuspicious(string identity, string message)
    {
        // Log to master log
        if (LogConfig.EnableMasterLog)
            Logger.Log(LogLevel.Warn, $"[SECURITY] {identity} {message}");

        // Log to per-bot log
        if (LogConfig.EnablePerBotLogging)
        {
            var botLogger = GetOrCreateBotLogger(identity);
            botLogger.Log(LogLevel.Warn, $"[SECURITY] {message}");
        }

        // Forward to external listeners (Discord, etc.)
        foreach (var fwd in Forwarders)
        {
            try
            {
                fwd.Forward($"[SECURITY] {message}", identity);
            }
            catch { }
        }
    }

    public static void LogSafe(Exception exception, string identity)
    {
        // Log to master log
        if (LogConfig.EnableMasterLog)
        {
            Logger.Log(LogLevel.Error, $"Exception from {identity}:");
            Logger.Log(LogLevel.Error, exception);
        }

        // Log to per-bot log
        if (LogConfig.EnablePerBotLogging)
        {
            var botLogger = GetOrCreateBotLogger(identity);
            botLogger.Log(LogLevel.Error, "Exception occurred:");
            botLogger.Log(LogLevel.Error, exception);
        }

        var err = exception.InnerException;
        while (err is not null)
        {
            if (LogConfig.EnableMasterLog)
                Logger.Log(LogLevel.Error, err);

            if (LogConfig.EnablePerBotLogging)
            {
                var botLogger = GetOrCreateBotLogger(identity);
                botLogger.Log(LogLevel.Error, err);
            }

            err = err.InnerException;
        }
    }

    public static void LogText(string message) => Logger.Log(LogLevel.Info, message);

    /// <summary>
    /// Clears the per-bot logger cache for a specific bot (useful when a bot disconnects)
    /// </summary>
    public static void ClearBotLogger(string identity)
    {
        BotLoggers.TryRemove(identity, out _);
    }

    /// <summary>
    /// Gets the log file path for a specific bot
    /// </summary>
    public static string GetBotLogPath(string identity)
    {
        var safeBotName = SanitizeBotName(identity);
        var botLogDir = Path.Combine(WorkingDirectory, "logs", safeBotName);
        var fileName = LogConfig.IncludeTimestampInFilename
            ? $"SysBotLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt"
            : "SysBotLog.txt";
        return Path.Combine(botLogDir, fileName);
    }

    private static void Log(string message, string identity)
    {
        foreach (var fwd in Forwarders)
        {
            try
            {
                fwd.Forward(message, identity);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Failed to forward log from {identity} - {message}");
                Logger.Log(LogLevel.Error, ex);
            }
        }

        LastLogged = DateTime.Now;
    }
}
