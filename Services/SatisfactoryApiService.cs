using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using SSMD.Models;

namespace SSMD.Services;

public class SatisfactoryApiService
{
    private readonly HttpClient _httpClient;
    private readonly ServerConfig _serverConfig;

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
            }
            // Fallback to session token if no application token
            else if (!string.IsNullOrEmpty(_serverConfig.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serverConfig.AuthToken);
            }

            var url = _serverConfig.BaseUrl;
            Console.WriteLine($"Making request to: {url}");
            Console.WriteLine($"Request JSON: {json}");
            Console.WriteLine($"Using Application Token: {!string.IsNullOrEmpty(_serverConfig.ApplicationToken)}");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Response Status: {response.StatusCode}");
            Console.WriteLine($"Response Content: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                // The API returns responses in format: {"data": {...}}
                // We need to extract the actual data from the "data" property
                try
                {
                    var jsonDoc = JsonDocument.Parse(responseContent);
                    if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                    {
                        // Extract the data property and deserialize it
                        var dataJson = dataElement.GetRawText();
                        var result = JsonSerializer.Deserialize<T>(dataJson);
                        
                        return new ApiResponse<T>
                        {
                            Success = true,
                            Data = result
                        };
                    }
                    else
                    {
                        // Fallback: try to deserialize the entire response
                        var result = JsonSerializer.Deserialize<T>(responseContent);
                        return new ApiResponse<T>
                        {
                            Success = true,
                            Data = result
                        };
                    }
                }
                catch (JsonException ex)
                {
                    return new ApiResponse<T>
                    {
                        Success = false,
                        ErrorMessage = $"Failed to parse response: {ex.Message}"
                    };
                }
            }
            else
            {
                return new ApiResponse<T>
                {
                    Success = false,
                    ErrorMessage = $"HTTP {response.StatusCode}: {responseContent}"
                };
            }
        }
        catch (TaskCanceledException ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                ErrorMessage = $"Connection timeout: {ex.Message}"
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                ErrorMessage = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                ErrorMessage = $"Connection error: {ex.Message}"
            };
        }
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

    public async Task<ApiResponse<object>> LoadGameAsync(string sessionName, string saveName)
    {
        return await MakeRequestAsync<object>("LoadGame", new { SessionName = sessionName, SaveName = saveName });
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
    public string? Health { get; set; }
    public string? ServerCustomData { get; set; }
}

public class LoginResponse
{
    public string? AuthenticationToken { get; set; }
}

public class ServerStateResponse
{
    public ServerGameState? ServerGameState { get; set; }
}

public class ServerGameState
{
    public string? ActiveSessionName { get; set; }
    public int NumConnectedPlayers { get; set; }
    public int PlayerLimit { get; set; }
    public string? TechTier { get; set; }
    public string? GamePhase { get; set; }
    public bool IsGameRunning { get; set; }
    public bool IsGamePaused { get; set; }
    public double AverageTickRate { get; set; }
    public int TotalGameDuration { get; set; }
}

public class CommandResponse
{
    public string? CommandResult { get; set; }
    public bool ReturnValue { get; set; }
}

public class SessionsResponse
{
    public List<SessionSaveStruct>? Sessions { get; set; }
}

public class SessionSaveStruct
{
    public string? SessionName { get; set; }
    public List<SaveHeader>? SaveHeaders { get; set; }
}

public class SaveHeader
{
    public string? SaveName { get; set; }
    public string? SaveDateTime { get; set; }
    public int PlayDurationSeconds { get; set; }
    public bool IsModdedSave { get; set; }
    public bool IsEditedSave { get; set; }
    public bool IsCreativeModeEnabled { get; set; }
} 