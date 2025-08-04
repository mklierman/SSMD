using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;

namespace SSMD.Services;

public static class ValidationService
{
    private static readonly Regex IpAddressRegex = new Regex(@"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$");
    private static readonly Regex HostnameRegex = new Regex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$");

    public static bool IsValidIpAddress(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        // Check for localhost variations
        if (ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            ipAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return true;

        return IpAddressRegex.IsMatch(ipAddress);
    }

    public static bool IsValidPort(int port)
    {
        return port > 0 && port <= 65535;
    }

    public static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        return HostnameRegex.IsMatch(hostname);
    }

    public static bool IsValidCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        // Basic command validation - prevent obviously dangerous commands
        var dangerousCommands = new[] 
        { 
            "format", "del", "rm", "shutdown", "restart", "reboot", 
            "kill", "taskkill", "wmic", "powershell", "cmd"
        };

        var lowerCommand = command.ToLowerInvariant();
        
        // Check for dangerous commands
        foreach (var dangerous in dangerousCommands)
        {
            if (lowerCommand.Contains(dangerous))
                return false;
        }

        return true;
    }

    public static bool IsValidSaveName(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
            return false;

        // Check for invalid characters in file names
        var invalidChars = Path.GetInvalidFileNameChars();
        return !saveName.Any(c => invalidChars.Contains(c));
    }

    public static string SanitizeSaveName(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
            return "Save";

        // Replace invalid characters with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = saveName;
        
        foreach (var invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        // Ensure it's not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
            return "Save";

        return sanitized;
    }
} 