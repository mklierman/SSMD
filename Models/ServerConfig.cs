using System.ComponentModel;
using System.Text.Json;
using System;
using System.IO;
using SSMD.Services;

namespace SSMD.Models;

public class ServerConfig : INotifyPropertyChanged
{
    private string _serverIp = "127.0.0.1";
    private int _serverPort = 7777;
    private string _username = "";
    private string _password = "";
    private string _authToken = "";
    private string _applicationToken = "";

    public string ServerIp
    {
        get => _serverIp;
        set
        {
            _serverIp = value;
            OnPropertyChanged(nameof(ServerIp));
        }
    }

    public int ServerPort
    {
        get => _serverPort;
        set
        {
            _serverPort = value;
            OnPropertyChanged(nameof(ServerPort));
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged(nameof(Username));
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged(nameof(Password));
        }
    }

    public string AuthToken
    {
        get => _authToken;
        set
        {
            _authToken = value;
            OnPropertyChanged(nameof(AuthToken));
        }
    }

    public string ApplicationToken
    {
        get => _applicationToken;
        set
        {
            _applicationToken = value;
            OnPropertyChanged(nameof(ApplicationToken));
        }
    }

    public string BaseUrl => $"https://{ServerIp}:{ServerPort}/api/v1";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Persistence methods
    private static readonly string ConfigFilePath = "server_config.json";

    public bool Save()
    {
        try
        {
            var config = new
            {
                ServerIp = _serverIp,
                ServerPort = _serverPort,
                Username = _username,
                ApplicationToken = _applicationToken
                // Note: We don't save password for security reasons
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
            LoggingService.LogInfo("Configuration saved successfully");
            return true;
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - don't want to break the app if saving fails
            LoggingService.LogError("Failed to save configuration", ex);
            return false;
        }
    }

    public bool Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<ServerConfig>(json);
                
                if (config != null)
                {
                    ServerIp = config.ServerIp;
                    ServerPort = config.ServerPort;
                    Username = config.Username;
                    ApplicationToken = config.ApplicationToken;
                    // Password is not loaded for security reasons
                    LoggingService.LogInfo("Configuration loaded successfully");
                    return true;
                }
            }
            LoggingService.LogInfo("No saved configuration found, using defaults");
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - don't want to break the app if loading fails
            LoggingService.LogError("Failed to load configuration", ex);
            return false;
        }
    }
} 