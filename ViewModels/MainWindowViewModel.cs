using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSMD.Models;
using SSMD.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.Diagnostics;

namespace SSMD.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ServerConfig _serverConfig = new();

    [ObservableProperty]
    private string _statusMessage = "Ready to connect";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private ServerGameState? _currentServerState;

    [ObservableProperty]
    private ObservableCollection<SessionSaveStruct> _sessions = new();

    [ObservableProperty]
    private string _commandInput = "";

    [ObservableProperty]
    private string _commandOutput = "";

    [ObservableProperty]
    private bool _isLoading = false;

    private SatisfactoryApiService? _apiService;

    public MainWindowViewModel()
    {
        // Load saved configuration on startup
        ServerConfig.Load();
        
        // Subscribe to property changes to save configuration
        ServerConfig.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ServerConfig.ServerIp) || 
                e.PropertyName == nameof(ServerConfig.ServerPort) ||
                e.PropertyName == nameof(ServerConfig.Username) ||
                e.PropertyName == nameof(ServerConfig.ApplicationToken))
            {
                ServerConfig.Save();
            }
        };
    }

    [RelayCommand]
    private async Task Connect()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Connecting to server...";

            _apiService = new SatisfactoryApiService(ServerConfig);

            // Test connection with health check
            var healthResponse = await _apiService.HealthCheckAsync();
            
            if (healthResponse.Success)
            {
                IsConnected = true;
                StatusMessage = $"Connected - Server Health: {healthResponse.Data?.Health}";
                
                // Try to login
                await Login();
                
                // Get initial server state
                await RefreshServerState();
            }
            else
            {
                StatusMessage = $"Connection failed: {healthResponse.ErrorMessage}";
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsConnected = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Testing connection...";

            _apiService = new SatisfactoryApiService(ServerConfig);

            // Test basic connectivity
            var healthResponse = await _apiService.HealthCheckAsync();
            
            if (healthResponse.Success)
            {
                StatusMessage = $"✅ Connection successful! Server Health: {healthResponse.Data?.Health}";
                CommandOutput = $"Connection test successful!\nServer Health: {healthResponse.Data?.Health}\nServer Custom Data: {healthResponse.Data?.ServerCustomData}\n\nNote: For full access, use an Application Token generated with 'server.GenerateAPIToken' in the server console.";
            }
            else
            {
                StatusMessage = $"❌ Connection failed: {healthResponse.ErrorMessage}";
                CommandOutput = $"Connection test failed:\n{healthResponse.ErrorMessage}\n\nTroubleshooting tips:\n1. Verify server IP and port\n2. Check if HTTPS API is enabled on server\n3. Ensure server is running\n4. Try different port (default: 7777)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Test error: {ex.Message}";
            CommandOutput = $"Test error: {ex.Message}\n\nTroubleshooting tips:\n1. Check network connectivity\n2. Verify server is running\n3. Check firewall settings\n4. Try localhost (127.0.0.1) for local servers";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Login()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Logging in...";

            ApiResponse<LoginResponse> loginResponse;

            if (string.IsNullOrEmpty(ServerConfig.Password))
            {
                // Try passwordless login with different privilege levels
                loginResponse = await _apiService.PasswordlessLoginAsync("Administrator");
                if (!loginResponse.Success)
                {
                    loginResponse = await _apiService.PasswordlessLoginAsync("Client");
                }
            }
            else
            {
                // Try password login with different privilege levels
                loginResponse = await _apiService.PasswordLoginAsync(ServerConfig.Password, "Administrator");
                if (!loginResponse.Success)
                {
                    loginResponse = await _apiService.PasswordLoginAsync(ServerConfig.Password, "Client");
                }
            }

            if (loginResponse.Success && loginResponse.Data != null)
            {
                ServerConfig.AuthToken = loginResponse.Data.AuthenticationToken;
                StatusMessage = "Successfully logged in";
            }
            else
            {
                StatusMessage = $"Login failed: {loginResponse.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Login error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshServerState()
    {
        if (_apiService == null) return;

        try
        {
            var response = await _apiService.QueryServerStateAsync();
            
            if (response.Success && response.Data?.ServerGameState != null)
            {
                CurrentServerState = response.Data.ServerGameState;
                StatusMessage = $"Server: {CurrentServerState.ActiveSessionName} ({CurrentServerState.NumConnectedPlayers}/{CurrentServerState.PlayerLimit} players)";
            }
            else
            {
                if (response.ErrorMessage?.Contains("insufficient_scope") == true)
                {
                    StatusMessage = "Connected but insufficient privileges for server state";
                    CommandOutput = "Login successful but you need Administrator privileges to access server state.\n\nTry logging in with Administrator privileges or contact your server admin.";
                }
                else
                {
                    StatusMessage = $"Failed to get server state: {response.ErrorMessage}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing state: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunCommand()
    {
        if (_apiService == null || string.IsNullOrEmpty(CommandInput)) return;

        try
        {
            StatusMessage = "Running command...";
            var response = await _apiService.RunCommandAsync(CommandInput);
            
            if (response.Success && response.Data != null)
            {
                CommandOutput = response.Data.CommandResult;
                StatusMessage = response.Data.ReturnValue ? "Command executed successfully" : "Command failed";
            }
            else
            {
                CommandOutput = $"Error: {response.ErrorMessage}";
                StatusMessage = "Command failed";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Command error";
        }
    }

    [RelayCommand]
    private async Task SaveGame()
    {
        if (_apiService == null) return;

        try
        {
            var saveName = $"AutoSave_{DateTime.Now:yyyyMMdd_HHmmss}";
            StatusMessage = "Saving game...";
            
            var response = await _apiService.SaveGameAsync(saveName);
            
            if (response.Success)
            {
                StatusMessage = $"Game saved as {saveName}";
                await RefreshSessions();
            }
            else
            {
                StatusMessage = $"Save failed: {response.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshSessions()
    {
        if (_apiService == null) return;

        try
        {
            var response = await _apiService.EnumerateSessionsAsync();
            
            if (response.Success && response.Data != null)
            {
                Sessions.Clear();
                foreach (var session in response.Data.Sessions)
                {
                    Sessions.Add(session);
                }
                StatusMessage = $"Found {Sessions.Count} sessions";
            }
            else
            {
                if (response.ErrorMessage?.Contains("insufficient_scope") == true)
                {
                    StatusMessage = "Connected but insufficient privileges for sessions";
                    CommandOutput = "Login successful but you need Administrator privileges to access sessions.\n\nTry logging in with Administrator privileges or contact your server admin.";
                }
                else
                {
                    StatusMessage = $"Failed to get sessions: {response.ErrorMessage}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing sessions: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ShutdownServer()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Shutting down server...";
            var response = await _apiService.ShutdownAsync();
            
            if (response.Success)
            {
                StatusMessage = "Server shutdown initiated";
                IsConnected = false;
            }
            else
            {
                StatusMessage = $"Shutdown failed: {response.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Shutdown error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        IsConnected = false;
        StatusMessage = "Disconnected";
        CurrentServerState = null;
        Sessions.Clear();
        CommandOutput = "";
        _apiService = null;
    }
}
