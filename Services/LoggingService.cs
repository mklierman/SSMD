using System;
using System.Diagnostics;

namespace SSMD.Services;

public static class LoggingService
{
    public static void LogInfo(string message)
    {
        Debug.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
    }

    public static void LogWarning(string message)
    {
        Debug.WriteLine($"[WARN] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
    }

    public static void LogError(string message, Exception? exception = null)
    {
        var errorMessage = $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}";
        if (exception != null)
        {
            errorMessage += $"\nException: {exception.Message}\nStackTrace: {exception.StackTrace}";
        }
        Debug.WriteLine(errorMessage);
    }

    public static void LogDebug(string message)
    {
        #if DEBUG
        Debug.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        #endif
    }
} 