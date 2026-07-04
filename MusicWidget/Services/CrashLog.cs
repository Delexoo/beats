using System;
using System.IO;

namespace MusicWidget.Services;

/// <summary>Best-effort crash logging so unexpected faults are diagnosable without taking down the app.</summary>
public static class CrashLog
{
    public static void Write(Exception ex, string context)
    {
        try
        {
            var path = Path.Combine(App.AppDataDir, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, entry);
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
