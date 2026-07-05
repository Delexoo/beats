namespace MusicWidget.Services;

public readonly record struct DownloadProgressUpdate(
    double Percent,
    string Message,
    string? CurrentSong = null,
    string? CompletedFilePath = null,
    bool RefreshPlaylistTracks = false);
