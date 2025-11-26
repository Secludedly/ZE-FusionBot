using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using SysBot.Base;
using SysBot.Pokemon.WinForms.WebApi.Models;
using static SysBot.Pokemon.WinForms.WebApi.RestartManager;

namespace SysBot.Pokemon.WinForms.WebApi;

public partial class BotServer(Main mainForm, int port = 8080, int tcpPort = 8081) : IDisposable
{
    private HttpListener? _listener;
    private Thread? _listenerThread;
    private readonly int _port = port;
    private readonly int _tcpPort = tcpPort;
    private readonly CancellationTokenSource _cts = new();
    private readonly Main _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
    private volatile bool _running;
    private string? _htmlTemplate;
    
    // Whitelist of allowed method names for security
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "SendAll"
    };
    
    // JSON serialization options
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    
    // Cached JsonSerializer options for security contexts
    private static class CachedJsonOptions
    {
        public static readonly JsonSerializerOptions Secure = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 10,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
    
    
    [System.Text.RegularExpressions.GeneratedRegex(@"[<>""'&;\/]")]
    private static partial System.Text.RegularExpressions.Regex CleanupRegex();
    
    private string HtmlTemplate
    {
        get
        {
            _htmlTemplate ??= LoadEmbeddedResource("BotControlPanel.html");
            return _htmlTemplate;
        }
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(fullResourceName))
        {
            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found. Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var stream = assembly.GetManifestResourceStream(fullResourceName) ?? throw new FileNotFoundException($"Could not load embedded resource '{fullResourceName}'");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    
    private static byte[] LoadEmbeddedResourceBinary(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(fullResourceName))
        {
            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found. Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var stream = assembly.GetManifestResourceStream(fullResourceName) ?? throw new FileNotFoundException($"Could not load embedded resource '{fullResourceName}'");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public void Start()
    {
        if (_running) return;

        try
        {
            _listener = new HttpListener();

            try
            {
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
                LogUtil.LogInfo($"Web server listening on all interfaces at port {_port}", "WebServer");
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();

                LogUtil.LogError($"Web server requires administrator privileges for network access. Currently limited to localhost only.", "WebServer");
                LogUtil.LogInfo("To enable network access, either:", "WebServer");
                LogUtil.LogInfo("1. Run this application as Administrator", "WebServer");
                LogUtil.LogInfo($"2. Or run this command as admin: netsh http add urlacl url=http://+:{_port}/ user=Everyone", "WebServer");
            }

            _running = true;

            _listenerThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = "BotWebServer"
            };
            _listenerThread.Start();
            
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to start web server: {ex.Message}", "WebServer");
            throw;
        }
    }

    public void Stop()
    {
        if (!_running) return;

        try
        {
            _running = false;
            _cts.Cancel();
            
            
            _listener?.Stop();
            _listenerThread?.Join(5000);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error stopping web server: {ex.Message}", "WebServer");
        }
    }

    private void Listen()
    {
        while (_running && _listener != null)
        {
            try
            {
                var asyncResult = _listener.BeginGetContext(null, null);

                while (_running && !asyncResult.AsyncWaitHandle.WaitOne(100))
                {
                }

                if (!_running)
                    break;

                var context = _listener.EndGetContext(asyncResult);

                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    try
                    {
                        await HandleRequest(context);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Error handling request: {ex.Message}", "WebServer");
                    }
                });
            }
            catch (HttpListenerException ex) when (!_running || ex.ErrorCode == 995)
            {
                break;
            }
            catch (ObjectDisposedException) when (!_running)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    LogUtil.LogError($"Error in listener: {ex.Message}", "WebServer");
                }
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            SetCorsHeaders(request, response);

            if (request.HttpMethod == "OPTIONS")
            {
                await SendResponseAsync(response, 200, "");
                return;
            }


            var (statusCode, content, contentType) = await ProcessRequestAsync(request);
            
            if ((contentType == "image/x-icon" || contentType == "image/png") && content is byte[] imageBytes)
            {
                await SendBinaryResponseAsync(response, 200, imageBytes, contentType);
            }
            else
            {
                var responseContent = content?.ToString() ?? "Not Found";
                if (request.Url?.LocalPath?.Contains("/bots") == true)
                {
                    LogUtil.LogInfo($"Bots API response: {responseContent[..Math.Min(200, responseContent.Length)]}", "WebAPI");
                }
                await SendResponseAsync(response, statusCode, responseContent, contentType);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error processing request: {ex.Message}", "WebServer");
            await TrySendErrorResponseAsync(response, 500, "Internal Server Error");
        }
    }
    
    private static void SetCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"] ?? "http://localhost";
        if (IsAllowedOrigin(origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
    private async Task<(int statusCode, object? content, string contentType)> ProcessRequestAsync(HttpListenerRequest request)
    {
        var path = request.Url?.LocalPath ?? "";
        
        return path switch
        {
            "/" => (200, HtmlTemplate, "text/html"),
            "/BotControlPanel.css" => (200, LoadEmbeddedResource("BotControlPanel.css"), "text/css"),
            "/BotControlPanel.js" => (200, LoadEmbeddedResource("BotControlPanel.js"), "text/javascript"),
            "/api/bot/instances" => (200, await GetInstancesAsync(), "application/json"),
            "/api/bot/queue/status" => (200, await Task.FromResult(GetQueueStatus()), "application/json"),
            var p when p.StartsWith("/api/bot/instances/") && p.EndsWith("/bots") =>
                (200, await Task.FromResult(GetBots(ExtractPort(p))), "application/json"),
            var p when p.StartsWith("/api/bot/instances/") && p.EndsWith("/command") =>
                (200, await RunCommand(request, ExtractPort(p)), "application/json"),
            "/api/bot/command/all" => (200, await RunAllCommandAsync(request), "application/json"),
            "/api/bot/update/check" => (200, await CheckForUpdates(), "application/json"),
            "/api/bot/update/idle-status" => (200, await GetIdleStatusAsync(), "application/json"),
            "/api/bot/update/all" => (200, await UpdateAllInstances(request), "application/json"),
            "/api/bot/update/active" => (200, await Task.FromResult(GetActiveUpdates()), "application/json"),
            "/api/bot/update/clear" => (200, await Task.FromResult(ClearUpdateSession()), "application/json"),
            var p when p.StartsWith("/api/bot/instances/") && p.EndsWith("/update") && request.HttpMethod == "POST" =>
                (200, await UpdateSingleInstance(request, ExtractPort(p)), "application/json"),
            "/api/bot/restart/all" => (200, await RestartAllInstances(request), "application/json"),
            "/api/bot/restart/status" => (200, GetRestartStatus(), "application/json"),
            "/api/bot/restart/schedule" => (200, await UpdateRestartSchedule(request), "application/json"),
            var p when p.StartsWith("/api/bot/instances/") && p.EndsWith("/remote/button") =>
                (200, await HandleRemoteButton(request, ExtractPort(p)), "application/json"),
            var p when p.StartsWith("/api/bot/instances/") && p.EndsWith("/remote/macro") =>
                (200, await HandleRemoteMacro(request, ExtractPort(p)), "application/json"),
            "/icon.ico" => (200, GetIconBytes(), "image/x-icon"),
            "/LeftJoyCon.png" => (200, LoadEmbeddedResourceBinary("LeftJoyCon.png"), "image/png"),
            "/RightJoyCon.png" => (200, LoadEmbeddedResourceBinary("RightJoyCon.png"), "image/png"),
            _ => (404, null, "text/plain")
        };
    }
    
    private static async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string content, string contentType = "text/plain")
    {
        try
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.Headers.Add("Cache-Control", "no-cache");
            
            var buffer = Encoding.UTF8.GetBytes(content ?? "");
            response.ContentLength64 = buffer.Length;
            
            await response.OutputStream.WriteAsync(buffer.AsMemory(0, buffer.Length));
            await response.OutputStream.FlushAsync();
            response.Close();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 64 || ex.ErrorCode == 1229)
        {
            // Client disconnected - ignore
        }
        catch (ObjectDisposedException)
        {
            // Response already closed - ignore
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }
    
    private static async Task SendBinaryResponseAsync(HttpListenerResponse response, int statusCode, byte[] content, string contentType)
    {
        try
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = content.Length;
            
            await response.OutputStream.WriteAsync(content.AsMemory(0, content.Length));
            await response.OutputStream.FlushAsync();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 64 || ex.ErrorCode == 1229)
        {
            // Client disconnected - ignore
        }
        catch (ObjectDisposedException)
        {
            // Response already closed - ignore
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }
    
    private static async Task TrySendErrorResponseAsync(HttpListenerResponse response, int statusCode, string message)
    {
        try
        {
            if (response.OutputStream.CanWrite)
            {
                await SendResponseAsync(response, statusCode, message);
            }
        }
        catch { }
    }

    private async Task<string> UpdateAllInstances(HttpListenerRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            bool forceUpdate = false;

            // Check if this is a status check for an existing update
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var requestData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
                    
                    // Check for force flag
                    if (requestData?.ContainsKey("force") == true)
                    {
                        forceUpdate = requestData["force"].GetBoolean();
                    }
                }
                catch
                {
                    // Not JSON, ignore
                }
            }

            // Check if update is already in progress
            if (UpdateManager.IsUpdateInProgress())
            {
                return CreateErrorResponse("An update is already in progress");
            }
            
            if (RestartManager.IsRestartInProgress)
            {
                return CreateErrorResponse("Cannot update while restart is in progress");
            }

            // Start or resume update
            var session = await UpdateManager.StartOrResumeUpdateAsync(_mainForm, _tcpPort, forceUpdate);

            LogUtil.LogInfo($"Started update session with ID: {session.SessionId}, Force: {forceUpdate}", "WebServer");

            return JsonSerializer.Serialize(new
            {
                sessionId = session.SessionId,
                phase = session.Phase.ToString(),
                message = session.Message,
                totalInstances = session.TotalInstances,
                completedInstances = session.CompletedInstances,
                failedInstances = session.FailedInstances,
                startTime = session.StartTime.ToString("o"),
                success = true,
                info = "Update process started in background. Use /api/bot/update/active to check status."
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to start update: {ex.Message}", "WebServer");
            return CreateErrorResponse(ex.Message);
        }
    }

    private async Task<string> UpdateSingleInstance(HttpListenerRequest request, int port)
    {
        try
        {
            // Validate port range - ensure it's within valid TCP port range
            if (port <= 0 || port > 65535)
            {
                LogUtil.LogError($"Invalid port number: {port} (must be 1-65535)", "WebServer");
                return CreateErrorResponse($"Invalid port number: {port}. Port must be between 1 and 65535.");
            }

            // Additional validation - ensure port is not in reserved ranges
            if (port < 1024 && port != _tcpPort) // Allow only well-known ports that are explicitly configured
            {
                LogUtil.LogError($"Attempted to update instance on reserved port: {port}", "WebServer");
                return CreateErrorResponse($"Cannot update instances on reserved system ports (1-1023).");
            }

            // Check if this is the master instance trying to update itself
            if (port == _tcpPort)
            {
                LogUtil.LogError($"Master instance (port {port}) attempted to update itself. This is not supported.", "WebServer");
                return CreateErrorResponse("Master instance cannot update itself. Use /api/bot/update/all to update all instances including master.");
            }

            // Check if instance exists and is online
            var instances = await ScanRemoteInstancesAsync();
            var targetInstance = instances.FirstOrDefault(i => i.Port == port);
            
            if (targetInstance == null)
            {
                LogUtil.LogError($"Instance with port {port} not found", "WebServer");
                return CreateErrorResponse($"Instance with port {port} not found");
            }

            if (!targetInstance.IsOnline)
            {
                LogUtil.LogError($"Instance with port {port} is not online", "WebServer");
                return CreateErrorResponse($"Instance with port {port} is not online");
            }

            // Check if any update is already in progress
            if (UpdateManager.IsUpdateInProgress())
            {
                var currentState = UpdateManager.GetCurrentState();
                
                // Check if this specific instance is already being updated
                if (currentState?.Instances?.Any(i => i.TcpPort == port) == true)
                {
                    var instanceState = currentState.Instances.First(i => i.TcpPort == port);
                    
                    LogUtil.LogInfo($"Instance {port} is already updating. Status: {instanceState.Status}", "WebServer");
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = $"Instance {port} is already being updated",
                        status = instanceState.Status.ToString(),
                        error = instanceState.Error
                    }, JsonOptions);
                }
                
                // Another update is in progress but not for this instance
                LogUtil.LogError($"Cannot update instance {port} - another update is in progress", "WebServer");
                return CreateErrorResponse("Another update is already in progress. Please wait for it to complete or clear the session.");
            }

            // Check if restart is in progress
            if (RestartManager.IsRestartInProgress)
            {
                LogUtil.LogError($"Cannot update instance {port} - restart is in progress", "WebServer");
                return CreateErrorResponse("Cannot update while restart is in progress");
            }

            // Parse request body for optional parameters
            bool forceUpdate = false;
            if (request.ContentLength64 > 0 && request.ContentLength64 < 1024) // Limit request size
            {
                using var reader = new StreamReader(request.InputStream);
                var body = await reader.ReadToEndAsync();
                
                var sanitizedJson = SanitizeJsonInput(body);
                if (!string.IsNullOrEmpty(sanitizedJson))
                {
                    try
                    {
                        var requestData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(sanitizedJson, CachedJsonOptions.Secure);
                        if (requestData?.ContainsKey("force") == true)
                        {
                            forceUpdate = requestData["force"].GetBoolean();
                        }
                    }
                    catch (JsonException ex)
                    {
                        LogUtil.LogError($"Failed to parse request body: {ex.Message}", "WebServer");
                        // Continue without force flag
                    }
                }
            }
            else if (request.ContentLength64 >= 1024)
            {
                LogUtil.LogError($"Request body too large: {request.ContentLength64} bytes", "WebServer");
                return CreateErrorResponse("Request body too large");
            }

            LogUtil.LogInfo($"Starting single instance update for port {port} (Force: {forceUpdate})", "WebServer");

            // Start the update for the single instance
            var success = await UpdateManager.UpdateSingleInstanceAsync(_mainForm, port, _cts.Token);

            if (success)
            {
                LogUtil.LogInfo($"Successfully updated instance on port {port}", "WebServer");
                
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Instance on port {port} updated successfully",
                    port,
                    processId = targetInstance.ProcessId
                }, JsonOptions);
            }
            else
            {
                LogUtil.LogError($"Failed to update instance on port {port}", "WebServer");
                
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Failed to update instance on port {port}",
                    port,
                    error = "Update failed - check logs for details"
                }, JsonOptions);
            }
        }
        catch (OperationCanceledException)
        {
            LogUtil.LogError($"Update for instance {port} was cancelled", "WebServer");
            return CreateErrorResponse($"Update for instance {port} was cancelled");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error updating single instance {port}: {ex.Message}", "WebServer");
            LogUtil.LogError($"Stack trace: {ex.StackTrace}", "WebServer");
            return CreateErrorResponse($"Failed to update instance: {ex.Message}");
        }
    }

    private static async Task<string> RestartAllInstances(HttpListenerRequest _)
    {
        try
        {
            var result = await RestartManager.TriggerManualRestartAsync();

            return JsonSerializer.Serialize(new
            {
                result.Success,
                result.TotalInstances,
                result.MasterRestarting,
                result.Error,
                Reason = result.Reason.ToString(),
                Results = result.InstanceResults.Select(r => new
                {
                    r.Port,
                    r.ProcessId,
                    r.Success,
                    r.Error
                }),
                Message = result.Success ? "Restart completed successfully" : "Restart failed"
            });
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex.Message);
        }
    }

    private static string GetRestartStatus()
    {
        try
        {
            var state = RestartManager.CurrentState;
            var isInProgress = RestartManager.IsRestartInProgress;

            return JsonSerializer.Serialize(new
            {
                state = state.ToString().ToLowerInvariant(),
                message = GetStateMessage(state),
                inProgress = isInProgress,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to get restart status: {ex.Message}");
        }
    }

    private static string GetStateMessage(RestartState state)
    {
        return state switch
        {
            RestartState.Idle => "No restart in progress",
            RestartState.Preparing => "Preparing restart sequence",
            RestartState.DiscoveringInstances => "Discovering all instances",
            RestartState.IdlingBots => "Sending idle commands to all bots",
            RestartState.WaitingForIdle => "Waiting for bots to become idle",
            RestartState.RestartingSlaves => "Restarting slave instances",
            RestartState.RestartingMaster => "Restarting master instance",
            _ => "Processing..."
        };
    }

    private static async Task<string> UpdateRestartSchedule(HttpListenerRequest request)
    {
        try
        {
            if (request.HttpMethod == "GET")
            {
                var config = RestartManager.GetScheduleConfig();
                var nextRestart = RestartManager.NextScheduledRestart;
                
                var response = new
                {
                    config.Enabled,
                    config.Time,
                    NextRestart = nextRestart?.ToString("yyyy-MM-dd HH:mm:ss"),
                    RestartManager.IsRestartInProgress,
                    CurrentState = RestartManager.CurrentState.ToString()
                };

                return JsonSerializer.Serialize(response);
            }
            else if (request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(request.InputStream);
                var body = await reader.ReadToEndAsync();

                LogUtil.LogInfo($"Received restart schedule POST: {body}", "WebServer");

                // Use case-insensitive deserialization for RestartScheduleConfig
                var restartConfigOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = false
                };

                var config = JsonSerializer.Deserialize<RestartScheduleConfig>(body, restartConfigOptions);
                if (config == null)
                {
                    LogUtil.LogError($"Failed to deserialize RestartScheduleConfig from: {body}", "WebServer");
                    return CreateErrorResponse("Invalid schedule configuration");
                }

                LogUtil.LogInfo($"Updating restart schedule - Enabled: {config.Enabled}, Time: {config.Time}", "WebServer");
                RestartManager.UpdateScheduleConfig(config);
                
                var result = new 
                { 
                    Success = true, 
                    Message = "Restart schedule updated successfully",
                    NextRestart = RestartManager.NextScheduledRestart?.ToString("yyyy-MM-dd HH:mm:ss")
                };
                
                return JsonSerializer.Serialize(result);
            }

            LogUtil.LogError($"Invalid HTTP method: {request.HttpMethod}", "WebServer");
            return CreateErrorResponse("Invalid method");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error in UpdateRestartSchedule: {ex.Message}", "WebServer");
            LogUtil.LogError($"Stack trace: {ex.StackTrace}", "WebServer");
            return CreateErrorResponse(ex.Message);
        }
    }

    private async Task<string> GetIdleStatusAsync()
    {
        try
        {
            var instances = new List<InstanceIdleInfo>();
            
            // Get local instance idle status
            var localInfo = GetLocalIdleInfo();
            instances.Add(localInfo);
            
            // Get remote instances idle status
            var remoteInstances = (await ScanRemoteInstancesAsync()).Where(i => i.IsOnline);
            instances.AddRange(GetRemoteIdleInfo(remoteInstances));
            
            var response = new IdleStatusResponse
            {
                Instances = instances,
                TotalBots = instances.Sum(i => i.TotalBots),
                TotalIdleBots = instances.Sum(i => i.IdleBots),
                AllBotsIdle = instances.All(i => i.AllIdle)
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex.Message);
        }
    }
    
    private InstanceIdleInfo GetLocalIdleInfo()
    {
        var localBots = GetBotControllers();
        var config = GetConfig();
        var nonIdleBots = new List<NonIdleBot>();
        var idleCount = 0;
        
        foreach (var controller in localBots)
        {
            var status = controller.ReadBotState();
            var upperStatus = status?.ToUpper() ?? "";
            
            if (upperStatus == "IDLE" || upperStatus == "STOPPED")
            {
                idleCount++;
            }
            else
            {
                nonIdleBots.Add(new NonIdleBot
                {
                    Name = GetBotName(controller.State, config),
                    Status = status ?? "Unknown"
                });
            }
        }
        
        return new InstanceIdleInfo
        {
            Port = _tcpPort,
            ProcessId = Environment.ProcessId,
            TotalBots = localBots.Count,
            IdleBots = idleCount,
            NonIdleBots = nonIdleBots,
            AllIdle = idleCount == localBots.Count
        };
    }
    
    private static List<InstanceIdleInfo> GetRemoteIdleInfo(IEnumerable<BotInstance> remoteInstances)
    {
        var instances = new List<InstanceIdleInfo>();
        
        foreach (var instance in remoteInstances)
        {
            try
            {
                var botsResponse = QueryRemote(instance.Port, "LISTBOTS");
                if (botsResponse.StartsWith('{') && botsResponse.Contains("Bots"))
                {
                    var botsData = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(botsResponse);
                    if (botsData?.ContainsKey("Bots") == true)
                    {
                        var bots = botsData["Bots"];
                        var idleCount = 0;
                        var nonIdleBots = new List<NonIdleBot>();
                        
                        foreach (var bot in bots)
                        {
                            if (bot.TryGetValue("Status", out var status))
                            {
                                var statusStr = status?.ToString()?.ToUpperInvariant() ?? "";
                                if (statusStr == "IDLE" || statusStr == "STOPPED")
                                {
                                    idleCount++;
                                }
                                else
                                {
                                    nonIdleBots.Add(new NonIdleBot
                                    {
                                        Name = bot.TryGetValue("Name", out var name) ? name?.ToString() ?? "Unknown" : "Unknown",
                                        Status = statusStr
                                    });
                                }
                            }
                        }
                        
                        instances.Add(new InstanceIdleInfo
                        {
                            Port = instance.Port,
                            ProcessId = instance.ProcessId,
                            TotalBots = bots.Count,
                            IdleBots = idleCount,
                            NonIdleBots = nonIdleBots,
                            AllIdle = idleCount == bots.Count
                        });
                    }
                }
            }
            catch { }
        }
        
        return instances;
    }

    private static async Task<string> CheckForUpdates()
    {
        try
        {
            var (updateAvailable, _, latestVersion) = await UpdateChecker.CheckForUpdatesAsync(false);
            var changelog = await UpdateChecker.FetchChangelogAsync();
            
            var response = new UpdateCheckResponse
            {
                Version = latestVersion ?? "Unknown",
                Changelog = changelog,
                Available = updateAvailable
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            var response = new UpdateCheckResponse
            {
                Version = "Unknown",
                Changelog = "Unable to fetch update information",
                Available = false,
                Error = ex.Message
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
    }

    private static int ExtractPort(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0;

            var parts = path.Split('/');
            if (parts.Length <= 4)
                return 0;

            var portString = parts[4];
            
            // Validate port string length to prevent overflow
            if (portString.Length > 10)
                return 0;

            if (int.TryParse(portString, out var port))
            {
                // Validate port range
                if (port > 0 && port <= 65535)
                    return port;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<string> GetInstancesAsync()
    {
        var remoteInstances = await ScanRemoteInstancesAsync();
        var response = new InstancesResponse
        {
            Instances = [CreateLocalInstance(), .. remoteInstances]
        };
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private BotInstance CreateLocalInstance()
    {
        var config = GetConfig();
        var controllers = GetBotControllers();

        var mode = config?.Mode.ToString() ?? "Unknown";
        var name = config?.Hub?.BotName ?? "ZE_FusionBot";

        var version = SysBot.Pokemon.Helpers.TradeBot.Version;

        var botStatuses = controllers.Select(c => new BotStatusInfo
        {
            Name = GetBotName(c.State, config),
            Status = c.ReadBotState()
        }).ToList();

        return new BotInstance
        {
            ProcessId = Environment.ProcessId,
            Name = name,
            Port = _tcpPort,
            WebPort = _port,
            Version = version,
            Mode = mode,
            BotCount = botStatuses.Count,
            IsOnline = true,
            IsMaster = IsMasterInstance(),
            BotStatuses = botStatuses,
            ProcessPath = Environment.ProcessPath
        };
    }

    private async Task<List<BotInstance>> ScanRemoteInstancesAsync()
    {
        var instances = new List<BotInstance>();
        var currentPid = Environment.ProcessId;
        var discoveredPorts = new HashSet<int> { _tcpPort }; // Exclude current instance port

        // Method 1: Scan TCP ports with throttling to avoid system overload
        // Only scan a smaller range by default to avoid timeout
        const int startPort = 8081;
        const int endPort = 8090; // Reduced from 8181 to 8090 for faster scanning
        const int maxConcurrentScans = 5; // Throttle concurrent connections
        
        var semaphore = new SemaphoreSlim(maxConcurrentScans, maxConcurrentScans);
        var tasks = new List<Task>();
        
        for (int port = startPort; port <= endPort; port++)
        {
            if (port == _tcpPort)
                continue;
                
            int capturedPort = port; // Capture for closure
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Port check with more reasonable timeout for slower systems
                    using var client = new TcpClient();
                    client.ReceiveTimeout = 500; // Increased from 200ms to 500ms
                    client.SendTimeout = 500;
                    
                    var connectTask = client.ConnectAsync("127.0.0.1", capturedPort);
                    var timeoutTask = Task.Delay(500);
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    if (completedTask == timeoutTask || !client.Connected)
                        return;
                    
                    using var stream = client.GetStream();
                    using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    await writer.WriteLineAsync("INFO");
                    await writer.FlushAsync();
                    
                    // Read response with timeout
                    stream.ReadTimeout = 1000; // Increased from 500ms to 1000ms
                    var response = await reader.ReadLineAsync();
                    
                    if (!string.IsNullOrEmpty(response) && response.StartsWith('{'))
                    {
                        // This is a ZE_FusionBot instance - find the process ID
                        int processId = FindProcessIdForPort(capturedPort);
                        
                        var instance = new BotInstance
                        {
                            ProcessId = processId,
                            Name = "ZE_FusionBot",
                            Port = capturedPort,
                            WebPort = 8080,
                            Version = "Unknown",
                            Mode = "Unknown",
                            BotCount = 0,
                            IsOnline = true,
                            IsMaster = false, // Will be determined by who's hosting web server
                            ProcessPath = GetProcessPathForId(processId)
                        };

                        // Update instance info from the response
                        UpdateInstanceInfo(instance, capturedPort);
                        
                        lock (instances) // Thread-safe addition
                        {
                            instances.Add(instance);
                        }
                        discoveredPorts.Add(capturedPort);
                    }
                }
                catch { /* Port not open or not a ZE_FusionBot instance */ }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
        
        // Wait for all port scans to complete with overall timeout
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Method 2: Check local ZE_FusionBot processes (fallback for instances not in standard port range)
        try
        {
            var processes = Process.GetProcessesByName("ZE_FusionBot")
                .Where(p => p.Id != currentPid);

            foreach (var process in processes)
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                        continue;

                    var botName = "ZE_FusionBot"; // default
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        botName = Path.GetFileNameWithoutExtension(exePath);
                    }

                    var portFile = Path.Combine(Path.GetDirectoryName(exePath)!, $"ZE_FusionBot_{process.Id}.port");
                    if (!File.Exists(portFile))
                        continue;

                    var portText = File.ReadAllText(portFile).Trim();
                    if (portText.StartsWith("ERROR:"))
                        continue;
                        
                    // Port file now only contains TCP port
                    var lines = portText.Split('\n', '\r').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                    if (lines.Length == 0 || !int.TryParse(lines[0], out var port))
                        continue;

                    // Skip if already discovered
                    if (discoveredPorts.Contains(port))
                        continue;

                    var isOnline = IsPortOpen(port);
                    var instance = new BotInstance
                    {
                        ProcessId = process.Id,
                        Name = botName,
                        Port = port,
                        WebPort = 8080,
                        Version = "Unknown",
                        Mode = "Unknown",
                        BotCount = 0,
                        IsOnline = isOnline,
                        ProcessPath = exePath
                    };

                    if (isOnline)
                    {
                        UpdateInstanceInfo(instance, port);
                    }

                    instances.Add(instance);
                    discoveredPorts.Add(port);
                }
                catch { /* Ignore */ }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error scanning local processes: {ex.Message}", "WebServer");
        }

        return instances;
    }

    /// <summary>
    /// Find process ID for a given port by checking port files
    /// </summary>
    private static int FindProcessIdForPort(int port)
    {
        try
        {
            var processes = Process.GetProcessesByName("ZE_FusionBot");
            foreach (var proc in processes)
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                        continue;

                    var portFile = Path.Combine(Path.GetDirectoryName(exePath)!, $"ZE_FusionBot_{proc.Id}.port");
                    if (File.Exists(portFile))
                    {
                        var portText = File.ReadAllText(portFile).Trim();
                        if (int.TryParse(portText, out var filePort) && filePort == port)
                        {
                            return proc.Id;
                        }
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
        
        return 0; // Process not found
    }

    /// <summary>
    /// Get process path for a given process ID
    /// </summary>
    private static string? GetProcessPathForId(int processId)
    {
        if (processId == 0) return null;
        
        try
        {
            var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch 
        {
            return null;
        }
    }

    private static void UpdateInstanceInfo(BotInstance instance, int port)
    {
        try
        {
            var infoResponse = QueryRemote(port, "INFO");
            if (infoResponse.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(infoResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("Version", out var version))
                    instance.Version = version.GetString() ?? "Unknown";

                if (root.TryGetProperty("Mode", out var mode))
                    instance.Mode = mode.GetString() ?? "Unknown";

                if (root.TryGetProperty("Name", out var name))
                    instance.Name = name.GetString() ?? "ZE_FusionBot";
                    
                if (root.TryGetProperty("ProcessPath", out var processPath))
                    instance.ProcessPath = processPath.GetString();
            }

            var botsResponse = QueryRemote(port, "LISTBOTS");
            if (botsResponse.StartsWith('{') && botsResponse.Contains("Bots"))
            {
                var botsData = JsonSerializer.Deserialize<Dictionary<string, List<BotInfo>>>(botsResponse);
                if (botsData?.ContainsKey("Bots") == true)
                {
                    instance.BotCount = botsData["Bots"].Count;
                    instance.BotStatuses = [.. botsData["Bots"].Select(b => new BotStatusInfo
                    {
                        Name = b.Name,
                        Status = b.Status
                    })];
                }
            }
        }
        catch { }
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            // Validate port range
            if (port <= 0 || port > 65535)
                return false;

            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (success)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private string GetBots(int port)
    {
        try
        {
            if (port == _tcpPort)
            {
                var config = GetConfig();
                var controllers = GetBotControllers();

                var response = new BotsResponse
                {
                    Bots = [.. controllers.Select(c => new BotInfo
                    {
                        Id = $"{c.State.Connection.IP}:{c.State.Connection.Port}",
                        Name = GetBotName(c.State, config),
                        RoutineType = c.State.InitialRoutine.ToString(),
                        Status = c.ReadBotState(),
                        ConnectionType = c.State.Connection.Protocol.ToString(),
                        IP = c.State.Connection.IP,
                        Port = c.State.Connection.Port
                    })]
                };

                var json = JsonSerializer.Serialize(response, JsonOptions);
                LogUtil.LogInfo($"GetBots returning {json.Length} bytes for port {port}", "WebAPI");
                return json;
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error in GetBots for port {port}: {ex.Message}", "WebAPI");
            return JsonSerializer.Serialize(new BotsResponse 
            { 
                Bots = [],
                Error = $"Error getting bots: {ex.Message}"
            }, JsonOptions);
        }

        // Query remote instance
        var result = QueryRemote(port, "LISTBOTS");
        
        // Check if the result is an error
        if (result.StartsWith("ERROR"))
        {
            return JsonSerializer.Serialize(new BotsResponse 
            { 
                Bots = [],
                Error = result
            }, JsonOptions);
        }
        
        // If it's already valid JSON, return it
        // Otherwise wrap it in a valid response
        try
        {
            // Try to parse to validate it's JSON
            using var doc = JsonDocument.Parse(result);
            return result;
        }
        catch
        {
            // If not valid JSON, return empty bot list with error
            return JsonSerializer.Serialize(new BotsResponse 
            { 
                Bots = [],
                Error = "Invalid response from remote instance"
            }, JsonOptions);
        }
    }

    private async Task<string> RunCommand(HttpListenerRequest request, int port)
    {
        try
        {
            var commandRequest = await DeserializeRequestAsync<BotCommandRequest>(request);
            if (commandRequest == null)
                return CreateErrorResponse("Invalid command request");

            if (port == _tcpPort)
            {
                return RunLocalCommand(commandRequest.Command);
            }

            var tcpCommand = $"{commandRequest.Command}All".ToUpper();
            var result = QueryRemote(port, tcpCommand);

            var response = new CommandResponse
            {
                Message = result,
                Port = port,
                Command = commandRequest.Command,
                Error = result.StartsWith("ERROR") ? result : null
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex.Message);
        }
    }

    private async Task<string> RunAllCommandAsync(HttpListenerRequest request)
    {
        try
        {
            var commandRequest = await DeserializeRequestAsync<BotCommandRequest>(request);
            if (commandRequest == null)
                return CreateErrorResponse("Invalid command request");

            var results = await ExecuteCommandOnAllInstancesAsync(commandRequest.Command);
            
            var response = new BatchCommandResponse
            {
                Results = results,
                TotalInstances = results.Count,
                SuccessfulCommands = results.Count(r => r.Success)
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex.Message);
        }
    }
    
    private async Task<List<CommandResponse>> ExecuteCommandOnAllInstancesAsync(string command)
    {
        var tasks = new List<Task<CommandResponse?>>
        {
            // Execute local command
            Task.Run(() =>
            {
                var localResult = JsonSerializer.Deserialize<CommandResponse>(RunLocalCommand(command), JsonOptions);
                if (localResult != null)
                {
                    localResult.InstanceName = _mainForm.Text;
                }
                return localResult;
            })
        };
        
        // Execute remote commands
        var remoteInstances = (await ScanRemoteInstancesAsync()).Where(i => i.IsOnline);
        foreach (var instance in remoteInstances)
        {
            var instanceCopy = instance; // Capture for closure
            tasks.Add(Task.Run<CommandResponse?>(() =>
            {
                try
                {
                    var result = QueryRemote(instanceCopy.Port, $"{command}All".ToUpper());
                    return new CommandResponse
                    {
                        Message = result,
                        Port = instanceCopy.Port,
                        Command = command,
                        InstanceName = instanceCopy.Name,
                        Error = result.StartsWith("ERROR") ? result : null
                    };
                }
                catch (Exception ex)
                {
                    return new CommandResponse
                    {
                        Error = ex.Message,
                        Port = instanceCopy.Port,
                        Command = command,
                        InstanceName = instanceCopy.Name
                    };
                }
            }));
        }
        
        var results = await Task.WhenAll(tasks);
        return [.. results.Where(r => r != null).Cast<CommandResponse>()];
    }

    private string RunLocalCommand(string command)
    {
        try
        {
            var cmd = MapCommand(command);
            ExecuteMainFormCommand(cmd);
            
            var response = new CommandResponse
            {
                Message = $"Command {command} sent successfully",
                Port = _tcpPort,
                Command = command
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error executing local command {command}: {ex.Message}", "WebServer");
            // Still return success as the command was queued
            var response = new CommandResponse
            {
                Message = $"Command {command} queued",
                Port = _tcpPort,
                Command = command
            };
            
            return JsonSerializer.Serialize(response, JsonOptions);
        }
    }
    
    private void ExecuteMainFormCommand(BotControlCommand command)
    {
        _mainForm.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
        {
            const string methodName = "SendAll";
            if (AllowedMethods.Contains(methodName))
            {
                var sendAllMethod = _mainForm.GetType().GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                sendAllMethod?.Invoke(_mainForm, new object[] { command });
            }
        }));
    }

    private static BotControlCommand MapCommand(string webCommand)
    {
        return webCommand.ToLower() switch
        {
            "start" => WebApi.BotControlCommand.Start,
            "stop" => WebApi.BotControlCommand.Stop,
            "idle" => WebApi.BotControlCommand.Idle,
            "resume" => WebApi.BotControlCommand.Resume,
            "restart" => WebApi.BotControlCommand.Restart,
            "reboot" => WebApi.BotControlCommand.RebootAndStop,
            "screenon" => WebApi.BotControlCommand.ScreenOnAll,
            "screenoff" => WebApi.BotControlCommand.ScreenOffAll,
            _ => WebApi.BotControlCommand.None
        };
    }

    public static string QueryRemote(int port, string command)
    {
        try
        {
            // Validate inputs
            if (port <= 0 || port > 65535)
            {
                return "ERROR: Invalid port number";
            }

            if (string.IsNullOrWhiteSpace(command) || command.Length > 1000)
            {
                return "ERROR: Invalid command";
            }

            // Sanitize command to prevent injection
            var sanitizedCommand = SanitizeCommand(command);
            if (sanitizedCommand == null)
            {
                return "ERROR: Command contains invalid characters";
            }

            using var client = new TcpClient();
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            var connectTask = client.ConnectAsync("127.0.0.1", port);
            if (!connectTask.Wait(5000))
            {
                return "ERROR: Connection timeout";
            }

            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            writer.WriteLine(sanitizedCommand);
            var response = reader.ReadLine();
            
            // Limit response size to prevent DoS
            if (response != null && response.Length > 10000)
            {
                response = string.Concat(response.AsSpan(0, 10000), "... [truncated]");
            }
            
            return response ?? "ERROR: No response";
        }
        catch (Exception ex)
        {
            // Sanitize exception message to prevent information disclosure
            var sanitizedMessage = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            return $"ERROR: {sanitizedMessage}";
        }
    }

    private List<BotController> GetBotControllers()
    {
        try
        {
            var results = new List<BotController>();

            var type = _mainForm.GetType();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // 1) Try direct field named FLP_Bots (legacy)
            var flpField = type.GetField("FLP_Bots", flags);
            if (flpField?.GetValue(_mainForm) is FlowLayoutPanel flp1)
                results.AddRange(flp1.Controls.OfType<BotController>());

            // 2) Scan all fields/properties on Main for FlowLayoutPanel
            foreach (var f in type.GetFields(flags))
            {
                if (typeof(FlowLayoutPanel).IsAssignableFrom(f.FieldType) && f.GetValue(_mainForm) is FlowLayoutPanel flp)
                    results.AddRange(flp.Controls.OfType<BotController>());
            }

            foreach (var p in type.GetProperties(flags))
            {
                if (typeof(FlowLayoutPanel).IsAssignableFrom(p.PropertyType))
                {
                    try
                    {
                        if (p.GetValue(_mainForm) is FlowLayoutPanel flpProp)
                            results.AddRange(flpProp.Controls.OfType<BotController>());
                    }
                    catch { /* ignore property getters with side effects */ }
                }
            }

            // 3) Try the common pattern: a private field/property _botsForm that contains BotPanel
            var botsFormField = type.GetField("_botsForm", flags) ?? (MemberInfo?)type.GetProperty("_botsForm", flags);
            if (botsFormField != null)
            {
                object? botsFormObj = null;
                if (botsFormField is FieldInfo fi) botsFormObj = fi.GetValue(_mainForm);
                else if (botsFormField is PropertyInfo pi) botsFormObj = pi.GetValue(_mainForm);
                if (botsFormObj != null)
                {
                    var botPanelProp = botsFormObj.GetType().GetProperty("BotPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (botPanelProp?.GetValue(botsFormObj) is FlowLayoutPanel botPanel)
                        results.AddRange(botPanel.Controls.OfType<BotController>());
                }
            }

            // Final: dedupe and return
            return results.Distinct().ToList();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"GetBotControllers failed: {ex.Message}", "WebServer");
            return new List<BotController>();
        }
    }

    private ProgramConfig? GetConfig()
    {
        var type = _mainForm.GetType();

        // Try a static property first (Main.Config is public static in your Main)
        var staticProp = type.GetProperty("Config", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (staticProp != null)
            return staticProp.GetValue(null) as ProgramConfig;

        // Fallback: try instance property
        var instProp = type.GetProperty("Config", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (instProp != null)
            return instProp.GetValue(_mainForm) as ProgramConfig;

        return null;
    }

    private static string GetBotName(PokeBotState state, ProgramConfig? _)
    {
        return state.Connection.IP;
    }

    private string GetQueueStatus()
    {
        try
        {
            var config = GetConfig();
            if (config?.Hub == null)
            {
                return JsonSerializer.Serialize(new
                {
                    queueCount = 0,
                    maxQueueCount = 30,
                    isFull = false,
                    canQueue = true,
                    message = "Queue information unavailable"
                }, JsonOptions);
            }

            // Get the Hub from the config using reflection
            var hubProperty = config.Hub.GetType().GetProperty("Queues");
            if (hubProperty?.GetValue(config.Hub) is not object queues)
            {
                return JsonSerializer.Serialize(new
                {
                    queueCount = 0,
                    maxQueueCount = config.Hub.Queues.MaxQueueCount,
                    isFull = false,
                    canQueue = config.Hub.Queues.CanQueue,
                    message = "Queue system not initialized"
                }, JsonOptions);
            }

            var infoProperty = queues.GetType().GetProperty("Info");
            if (infoProperty?.GetValue(queues) is not object info)
            {
                return JsonSerializer.Serialize(new
                {
                    queueCount = 0,
                    maxQueueCount = config.Hub.Queues.MaxQueueCount,
                    isFull = false,
                    canQueue = config.Hub.Queues.CanQueue,
                    message = "Queue info not available"
                }, JsonOptions);
            }

            var countProperty = info.GetType().GetProperty("Count");
            var queueCount = countProperty?.GetValue(info) as int? ?? 0;
            var maxQueueCount = config.Hub.Queues.MaxQueueCount;
            var isFull = queueCount >= maxQueueCount;
            var canQueue = config.Hub.Queues.CanQueue && !isFull;

            return JsonSerializer.Serialize(new
            {
                queueCount,
                maxQueueCount,
                isFull,
                canQueue,
                message = isFull ? "Queue is currently full" : canQueue ? "Queue is open" : "Queue is closed"
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error getting queue status: {ex.Message}", "WebServer");
            return JsonSerializer.Serialize(new
            {
                queueCount = 0,
                maxQueueCount = 30,
                isFull = false,
                canQueue = false,
                message = "Error retrieving queue status"
            }, JsonOptions);
        }
    }

    private static string CreateErrorResponse(string message)
    {
        return JsonSerializer.Serialize(ApiResponseFactory.CreateSimpleError(message), JsonOptions);
    }

    private bool IsMasterInstance()
    {
        // Master is the instance hosting the web server on the configured control panel port
        var configuredPort = Main.Config?.Hub?.WebServer?.ControlPanelPort ?? 8080;
        return _port == configuredPort;
    }
    

    private static bool IsAllowedOrigin(string origin)
    {
        // Define allowed origins - adjust based on your security requirements
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http://localhost",
            "https://localhost",
            "http://127.0.0.1",
            "https://127.0.0.1"
        };

        // Allow localhost with any port
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (uri.Host == "localhost" || uri.Host == "127.0.0.1")
            {
                return true;
            }
        }

        return allowedOrigins.Contains(origin);
    }


    private static string ClearUpdateSession()
    {
        try
        {
            // First try to force complete if there's a stuck session
            var currentState = UpdateManager.GetCurrentState();
            if (currentState != null)
            {
                // Check if master instance actually updated
                var currentVersion = SysBot.Pokemon.Helpers.TradeBot.Version;
                LogUtil.LogInfo($"Checking session state: current={currentVersion}, target={currentState.TargetVersion}, isComplete={currentState.IsComplete}", "WebServer");
                
                // If version matches target, force complete regardless of what the state says
                if (currentVersion == currentState.TargetVersion)
                {
                    if (!currentState.IsComplete || !currentState.Success)
                    {
                        LogUtil.LogInfo("Force completing update session - version matches target", "WebServer");
                        UpdateManager.ForceCompleteSession();
                        return JsonSerializer.Serialize(new { 
                            success = true, 
                            message = "Update completed successfully - all instances updated to target version",
                            action = "force_completed",
                            currentVersion,
                            targetVersion = currentState.TargetVersion
                        }, JsonOptions);
                    }
                    else
                    {
                        // Already complete and successful
                        UpdateManager.ClearState();
                        return JsonSerializer.Serialize(new { 
                            success = true, 
                            message = "Update was already successful - session cleared",
                            action = "cleared",
                            currentVersion
                        }, JsonOptions);
                    }
                }
                else
                {
                    LogUtil.LogInfo($"Version mismatch - clearing session (current={currentVersion}, target={currentState.TargetVersion})", "WebServer");
                }
            }
            
            // Clear the session
            UpdateManager.ClearState();
            return JsonSerializer.Serialize(new { 
                success = true, 
                message = "Update session cleared",
                action = "cleared"
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error clearing update session: {ex.Message}", "WebServer");
            return CreateErrorResponse(ex.Message);
        }
    }

    private static string GetActiveUpdates()
    {
        try
        {
            var session = UpdateManager.GetCurrentState();
            if (session == null)
            {
                return JsonSerializer.Serialize(new { active = false }, JsonOptions);
            }

            var response = new
            {
                active = true,
                session = new
                {
                    id = session.SessionId,
                    phase = session.Phase.ToString(),
                    message = session.Message,
                    totalInstances = session.TotalInstances,
                    completedInstances = session.CompletedInstances,
                    failedInstances = session.FailedInstances,
                    isComplete = session.IsComplete,
                    success = session.Success,
                    startTime = session.StartTime.ToString("o"),
                    targetVersion = session.TargetVersion,
                    currentUpdatingInstance = session.CurrentUpdatingInstance,
                    idleProgress = session.IdleProgress != null ? new
                    {
                        startTime = session.IdleProgress.StartTime.ToString("o"),
                        totalBots = session.IdleProgress.TotalBots,
                        idleBots = session.IdleProgress.IdleBots,
                        allIdle = session.IdleProgress.AllIdle,
                        elapsedSeconds = (int)session.IdleProgress.ElapsedTime.TotalSeconds,
                        remainingSeconds = Math.Max(0, (int)session.IdleProgress.TimeRemaining.TotalSeconds),
                        instances = session.IdleProgress.Instances.Select(i => new
                        {
                            tcpPort = i.TcpPort,
                            name = i.Name,
                            isMaster = i.IsMaster,
                            totalBots = i.TotalBots,
                            idleBots = i.IdleBots,
                            allIdle = i.AllIdle,
                            nonIdleBots = i.NonIdleBots
                        }).ToList()
                    } : null,
                    instances = session.Instances.Select(i => new
                    {
                        tcpPort = i.TcpPort,
                        processId = i.ProcessId,
                        isMaster = i.IsMaster,
                        status = i.Status.ToString(),
                        error = i.Error,
                        retryCount = i.RetryCount,
                        updateStartTime = i.UpdateStartTime?.ToString("o"),
                        updateEndTime = i.UpdateEndTime?.ToString("o"),
                        version = i.Version
                    }).ToList()
                }
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error getting active updates: {ex.Message}", "WebServer");
            return CreateErrorResponse(ex.Message);
        }
    }

    private static byte[]? GetIconBytes()
    {
        try
        {
            // First try to find icon.ico in the executable directory
            var exePath = Application.ExecutablePath;
            var exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            var iconPath = Path.Combine(exeDir, "icon.ico");
            
            if (File.Exists(iconPath))
            {
                return File.ReadAllBytes(iconPath);
            }
            
            // If not found, try to extract from embedded resources
            var assembly = Assembly.GetExecutingAssembly();
            var iconStream = assembly.GetManifestResourceStream("SysBot.Pokemon.WinForms.icon.ico");
            
            if (iconStream != null)
            {
                using (iconStream)
                {
                    var buffer = new byte[iconStream.Length];
                    iconStream.ReadExactly(buffer);
                    return buffer;
                }
            }
            
            // Try to get the application icon as a fallback
            var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon != null)
            {
                using var ms = new MemoryStream();
                icon.Save(ms);
                return ms.ToArray();
            }
            
            return null;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to load icon: {ex.Message}", "WebServer");
            return null;
        }
    }


    private static async Task<T?> DeserializeRequestAsync<T>(HttpListenerRequest request) where T : class
    {
        try
        {
            // Limit request size to prevent DoS attacks
            if (request.ContentLength64 > 10000)
                return null;

            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            
            var sanitizedJson = SanitizeJsonInput(body);
            if (sanitizedJson == null)
                return null;
                
            return JsonSerializer.Deserialize<T>(sanitizedJson, CachedJsonOptions.Secure);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sanitize query parameters to prevent injection attacks
    /// </summary>
    private static string? SanitizeQueryParameter(string? parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(parameter))
            return null;

        if (parameter.Length > maxLength)
            parameter = parameter[..maxLength];

        // Remove potentially dangerous characters
        var sanitized = CleanupRegex().Replace(parameter, "");
        
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
    
    
    
    
    private async Task<string> HandleRemoteButton(HttpListenerRequest request, int port)
    {
        try
        {
            var requestData = await DeserializeRequestAsync<RemoteButtonRequest>(request);
            if (requestData == null)
                return CreateErrorResponse("Invalid remote button request");

            // Send button command via TCP to the target instance
            var tcpCommand = $"REMOTE_BUTTON:{requestData.Button}:{requestData.BotIndex}";
            
            if (port == _tcpPort)
            {
                // Local command - execute directly
                return await ExecuteLocalRemoteButton(requestData.Button, requestData.BotIndex);
            }
            
            // Remote command
            var result = QueryRemote(port, tcpCommand);
            
            return JsonSerializer.Serialize(new
            {
                success = !result.StartsWith("ERROR"),
                message = result,
                port,
                button = requestData.Button
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to send button command: {ex.Message}");
        }
    }
    
    private async Task<string> HandleRemoteMacro(HttpListenerRequest request, int port)
    {
        try
        {
            var requestData = await DeserializeRequestAsync<RemoteMacroRequest>(request);
            if (requestData == null)
                return CreateErrorResponse("Invalid remote macro request");

            // Send macro command via TCP to the target instance  
            var tcpCommand = $"REMOTE_MACRO:{requestData.Macro}:{requestData.BotIndex}";
            
            if (port == _tcpPort)
            {
                // Local command - execute directly
                return await ExecuteLocalRemoteMacro(requestData.Macro, requestData.BotIndex);
            }
            
            // Remote command
            var result = QueryRemote(port, tcpCommand);
            
            return JsonSerializer.Serialize(new
            {
                success = !result.StartsWith("ERROR"),
                message = result,
                port,
                macro = requestData.Macro
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to execute macro: {ex.Message}");
        }
    }
    
    private async Task<string> ExecuteLocalRemoteButton(string button, int botIndex = 0)
    {
        try
        {
            // Get all bot controllers
            var controllers = await Task.Run(() =>
            {
                if (_mainForm.InvokeRequired)
                {
                    return _mainForm.Invoke(() => 
                        _mainForm.Controls.Find("FLP_Bots", true).FirstOrDefault()?.Controls
                            .OfType<BotController>()
                            .ToList()
                    ) as List<BotController> ?? [];
                }
                return _mainForm.Controls.Find("FLP_Bots", true).FirstOrDefault()?.Controls
                    .OfType<BotController>()
                    .ToList() ?? [];
            });
            
            if (controllers.Count == 0)
                return CreateErrorResponse("No bots available");
                
            // Validate bot index
            if (botIndex < 0 || botIndex >= controllers.Count)
                return CreateErrorResponse($"Invalid bot index: {botIndex}");
                
            var botSource = controllers[botIndex].GetBot();
            if (botSource?.Bot == null)
                return CreateErrorResponse($"Bot at index {botIndex} not available");
                
            if (!botSource.IsRunning)
                return CreateErrorResponse($"Bot at index {botIndex} is not running");
                
            var bot = botSource.Bot;
            if (bot.Connection is not ISwitchConnectionAsync connection)
                return CreateErrorResponse("Bot connection not available");
            
            var switchButton = MapButtonToSwitch(button);
            if (switchButton == null)
                return CreateErrorResponse($"Invalid button: {button}");
            
            var cmd = SwitchCommand.Click(switchButton.Value);
            await connection.SendAsync(cmd, CancellationToken.None);
            
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Button {button} pressed on bot {botIndex}",
                button,
                botIndex
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to execute button press: {ex.Message}");
        }
    }
    
    private async Task<string> ExecuteLocalRemoteMacro(string macro, int botIndex = 0)
    {
        try
        {
            // Get all bot controllers
            var controllers = await Task.Run(() =>
            {
                if (_mainForm.InvokeRequired)
                {
                    return _mainForm.Invoke(() => 
                        _mainForm.Controls.Find("FLP_Bots", true).FirstOrDefault()?.Controls
                            .OfType<BotController>()
                            .ToList()
                    ) as List<BotController> ?? [];
                }
                return _mainForm.Controls.Find("FLP_Bots", true).FirstOrDefault()?.Controls
                    .OfType<BotController>()
                    .ToList() ?? [];
            });
            
            if (controllers.Count == 0)
                return CreateErrorResponse("No bots available");
                
            // Validate bot index
            if (botIndex < 0 || botIndex >= controllers.Count)
                return CreateErrorResponse($"Invalid bot index: {botIndex}");
                
            var botSource = controllers[botIndex].GetBot();
            if (botSource?.Bot == null)
                return CreateErrorResponse($"Bot at index {botIndex} not available");
                
            if (!botSource.IsRunning)
                return CreateErrorResponse($"Bot at index {botIndex} is not running");
                
            var bot = botSource.Bot;
            if (bot.Connection is not ISwitchConnectionAsync connection)
                return CreateErrorResponse("Bot connection not available");
            
            var commands = ParseMacroCommands(macro);
            foreach (var cmd in commands)
            {
                if (cmd.StartsWith('d'))
                {
                    // Delay command
                    if (int.TryParse(cmd[1..], out int delay))
                    {
                        await Task.Delay(delay);
                    }
                }
                else
                {
                    // Button command
                    var parts = cmd.Split(':', 2);
                    var buttonName = parts[0];
                    
                    var switchButton = MapButtonToSwitch(buttonName);
                    if (switchButton != null)
                    {
                        var command = SwitchCommand.Click(switchButton.Value);
                        await connection.SendAsync(command, CancellationToken.None);
                        await Task.Delay(100); // Small delay between button presses
                    }
                }
            }
            
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Macro executed successfully on bot {botIndex}",
                commandCount = commands.Count,
                botIndex
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to execute macro: {ex.Message}");
        }
    }
    
    private static SwitchButton? MapButtonToSwitch(string button)
    {
        return button.ToUpper() switch
        {
            "A" => SwitchButton.A,
            "B" => SwitchButton.B,
            "X" => SwitchButton.X,
            "Y" => SwitchButton.Y,
            "L" => SwitchButton.L,
            "R" => SwitchButton.R,
            "ZL" => SwitchButton.ZL,
            "ZR" => SwitchButton.ZR,
            "PLUS" or "+" => SwitchButton.PLUS,
            "MINUS" or "-" => SwitchButton.MINUS,
            "LSTICK" or "LTS" => SwitchButton.LSTICK,
            "RSTICK" or "RTS" => SwitchButton.RSTICK,
            "HOME" or "H" => SwitchButton.HOME,
            "CAPTURE" or "SS" => SwitchButton.CAPTURE,
            "UP" or "DUP" => SwitchButton.DUP,
            "DOWN" or "DDOWN" => SwitchButton.DDOWN,
            "LEFT" or "DLEFT" => SwitchButton.DLEFT,
            "RIGHT" or "DRIGHT" => SwitchButton.DRIGHT,
            _ => null
        };
    }
    
    private static List<string> ParseMacroCommands(string macro)
    {
        return [.. macro.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())];
    }
    
    private class RemoteButtonRequest
    {
        public string Button { get; set; } = "";
        public int BotIndex { get; set; } = 0;
    }
    
    private class RemoteMacroRequest
    {
        public string Macro { get; set; } = "";
        public int BotIndex { get; set; } = 0;
    }
    
    /// <summary>
    /// Sanitize command strings to prevent injection attacks
    /// </summary>
    private static string? SanitizeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        // Allow only alphanumeric characters, underscores, colons, and common command separators
        // This whitelist approach is more secure than blacklisting
        var allowedPattern = @"^[a-zA-Z0-9_:.-]+$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(command, allowedPattern))
            return null;

        // Additional validation for known command patterns
        var validCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INFO", "LISTBOTS", "IDLEALL", "UPDATE", "STARTALL", "STOPALL", 
            "RESUMEALL", "RESTARTALL", "REBOOTALL", "SCREENONALL", "SCREENOFFALL"
        };

        // Check if it's a basic command or a compound command
        var parts = command.Split(':');
        var baseCommand = parts[0].ToUpperInvariant();
        
        if (validCommands.Contains(baseCommand) || 
            baseCommand.StartsWith("REMOTE_") && parts.Length <= 3)
        {
            return command;
        }

        return null;
    }

    /// <summary>
    /// Sanitize JSON input to prevent injection attacks
    /// </summary>
    private static string? SanitizeJsonInput(string json, int maxLength = 10000)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        if (json.Length > maxLength)
            return null;

        try
        {
            // Validate JSON structure by attempting to parse it
            using var document = JsonDocument.Parse(json);
            
            // Check for excessive nesting depth
            if (GetJsonDepth(document.RootElement) > 10)
                return null;

            return json;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calculate JSON nesting depth to prevent deeply nested attacks
    /// </summary>
    private static int GetJsonDepth(JsonElement element, int currentDepth = 0)
    {
        if (currentDepth > 10) // Prevent stack overflow from deeply nested JSON
            return currentDepth;

        var maxDepth = currentDepth;
        
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var depth = GetJsonDepth(property.Value, currentDepth + 1);
                maxDepth = Math.Max(maxDepth, depth);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var depth = GetJsonDepth(item, currentDepth + 1);
                maxDepth = Math.Max(maxDepth, depth);
            }
        }

        return maxDepth;
    }
    
    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
