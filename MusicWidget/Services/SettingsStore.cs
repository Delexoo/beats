using System;
using System.IO;
using System.Text.Json;
using MusicWidget.Models;

namespace MusicWidget.Services;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public AppSettings Current { get; private set; } = new();

    public SettingsStore(string path)
    {
        _path = path;
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded is not null)
                    {
                        Current = loaded;
                    }
                }
            }
            catch
            {
                Current = new AppSettings();
            }

            Directory.CreateDirectory(Current.PlaylistsRoot);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _path, overwrite: true);
            }
            catch
            {
                // Best-effort; settings will fall back to defaults on next launch.
            }
        }
    }

    private System.Timers.Timer? _saveDebounceTimer;

    /// <summary>Coalesces rapid setting tweaks (e.g. volume slider) into one disk write.</summary>
    public void ScheduleSave(int delayMs = 400)
    {
        lock (_lock)
        {
            _saveDebounceTimer ??= new System.Timers.Timer { AutoReset = false };
            _saveDebounceTimer.Interval = delayMs;
            _saveDebounceTimer.Elapsed -= OnSaveDebounceElapsed;
            _saveDebounceTimer.Elapsed += OnSaveDebounceElapsed;
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }
    }

    private void OnSaveDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Save();
    }
}
