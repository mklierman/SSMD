using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSMD.Models;
using SSMD.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic; // Added for Dictionary

namespace SSMD.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
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

    // Individual loading states
    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private bool _isRunningCommand = false;

    [ObservableProperty]
    private bool _isSaving = false;

    [ObservableProperty]
    private bool _isRefreshingSessions = false;

    [ObservableProperty]
    private bool _isShuttingDown = false;

    [ObservableProperty]
    private string _formattedTotalDuration = "";

    [ObservableProperty]
    private string _formattedGamePhase = "";

    [ObservableProperty]
    private string _formattedActiveSchematic = "";

    public IRelayCommand<string> LoadSaveGameCommand { get; }

    private SatisfactoryApiService? _apiService;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainWindowViewModel()
    {
        // Initialize commands
        LoadSaveGameCommand = new AsyncRelayCommand<string>(LoadSaveGame);
        
        // Load saved configuration on startup
        var configLoaded = ServerConfig.Load();
        if (!configLoaded)
        {
            StatusMessage = "Failed to load saved configuration";
        }
        
        // Subscribe to property changes to save configuration
        ServerConfig.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ServerConfig.ServerIp) || 
                e.PropertyName == nameof(ServerConfig.ServerPort) ||
                e.PropertyName == nameof(ServerConfig.Username) ||
                e.PropertyName == nameof(ServerConfig.ApplicationToken))
            {
                var saved = ServerConfig.Save();
                if (!saved)
                {
                    StatusMessage = "Failed to save configuration";
                }
            }
        };
    }

    [RelayCommand]
    private async Task Connect()
    {
        try
        {
            IsConnecting = true;
            StatusMessage = "Connecting to server...";

            // Validate server configuration
            if (!ValidationService.IsValidIpAddress(ServerConfig.ServerIp))
            {
                StatusMessage = "Error: Invalid server IP address";
                return;
            }

            if (!ValidationService.IsValidPort(ServerConfig.ServerPort))
            {
                StatusMessage = "Error: Invalid port number (must be 1-65535)";
                return;
            }

            _apiService = new SatisfactoryApiService(ServerConfig);

            // Test connection with health check
            var healthResponse = await _apiService.HealthCheckAsync();
            
            if (healthResponse.Success)
            {
                IsConnected = true;
                StatusMessage = $"Connected - Server Health: {healthResponse.Data?.Health}";
                
                // Only try to login if no Application Token is provided
                if (string.IsNullOrEmpty(ServerConfig.ApplicationToken))
                {
                    // Try to login
                    await Login();
                }
                else
                {
                    // Application Token is provided, no need to login
                    StatusMessage = "Connected with Application Token";
                }
                
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
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        try
        {
            IsConnecting = true;
            StatusMessage = "Testing connection...";

            // Validate server configuration
            if (!ValidationService.IsValidIpAddress(ServerConfig.ServerIp))
            {
                StatusMessage = "Error: Invalid server IP address";
                return;
            }

            if (!ValidationService.IsValidPort(ServerConfig.ServerPort))
            {
                StatusMessage = "Error: Invalid port number (must be 1-65535)";
                return;
            }

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
            IsConnecting = false;
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
            StatusMessage = "Refreshing server state...";
            var response = await _apiService.QueryServerStateAsync();
            
            if (response.Success && response.Data?.ServerGameState != null)
            {
                CurrentServerState = response.Data.ServerGameState;
                StatusMessage = $"Server: {CurrentServerState.ActiveSessionName} ({CurrentServerState.NumConnectedPlayers}/{CurrentServerState.PlayerLimit} players)";
                
                // Format the total duration as hours:minutes
                if (CurrentServerState.TotalGameDuration > 0)
                {
                    var totalSeconds = CurrentServerState.TotalGameDuration;
                    var hours = totalSeconds / 3600;
                    var minutes = (totalSeconds % 3600) / 60;
                    FormattedTotalDuration = $"{hours:D2}:{minutes:D2}";
                }
                else
                {
                    FormattedTotalDuration = "00:00";
                }
                
                // Format the game phase to show only the part after the last period
                if (!string.IsNullOrEmpty(CurrentServerState.GamePhase))
                {
                    var lastPeriodIndex = CurrentServerState.GamePhase.LastIndexOf('.');
                    if (lastPeriodIndex >= 0 && lastPeriodIndex < CurrentServerState.GamePhase.Length - 1)
                    {
                        FormattedGamePhase = CurrentServerState.GamePhase.Substring(lastPeriodIndex + 1);
                    }
                    else
                    {
                        FormattedGamePhase = CurrentServerState.GamePhase;
                    }
                    
                    // Remove trailing single quotes and "_C"
                    FormattedGamePhase = FormattedGamePhase.TrimEnd('\'');
                    if (FormattedGamePhase.EndsWith("_C"))
                    {
                        FormattedGamePhase = FormattedGamePhase.Substring(0, FormattedGamePhase.Length - 2);
                    }
                }
                else
                {
                    FormattedGamePhase = "Unknown";
                }
                
                // Format the active schematic to show only the part after the last period
                if (!string.IsNullOrEmpty(CurrentServerState.ActiveSchematic))
                {
                    var lastPeriodIndex = CurrentServerState.ActiveSchematic.LastIndexOf('.');
                    if (lastPeriodIndex >= 0 && lastPeriodIndex < CurrentServerState.ActiveSchematic.Length - 1)
                    {
                        FormattedActiveSchematic = CurrentServerState.ActiveSchematic.Substring(lastPeriodIndex + 1);
                    }
                    else
                    {
                        FormattedActiveSchematic = CurrentServerState.ActiveSchematic;
                    }
                    
                    // Remove trailing single quotes and "_C"
                    FormattedActiveSchematic = FormattedActiveSchematic.TrimEnd('\'');
                    if (FormattedActiveSchematic.EndsWith("_C"))
                    {
                        FormattedActiveSchematic = FormattedActiveSchematic.Substring(0, FormattedActiveSchematic.Length - 2);
                    }
                }
                else
                {
                    FormattedActiveSchematic = "None";
                }
                
                LoggingService.LogInfo($"Server state refreshed successfully: {CurrentServerState.ActiveSessionName}");
            }
            else
            {
                if (response.ErrorMessage?.Contains("insufficient_scope") == true)
                {
                    StatusMessage = "Connected but insufficient privileges for server state";
                    CommandOutput = "Login successful but you need Administrator privileges to access server state.\n\nTry logging in with Administrator privileges or contact your server admin.";
                    LoggingService.LogWarning("Insufficient privileges for server state query");
                }
                else
                {
                    StatusMessage = $"Failed to get server state: {response.ErrorMessage}";
                    LoggingService.LogError($"Failed to get server state: {response.ErrorMessage}");
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing state: {ex.Message}";
            LoggingService.LogError("Exception while refreshing server state", ex);
        }
    }

    [RelayCommand]
    private async Task RunCommand()
    {
        if (_apiService == null || string.IsNullOrEmpty(CommandInput)) return;

        // Validate command input
        if (string.IsNullOrWhiteSpace(CommandInput))
        {
            StatusMessage = "Error: Command cannot be empty";
            return;
        }

        if (!ValidationService.IsValidCommand(CommandInput))
        {
            StatusMessage = "Error: Command contains potentially dangerous operations";
            return;
        }

        // Basic command validation - warn about potentially dangerous commands
        var warningCommands = new[] { "shutdown", "quit", "exit", "restart" };
        var lowerCommand = CommandInput.ToLowerInvariant();
        
        if (warningCommands.Any(cmd => lowerCommand.Contains(cmd)))
        {
            StatusMessage = "Warning: This command may affect server operation. Use with caution.";
        }

        try
        {
            IsRunningCommand = true;
            StatusMessage = "Running command...";
            
            _cancellationTokenSource = new CancellationTokenSource();
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
        catch (OperationCanceledException)
        {
            CommandOutput = "Command execution was cancelled";
            StatusMessage = "Command cancelled";
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Command error";
        }
        finally
        {
            IsRunningCommand = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private async Task SaveGame()
    {
        if (_apiService == null) return;

        try
        {
            IsSaving = true;
            var saveName = ValidationService.SanitizeSaveName($"AutoSave_{DateTime.Now:yyyyMMdd_HHmmss}");
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
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task RefreshSessions()
    {
        if (_apiService == null) return;

        try
        {
            IsRefreshingSessions = true;
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
        finally
        {
            IsRefreshingSessions = false;
        }
    }

    [RelayCommand]
    private async Task ShutdownServer()
    {
        if (_apiService == null) return;

        try
        {
            IsShuttingDown = true;
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
        finally
        {
            IsShuttingDown = false;
        }
    }

    [RelayCommand]
    private async Task TestApiToken()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Testing API token permissions...";
            
            // Test basic health check
            var healthResponse = await _apiService.HealthCheckAsync();
            CommandOutput = $"Health Check Result:\nSuccess: {healthResponse.Success}\nHealth: '{healthResponse.Data?.Health}'\nError: {healthResponse.ErrorMessage}\n\n";
            
            // Test server state query
            var stateResponse = await _apiService.QueryServerStateAsync();
            CommandOutput += $"Server State Query Result:\nSuccess: {stateResponse.Success}\nError: {stateResponse.ErrorMessage}\n\n";
            
            if (stateResponse.Success && stateResponse.Data?.ServerGameState != null)
            {
                var state = stateResponse.Data.ServerGameState;
                CommandOutput += $"Server Information:\n";
                CommandOutput += $"Session Name: '{state.ActiveSessionName}'\n";
                CommandOutput += $"Players: {state.NumConnectedPlayers}/{state.PlayerLimit}\n";
                CommandOutput += $"Game Running: {state.IsGameRunning}\n";
                CommandOutput += $"Game Paused: {state.IsGamePaused}\n";
                CommandOutput += $"Tech Tier: {state.TechTier}\n";
                CommandOutput += $"Game Phase: '{state.GamePhase}'\n";
                CommandOutput += $"Tick Rate: {state.AverageTickRate:F1} TPS\n";
                CommandOutput += $"Total Duration: {state.TotalGameDuration} seconds\n";
            }
            else
            {
                CommandOutput += $"No server state data available. This could mean:\n";
                CommandOutput += $"1. No game session is currently running\n";
                CommandOutput += $"2. The server is in a loading state\n";
                CommandOutput += $"3. The server hasn't been claimed yet\n";
                CommandOutput += $"4. There's an issue with the response parsing\n\n";
            }
            
            // Test sessions enumeration
            var sessionsResponse = await _apiService.EnumerateSessionsAsync();
            CommandOutput += $"Sessions Query Result:\nSuccess: {sessionsResponse.Success}\nError: {sessionsResponse.ErrorMessage}\n";
            
            if (sessionsResponse.Success && sessionsResponse.Data?.Sessions != null)
            {
                CommandOutput += $"Found {sessionsResponse.Data.Sessions.Count} sessions\n";
                foreach (var session in sessionsResponse.Data.Sessions)
                {
                    CommandOutput += $"- {session.SessionName} ({session.SaveHeaders?.Count ?? 0} saves)\n";
                }
            }
            else
            {
                CommandOutput += $"No sessions found. This could mean:\n";
                CommandOutput += $"1. No save files exist on the server\n";
                CommandOutput += $"2. Insufficient privileges to access sessions\n";
                CommandOutput += $"3. The server hasn't been claimed yet\n\n";
            }
            
            // Test a simple command to see if we have admin privileges
            var commandResponse = await _apiService.RunCommandAsync("help");
            CommandOutput += $"Command Test Result:\nSuccess: {commandResponse.Success}\nError: {commandResponse.ErrorMessage}\n";
            if (commandResponse.Success && commandResponse.Data != null)
            {
                CommandOutput += $"Command Output: {commandResponse.Data.CommandResult}\n";
            }
            
            StatusMessage = "API token test completed - check command output for details";
        }
        catch (Exception ex)
        {
            CommandOutput = $"Test error: {ex.Message}";
            StatusMessage = "API token test failed";
        }
    }

    // Server Management Commands
    [RelayCommand]
    private async Task GetServerOptions()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Getting server options...";
            var response = await _apiService.GetServerOptionsAsync();
            
            if (response.Success && response.Data != null)
            {
                CommandOutput = "Server Options:\n";
                if (response.Data.ServerOptions != null)
                {
                    foreach (var option in response.Data.ServerOptions)
                    {
                        CommandOutput += $"{option.Key}: {option.Value}\n";
                    }
                }
                
                if (response.Data.PendingServerOptions?.Count > 0)
                {
                    CommandOutput += "\nPending Server Options:\n";
                    foreach (var option in response.Data.PendingServerOptions)
                    {
                        CommandOutput += $"{option.Key}: {option.Value}\n";
                    }
                }
                StatusMessage = "Server options retrieved successfully";
            }
            else
            {
                CommandOutput = $"Failed to get server options: {response.ErrorMessage}";
                StatusMessage = "Failed to get server options";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error getting server options";
        }
    }

    [RelayCommand]
    private async Task GetAdvancedGameSettings()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Getting advanced game settings...";
            var response = await _apiService.GetAdvancedGameSettingsAsync();
            
            if (response.Success && response.Data != null)
            {
                CommandOutput = $"Creative Mode Enabled: {response.Data.CreativeModeEnabled}\n\n";
                CommandOutput += "Advanced Game Settings:\n";
                if (response.Data.AdvancedGameSettings != null)
                {
                    foreach (var setting in response.Data.AdvancedGameSettings)
                    {
                        CommandOutput += $"{setting.Key}: {setting.Value}\n";
                    }
                }
                StatusMessage = "Advanced game settings retrieved successfully";
            }
            else
            {
                CommandOutput = $"Failed to get advanced game settings: {response.ErrorMessage}";
                StatusMessage = "Failed to get advanced game settings";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error getting advanced game settings";
        }
    }

    [RelayCommand]
    private async Task ClaimServer()
    {
        if (_apiService == null) return;

        try
        {
            // For now, use default values - in a real app, you'd get these from the UI
            var serverName = "My Server";
            var adminPassword = "admin123";
            
            StatusMessage = "Claiming server...";
            var response = await _apiService.ClaimServerAsync(serverName, adminPassword);
            
            if (response.Success && response.Data != null)
            {
                CommandOutput = $"Server claimed successfully!\nNew Auth Token: {response.Data.AuthenticationToken}";
                StatusMessage = "Server claimed successfully";
            }
            else
            {
                CommandOutput = $"Failed to claim server: {response.ErrorMessage}";
                StatusMessage = "Failed to claim server";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error claiming server";
        }
    }

    [RelayCommand]
    private async Task RenameServer()
    {
        if (_apiService == null) return;

        try
        {
            var newServerName = "My Renamed Server";
            StatusMessage = "Renaming server...";
            var response = await _apiService.RenameServerAsync(newServerName);
            
            if (response.Success)
            {
                CommandOutput = "Server renamed successfully";
                StatusMessage = "Server renamed successfully";
            }
            else
            {
                CommandOutput = $"Failed to rename server: {response.ErrorMessage}";
                StatusMessage = "Failed to rename server";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error renaming server";
        }
    }

    [RelayCommand]
    private async Task SetClientPassword()
    {
        if (_apiService == null) return;

        try
        {
            var password = "client123";
            StatusMessage = "Setting client password...";
            var response = await _apiService.SetClientPasswordAsync(password);
            
            if (response.Success)
            {
                CommandOutput = "Client password set successfully";
                StatusMessage = "Client password set successfully";
            }
            else
            {
                CommandOutput = $"Failed to set client password: {response.ErrorMessage}";
                StatusMessage = "Failed to set client password";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error setting client password";
        }
    }

    [RelayCommand]
    private async Task SetAdminPassword()
    {
        if (_apiService == null) return;

        try
        {
            var password = "admin123";
            var newAuthToken = "new_token_here"; // In a real app, you'd generate this
            StatusMessage = "Setting admin password...";
            var response = await _apiService.SetAdminPasswordAsync(password, newAuthToken);
            
            if (response.Success)
            {
                CommandOutput = "Admin password set successfully";
                StatusMessage = "Admin password set successfully";
            }
            else
            {
                CommandOutput = $"Failed to set admin password: {response.ErrorMessage}";
                StatusMessage = "Failed to set admin password";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error setting admin password";
        }
    }

    [RelayCommand]
    private async Task SetAutoLoadSession()
    {
        if (_apiService == null) return;

        try
        {
            var sessionName = "My Session";
            StatusMessage = "Setting auto-load session...";
            var response = await _apiService.SetAutoLoadSessionNameAsync(sessionName);
            
            if (response.Success)
            {
                CommandOutput = "Auto-load session set successfully";
                StatusMessage = "Auto-load session set successfully";
            }
            else
            {
                CommandOutput = $"Failed to set auto-load session: {response.ErrorMessage}";
                StatusMessage = "Failed to set auto-load session";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error setting auto-load session";
        }
    }

    [RelayCommand]
    private async Task CreateNewGame()
    {
        if (_apiService == null) return;

        try
        {
            var newGameData = new ServerNewGameData
            {
                SessionName = "New Game Session",
                MapName = "/Game/FactoryGame/Map/LevelGen_World_1/World_1",
                StartingLocation = "",
                SkipOnboarding = true,
                AdvancedGameSettings = new Dictionary<string, string>(),
                CustomOptionsOnlyForModding = new Dictionary<string, string>()
            };
            
            StatusMessage = "Creating new game...";
            var response = await _apiService.CreateNewGameAsync(newGameData);
            
            if (response.Success)
            {
                CommandOutput = "New game created successfully";
                StatusMessage = "New game created successfully";
            }
            else
            {
                CommandOutput = $"Failed to create new game: {response.ErrorMessage}";
                StatusMessage = "Failed to create new game";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error creating new game";
        }
    }

    [RelayCommand]
    private async Task DeleteSaveFile()
    {
        if (_apiService == null) return;

        try
        {
            var saveName = "test_save";
            StatusMessage = "Deleting save file...";
            var response = await _apiService.DeleteSaveFileAsync(saveName);
            
            if (response.Success)
            {
                CommandOutput = "Save file deleted successfully";
                StatusMessage = "Save file deleted successfully";
            }
            else
            {
                CommandOutput = $"Failed to delete save file: {response.ErrorMessage}";
                StatusMessage = "Failed to delete save file";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error deleting save file";
        }
    }

    [RelayCommand]
    private async Task DeleteSaveSession()
    {
        if (_apiService == null) return;

        try
        {
            var sessionName = "test_session";
            StatusMessage = "Deleting save session...";
            var response = await _apiService.DeleteSaveSessionAsync(sessionName);
            
            if (response.Success)
            {
                CommandOutput = "Save session deleted successfully";
                StatusMessage = "Save session deleted successfully";
            }
            else
            {
                CommandOutput = $"Failed to delete save session: {response.ErrorMessage}";
                StatusMessage = "Failed to delete save session";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error deleting save session";
        }
    }

    [RelayCommand]
    private async Task VerifyAuthToken()
    {
        if (_apiService == null) return;

        try
        {
            StatusMessage = "Verifying authentication token...";
            var response = await _apiService.VerifyAuthenticationTokenAsync();
            
            if (response.Success)
            {
                CommandOutput = $"Authentication token verification result: Success: {response.Success} Token Valid: {response.Data}";
                StatusMessage = "Authentication token verified";
            }
            else
            {
                CommandOutput = $"Authentication token verification failed: {response.ErrorMessage}";
                StatusMessage = "Authentication token verification failed";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error verifying authentication token";
        }
    }

    [RelayCommand]
    private async Task LoadGame()
    {
        if (_apiService == null) return;

        try
        {
            var saveName = "test_save"; // Placeholder - could be made configurable
            StatusMessage = "Loading save game...";
            var response = await _apiService.LoadGameAsync(saveName);
            
            if (response.Success)
            {
                CommandOutput = "Save game loaded successfully";
                StatusMessage = "Save game loaded";
            }
            else
            {
                CommandOutput = $"Failed to load save game: {response.ErrorMessage}";
                StatusMessage = "Failed to load save game";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error: {ex.Message}";
            StatusMessage = "Error loading save game";
        }
    }

    private async Task LoadSaveGame(string saveName)
    {
        if (_apiService == null || string.IsNullOrEmpty(saveName)) return;

        try
        {
            StatusMessage = $"Loading save game: {saveName}...";
            var response = await _apiService.LoadGameAsync(saveName);
            
            if (response.Success)
            {
                CommandOutput = $"Save game '{saveName}' loaded successfully";
                StatusMessage = $"Save game '{saveName}' loaded";
                
                // Refresh server state after loading
                await RefreshServerState();
            }
            else
            {
                CommandOutput = $"Failed to load save game '{saveName}': {response.ErrorMessage}";
                StatusMessage = $"Failed to load save game '{saveName}'";
            }
        }
        catch (Exception ex)
        {
            CommandOutput = $"Error loading save game '{saveName}': {ex.Message}";
            StatusMessage = $"Error loading save game '{saveName}'";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        // Cancel any ongoing operations
        _cancellationTokenSource?.Cancel();
        
        IsConnected = false;
        StatusMessage = "Disconnected";
        CurrentServerState = null;
        Sessions.Clear();
        CommandOutput = "";
        
        // Dispose the API service
        _apiService?.Dispose();
        _apiService = null;
    }

    // Cleanup when the view model is disposed
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _apiService?.Dispose();
    }
}
