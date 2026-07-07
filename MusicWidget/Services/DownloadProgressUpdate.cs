namespace MusicWidget.Services;

public readonly record struct DownloadProgressUpdate(
    double Percent,
    string Message,
    string? CurrentSong = null,
    string? CompletedFilePath = null,
    bool RefreshPlaylistTracks = false)
{
    /// <summary>Status text only — does not move the progress bar.</summary>
    public static DownloadProgressUpdate Status(string message, string? currentSong = null) =>
        new(-1, message, currentSong);
}
