using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon.WinForms.WebApi.Models;

/// <summary>
/// Base class for all API responses with common properties
/// </summary>
public abstract class ApiResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
    
    public bool Success => string.IsNullOrEmpty(Error);
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Generic response wrapper for simple operations
/// </summary>
public class SimpleResponse : ApiResponse
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Instance information with all bot details
/// </summary>
public class BotInstance
{
    public int ProcessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public int WebPort { get; set; }
    public string Version { get; set; } = string.Empty;
    public int BotCount { get; set; }
    public string Mode { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsMaster { get; set; }
    public string? ProcessPath { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BotStatusInfo>? BotStatuses { get; set; }
}

/// <summary>
/// Individual bot status within an instance
/// </summary>
public class BotStatusInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Detailed bot information
/// </summary>
public class BotInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RoutineType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
}

/// <summary>
/// Request for bot commands
/// </summary>
public class BotCommandRequest
{
    public string Command { get; set; } = string.Empty;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BotId { get; set; }
}

/// <summary>
/// Response for individual command execution
/// </summary>
public class CommandResponse : ApiResponse
{
    public string Message { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Command { get; set; } = string.Empty;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceName { get; set; }
}

/// <summary>
/// Response for batch command execution
/// </summary>
public class BatchCommandResponse : ApiResponse
{
    public List<CommandResponse> Results { get; set; } = [];
    public int TotalInstances { get; set; }
    public int SuccessfulCommands { get; set; }
}

/// <summary>
/// Response for instance listing
/// </summary>
public class InstancesResponse : ApiResponse
{
    public List<BotInstance> Instances { get; set; } = [];
}

/// <summary>
/// Response for bot listing
/// </summary>
public class BotsResponse : ApiResponse
{
    public List<BotInfo> Bots { get; set; } = [];
}

/// <summary>
/// Response for idle status check
/// </summary>
public class IdleStatusResponse : ApiResponse
{
    public List<InstanceIdleInfo> Instances { get; set; } = [];
    public int TotalBots { get; set; }
    public int TotalIdleBots { get; set; }
    public bool AllBotsIdle { get; set; }
}

/// <summary>
/// Instance idle information
/// </summary>
public class InstanceIdleInfo
{
    public int Port { get; set; }
    public int ProcessId { get; set; }
    public int TotalBots { get; set; }
    public int IdleBots { get; set; }
    public List<NonIdleBot> NonIdleBots { get; set; } = [];
    public bool AllIdle { get; set; }
}

/// <summary>
/// Non-idle bot information
/// </summary>
public class NonIdleBot
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Update check response
/// </summary>
public class UpdateCheckResponse : ApiResponse
{
    public string Version { get; set; } = "Unknown";
    public string Changelog { get; set; } = string.Empty;
    public bool Available { get; set; }
}

/// <summary>
/// Factory for creating error responses
/// </summary>
public static class ApiResponseFactory
{
    public static T CreateError<T>(string message) where T : ApiResponse, new()
    {
        return new T { Error = message };
    }
    
    public static SimpleResponse CreateSimpleError(string message)
    {
        return new SimpleResponse { Error = message, Message = message };
    }
    
    public static SimpleResponse CreateSimpleSuccess(string message)
    {
        return new SimpleResponse { Message = message };
    }
}