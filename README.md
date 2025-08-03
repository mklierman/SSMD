# Satisfactory Server Manager

A C# Avalonia desktop application for managing Satisfactory dedicated servers using the official HTTPS API.

## Features

- **🔌 Server Connection**: Connect to Satisfactory dedicated servers with IP, port, and password
- **📊 Server Status**: Real-time monitoring of server state, players, tech tier, and game phase
- **💻 Command Console**: Execute server commands directly from the application
- **💾 Save Management**: View and manage save sessions and files
- **🛡️ Authentication**: Support for both passwordless and password-based login
- **⚡ Real-time Updates**: Automatic refresh of server status and session information
- **🎮 Game Control**: Save games, shutdown server, and run console commands

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Avalonia templates installed
- Satisfactory Dedicated Server running

### Installation

1. Clone or download this project
2. Navigate to the project directory
3. Restore dependencies:
   ```bash
   dotnet restore
   ```

### Running the Application

Build and run the application:
```bash
dotnet build
dotnet run
```

### Connecting to a Server

1. **Enter Server Details**:
   - **Server IP**: The IP address of your Satisfactory server (default: 127.0.0.1)
   - **Port**: The HTTPS API port (default: 7777)
   - **Password**: Optional admin/client password

2. **Click Connect**: The app will attempt to connect and authenticate

3. **Monitor Server**: Once connected, you can view server status and manage the server

## API Features

The application supports all major Satisfactory Dedicated Server API endpoints:

### Authentication
- **PasswordlessLogin**: Connect without password (if server allows)
- **PasswordLogin**: Connect with admin/client password
- **VerifyAuthenticationToken**: Validate authentication tokens

### Server Information
- **HealthCheck**: Check server health and connectivity
- **QueryServerState**: Get current server state and player information
- **GetServerOptions**: Retrieve server configuration
- **GetAdvancedGameSettings**: View advanced game settings

### Server Management
- **Run Command**: Execute console commands on the server
- **Shutdown**: Safely shutdown the server
- **SaveGame**: Create save files with custom names
- **LoadGame**: Load existing save files
- **EnumerateSessions**: List all available save sessions

### Save Management
- **UploadSaveGame**: Upload save files to the server
- **DownloadSaveGame**: Download save files from the server
- **DeleteSaveFile**: Remove save files
- **DeleteSaveSession**: Remove entire save sessions

## Project Structure

```
AvaloniaWelcomeApp/
├── Models/
│   └── ServerConfig.cs          # Server configuration model
├── Services/
│   └── SatisfactoryApiService.cs # API client for server communication
├── ViewModels/
│   └── MainWindowViewModel.cs    # Main application logic
├── Views/
│   └── MainWindow.axaml         # User interface
└── App.axaml                    # Application entry point
```

## Technology Stack

- **Avalonia UI**: Cross-platform UI framework
- **CommunityToolkit.Mvvm**: MVVM toolkit for C#
- **.NET 8.0**: Latest .NET framework
- **HttpClient**: HTTP communication with server API
- **JSON**: Data serialization for API requests

## API Documentation

This application is based on the official Satisfactory Dedicated Server HTTPS API. The API endpoint is available at `/api/v1` (e.g., `https://127.0.0.1:7777/api/v1`).

### Request Format
```json
{
  "function": "QueryServerState",
  "data": {
    "clientCustomData": ""
  }
}
```

### Authentication
Most API functions require authentication using Bearer tokens. The application handles:
- Passwordless login for unclaimed servers
- Password-based login for protected servers
- Automatic token management and refresh

## Security Notes

- The application handles self-signed certificates for local servers
- Passwords are stored in memory only and not persisted
- HTTPS encryption is used for all server communication
- Authentication tokens are automatically managed

## Troubleshooting

### Connection Issues
- Ensure the Satisfactory server is running and accessible
- Check that the HTTPS API is enabled (port 7777 by default)
- Verify firewall settings allow HTTPS connections
- For local servers, ensure the server is not loading a save game

### Authentication Issues
- Try passwordless login first for unclaimed servers
- Use admin password for full server control
- Check server logs for authentication errors

### API Errors
- The application displays detailed error messages from the server
- Check server console for additional error information
- Ensure server is not in a loading state when making requests

## Learn More

- [Satisfactory Dedicated Server Documentation](https://satisfactory.wiki.gg/wiki/Dedicated_servers)
- [Avalonia UI Documentation](https://docs.avaloniaui.net/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

## License

This project is open source and available under the MIT License. 