using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using SSMD.Models;

namespace SSMD.Services;

public class SatisfactoryApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ServerConfig _serverConfig;
    private bool _disposed = false;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;

    public SatisfactoryApiService(ServerConfig serverConfig)
    {
        _serverConfig = serverConfig;
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true; // Trust all certs
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // Add timeout
    }

    public async Task<ApiResponse<T>> MakeRequestAsync<T>(string function, object? data = null)
    {
        if (_disposed)
        {
            return new ApiResponse<T>
            {
                Success = false,
                ErrorMessage = "Service has been disposed"
            };
        }

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var request = new
                {
                    function = function,
                    data = data ?? new { }
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Clear any previous headers
                _httpClient.DefaultRequestHeaders.Clear();

                // Use Application Token if available (recommended for third-party apps)
                if (!string.IsNullOrEmpty(_serverConfig.ApplicationToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serverConfig.ApplicationToken);
                    LoggingService.LogDebug($"Using Application Token for request: {function}");
                }
                // Fallback to session token if no application token
                else if (!string.IsNullOrEmpty(_serverConfig.AuthToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serverConfig.AuthToken);
                    LoggingService.LogDebug($"Using Auth Token for request: {function}");
                }
                else
                {
                    LoggingService.LogDebug($"No authentication token for request: {function}");
                }

                var url = _serverConfig.BaseUrl;
                LoggingService.LogDebug($"Making request to: {url}, Function: {function}");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggingService.LogDebug($"Response Status: {response.StatusCode}, Content Length: {responseContent.Length}");

                if (response.IsSuccessStatusCode)
                {
                    // Log the first 500 characters of the response for debugging
                    var previewLength = Math.Min(responseContent.Length, 500);
                    LoggingService.LogDebug($"Response Preview: {responseContent.Substring(0, previewLength)}");
                    
                    // The API returns responses in format: {"data": {...}}
                    // We need to extract the actual data from the "data" property
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(responseContent);
                        if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                        {
                            // Extract the data property and deserialize it
                            var dataJson = dataElement.GetRawText();
                            LoggingService.LogDebug($"Extracted data JSON: {dataJson}");
                            var result = JsonSerializer.Deserialize<T>(dataJson);
                            
                            if (result == null)
                            {
                                LoggingService.LogError($"Failed to deserialize response data for function: {function}");
                                return new ApiResponse<T>
                                {
                                    Success = false,
                                    ErrorMessage = "Failed to deserialize response data"
                                };
                            }
                            
                            LoggingService.LogDebug($"Successfully deserialized response for function: {function}");
                            return new ApiResponse<T>
                            {
                                Success = true,
                                Data = result
                            };
                        }
                        else
                        {
                            // Fallback: try to deserialize the entire response
                            LoggingService.LogDebug($"No 'data' property found, trying to deserialize entire response");
                            var result = JsonSerializer.Deserialize<T>(responseContent);
                            
                            if (result == null)
                            {
                                LoggingService.LogError($"Failed to deserialize response for function: {function}");
                                return new ApiResponse<T>
                                {
                                    Success = false,
                                    ErrorMessage = "Failed to deserialize response"
                                };
                            }
                            
                            LoggingService.LogDebug($"Successfully deserialized fallback response for function: {function}");
                            return new ApiResponse<T>
                            {
                                Success = true,
                                Data = result
                            };
                        }
                    }
                    catch (JsonException ex)
                    {
                        LoggingService.LogError($"JSON parsing error for function {function}: {ex.Message}");
                        return new ApiResponse<T>
                        {
                            Success = false,
                            ErrorMessage = $"Failed to parse response: {ex.Message}"
                        };
                    }
                }
                else
                {
                    // Don't retry on HTTP errors (4xx, 5xx)
                    LoggingService.LogError($"HTTP error {response.StatusCode} for function {function}: {responseContent}");
                    return new ApiResponse<T>
                    {
                        Success = false,
                        ErrorMessage = $"HTTP {response.StatusCode}: {responseContent}"
                    };
                }
            }
            catch (TaskCanceledException ex)
            {
                LoggingService.LogWarning($"Connection timeout (attempt {attempt}/{MaxRetries}) for function {function}: {ex.Message}");
                if (attempt == MaxRetries)
                {
                    return new ApiResponse<T>
                    {
                        Success = false,
                        ErrorMessage = $"Connection timeout after {MaxRetries} attempts: {ex.Message}"
                    };
                }
                
                // Wait before retrying
                await Task.Delay(RetryDelayMs * attempt);
                continue;
            }
            catch (HttpRequestException ex)
            {
                LoggingService.LogWarning($"Network error (attempt {attempt}/{MaxRetries}) for function {function}: {ex.Message}");
                if (attempt == MaxRetries)
                {
                    return new ApiResponse<T>
                    {
                        Success = false,
                        ErrorMessage = $"Network error after {MaxRetries} attempts: {ex.Message}"
                    };
                }
                
                // Wait before retrying
                await Task.Delay(RetryDelayMs * attempt);
                continue;
            }
            catch (Exception ex)
            {
                // Don't retry on other exceptions
                LoggingService.LogError($"Unexpected error for function {function}: {ex.Message}", ex);
                return new ApiResponse<T>
                {
                    Success = false,
                    ErrorMessage = $"Connection error: {ex.Message}"
                };
            }
        }

        LoggingService.LogError($"Request failed after {MaxRetries} attempts for function {function}");
        return new ApiResponse<T>
        {
            Success = false,
            ErrorMessage = $"Request failed after {MaxRetries} attempts"
        };
    }

    public async Task<ApiResponse<HealthCheckResponse>> HealthCheckAsync()
    {
        return await MakeRequestAsync<HealthCheckResponse>("HealthCheck", new { ClientCustomData = "" });
    }

    public async Task<ApiResponse<LoginResponse>> PasswordlessLoginAsync(string minimumPrivilegeLevel = "Client")
    {
        return await MakeRequestAsync<LoginResponse>("PasswordlessLogin", new { MinimumPrivilegeLevel = minimumPrivilegeLevel });
    }

    public async Task<ApiResponse<LoginResponse>> PasswordLoginAsync(string password, string minimumPrivilegeLevel = "Client")
    {
        return await MakeRequestAsync<LoginResponse>("PasswordLogin", new { Password = password, MinimumPrivilegeLevel = minimumPrivilegeLevel });
    }

    public async Task<ApiResponse<ServerStateResponse>> QueryServerStateAsync()
    {
        return await MakeRequestAsync<ServerStateResponse>("QueryServerState", new { ClientCustomData = "" });
    }

    public async Task<ApiResponse<CommandResponse>> RunCommandAsync(string command)
    {
        return await MakeRequestAsync<CommandResponse>("RunCommand", new { Command = command });
    }

    public async Task<ApiResponse<object>> ShutdownAsync()
    {
        return await MakeRequestAsync<object>("Shutdown", new { });
    }

    public async Task<ApiResponse<object>> SaveGameAsync(string saveName)
    {
        return await MakeRequestAsync<object>("SaveGame", new { SaveName = saveName });
    }

    public async Task<ApiResponse<SessionsResponse>> EnumerateSessionsAsync()
    {
        return await MakeRequestAsync<SessionsResponse>("EnumerateSessions", new { });
    }

    public async Task<ApiResponse<object>> LoadGameAsync(string saveName, bool enableAdvancedGameSettings = false)
    {
        return await MakeRequestAsync<object>("LoadGame", new { SaveName = saveName, EnableAdvancedGameSettings = enableAdvancedGameSettings });
    }

    // Server Management Endpoints
    public async Task<ApiResponse<ServerOptionsResponse>> GetServerOptionsAsync()
    {
        return await MakeRequestAsync<ServerOptionsResponse>("GetServerOptions", new { });
    }

    public async Task<ApiResponse<AdvancedGameSettingsResponse>> GetAdvancedGameSettingsAsync()
    {
        return await MakeRequestAsync<AdvancedGameSettingsResponse>("GetAdvancedGameSettings", new { });
    }

    public async Task<ApiResponse<object>> ApplyAdvancedGameSettingsAsync(Dictionary<string, string> settings)
    {
        return await MakeRequestAsync<object>("ApplyAdvancedGameSettings", new { AppliedAdvancedGameSettings = settings });
    }

    public async Task<ApiResponse<ClaimServerResponse>> ClaimServerAsync(string serverName, string adminPassword)
    {
        return await MakeRequestAsync<ClaimServerResponse>("ClaimServer", new { ServerName = serverName, AdminPassword = adminPassword });
    }

    public async Task<ApiResponse<object>> RenameServerAsync(string serverName)
    {
        return await MakeRequestAsync<object>("RenameServer", new { ServerName = serverName });
    }

    public async Task<ApiResponse<object>> SetClientPasswordAsync(string password)
    {
        return await MakeRequestAsync<object>("SetClientPassword", new { Password = password });
    }

    public async Task<ApiResponse<object>> SetAdminPasswordAsync(string password, string newAuthToken)
    {
        return await MakeRequestAsync<object>("SetAdminPassword", new { Password = password, AuthenticationToken = newAuthToken });
    }

    public async Task<ApiResponse<object>> SetAutoLoadSessionNameAsync(string sessionName)
    {
        return await MakeRequestAsync<object>("SetAutoLoadSessionName", new { SessionName = sessionName });
    }

    public async Task<ApiResponse<object>> ApplyServerOptionsAsync(Dictionary<string, string> options)
    {
        return await MakeRequestAsync<object>("ApplyServerOptions", new { UpdatedServerOptions = options });
    }

    // Game Management Endpoints
    public async Task<ApiResponse<object>> CreateNewGameAsync(ServerNewGameData newGameData)
    {
        return await MakeRequestAsync<object>("CreateNewGame", new { NewGameData = newGameData });
    }

    // Save Management Endpoints
    public async Task<ApiResponse<object>> DeleteSaveFileAsync(string saveName)
    {
        return await MakeRequestAsync<object>("DeleteSaveFile", new { SaveName = saveName });
    }

    public async Task<ApiResponse<object>> DeleteSaveSessionAsync(string sessionName)
    {
        return await MakeRequestAsync<object>("DeleteSaveSession", new { SessionName = sessionName });
    }

    public async Task<ApiResponse<object>> UploadSaveGameAsync(string saveName, bool loadSaveGame = false, bool enableAdvancedGameSettings = false)
    {
        return await MakeRequestAsync<object>("UploadSaveGame", new { 
            SaveName = saveName, 
            LoadSaveGame = loadSaveGame, 
            EnableAdvancedGameSettings = enableAdvancedGameSettings 
        });
    }

    public async Task<ApiResponse<object>> DownloadSaveGameAsync(string saveName)
    {
        return await MakeRequestAsync<object>("DownloadSaveGame", new { SaveName = saveName });
    }

    // Authentication Endpoints
            public async Task<ApiResponse<bool>> VerifyAuthenticationTokenAsync()
        {
            try
            {
                var response = await MakeRequestAsync<object>("VerifyAuthenticationToken", new { });
                
                // VerifyAuthenticationToken returns "No Content" (204) or empty response if valid
                // If we get here without an exception, the token is valid
                return new ApiResponse<bool>
                {
                    Success = true,
                    Data = true,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Failed to verify authentication token", ex);
                return new ApiResponse<bool>
                {
                    Success = false,
                    Data = false,
                    ErrorMessage = ex.Message
                };
            }
        }

    // IDisposable implementation
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _httpClient?.Dispose();
        }
        _disposed = true;
    }
}

// Response classes
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
}

public class HealthCheckResponse
{
    [JsonPropertyName("health")]
    public string? Health { get; set; }
    
    [JsonPropertyName("serverCustomData")]
    public string? ServerCustomData { get; set; }
}

public class LoginResponse
{
    [JsonPropertyName("authenticationToken")]
    public string? AuthenticationToken { get; set; }
}

public class ServerStateResponse
{
    [JsonPropertyName("serverGameState")]
    public ServerGameState? ServerGameState { get; set; }
}

public class ServerGameState
{
    [JsonPropertyName("activeSessionName")]
    public string? ActiveSessionName { get; set; }
    
    [JsonPropertyName("numConnectedPlayers")]
    public int NumConnectedPlayers { get; set; }
    
    [JsonPropertyName("playerLimit")]
    public int PlayerLimit { get; set; }
    
    [JsonPropertyName("techTier")]
    public int TechTier { get; set; }
    
    [JsonPropertyName("gamePhase")]
    public string? GamePhase { get; set; }
    
    [JsonPropertyName("activeSchematic")]
    public string? ActiveSchematic { get; set; }
    
    [JsonPropertyName("isGameRunning")]
    public bool IsGameRunning { get; set; }
    
    [JsonPropertyName("isGamePaused")]
    public bool IsGamePaused { get; set; }
    
    [JsonPropertyName("averageTickRate")]
    public double AverageTickRate { get; set; }
    
    [JsonPropertyName("totalGameDuration")]
    public int TotalGameDuration { get; set; }
}

public class CommandResponse
{
    [JsonPropertyName("commandResult")]
    public string? CommandResult { get; set; }
    
    [JsonPropertyName("returnValue")]
    public bool ReturnValue { get; set; }
}

public class SessionsResponse
{
    [JsonPropertyName("sessions")]
    public List<SessionSaveStruct>? Sessions { get; set; }
}

public class SessionSaveStruct
{
    [JsonPropertyName("sessionName")]
    public string? SessionName { get; set; }
    
    [JsonPropertyName("saveHeaders")]
    public List<SaveHeader>? SaveHeaders { get; set; }
}

public class SaveHeader
{
    [JsonPropertyName("saveName")]
    public string? SaveName { get; set; }
    
    [JsonPropertyName("saveDateTime")]
    public string? SaveDateTime { get; set; }
    
    [JsonPropertyName("playDurationSeconds")]
    public int PlayDurationSeconds { get; set; }
    
    [JsonPropertyName("isModdedSave")]
    public bool IsModdedSave { get; set; }
    
    [JsonPropertyName("isEditedSave")]
    public bool IsEditedSave { get; set; }
    
    [JsonPropertyName("isCreativeModeEnabled")]
    public bool IsCreativeModeEnabled { get; set; }
}

// Server Management Response Models
public class ServerOptionsResponse
{
    [JsonPropertyName("serverOptions")]
    public Dictionary<string, string>? ServerOptions { get; set; }
    
    [JsonPropertyName("pendingServerOptions")]
    public Dictionary<string, string>? PendingServerOptions { get; set; }
}

public class AdvancedGameSettingsResponse
{
    [JsonPropertyName("creativeModeEnabled")]
    public bool CreativeModeEnabled { get; set; }
    
    [JsonPropertyName("advancedGameSettings")]
    public Dictionary<string, string>? AdvancedGameSettings { get; set; }
}

public class ClaimServerResponse
{
    [JsonPropertyName("authenticationToken")]
    public string? AuthenticationToken { get; set; }
}

// Game Management Models
public class ServerNewGameData
{
    [JsonPropertyName("sessionName")]
    public string? SessionName { get; set; }
    
    [JsonPropertyName("mapName")]
    public string? MapName { get; set; }
    
    [JsonPropertyName("startingLocation")]
    public string? StartingLocation { get; set; }
    
    [JsonPropertyName("skipOnboarding")]
    public bool SkipOnboarding { get; set; }
    
    [JsonPropertyName("advancedGameSettings")]
    public Dictionary<string, string>? AdvancedGameSettings { get; set; }
    
    [JsonPropertyName("customOptionsOnlyForModding")]
    public Dictionary<string, string>? CustomOptionsOnlyForModding { get; set; }
} 