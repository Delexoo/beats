using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using MusicWidget.Models;
using MusicWidget.Services;

namespace MusicWidget.Views;

public partial class DownloadUrlWindow : Window
{
    private readonly Playlist _playlist;
    private bool _running;
    public bool AnyDownloaded { get; private set; }
    public bool OpenYoutubeCookiesRequested { get; private set; }

    public DownloadUrlWindow(Playlist playlist)
    {
        InitializeComponent();
        _playlist = playlist;
        TargetPlaylistText.Text = $"Saving to \"{playlist.Name}\"";
        Loaded += (_, _) => UrlBox.Focus();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_running)
        {
            Close();
        }
        else if (e.Key == Key.Enter && !_running)
        {
            Download_Click(sender, new RoutedEventArgs());
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            DownloadStatus.Text = "Paste a link first.";
            return;
        }

        var dest = App.Playlists.EnsurePlaylistFolder(_playlist.Name);
        var existingFiles = Directory.Exists(dest)
            ? new HashSet<string>(Directory.EnumerateFiles(dest), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _running = true;
        DownloadButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        ProgressDetailRow.Visibility = Visibility.Collapsed;
        DownloadCountText.Text = string.Empty;
        DownloadSongText.Text = string.Empty;
        DownloadStatus.Text = "Preparing...";

        var progress = new Progress<DownloadProgressUpdate>(p =>
        {
            if (!IsLoaded) return;

            if (p.Percent >= 0)
            {
                DownloadProgress.Value = p.Percent;
            }

            if (!string.IsNullOrWhiteSpace(p.CurrentSong))
            {
                DownloadSongText.Text = p.CurrentSong;
            }

            if (!string.IsNullOrWhiteSpace(p.Message))
            {
                if (IsPlaylistProgressMessage(p.Message))
                {
                    ProgressDetailRow.Visibility = Visibility.Visible;
                    DownloadCountText.Text = p.Message;
                    DownloadStatus.Text = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(p.CurrentSong)
                         && string.Equals(p.Message, "Downloading...", StringComparison.OrdinalIgnoreCase))
                {
                    ProgressDetailRow.Visibility = Visibility.Visible;
                    DownloadCountText.Text = p.Message;
                    DownloadStatus.Text = string.Empty;
                }
                else
                {
                    DownloadStatus.Text = p.Message;
                }
            }
        });

        try
        {
            await App.Tools.EnsureToolsAsync(progress).ConfigureAwait(true);
            DownloadStatus.Text = "Downloading...";
            var result = await App.Downloader.DownloadAsync(url, dest, progress, CancellationToken.None)
                .ConfigureAwait(true);
            if (result.Success)
            {
                AnyDownloaded = true;

                var downloadedOrder = result.DownloadedPathsInOrder?.ToList();
                if (downloadedOrder is null || downloadedOrder.Count == 0)
                {
                    downloadedOrder = await Task.Run(() => Directory.EnumerateFiles(dest)
                        .Where(PlaylistManager.IsAudioFile)
                        .Where(f => !existingFiles.Contains(f))
                        .OrderBy(f => File.GetLastWriteTimeUtc(f))
                        .ToList()).ConfigureAwait(true);
                }

                if (downloadedOrder.Count > 0)
                {
                    App.PlaylistOrders.MergeDownloadOrder(_playlist.Name, downloadedOrder);
                }

                App.Playlists.ReloadTracks(_playlist);

                DownloadStatus.Text = "Done.";
                DownloadCountText.Text = string.Empty;
                DownloadSongText.Text = string.Empty;
                ProgressDetailRow.Visibility = Visibility.Collapsed;
                DownloadProgress.Value = 100;
                App.Settings.Current.LastDownloadPlaylist = _playlist.Name;
                App.Settings.Save();
                UrlBox.Text = string.Empty;
            }
            else
            {
                var userMsg = string.IsNullOrWhiteSpace(result.Error)
                    ? "Download failed."
                    : result.Error;
                DownloadStatus.Text = userMsg;
            }
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = ex.Message;
        }
        finally
        {
            _running = false;
            DownloadButton.IsEnabled = true;
            UrlBox.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void YoutubeCookiesSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        OpenYoutubeCookiesRequested = true;
        Close();
    }

    private static bool IsPlaylistProgressMessage(string message) =>
        message.Contains(" of ", StringComparison.OrdinalIgnoreCase)
        && message.Contains("songs", StringComparison.OrdinalIgnoreCase);
}
