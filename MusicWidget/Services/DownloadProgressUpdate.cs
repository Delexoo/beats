namespace MusicWidget.Services;

public readonly record struct DownloadProgressUpdate(
    double Percent,
    string Message,
    string? CurrentSong = null);
