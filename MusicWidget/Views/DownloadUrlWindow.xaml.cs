using System;
using System.Windows;
using System.Windows.Input;
using MusicWidget.Models;
using MusicWidget.Services;

namespace MusicWidget.Views;

public partial class DownloadUrlWindow : Window
{
    private readonly Playlist _playlist;
    private bool _trackingDownload;
    public bool AnyDownloaded { get; private set; }
    public bool OpenYoutubeCookiesRequested { get; private set; }

    public DownloadUrlWindow(Playlist playlist)
    {
        InitializeComponent();
        _playlist = playlist;
        TargetPlaylistText.Text = $"Saving to \"{playlist.Name}\"";
        Loaded += (_, _) => UrlBox.Focus();
        Closed += OnClosed;
        KeyDown += OnKeyDown;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachDownloadHandlers();
    }

    private void DetachDownloadHandlers()
    {
        if (!_trackingDownload)
        {
            return;
        }

        App.BackgroundDownloads.ProgressChanged -= OnBackgroundDownloadProgress;
        App.BackgroundDownloads.Completed -= OnBackgroundDownloadCompleted;
        _trackingDownload = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter && !App.BackgroundDownloads.IsRunning)
        {
            Download_Click(sender, new RoutedEventArgs());
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim();
        if (!App.BackgroundDownloads.TryStart(_playlist, url ?? string.Empty, out var error))
        {
            DownloadStatus.Text = error ?? "Could not start download.";
            return;
        }

        _trackingDownload = true;
        App.BackgroundDownloads.ProgressChanged += OnBackgroundDownloadProgress;
        App.BackgroundDownloads.Completed += OnBackgroundDownloadCompleted;

        DownloadButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        ProgressDetailRow.Visibility = Visibility.Collapsed;
        DownloadCountText.Text = string.Empty;
        DownloadSongText.Text = string.Empty;
        BackgroundHint.Visibility = Visibility.Visible;
        DownloadStatus.Text = "Preparing...";
    }

    private void OnBackgroundDownloadProgress(object? sender, BackgroundDownloadProgressEventArgs e)
    {
        if (!string.Equals(e.PlaylistName, _playlist.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        var p = e.Update;
        if (p.Percent >= 0)
        {
            DownloadProgress.Value = p.Percent;
        }

        if (!string.IsNullOrWhiteSpace(p.CurrentSong))
        {
            DownloadSongText.Text = p.CurrentSong;
        }

        if (string.IsNullOrWhiteSpace(p.Message))
        {
            return;
        }

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
        else if (string.Equals(p.Message, "Preparing...", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(p.Message, "Downloading...", StringComparison.OrdinalIgnoreCase))
        {
            DownloadStatus.Text = p.Message;
        }
        else
        {
            DownloadStatus.Text = p.Message;
        }
    }

    private void OnBackgroundDownloadCompleted(object? sender, BackgroundDownloadCompletedEventArgs e)
    {
        if (!string.Equals(e.PlaylistName, _playlist.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        HandleDownloadFinished(e.Success, e.Success ? "Done." : e.Error ?? "Download failed.");
    }

    private void HandleDownloadFinished(bool success, string message)
    {
        if (success)
        {
            AnyDownloaded = true;
            DownloadStatus.Text = "Done.";
            DownloadCountText.Text = string.Empty;
            DownloadSongText.Text = string.Empty;
            ProgressDetailRow.Visibility = Visibility.Collapsed;
            DownloadProgress.Value = 100;
            UrlBox.Text = string.Empty;
        }
        else
        {
            DownloadStatus.Text = message;
        }

        BackgroundHint.Visibility = Visibility.Collapsed;
        ResetInputs();
    }

    private void ResetInputs()
    {
        DownloadButton.IsEnabled = true;
        UrlBox.IsEnabled = true;
        DetachDownloadHandlers();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void YoutubeCookiesSetup_Click(object sender, RoutedEventArgs e)
    {
        if (App.BackgroundDownloads.IsRunning) return;
        OpenYoutubeCookiesRequested = true;
        Close();
    }

    private static bool IsPlaylistProgressMessage(string message) =>
        message.Contains(" of ", StringComparison.OrdinalIgnoreCase)
        && message.Contains("songs", StringComparison.OrdinalIgnoreCase);
}
