using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Base;
using SysBot.Pokemon.Helpers;

namespace SysBot.Pokemon.WinForms.WebApi;

/// <summary>
/// Helper class for generated regex patterns
/// </summary>
internal static partial class UpdateManagerRegexHelper
{
    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9.-_]")]
    public static partial System.Text.RegularExpressions.Regex VersionSanitizer();
}

/// <summary>
/// Simplified update manager with clearer state transitions and better error recovery
/// </summary>
public static class UpdateManager
{
    private static readonly object _lock = new();
    private static UpdateState? _state;
    private static readonly string StateFilePath = Path.Combine(
        Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory,
        "update_state.json"
    );
    
    // Store the configured web port
    private static int _configuredWebPort = 8080;
    
    // Simplified configuration with reasonable defaults
    private static readonly UpdateConfig Config = new();
    
    // Cached JsonSerializerOptions for performance
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 10 // Prevent deeply nested JSON attacks
    };
    
    /// <summary>
    /// Configurable update settings
    /// </summary>
    public class UpdateConfig
    {
        public int StopBotsTimeoutMinutes { get; set; } = 5;
        public int ProcessTerminationTimeoutSeconds { get; set; } = 30;
        public int NewProcessStartTimeoutMinutes { get; set; } = 2;
        public int NetworkTimeoutMs { get; set; } = 30000;
        public int VersionCheckTimeoutMs { get; set; } = 10000;
        public int ProcessCheckDelayMs { get; set; } = 2000;
        public int MaxRetryCount { get; set; } = 3;
        public int MasterWebPort { get; set; } = 8080; // Will be updated from config
        public int MasterTcpPort { get; set; } = 8081;
    }

    /// <summary>
    /// Simplified update state with only essential fields
    /// </summary>
    public class UpdateState
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public string TargetVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public UpdatePhase Phase { get; set; } = UpdatePhase.Checking;
        public string Message { get; set; } = "Initializing...";
        public List<Instance> Instances { get; set; } = [];
        public bool IsComplete { get; set; }
        public bool Success { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public IdleProgress? IdleProgress { get; set; }
        public string? CurrentUpdatingInstance { get; set; }
        
        [JsonIgnore]
        public int TotalInstances => Instances.Count;
        
        [JsonIgnore]
        public int CompletedInstances => Instances.Count(i => i.Status == InstanceStatus.Completed);
        
        [JsonIgnore]
        public int FailedInstances => Instances.Count(i => i.Status == InstanceStatus.Failed);
        
        [JsonIgnore]
        public bool IsStale => !IsComplete && (DateTime.UtcNow - StartTime) > TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Tracks idle progress for bots
    /// </summary>
    public class IdleProgress
    {
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public List<InstanceIdleStatus> Instances { get; set; } = [];
        public int TotalBots => Instances.Sum(i => i.TotalBots);
        public int IdleBots => Instances.Sum(i => i.IdleBots);
        public bool AllIdle => Instances.All(i => i.AllIdle);
        public TimeSpan ElapsedTime => DateTime.UtcNow - StartTime;
        public TimeSpan TimeRemaining => TimeSpan.FromMinutes(UpdateManager.Config.StopBotsTimeoutMinutes) - ElapsedTime;
    }

    /// <summary>
    /// Idle status for a single instance
    /// </summary>
    public class InstanceIdleStatus
    {
        public int TcpPort { get; set; }
        public string Name => IsMaster ? "Master" : $"Instance {TcpPort}";
        public bool IsMaster { get; set; }
        public int TotalBots { get; set; }
        public int IdleBots { get; set; }
        public bool AllIdle => IdleBots == TotalBots;
        public List<string> NonIdleBots { get; set; } = [];
    }

    /// <summary>
    /// Simplified update phases - reduced from 8 to 4
    /// </summary>
    public enum UpdatePhase
    {
        Checking,    // Checking for updates and discovering instances
        Idling,      // Waiting for bots to idle
        Updating,    // Performing updates (both slaves and master)
        Verifying,   // Verifying all updates completed
        Complete     // Update complete (success or failure)
    }

    /// <summary>
    /// Instance information
    /// </summary>
    public class Instance
    {
        public int ProcessId { get; set; }
        public int TcpPort { get; set; }
        public bool IsMaster { get; set; }
        public InstanceStatus Status { get; set; } = InstanceStatus.Pending;
        public string? Error { get; set; }
        public int RetryCount { get; set; }
        public DateTime? UpdateStartTime { get; set; }
        public DateTime? UpdateEndTime { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>
    /// Simplified instance statuses
    /// </summary>
    public enum InstanceStatus
    {
        Pending,
        Updating,
        Completed,
        Failed
    }

    /// <summary>
    /// Set the configured web port for the control panel
    /// </summary>
    public static void SetConfiguredWebPort(int port)
    {
        if (port < 1 || port > 65535)
        {
            LogUtil.LogError($"Invalid web port {port}. Using default 8080.", "UpdateManager");
            port = 8080;
        }
        _configuredWebPort = port;
    }
    
    /// <summary>
    /// Get or load the current update state
    /// </summary>
    public static UpdateState? GetCurrentState()
    {
        lock (_lock)
        {
            if (_state != null) return _state;
            
            // Validate state file path to prevent directory traversal
            var safePath = ValidateAndSanitizePath(StateFilePath);
            if (safePath == null || !File.Exists(safePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(safePath);
                
                // Limit file size to prevent DoS attacks
                if (json.Length > 1024 * 1024) // 1MB limit
                {
                    LogUtil.LogError("State file too large, potential DoS attack", "UpdateManager");
                    try { File.Delete(safePath); } catch { }
                    return null;
                }

                _state = JsonSerializer.Deserialize<UpdateState>(json, JsonOptions);
                
                // Check if state is stale
                if (_state?.IsStale == true)
                {
                    LogUtil.LogInfo($"Found stale update state from {_state.StartTime:u}, marking as failed", "UpdateManager");
                    _state.Phase = UpdatePhase.Complete;
                    _state.Message = "Update timed out";
                    _state.IsComplete = true;
                    _state.Success = false;
                    SaveState();
                }
                
                return _state;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to load update state: {ex.Message}", "UpdateManager");
                try { File.Delete(safePath); } catch { }
                return null;
            }
        }
    }

    /// <summary>
    /// Start or resume an update process
    /// </summary>
    public static Task<UpdateState> StartOrResumeUpdateAsync(
        Main? mainForm, 
        int currentTcpPort, 
        bool forceUpdate = false, 
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters
        if (currentTcpPort <= 0 || currentTcpPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTcpPort), "Port must be between 1 and 65535");
        }

        LogUtil.LogInfo("Starting update process", "UpdateManager");
        
        // Check for existing state with proper locking
        UpdateState? existingState;
        lock (_lock)
        {
            existingState = GetCurrentState();
            if (existingState != null && !existingState.IsComplete)
            {
                LogUtil.LogInfo($"Resuming existing update session {existingState.SessionId}", "UpdateManager");
                // Return existing state immediately - background task will handle resume
                _ = Task.Run(async () => 
                {
                    try 
                    {
                        await ResumeUpdateAsync(mainForm, currentTcpPort, existingState, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Failed to resume update: {ex.Message}", "UpdateManager");
                        CompleteUpdate(existingState, false, $"Resume failed: {ex.Message}");
                    }
                }, cancellationToken);
                return Task.FromResult(existingState);
            }

            // Create new state atomically
            _state = new UpdateState
            {
                CurrentVersion = GetCurrentVersion()
            };
            SaveState();
        }

        // Start new update with the state created under lock
        var state = _state!;
        _ = Task.Run(async () => 
        {
            try 
            {
                await ExecuteUpdateAsync(mainForm, currentTcpPort, forceUpdate, state, cancellationToken);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to execute update: {ex.Message}", "UpdateManager");
                CompleteUpdate(state, false, $"Update failed: {ex.Message}");
            }
        }, cancellationToken);
        return Task.FromResult(state);
    }

    /// <summary>
    /// Update a single instance by port
    /// </summary>
    public static async Task<bool> UpdateSingleInstanceAsync(
        Main? mainForm,
        int instancePort,
        CancellationToken cancellationToken = default)
    {
        // Validate port range
        if (instancePort <= 0 || instancePort > 65535)
        {
            LogUtil.LogError($"Invalid port number: {instancePort}", "UpdateManager");
            return false;
        }

        LogUtil.LogInfo($"Starting single instance update for port {instancePort}", "UpdateManager");
        
        CancellationTokenSource? cts = null;
        try
        {
            // Create a linked token source for timeout control
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(Config.NewProcessStartTimeoutMinutes));

            // Check for updates first with timeout
            var (updateAvailable, _, latestVersion) = await UpdateChecker.CheckForUpdatesAsync(false);
            if (!updateAvailable)
            {
                LogUtil.LogInfo("No updates available", "UpdateManager");
                return false;
            }

            // Sanitize version string
            var safeVersion = SanitizeVersionString(latestVersion ?? "latest");

            // Create a minimal state for single instance
            var instance = new Instance
            {
                TcpPort = instancePort,
                IsMaster = instancePort == Config.MasterTcpPort,
                ProcessId = instancePort == GetCurrentPort() ? Environment.ProcessId : 0
            };

            // Stop bots on this instance if it's local
            if (instance.ProcessId == Environment.ProcessId && mainForm != null)
            {
                await StopLocalBotsAsync(mainForm, cts.Token);
            }
            else
            {
                await StopRemoteBotsAsync(instancePort, cts.Token);
            }

            // Perform update
            await UpdateInstanceAsync(mainForm, instance, safeVersion, cts.Token);
            
            return instance.Status == InstanceStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            LogUtil.LogError($"Single instance update timed out for port {instancePort}", "UpdateManager");
            return false;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Single instance update failed: {ex.Message}", "UpdateManager");
            return false;
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Main update execution with simplified phases
    /// </summary>
    private static async Task ExecuteUpdateAsync(
        Main? mainForm,
        int currentTcpPort,
        bool forceUpdate,
        UpdateState state,
        CancellationToken cancellationToken)
    {
        try
        {
            // Phase 1: Check for updates and discover instances
            state.Phase = UpdatePhase.Checking;
            state.Message = "Checking for updates and discovering instances...";
            SaveState();

            // Check for updates with timeout
            bool updateAvailable;
            string? latestVersion;
            CancellationTokenSource? versionCts = null;
            try
            {
                versionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                versionCts.CancelAfter(Config.VersionCheckTimeoutMs);
                
                (updateAvailable, _, latestVersion) = await UpdateChecker.CheckForUpdatesAsync(false).WaitAsync(versionCts.Token);
            }
            finally
            {
                versionCts?.Dispose();
            }
            
            if (!forceUpdate && !updateAvailable)
            {
                CompleteUpdate(state, true, "No updates available");
                return;
            }

            state.TargetVersion = latestVersion ?? "latest";
            
            // Discover instances - scan TCP ports for all instances
            state.Instances = await DiscoverInstancesSimpleAsync(currentTcpPort);
            SaveState();

            // Phase 2: Idle all bots
            state.Phase = UpdatePhase.Idling;
            state.Message = "Idling bots on all instances...";
            state.IdleProgress = new IdleProgress();
            SaveState();

            // Stop all bots with progress tracking
            await StopAllBotsSimpleAsync(mainForm, state.Instances, cancellationToken);

            // Phase 3: Update all instances
            state.Phase = UpdatePhase.Updating;
            state.Message = "Updating instances...";
            state.IdleProgress = null; // Clear idle progress
            SaveState();

            // Update slaves first, then master
            var slaves = state.Instances.Where(i => !i.IsMaster).OrderBy(i => i.TcpPort).ToList();
            var master = state.Instances.FirstOrDefault(i => i.IsMaster);
            
            // Log the update order for debugging
            LogUtil.LogInfo($"Update order - Slaves: {string.Join(", ", slaves.Select(s => s.TcpPort))}, Master: {master?.TcpPort ?? 0}", "UpdateManager");

            // Update all slave instances first
            foreach (var slave in slaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.CurrentUpdatingInstance = $"Instance {slave.TcpPort}";
                state.Message = $"Updating slave instance on port {slave.TcpPort}...";
                LogUtil.LogInfo($"Updating slave instance on port {slave.TcpPort}", "UpdateManager");
                SaveState();
                await UpdateInstanceAsync(mainForm, slave, state.TargetVersion, cancellationToken);
            }

            // Update master last - this will trigger a restart
            if (master != null)
            {
                state.CurrentUpdatingInstance = "Master";
                state.Message = "Updating master instance (will restart)...";
                LogUtil.LogInfo($"Updating master instance on port {master.TcpPort} - this will restart the application", "UpdateManager");
                SaveState();
                await UpdateInstanceAsync(mainForm, master, state.TargetVersion, cancellationToken);
                // Master update triggers restart - state will be resumed
                return;
            }

            // Phase 3: Verify updates
            state.Phase = UpdatePhase.Verifying;
            state.Message = "Verifying updates...";
            SaveState();
            
            await Task.Delay(3000, cancellationToken); // Let system stabilize
            
            var allSuccess = state.Instances.All(i => i.Status == InstanceStatus.Completed);
            CompleteUpdate(state, allSuccess, allSuccess ? "Update completed successfully" : "Update completed with errors");
        }
        catch (OperationCanceledException)
        {
            CompleteUpdate(state, false, "Update cancelled");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Update failed: {ex}", "UpdateManager");
            CompleteUpdate(state, false, $"Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resume an interrupted update
    /// </summary>
    private static async Task ResumeUpdateAsync(
        Main? mainForm,
        int _,  // Not used but kept for API compatibility
        UpdateState state,
        CancellationToken cancellationToken)
    {
        try
        {
            LogUtil.LogInfo($"Resuming update from phase {state.Phase}", "UpdateManager");

            // Check if we just restarted after master update
            if (state.Phase == UpdatePhase.Updating)
            {
                var master = state.Instances.FirstOrDefault(i => i.IsMaster);
                if (master != null)
                {
                    // Always check master version after restart
                    var currentVersion = GetCurrentVersion();
                    LogUtil.LogInfo($"Checking master after restart: current={currentVersion}, target={state.TargetVersion}, master status={master.Status}", "UpdateManager");
                    
                    // If we're running and the version matches target, the update was successful
                    if (currentVersion == state.TargetVersion)
                    {
                        if (master.Status != InstanceStatus.Completed)
                        {
                            master.Status = InstanceStatus.Completed;
                            master.Version = currentVersion;
                            master.UpdateEndTime = DateTime.UtcNow;
                            LogUtil.LogInfo($"Master update verified successfully: now running {currentVersion}", "UpdateManager");
                            SaveState();
                        }
                    }
                    else if (master.Status == InstanceStatus.Pending)
                    {
                        // Master hasn't updated yet - this shouldn't happen after restart
                        master.Status = InstanceStatus.Failed;
                        master.Error = $"Version mismatch after restart: expected {state.TargetVersion}, got {currentVersion}";
                        LogUtil.LogError($"Master update failed: expected {state.TargetVersion}, got {currentVersion}", "UpdateManager");
                        SaveState();
                    }
                }
            }

            // Continue with any pending instances
            var pending = state.Instances.Where(i => i.Status == InstanceStatus.Pending).ToList();
            foreach (var instance in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.Message = $"Updating instance on port {instance.TcpPort}...";
                SaveState();
                await UpdateInstanceAsync(mainForm, instance, state.TargetVersion, cancellationToken);
            }

            // Verify all updates
            state.Phase = UpdatePhase.Verifying;
            await Task.Delay(3000, cancellationToken);
            
            var allSuccess = state.Instances.All(i => i.Status == InstanceStatus.Completed);
            CompleteUpdate(state, allSuccess, allSuccess ? "Update resumed and completed" : "Update resumed with errors");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Resume failed: {ex}", "UpdateManager");
            CompleteUpdate(state, false, $"Resume failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Simplified instance discovery - find current process and scan TCP ports
    /// </summary>
    private static async Task<List<Instance>> DiscoverInstancesSimpleAsync(int currentTcpPort)
    {
        var instances = new List<Instance>();
        var discoveredPorts = new HashSet<int>();

        // Determine if current instance is master (hosting web server)
        bool currentIsMaster = await IsCurrentInstanceMasterAsync();

        // Add current instance first
        instances.Add(new Instance
        {
            ProcessId = Environment.ProcessId,
            TcpPort = currentTcpPort,
            IsMaster = currentIsMaster,
            Version = GetCurrentVersion()
        });
        discoveredPorts.Add(currentTcpPort);

        // Scan TCP ports in the range 8081-8090 for remote instances (reduced for faster scanning)
        var scanTasks = new List<Task<Instance?>>();
        for (int port = 8081; port <= 8090; port++)
        {
            if (discoveredPorts.Contains(port))
                continue;

            int portToScan = port; // Capture for closure
            scanTasks.Add(Task.Run(async () =>
            {
                try
                {
                    // Check if port is open with short timeout
                    using var client = new System.Net.Sockets.TcpClient();
                    var connectTask = client.ConnectAsync("127.0.0.1", portToScan);
                    if (await Task.WhenAny(connectTask, Task.Delay(200)) == connectTask)
                    {
                        // Port is open, send INFO command to check if it's a PokeBot instance
                        using var stream = client.GetStream();
                        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                        using var reader = new StreamReader(stream, Encoding.UTF8);

                        // Send INFO command with timeout
                        var writeTask = writer.WriteLineAsync("INFO");
                        if (await Task.WhenAny(writeTask, Task.Delay(500)) == writeTask)
                        {
                            // Read response with timeout
                            var readTask = reader.ReadLineAsync();
                            if (await Task.WhenAny(readTask, Task.Delay(500)) == readTask)
                            {
                                var response = await readTask;
                                if (!string.IsNullOrEmpty(response) && response.StartsWith('{'))
                                {
                                    // Parse the JSON response to get instance info
                                    try
                                    {
                                        using var doc = JsonDocument.Parse(response);
                                        var root = doc.RootElement;

                                        var version = "Unknown";
                                        if (root.TryGetProperty("Version", out var versionProp))
                                            version = versionProp.GetString() ?? "Unknown";

                                        // Try to find the process ID for this port
                                        int processId = 0;
                                        try
                                        {
                                            var processes = Process.GetProcessesByName("ZE_FusionBot");
                                            foreach (var proc in processes)
                                            {
                                                try
                                                {
                                                    var portFile = Path.Combine(
                                                        Path.GetDirectoryName(proc.MainModule?.FileName ?? "") ?? "",
                                                        $"ZE_FusionBot_{proc.Id}.port"
                                                    );
                                                    
                                                    if (File.Exists(portFile))
                                                    {
                                                        var portText = File.ReadAllText(portFile).Trim();
                                                        if (int.TryParse(portText, out var filePort) && filePort == portToScan)
                                                        {
                                                            processId = proc.Id;
                                                            break;
                                                        }
                                                    }
                                                }
                                                catch { }
                                                finally { proc.Dispose(); }
                                            }
                                        }
                                        catch { }

                                        return new Instance
                                        {
                                            ProcessId = processId,
                                            TcpPort = portToScan,
                                            IsMaster = false, // Will be determined after all instances are discovered
                                            Version = version
                                        };
                                    }
                                    catch (Exception ex)
                                    {
                                        LogUtil.LogError($"Failed to parse INFO response from port {portToScan}: {ex.Message}", "UpdateManager");
                                    }
                                }
                            }
                        }
                    }
                }
                catch { /* Port not open or not a PokeBot instance */ }
                return null;
            }));
        }

        // Wait for all scan tasks to complete
        var scanResults = await Task.WhenAll(scanTasks);
        
        // Add discovered instances
        foreach (var instance in scanResults.Where(i => i != null))
        {
            instances.Add(instance!);
            discoveredPorts.Add(instance!.TcpPort);
        }

        // Also check for local PokeBot processes with port files (fallback method)
        try
        {
            var otherProcesses = Process.GetProcessesByName("ZE_FusionBot")
                .Where(p => p.Id != Environment.ProcessId)
                .Take(10); // Limit to prevent resource exhaustion

            foreach (var process in otherProcesses)
            {
                try
                {
                    var moduleFileName = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(moduleFileName))
                        continue;

                    var sanitizedPath = ValidateAndSanitizePath(moduleFileName);
                    if (sanitizedPath == null)
                        continue;

                    var portFile = Path.Combine(
                        Path.GetDirectoryName(sanitizedPath) ?? "",
                        $"ZE_FusionBot_{process.Id}.port"
                    );

                    var safePortFile = ValidateAndSanitizePath(portFile);
                    if (safePortFile != null && File.Exists(safePortFile))
                    {
                        var portText = File.ReadAllText(safePortFile).Trim();
                        
                        // Validate port text to prevent injection
                        if (portText.Length <= 10 && int.TryParse(portText, out var port) && port > 0 && port <= 65535)
                        {
                            if (!discoveredPorts.Contains(port))
                            {
                                instances.Add(new Instance
                                {
                                    ProcessId = process.Id,
                                    TcpPort = port,
                                    IsMaster = false, // Will be determined after all instances are discovered
                                    Version = GetCurrentVersion()
                                });
                                discoveredPorts.Add(port);
                            }
                        }
                    }
                }
                catch { /* Ignore process access errors */ }
                finally { process.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error discovering local instances: {ex.Message}", "UpdateManager");
        }

        // After discovering all instances, determine which is the master
        // Master is the one hosting the web server on port 8080
        if (!currentIsMaster && instances.Count > 1)
        {
            // Try to find which instance is hosting the web server
            var masterPort = await FindMasterInstancePortAsync();
            if (masterPort > 0)
            {
                var masterInstance = instances.FirstOrDefault(i => i.TcpPort == masterPort);
                if (masterInstance != null)
                {
                    masterInstance.IsMaster = true;
                    LogUtil.LogInfo($"Identified master instance on TCP port {masterPort} (hosting web server)", "UpdateManager");
                }
            }
        }

        // If no master identified yet, fall back to default port
        if (!instances.Any(i => i.IsMaster))
        {
            var defaultMaster = instances.FirstOrDefault(i => i.TcpPort == Config.MasterTcpPort);
            if (defaultMaster != null)
            {
                defaultMaster.IsMaster = true;
                LogUtil.LogInfo($"Using default master on TCP port {Config.MasterTcpPort}", "UpdateManager");
            }
        }

        // Discovery complete - log instance details
        if (instances.Count > 0)
        {
            LogUtil.LogInfo($"Discovered {instances.Count} instance(s):", "UpdateManager");
            foreach (var inst in instances)
            {
                LogUtil.LogInfo($"  - Port {inst.TcpPort}: IsMaster={inst.IsMaster}, Version={inst.Version}", "UpdateManager");
            }
        }
        return instances;
    }

    /// <summary>
    /// Simplified bot stopping with progress tracking
    /// </summary>
    private static async Task StopAllBotsSimpleAsync(
        Main? mainForm,
        List<Instance> instances,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMinutes(Config.StopBotsTimeoutMinutes);
        var endTime = DateTime.UtcNow.Add(timeout);

        LogUtil.LogInfo($"Stopping all bots (timeout: {timeout.TotalMinutes} minutes)", "UpdateManager");

        // Initialize idle progress tracking
        if (_state?.IdleProgress != null)
        {
            _state.IdleProgress.StartTime = DateTime.UtcNow;
            foreach (var instance in instances)
            {
                _state.IdleProgress.Instances.Add(new InstanceIdleStatus
                {
                    TcpPort = instance.TcpPort,
                    IsMaster = instance.IsMaster
                });
            }
            SaveState();
        }

        // Send idle command to all instances
        foreach (var instance in instances)
        {
            try
            {
                if (instance.ProcessId == Environment.ProcessId)
                {
                    // Local instance
                    await StopLocalBotsAsync(mainForm, cancellationToken);
                }
                else
                {
                    // Remote instance
                    await StopRemoteBotsAsync(instance.TcpPort, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to stop bots on port {instance.TcpPort}: {ex.Message}", "UpdateManager");
            }
        }

        // Wait for bots to stop with progress updates
        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            var allStopped = true;
            
            // Update idle progress for each instance
            if (_state?.IdleProgress != null)
            {
                foreach (var instance in instances)
                {
                    var idleStatus = _state.IdleProgress.Instances.FirstOrDefault(i => i.TcpPort == instance.TcpPort);
                    if (idleStatus != null)
                    {
                        await UpdateInstanceIdleStatus(mainForm, instance, idleStatus, cancellationToken);
                        if (!idleStatus.AllIdle)
                        {
                            allStopped = false;
                        }
                    }
                }

                _state.Message = $"Waiting for bots to idle: {_state.IdleProgress.IdleBots}/{_state.IdleProgress.TotalBots} idle";
                SaveState();
            }
            else
            {
                // Fallback check without progress tracking
                foreach (var instance in instances)
                {
                    if (!await IsInstanceIdleAsync(mainForm, instance, cancellationToken))
                    {
                        allStopped = false;
                        break;
                    }
                }
            }

            if (allStopped)
            {
                LogUtil.LogInfo("All bots stopped successfully", "UpdateManager");
                if (_state != null)
                {
                    _state.Message = "All bots idled successfully";
                    SaveState();
                }
                return;
            }

            await Task.Delay(Config.ProcessCheckDelayMs, cancellationToken);
        }

        LogUtil.LogError("Bot stop timeout reached - proceeding anyway", "UpdateManager");
        if (_state != null)
        {
            _state.Message = "Bot idle timeout reached - forcing update";
            SaveState();
        }
    }

    /// <summary>
    /// Update idle status for a single instance
    /// </summary>
    private static async Task UpdateInstanceIdleStatus(Main? mainForm, Instance instance, InstanceIdleStatus idleStatus, CancellationToken cancellationToken)
    {
        try
        {
            if (instance.ProcessId == Environment.ProcessId)
            {
                // Check local bots
                if (mainForm == null) 
                {
                    idleStatus.TotalBots = 0;
                    idleStatus.IdleBots = 0;
                    return;
                }
                
                await Task.Run(() =>
                {
                    mainForm.Invoke((MethodInvoker)(() =>
                    {
                        var flpBotsField = mainForm.GetType().GetField("FLP_Bots",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (flpBotsField?.GetValue(mainForm) is FlowLayoutPanel flpBots)
                        {
                            var controllers = flpBots.Controls.OfType<BotController>().ToList();
                            idleStatus.TotalBots = controllers.Count;
                            idleStatus.NonIdleBots.Clear();
                            
                            var idleCount = 0;
                            foreach (var controller in controllers)
                            {
                                var state = controller.ReadBotState();
                                var upperState = state?.ToUpperInvariant() ?? "";
                                if (upperState == "IDLE" || upperState == "STOPPED")
                                {
                                    idleCount++;
                                }
                                else
                                {
                                    // Try to get bot name
                                    var botName = $"Bot {controllers.IndexOf(controller) + 1}";
                                    idleStatus.NonIdleBots.Add($"{botName}: {state}");
                                }
                            }
                            idleStatus.IdleBots = idleCount;
                        }
                    }));
                }, cancellationToken);
            }
            else
            {
                // Check remote bots
                var response = await Task.Run(() => BotServer.QueryRemote(instance.TcpPort, "LISTBOTS"), cancellationToken);
                if (response.StartsWith('{'))
                {
                    var botsData = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(response);
                    if (botsData?.ContainsKey("Bots") == true)
                    {
                        var bots = botsData["Bots"];
                        idleStatus.TotalBots = bots.Count;
                        idleStatus.NonIdleBots.Clear();
                        
                        var idleCount = 0;
                        for (int i = 0; i < bots.Count; i++)
                        {
                            var bot = bots[i];
                            if (bot.TryGetValue("Status", out var status))
                            {
                                var statusStr = status?.ToString()?.ToUpperInvariant() ?? "";
                                if (statusStr == "IDLE" || statusStr == "STOPPED")
                                {
                                    idleCount++;
                                }
                                else
                                {
                                    var botName = bot.TryGetValue("Name", out var name) ? name?.ToString() : null;
                                    botName ??= $"Bot {i + 1}";
                                    idleStatus.NonIdleBots.Add($"{botName}: {status}");
                                }
                            }
                        }
                        idleStatus.IdleBots = idleCount;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to update idle status for instance {instance.TcpPort}: {ex.Message}", "UpdateManager");
            // Assume idle on error
            idleStatus.TotalBots = 0;
            idleStatus.IdleBots = 0;
        }
    }

    /// <summary>
    /// Stop local bots
    /// </summary>
    private static async Task StopLocalBotsAsync(Main? mainForm, CancellationToken cancellationToken)
    {
        if (mainForm == null) return;

        await Task.Run(() =>
        {
            mainForm.BeginInvoke((MethodInvoker)(() =>
            {
                var sendAllMethod = mainForm.GetType().GetMethod("SendAll",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                sendAllMethod?.Invoke(mainForm, [BotControlCommand.Idle]);
            }));
        }, cancellationToken);
    }

    /// <summary>
    /// Stop remote bots
    /// </summary>
    private static async Task StopRemoteBotsAsync(int port, CancellationToken cancellationToken)
    {
        // Validate port range
        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        CancellationTokenSource? cts = null;
        try
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Config.NetworkTimeoutMs);
            await Task.Run(() => BotServer.QueryRemote(port, "IDLEALL"), cts.Token);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Check if instance bots are idle
    /// </summary>
    private static async Task<bool> IsInstanceIdleAsync(Main? mainForm, Instance instance, CancellationToken cancellationToken)
    {
        try
        {
            if (instance.ProcessId == Environment.ProcessId)
            {
                // Check local bots
                if (mainForm == null) return true;
                
                return await Task.Run(() =>
                {
                    var isIdle = true;
                    mainForm.Invoke((MethodInvoker)(() =>
                    {
                        var flpBotsField = mainForm.GetType().GetField("FLP_Bots",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (flpBotsField?.GetValue(mainForm) is FlowLayoutPanel flpBots)
                        {
                            var controllers = flpBots.Controls.OfType<BotController>().ToList();
                            isIdle = controllers.All(c =>
                            {
                                var state = c.ReadBotState();
                                return state == "IDLE" || state == "STOPPED";
                            });
                        }
                    }));
                    return isIdle;
                }, cancellationToken);
            }
            else
            {
                // Check remote bots
                var response = await Task.Run(() => BotServer.QueryRemote(instance.TcpPort, "LISTBOTS"), cancellationToken);
                if (response.StartsWith('{'))
                {
                    var botsData = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(response);
                    if (botsData?.ContainsKey("Bots") == true)
                    {
                        return botsData["Bots"].All(b =>
                        {
                            if (b.TryGetValue("Status", out var status))
                            {
                                var statusStr = status?.ToString()?.ToUpperInvariant() ?? "";
                                return statusStr == "IDLE" || statusStr == "STOPPED";
                            }
                            return true;
                        });
                    }
                }
                return true;
            }
        }
        catch
        {
            return true; // Assume idle on error
        }
    }

    /// <summary>
    /// Update a single instance with retry support
    /// </summary>
    private static async Task UpdateInstanceAsync(
        Main? mainForm,
        Instance instance,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        instance.UpdateStartTime = DateTime.UtcNow;
        instance.Status = InstanceStatus.Pending;

        for (int retry = 0; retry < Config.MaxRetryCount; retry++)
        {
            try
            {
                instance.RetryCount = retry;
                instance.Status = InstanceStatus.Updating;
                SaveState();

                LogUtil.LogInfo($"Updating instance on port {instance.TcpPort} (attempt {retry + 1}/{Config.MaxRetryCount})", "UpdateManager");

                if (instance.IsMaster)
                {
                    // Master update - trigger restart
                    var baseDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                    var updateFlagPath = Path.Combine(baseDir, "update_in_progress.flag");
                    
                    var safeFlagPath = ValidateAndSanitizePath(updateFlagPath);
                    if (safeFlagPath != null)
                    {
                        var flagData = JsonSerializer.Serialize(new
                        {
                            Timestamp = DateTime.UtcNow,
                            TargetVersion = SanitizeVersionString(targetVersion),
                            _state?.SessionId
                        });
                        
                        await File.WriteAllTextAsync(safeFlagPath, flagData, cancellationToken);
                    }

                    await Task.Run(() =>
                    {
                        mainForm?.BeginInvoke((MethodInvoker)(() =>
                        {
                            var updateForm = new UpdateForm(false, targetVersion, true);
                            updateForm.PerformUpdate();
                        }));
                    }, cancellationToken);

                    // Application will restart
                    return;
                }
                else
                {
                    // Slave update - send command
                    var response = await Task.Run(() => BotServer.QueryRemote(instance.TcpPort, "UPDATE"), cancellationToken);
                    if (response.StartsWith("ERROR", StringComparison.Ordinal))
                    {
                        throw new Exception($"Update command failed: {response}");
                    }

                    // Wait for process restart
                    await WaitForProcessRestartAsync(instance, cancellationToken);
                }

                instance.Status = InstanceStatus.Completed;
                instance.UpdateEndTime = DateTime.UtcNow;
                instance.Version = targetVersion;
                SaveState();
                
                LogUtil.LogInfo($"Instance on port {instance.TcpPort} updated successfully", "UpdateManager");
                return;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Update attempt {retry + 1} failed: {ex.Message}", "UpdateManager");
                
                if (retry == Config.MaxRetryCount - 1)
                {
                    instance.Status = InstanceStatus.Failed;
                    instance.Error = ex.Message;
                    instance.UpdateEndTime = DateTime.UtcNow;
                    SaveState();
                    throw;
                }
                
                await Task.Delay((retry + 1) * 1000, cancellationToken); // Exponential backoff
            }
        }
    }

    /// <summary>
    /// Wait for a process to restart after update
    /// </summary>
    private static async Task WaitForProcessRestartAsync(Instance instance, CancellationToken cancellationToken)
    {
        // Wait for old process to terminate
        var terminateTimeout = TimeSpan.FromSeconds(Config.ProcessTerminationTimeoutSeconds);
        var terminateEndTime = DateTime.UtcNow.Add(terminateTimeout);
        
        while (DateTime.UtcNow < terminateEndTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                using var process = Process.GetProcessById(instance.ProcessId);
                if (process.HasExited) break;
            }
            catch (ArgumentException)
            {
                break; // Process no longer exists
            }
            
            await Task.Delay(1000, cancellationToken);
        }

        // Wait for new process to start
        var startTimeout = TimeSpan.FromMinutes(Config.NewProcessStartTimeoutMinutes);
        var startEndTime = DateTime.UtcNow.Add(startTimeout);
        
        while (DateTime.UtcNow < startEndTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (IsPortResponding(instance.TcpPort))
            {
                LogUtil.LogInfo($"New process started on port {instance.TcpPort}", "UpdateManager");
                return;
            }
            
            await Task.Delay(2000, cancellationToken);
        }
        
        throw new TimeoutException("New process did not start within timeout");
    }

    /// <summary>
    /// Check if a TCP port is responding
    /// </summary>
    private static bool IsPortResponding(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(3000);
            if (success)
            {
                client.EndConnect(result);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Complete the update process
    /// </summary>
    private static void CompleteUpdate(UpdateState state, bool success, string message)
    {
        LogUtil.LogInfo($"Update complete: {message}", "UpdateManager");
        
        state.Phase = UpdatePhase.Complete;
        state.Message = message;
        state.IsComplete = true;
        state.Success = success;
        state.LastModified = DateTime.UtcNow;
        SaveState();

        // Keep state for a short time to allow UI to see completion, then clean up
        if (success)
        {
            // Delete state file after 10 seconds to allow UI to detect completion
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000, CancellationToken.None); // 10 second delay
                lock (_lock)
                {
                    _state = null;
                    try
                    {
                        if (File.Exists(StateFilePath))
                            File.Delete(StateFilePath);
                        
                        var baseDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                        var updateFlagPath = Path.Combine(baseDir, "update_in_progress.flag");
                        var safeFlagPath = ValidateAndSanitizePath(updateFlagPath);
                        if (safeFlagPath != null && File.Exists(safeFlagPath))
                            File.Delete(safeFlagPath);
                    }
                    catch { }
                }
            });
        }
    }

    /// <summary>
    /// Save current state to disk
    /// </summary>
    private static void SaveState()
    {
        try
        {
            lock (_lock)
            {
                if (_state != null)
                {
                    _state.LastModified = DateTime.UtcNow;
                    var json = JsonSerializer.Serialize(_state, JsonOptions);
                    File.WriteAllText(StateFilePath, json);
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to save update state: {ex.Message}", "UpdateManager");
        }
    }

    /// <summary>
    /// Clear the current update state
    /// </summary>
    public static void ClearState()
    {
        lock (_lock)
        {
            _state = null;
            try
            {
                if (File.Exists(StateFilePath))
                    File.Delete(StateFilePath);
            }
            catch { }
        }
    }

    /// <summary>
    /// Check if an update is currently in progress
    /// </summary>
    public static bool IsUpdateInProgress()
    {
        var state = GetCurrentState();
        return state != null && !state.IsComplete;
    }
    
    /// <summary>
    /// Clear the current update session
    /// </summary>
    public static void ClearSession()
    {
        lock (_lock)
        {
            _state = null;
            if (File.Exists(StateFilePath))
            {
                try
                {
                    File.Delete(StateFilePath);
                    LogUtil.LogInfo("Update session cleared", "UpdateManager");
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to delete state file: {ex.Message}", "UpdateManager");
                }
            }
        }
    }
    
    /// <summary>
    /// Force complete a stuck update session by checking actual versions
    /// </summary>
    public static void ForceCompleteSession()
    {
        lock (_lock)
        {
            var state = GetCurrentState();
            if (state != null && !state.IsComplete)
            {
                // Check actual current version
                var currentVersion = GetCurrentVersion();
                LogUtil.LogInfo($"Force completing update: current version={currentVersion}, target={state.TargetVersion}", "UpdateManager");
                
                // Fix master instance status based on actual version
                var master = state.Instances.FirstOrDefault(i => i.IsMaster);
                if (master != null && master.Status != InstanceStatus.Completed)
                {
                    if (currentVersion == state.TargetVersion)
                    {
                        master.Status = InstanceStatus.Completed;
                        master.Version = currentVersion;
                        master.UpdateEndTime = DateTime.UtcNow;
                        LogUtil.LogInfo("Master instance marked as completed based on version check", "UpdateManager");
                    }
                    else
                    {
                        master.Status = InstanceStatus.Failed;
                        master.Error = $"Version mismatch: expected {state.TargetVersion}, got {currentVersion}";
                        LogUtil.LogError($"Master instance marked as failed: {master.Error}", "UpdateManager");
                    }
                }
                
                // Check if all instances are complete
                var allSuccess = state.Instances.All(i => i.Status == InstanceStatus.Completed);
                
                // Complete the update
                state.Phase = UpdatePhase.Complete;
                state.IsComplete = true;
                state.Success = allSuccess;
                state.Message = allSuccess ? "Update completed successfully (forced)" : "Update completed with errors (forced)";
                state.LastModified = DateTime.UtcNow;
                
                SaveState();
                LogUtil.LogInfo($"Update session force completed: success={allSuccess}", "UpdateManager");
            }
        }
    }

    /// <summary>
    /// Get the current version
    /// </summary>
    private static string GetCurrentVersion()
    {
        try
        {
            return TradeBot.Version;
        }
        catch
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                return assembly.GetName().Version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    /// <summary>
    /// Get the current TCP port (simplified)
    /// </summary>
    private static int GetCurrentPort()
    {
        // Try to read from port file
        try
        {
            var baseDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            var portFile = Path.Combine(baseDir, $"ZE_FusionBot_{Environment.ProcessId}.port");
            
            var safePortFile = ValidateAndSanitizePath(portFile);
            if (safePortFile != null && File.Exists(safePortFile))
            {
                var portText = File.ReadAllText(safePortFile).Trim();
                
                // Validate port text length and content
                if (portText.Length <= 10 && int.TryParse(portText, out var port) && port > 0 && port <= 65535)
                {
                    return port;
                }
            }
        }
        catch { }
        
        // Default to master port
        return Config.MasterTcpPort;
    }

    /// <summary>
    /// Configure update settings
    /// </summary>
    public static void Configure(Action<UpdateConfig> configAction)
    {
        configAction(Config);
    }

    /// <summary>
    /// Validate and sanitize file paths to prevent directory traversal attacks
    /// </summary>
    private static string? ValidateAndSanitizePath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Get the full path and normalize it
            var fullPath = Path.GetFullPath(path);
            
            // Get the expected base directory
            var baseDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            var basePath = Path.GetFullPath(baseDir);

            // Ensure the path is within the expected directory
            if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                LogUtil.LogError($"Path traversal attempt detected: {path}", "UpdateManager");
                return null;
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Path validation failed for {path}: {ex.Message}", "UpdateManager");
            return null;
        }
    }

    /// <summary>
    /// Sanitize version strings to prevent injection attacks
    /// </summary>
    private static string SanitizeVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "latest";

        // Allow only alphanumeric characters, dots, hyphens, and underscores
        var sanitized = UpdateManagerRegexHelper.VersionSanitizer().Replace(version, "");
        
        // Limit length to prevent buffer overflow
        if (sanitized.Length > 50)
            sanitized = sanitized[..50];

        return string.IsNullOrEmpty(sanitized) ? "latest" : sanitized;
    }


    /// <summary>
    /// Check if current instance is the master (hosting web server)
    /// </summary>
    private static async Task<bool> IsCurrentInstanceMasterAsync()
    {
        try
        {
            // Master is the instance hosting web server on the configured port
            // Try to connect to the web server and check if it's us
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync($"http://localhost:{_configuredWebPort}/api/bot/instances");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                // Check if the current process is marked as master
                if (doc.RootElement.TryGetProperty("instances", out var instances))
                {
                    foreach (var inst in instances.EnumerateArray())
                    {
                        if (inst.TryGetProperty("processId", out var pidElement) && 
                            inst.TryGetProperty("isMaster", out var isMaster))
                        {
                            if (pidElement.GetInt32() == Environment.ProcessId && isMaster.GetBoolean())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Fallback: check if we can bind to the configured port
            using var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, _configuredWebPort);
            tcpListener.Start();
            tcpListener.Stop();
            return true;
        }
        catch
        {
            // Port is in use or we can't bind - we're not the master
            return false;
        }
    }

    /// <summary>
    /// Find which TCP port the master instance is using
    /// </summary>
    private static async Task<int> FindMasterInstancePortAsync()
    {
        try
        {
            // Query the web server to find master instance
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"http://localhost:{_configuredWebPort}/api/bot/instances");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("instances", out var instances))
                {
                    foreach (var inst in instances.EnumerateArray())
                    {
                        if (inst.TryGetProperty("isMaster", out var isMaster) && isMaster.GetBoolean())
                        {
                            if (inst.TryGetProperty("port", out var port))
                            {
                                return port.GetInt32();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogInfo($"Could not determine master port via API: {ex.Message}", "UpdateManager");
        }
        
        return 0; // Not found
    }
}
