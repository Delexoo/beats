using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicWidget.Models;

namespace MusicWidget.Services;

public sealed class BackgroundDownloadProgressEventArgs : EventArgs
{
    public required string PlaylistName { get; init; }
    public required DownloadProgressUpdate Update { get; init; }
}

public sealed class BackgroundDownloadCompletedEventArgs : EventArgs
{
    public required string PlaylistName { get; init; }
    public required bool Success { get; init; }
    public bool Cancelled { get; init; }
    public string? Error { get; init; }
    public int DownloadedCount { get; init; }
}

/// <summary>
/// Runs paste-audio-link downloads outside the dialog so they continue after it closes.
/// </summary>
public sealed class BackgroundDownloadService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public string? ActivePlaylistName { get; private set; }

    public event EventHandler<BackgroundDownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<BackgroundDownloadCompletedEventArgs>? Completed;

    public void Cancel()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public bool TryStart(Playlist playlist, string url, out string? errorMessage)
    {
        errorMessage = null;
        var trimmed = url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            errorMessage = "Paste a link first.";
            return false;
        }

        if (IsRunning)
        {
            errorMessage = "A download is already running.";
            return false;
        }

        if (!_gate.Wait(0))
        {
            errorMessage = "A download is already running.";
            return false;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ActivePlaylistName = playlist.Name;
        _ = RunAsync(playlist, trimmed, _cts.Token);
        return true;
    }

    private async Task RunAsync(Playlist playlist, string url, CancellationToken ct)
    {
        var dest = App.Playlists.EnsurePlaylistFolder(playlist.Name);
        var existingFiles = Directory.Exists(dest)
            ? new HashSet<string>(Directory.EnumerateFiles(dest), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<DownloadProgressUpdate>(p =>
            RaiseProgress(playlist.Name, p));

        var success = false;
        var cancelled = false;
        string? error = null;
        var downloadedCount = 0;

        try
        {
            Report(playlist.Name, new DownloadProgressUpdate(0, "Preparing..."));
            await App.Tools.EnsureToolsAsync(progress, ct).ConfigureAwait(false);
            Report(playlist.Name, new DownloadProgressUpdate(5, "Downloading..."));

            var result = await App.Downloader.DownloadAsync(url, dest, progress, ct).ConfigureAwait(false);
            if (result.Success)
            {
                success = true;
                var downloadedOrder = result.DownloadedPathsInOrder?.ToList();
                if (downloadedOrder is null || downloadedOrder.Count == 0)
                {
                    downloadedOrder = Directory.EnumerateFiles(dest)
                        .Where(PlaylistManager.IsAudioFile)
                        .Where(f => !existingFiles.Contains(f))
                        .OrderBy(f => File.GetLastWriteTimeUtc(f))
                        .ToList();
                }

                downloadedCount = downloadedOrder.Count;
                if (downloadedOrder.Count > 0)
                {
                    App.PlaylistOrders.MergeDownloadOrder(playlist.Name, downloadedOrder);
                }

                UiDispatcher.InvokeSafe(() => App.Playlists.ReloadTracks(playlist));

                App.Settings.Current.LastDownloadPlaylist = playlist.Name;
                App.Settings.Save();
                Report(playlist.Name, new DownloadProgressUpdate(100, "Done."));
            }
            else
            {
                error = string.IsNullOrWhiteSpace(result.Error) ? "Download failed." : result.Error;
                Report(playlist.Name, new DownloadProgressUpdate(-1, error));
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            error = "Download cancelled.";
            Report(playlist.Name, new DownloadProgressUpdate(-1, error));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Report(playlist.Name, new DownloadProgressUpdate(-1, error));
        }
        finally
        {
            IsRunning = false;
            ActivePlaylistName = null;
            _gate.Release();

            UiDispatcher.BeginInvokeSafe(() =>
            {
                Completed?.Invoke(this, new BackgroundDownloadCompletedEventArgs
                {
                    PlaylistName = playlist.Name,
                    Success = success,
                    Cancelled = cancelled,
                    Error = error,
                    DownloadedCount = downloadedCount,
                });
            });

            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Report(string playlistName, DownloadProgressUpdate update) =>
        RaiseProgress(playlistName, update);

    private void RaiseProgress(string playlistName, DownloadProgressUpdate update)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            ProgressChanged?.Invoke(this, new BackgroundDownloadProgressEventArgs
            {
                PlaylistName = playlistName,
                Update = update,
            });
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
