using System;
using System.IO;
using System.Text.Json;
using MusicWidget.Models;

namespace MusicWidget.Services;

public sealed class SettingsStore
{
    private readonly string _path;
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

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Best-effort; settings will fall back to defaults on next launch.
        }
    }
}
