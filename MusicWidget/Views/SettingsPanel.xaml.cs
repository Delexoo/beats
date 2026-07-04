using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using MusicWidget.Models;
using MusicWidget.Services;

namespace MusicWidget.Views;

public partial class SettingsPanel : UserControl
{
    private const string DonationUrl = "https://buy.stripe.com/9B63cu3RE2ouaKgcHLcjS00";

    private const double TracksZoomMin = 0.65;
    private const double TracksZoomMax = 1.35;
    private const double TracksZoomStep = 0.05;

    public static readonly DependencyProperty TracksZoomProperty =
        DependencyProperty.Register(
            nameof(TracksZoom),
            typeof(double),
            typeof(SettingsPanel),
            new PropertyMetadata(1.0, OnTracksZoomChanged));

    public double TracksZoom
    {
        get => (double)GetValue(TracksZoomProperty);
        set => SetValue(TracksZoomProperty, value);
    }

    private static void OnTracksZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsPanel panel)
        {
            panel.ApplyTracksListItemStyle();
        }
    }

    private Style? _tracksItemStyleBase;

    private Point _trackDragStart;
    private bool _trackDragPending;
    private bool _isPanelActive;

    private enum View { Home, Help, QuickStart, Shortcuts, YoutubeCookies }

    private const string MoreProgramsUrl = "https://delexo.store";
    private const string HelpPageUrl = AppBranding.HelpPageUrl;

    private readonly Playlist _likedPlaylist = new(
        "Liked Songs",
        folderPath: string.Empty,
        kind: PlaylistKind.Liked,
        iconKey: "Icon.Heart");

    private readonly Playlist _savesPlaylist = new(
        "Saves",
        folderPath: string.Empty,
        kind: PlaylistKind.Saves,
        iconKey: "Icon.Download");

    private List<Playlist> _playlistsCache = new();
    private readonly List<object> _playlistItems = new();

    // --- Now Playing footer state ---
    private Track? _nowPlayingTrack;
    private bool _suppressVolumeChanged;
    private bool _suppressProgressChanged;
    private bool _suppressKeepWidgetExpandedChanged;
    private int _lastNonZeroVolume = 80;
    private DispatcherTimer? _progressTimer;
    private DispatcherTimer? _backgroundDownloadHideTimer;
    private UpdateCheckResult? _pendingUpdate;
    private bool _updateCheckInFlight;
    private bool _updateInstallInFlight;

    public SettingsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isPanelActive = true;
        ShowView(View.Home);
        RebuildLikedTracks();
        RebuildSavedTracks();
        RefreshAll();
        InitializeTracksListZoom();
        UpdatePlayPauseTrackButton();
        UpdateShuffleButton();
        InitializeFooter();
        InitializeKeepWidgetExpandedToggle();
        StartProgressTimer();
        App.Playlists.PlaylistsChanged += OnPlaylistsChanged;
        App.Playlists.PlaylistTracksChanged += OnPlaylistTracksChanged;
        App.Player.PlayStateChanged += OnPlayerStateChanged;
        App.Player.CurrentTrackChanged += OnPlayerStateChanged;
        App.Player.PositionChanged += OnPlayerPositionChanged;
        App.Player.ShuffleChanged += OnShuffleChanged;
        App.Player.LoopCurrentChanged += OnLoopChanged;
        App.LikedSongs.Changed += OnLikedSongsChanged;
        App.SavedSongs.Changed += OnSavedSongsChanged;
        App.BackgroundDownloads.ProgressChanged += OnBackgroundDownloadProgress;
        App.BackgroundDownloads.Completed += OnBackgroundDownloadCompleted;
        _ = SyncUpdateButtonFromServiceAsync();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isPanelActive = false;
        App.Playlists.PlaylistsChanged -= OnPlaylistsChanged;
        App.Playlists.PlaylistTracksChanged -= OnPlaylistTracksChanged;
        App.Player.PlayStateChanged -= OnPlayerStateChanged;
        App.Player.CurrentTrackChanged -= OnPlayerStateChanged;
        App.Player.PositionChanged -= OnPlayerPositionChanged;
        App.Player.ShuffleChanged -= OnShuffleChanged;
        _progressTimer?.Stop();
        _progressTimer = null;
        App.Player.LoopCurrentChanged -= OnLoopChanged;
        App.LikedSongs.Changed -= OnLikedSongsChanged;
        App.SavedSongs.Changed -= OnSavedSongsChanged;
        App.BackgroundDownloads.ProgressChanged -= OnBackgroundDownloadProgress;
        App.BackgroundDownloads.Completed -= OnBackgroundDownloadCompleted;
        _backgroundDownloadHideTimer?.Stop();
        _backgroundDownloadHideTimer = null;
        if (_nowPlayingTrack is not null)
        {
            _nowPlayingTrack.PropertyChanged -= NowPlayingTrack_PropertyChanged;
            _nowPlayingTrack = null;
        }
    }

    private void OnShuffleChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateShuffleButton);
    }

    private void UpdateShuffleButton()
    {
        var on = App.Player.Shuffle;
        ShuffleIcon.Fill = (Brush)Application.Current.Resources[on ? "Brush.Blue" : "Brush.TextDim"];
        ShuffleButton.ToolTip = on ? "Shuffle: ON" : "Shuffle";
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        App.Player.SetShuffle(!App.Player.Shuffle);
    }

    private void SongsAddButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (PlaylistsList.SelectedItem is not Playlist playlist)
        {
            return;
        }
        ShowAddMenuFor(playlist, (UIElement)sender);
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            if (!_isPanelActive) return;
            UpdatePlayPauseTrackButton();
            UpdatePlaylistPlayingStates();
            UpdateNowPlayingFooter();
            UpdateFooterProgress();
        });
    }

    private void OnPlayerPositionChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateFooterProgress, DispatcherPriority.Background);
    }

    private void StartProgressTimer()
    {
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _progressTimer.Tick += (_, _) => UpdateFooterProgress();
        _progressTimer.Start();
    }

    private void OnLoopChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(UpdateFooterLoopIcon);
    }

    private void UpdatePlayPauseTrackButton()
    {
        var playing = App.Player.IsPlaying;
        var key = playing ? "Icon.Pause" : "Icon.Play";
        PlayPauseTrackIcon.Data = (Geometry)Application.Current.Resources[key];
        PlayPauseTrackLabel.Text = playing ? "Pause" : "Play";
        PlayPauseTrackTooltip.Text = playing ? "Pause" : "Play";
    }

    private void UpdatePlaylistPlayingStates()
    {
        var isPlaying = App.Player.IsPlaying;
        var all = new List<Playlist> { _likedPlaylist, _savesPlaylist };
        all.AddRange(_playlistsCache);

        foreach (var pl in all)
        {
            var active = IsPlaylistActive(pl);
            pl.IsActivePlaylist = active;
            pl.IsPlayingNow = active && isPlaying;
        }
    }

    private bool IsPlaylistActive(Playlist playlist)
    {
        var current = App.Player.CurrentTrack;
        if (current is null)
        {
            return false;
        }

        return playlist.Kind switch
        {
            PlaylistKind.Normal => !string.IsNullOrEmpty(App.Settings.Current.CurrentPlaylist) &&
                string.Equals(playlist.Name, App.Settings.Current.CurrentPlaylist, StringComparison.OrdinalIgnoreCase),
            PlaylistKind.Liked => App.LikedSongs.Contains(current.FilePath),
            PlaylistKind.Saves => App.SavedSongs.Contains(current.FilePath),
            _ => false,
        };
    }

    // ===== Now Playing footer =====

    private void InitializeFooter()
    {
        var v = Math.Clamp(App.Player.Volume, 0, 100);
        if (v > 0) _lastNonZeroVolume = v;

        _suppressVolumeChanged = true;
        FooterVolumeSlider.Value = v;
        _suppressVolumeChanged = false;

        UpdateFooterPlayPauseIcon();
        UpdateFooterLoopIcon();
        UpdateFooterVolumeIcon();
        UpdateFooterLikeIcon();
        UpdateFooterSaveIcon();
        UpdateFooterProgress();
        UpdateNowPlayingFooter();
    }

    private void UpdateNowPlayingFooter()
    {
        var t = App.Player.CurrentTrack;
        BindNowPlayingTrack(t);
        RenderNowPlayingMeta();
        UpdateFooterPlayPauseIcon();
        UpdateFooterLikeIcon();
        UpdateFooterSaveIcon();
        HighlightCurrentlyPlayingTrack();
    }

    /// <summary>
    /// If the currently-playing track exists in the visible TracksList, select it
    /// and scroll it into view so the user can always tell which row is playing.
    /// Match by FilePath because the player and the playlist sometimes hold
    /// different Track instances for the same file. If the playing track lives
    /// in a different playlist than the one being browsed, selection is left
    /// alone so we don't fight the user's manual navigation.
    /// </summary>
    private void HighlightCurrentlyPlayingTrack()
    {
        var current = App.Player.CurrentTrack;
        if (current is null) return;
        if (TracksList.ItemsSource is null) return;

        Track? match = null;
        foreach (var item in TracksList.ItemsSource)
        {
            if (item is Track tr &&
                string.Equals(tr.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                match = tr;
                break;
            }
        }
        if (match is null) return;
        if (ReferenceEquals(TracksList.SelectedItem, match)) return;

        TracksList.SelectedItem = match;
        try
        {
            TracksList.ScrollIntoView(match);
        }
        catch
        {
            // ListBox containers may not be generated yet.
        }
    }

    private void BindNowPlayingTrack(Track? track)
    {
        if (ReferenceEquals(_nowPlayingTrack, track))
        {
            return;
        }

        if (_nowPlayingTrack is not null)
        {
            _nowPlayingTrack.PropertyChanged -= NowPlayingTrack_PropertyChanged;
        }

        _nowPlayingTrack = track;

        if (_nowPlayingTrack is not null)
        {
            _nowPlayingTrack.PropertyChanged += NowPlayingTrack_PropertyChanged;
            // Kick off artwork/tag load if it hasn't already happened for this track.
            // Cached results return synchronously inside ArtworkService.
            if (_nowPlayingTrack.ArtworkSource is null)
            {
                var capture = _nowPlayingTrack;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var art = await App.Artwork.GetArtworkAsync(capture).ConfigureAwait(false);
                        if (art is null) return;
                        UiDispatcher.BeginInvokeSafe(() =>
                        {
                            if (!ReferenceEquals(_nowPlayingTrack, capture)) return;
                            capture.ArtworkSource = art;
                        }, DispatcherPriority.Background);
                    }
                    catch
                    {
                        // Best-effort: missing artwork falls back to initials.
                    }
                });
            }
        }
    }

    private void NowPlayingTrack_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Track is INotifyPropertyChanged — refresh the meta when title/artist/art arrive.
        Dispatcher.BeginInvoke(new Action(RenderNowPlayingMeta));
    }

    private void RenderNowPlayingMeta()
    {
        var t = _nowPlayingTrack;
        if (t is null)
        {
            NowPlayingTitle.Text = "Nothing playing";
            NowPlayingArtist.Text = string.Empty;
            NowPlayingInitials.Text = string.Empty;
            NowPlayingArtBrush.ImageSource = null;
            NowPlayingArt.Visibility = Visibility.Collapsed;
            return;
        }

        var title = !string.IsNullOrWhiteSpace(t.Title) ? t.Title : t.DisplayName;
        NowPlayingTitle.Text = title ?? string.Empty;
        NowPlayingArtist.Text = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist : "Unknown artist";
        NowPlayingInitials.Text = t.Initials;

        if (t.ArtworkSource is not null)
        {
            NowPlayingArtBrush.ImageSource = t.ArtworkSource;
            NowPlayingArt.Visibility = Visibility.Visible;
        }
        else
        {
            NowPlayingArtBrush.ImageSource = null;
            NowPlayingArt.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateFooterPlayPauseIcon()
    {
        var playing = App.Player.IsPlaying;
        var key = playing ? "Icon.Pause" : "Icon.Play";
        FooterPlayPauseIcon.Data = (Geometry)Application.Current.Resources[key];
        FooterPlayPauseTooltip.Text = playing ? "Pause" : "Play";
    }

    private void UpdateFooterLoopIcon()
    {
        var on = App.Player.LoopCurrent;
        var accent = (Brush)Application.Current.Resources["Brush.Blue"];
        var dim = (Brush)Application.Current.Resources["Brush.TextDim"];

        FooterLoopIconRect.Fill = on ? accent : dim;
        FooterLoopTooltip.Text = on ? "Loop: ON (current song)" : "Loop: OFF";
    }

    private void UpdateFooterVolumeIcon()
    {
        var v = App.Player.Volume;
        var key = v == 0 ? "Icon.VolumeMute" : "Icon.Volume";
        FooterVolumeIcon.Data = (Geometry)Application.Current.Resources[key];
    }

    private void UpdateFooterLikeIcon()
    {
        var path = App.Player.CurrentTrack?.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            // No track playing — show the inert outline-style heart and disable the button.
            FooterLikeButton.IsEnabled = false;
            FooterLikeIcon.Fill = (Brush)Application.Current.Resources["Brush.TextMuted"];
            FooterLikeTooltip.Text = "Like (nothing playing)";
            return;
        }

        FooterLikeButton.IsEnabled = true;
        var liked = App.LikedSongs.Contains(path);
        // Use the danger red for "liked" so the heart pops; dim grey when unliked.
        FooterLikeIcon.Fill = liked
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44))
            : (Brush)Application.Current.Resources["Brush.TextDim"];
        FooterLikeTooltip.Text = liked ? "Remove from Liked Songs" : "Add to Liked Songs";
    }

    private void UpdateFooterSaveIcon()
    {
        var path = App.Player.CurrentTrack?.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            FooterSaveButton.IsEnabled = false;
            FooterSaveIcon.Fill = (Brush)Application.Current.Resources["Brush.TextMuted"];
            FooterSaveTooltip.Text = "Save (nothing playing)";
            return;
        }

        FooterSaveButton.IsEnabled = true;
        var saved = App.SavedSongs.Contains(path);
        FooterSaveIcon.Fill = saved
            ? (Brush)Application.Current.Resources["Brush.Blue"]
            : (Brush)Application.Current.Resources["Brush.TextDim"];
        FooterSaveTooltip.Text = saved ? "Remove from Saves" : "Add to Saves";
    }

    private void FooterLike_Click(object sender, RoutedEventArgs e)
    {
        var path = App.Player.CurrentTrack?.FilePath;
        if (string.IsNullOrEmpty(path)) return;
        App.LikedSongs.Toggle(path);
        // OnLikedSongsChanged refreshes the icon on the UI thread.
    }

    private void FooterSave_Click(object sender, RoutedEventArgs e)
    {
        var path = App.Player.CurrentTrack?.FilePath;
        if (string.IsNullOrEmpty(path)) return;
        App.SavedSongs.Toggle(path);
        // OnSavedSongsChanged refreshes the icon on the UI thread.
    }

    private void FooterPrev_Click(object sender, RoutedEventArgs e)
    {
        App.Player.Previous();
    }

    private void FooterNext_Click(object sender, RoutedEventArgs e)
    {
        App.Player.Next();
    }

    private void FooterPlayPause_Click(object sender, RoutedEventArgs e)
    {
        App.Player.TogglePlayPause();
    }

    private void FooterLoop_Click(object sender, RoutedEventArgs e)
    {
        App.Player.SetLoopCurrent(!App.Player.LoopCurrent);
    }

    private void FooterMute_Click(object sender, RoutedEventArgs e)
    {
        var current = App.Player.Volume;
        if (current > 0)
        {
            _lastNonZeroVolume = current;
            FooterVolumeSlider.Value = 0;
        }
        else
        {
            FooterVolumeSlider.Value = _lastNonZeroVolume > 0 ? _lastNonZeroVolume : 50;
        }
    }

    private static string FormatPlaybackTime(long totalMs)
    {
        if (totalMs < 0)
        {
            totalMs = 0;
        }

        var totalSeconds = (int)(totalMs / 1000);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }

    private void UpdateFooterProgress()
    {
        if (FooterProgressSlider.IsMouseCaptureWithin)
        {
            return;
        }

        var track = App.Player.CurrentTrack;
        var durationMs = App.Player.DurationMs;
        var positionMs = App.Player.PositionMs;

        FooterPositionLabel.Text = FormatPlaybackTime(positionMs);
        FooterDurationLabel.Text = durationMs > 0 ? FormatPlaybackTime(durationMs) : "0:00";
        FooterProgressSlider.IsEnabled = track is not null && durationMs > 0;

        _suppressProgressChanged = true;
        if (durationMs > 0)
        {
            FooterProgressSlider.Maximum = durationMs;
            FooterProgressSlider.Value = Math.Clamp(positionMs, 0, durationMs);
        }
        else
        {
            FooterProgressSlider.Maximum = 1;
            FooterProgressSlider.Value = 0;
        }
        _suppressProgressChanged = false;
    }

    private void FooterProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressProgressChanged)
        {
            return;
        }

        if (!FooterProgressSlider.IsMouseCaptureWithin)
        {
            return;
        }

        var durationMs = App.Player.DurationMs;
        if (durationMs <= 0)
        {
            return;
        }

        var positionMs = (long)Math.Clamp(e.NewValue, 0, durationMs);
        FooterPositionLabel.Text = FormatPlaybackTime(positionMs);
        App.Player.SeekToMilliseconds(positionMs);
    }

    private void FooterVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeChanged) return;

        var v = (int)Math.Round(e.NewValue);
        v = Math.Clamp(v, 0, 100);
        if (v > 0) _lastNonZeroVolume = v;

        App.Player.Volume = v;
        App.Settings.Current.Volume = v;
        App.Settings.Save();

        UpdateFooterVolumeIcon();
    }

    // ===== Reset layout =====

    /// <summary>
    /// Restores the playlist &lt;-&gt; songs splitter to a 50/50 split. The GridSplitter
    /// writes absolute pixel widths when dragged; reinstating "1*" / "1*" makes the
    /// two panes share the remaining space evenly again. Public so the widget window
    /// can include the splitter when handling Ctrl+Shift+\ from anywhere.
    /// </summary>
    public void ResetSplitter()
    {
        if (PlaylistsSongsGrid is null || PlaylistsSongsGrid.ColumnDefinitions.Count < 3) return;
        PlaylistsSongsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PlaylistsSongsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ModernMessageBox.ConfirmYesNo(
            "Reset the widget's position and the dashboard's size to their defaults?",
            title: "Reset layout");
        if (!confirmed) return;

        // Walk up to the owning WidgetWindow and delegate the window-level reset
        // (position + dashboard dimensions + splitter).
        var owner = Window.GetWindow(this) as WidgetWindow;
        owner?.ResetLayoutToDefaults();
    }

    private void InitializeKeepWidgetExpandedToggle()
    {
        _suppressKeepWidgetExpandedChanged = true;
        KeepWidgetExpandedToggle.IsChecked = App.Settings.Current.KeepWidgetExpanded;
        _suppressKeepWidgetExpandedChanged = false;
    }

    private void KeepWidgetExpandedToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressKeepWidgetExpandedChanged)
        {
            return;
        }

        App.Settings.Current.KeepWidgetExpanded = KeepWidgetExpandedToggle.IsChecked == true;
        App.Settings.Save();

        if (Window.GetWindow(this) is WidgetWindow owner)
        {
            owner.ApplyKeepWidgetExpandedPreference();
        }
    }

    // ----- Navigation -----

    private View _currentView = View.Home;

    private void OpenHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = HelpPageUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not open browser: " + ex.Message);
        }
    }
    private void OpenQuickStart_Click(object sender, RoutedEventArgs e) => ShowView(View.QuickStart);
    private void OpenShortcuts_Click(object sender, RoutedEventArgs e) => ShowView(View.Shortcuts);
    private void OpenYoutubeCookies_Click(object sender, RoutedEventArgs e) => ShowView(View.YoutubeCookies);

    private void OpenMorePrograms_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MoreProgramsUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not open browser: " + ex.Message);
        }
    }

    private void NavBack_Click(object sender, RoutedEventArgs e)
    {
        var target = _currentView switch
        {
            View.QuickStart => View.Home,
            View.Shortcuts => View.Home,
            View.YoutubeCookies => View.Home,
            View.Help => View.Home,
            _ => View.Home,
        };
        ShowView(target);
    }

    private void ShowView(View view)
    {
        _currentView = view;
        HomeView.Visibility = view == View.Home ? Visibility.Visible : Visibility.Collapsed;
        HelpView.Visibility = view == View.Help ? Visibility.Visible : Visibility.Collapsed;
        QuickStartView.Visibility = view == View.QuickStart ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsView.Visibility = view == View.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
        YoutubeCookiesView.Visibility = view == View.YoutubeCookies ? Visibility.Visible : Visibility.Collapsed;
        if (view == View.YoutubeCookies)
        {
            RefreshYoutubeCookiesPathUi();
        }
    }

    private void RefreshYoutubeCookiesPathUi()
    {
        YoutubeCookiesPathBox.Text = string.IsNullOrWhiteSpace(App.Settings.Current.YoutubeCookiesFilePath)
            ? "(none - use Browse to pick cookies.txt)"
            : App.Settings.Current.YoutubeCookiesFilePath;
    }

    private void YoutubeCookiesBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Netscape cookies file for yt-dlp",
            Filter = "Cookies (*.txt)|cookies.txt;*.txt|All files|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            App.Settings.Current.YoutubeCookiesFilePath = dlg.FileName;
            App.Settings.Save();
            RefreshYoutubeCookiesPathUi();
        }
    }

    private void YoutubeCookiesClear_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.YoutubeCookiesFilePath = null;
        App.Settings.Save();
        RefreshYoutubeCookiesPathUi();
    }

    private void WikiLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not open browser: " + ex.Message);
        }

        e.Handled = true;
    }

    // ----- Data refresh -----

    private void OnPlaylistsChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            if (!_isPanelActive) return;
            try
            {
                RefreshAll();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "SettingsPanel.RefreshAll");
            }
        });
    }

    private void OnPlaylistTracksChanged(object? sender, string playlistName)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            if (!_isPanelActive) return;
            try
            {
                ApplyPlaylistTracksChanged(playlistName);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "SettingsPanel.OnPlaylistTracksChanged");
            }
        }, DispatcherPriority.Background);
    }

    private void ApplyPlaylistTracksChanged(string playlistName)
    {
        var cached = _playlistsCache.FirstOrDefault(p =>
            string.Equals(p.Name, playlistName, StringComparison.OrdinalIgnoreCase));
        if (cached is not null)
        {
            App.Playlists.ReloadTracks(cached);
        }

        if (PlaylistsList.SelectedItem is not Playlist selected) return;

        if (selected.Kind == PlaylistKind.Liked)
        {
            RebuildLikedTracks();
            TracksList.ItemsSource = null;
            TracksList.ItemsSource = _likedPlaylist.Tracks;
            TracksHeader.Text = $"{_likedPlaylist.Name} Playlist";
            LoadArtworkForTracks(_likedPlaylist);
        }
        else if (selected.Kind == PlaylistKind.Normal &&
                 string.Equals(selected.Name, playlistName, StringComparison.OrdinalIgnoreCase) &&
                 cached is not null)
        {
            TracksList.ItemsSource = null;
            TracksList.ItemsSource = cached.Tracks;
            TracksHeader.Text = $"{cached.Name} Playlist";
            LoadArtworkForTracks(cached);
        }
    }

    private void RefreshAll()
    {
        _playlistsCache = App.Playlists.GetPlaylists().ToList();

        // Re-apply the persisted "pinned by the user" flag on every refresh:
        // PlaylistManager returns fresh Playlist instances on every poll, so the
        // IsUserPinned bit lives in AppSettings rather than on the model.
        var pinnedNames = new HashSet<string>(
            App.Settings.Current.PinnedPlaylists ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var pl in _playlistsCache)
        {
            pl.IsUserPinned = pinnedNames.Contains(pl.Name);
        }

        var prevSelected = PlaylistsList.SelectedItem;

        _playlistItems.Clear();
        _playlistItems.Add(_likedPlaylist);
        _playlistItems.Add(_savesPlaylist);

        // User-pinned Normal playlists float between the virtual Liked/Saves and
        // the divider so they sit at the top alongside the always-pinned pair.
        var pinnedNormals = _playlistsCache.Where(p => p.IsUserPinned).ToList();
        var unpinnedNormals = _playlistsCache.Where(p => !p.IsUserPinned).ToList();
        foreach (var pl in pinnedNormals)
        {
            _playlistItems.Add(pl);
        }
        _playlistItems.Add(PlaylistDivider.Instance);
        foreach (var pl in unpinnedNormals)
        {
            _playlistItems.Add(pl);
        }

        PlaylistsList.ItemsSource = null;
        PlaylistsList.ItemsSource = _playlistItems;

        var curName = App.Settings.Current.CurrentPlaylist;
        object? toSelect = null;
        if (prevSelected is Playlist prevPl)
        {
            // Keep pinned-instance selection sticky across rebuilds.
            if (ReferenceEquals(prevPl, _likedPlaylist)) toSelect = _likedPlaylist;
            else if (ReferenceEquals(prevPl, _savesPlaylist)) toSelect = _savesPlaylist;
            else
            {
                toSelect = _playlistsCache.FirstOrDefault(p =>
                    string.Equals(p.Name, prevPl.Name, StringComparison.OrdinalIgnoreCase));
            }
        }
        if (toSelect is null && !string.IsNullOrEmpty(curName))
        {
            toSelect = _playlistsCache.FirstOrDefault(p =>
                string.Equals(p.Name, curName, StringComparison.OrdinalIgnoreCase));
        }
        toSelect ??= _playlistsCache.Count > 0 ? (object)_playlistsCache[0] : _likedPlaylist;

        PlaylistsList.SelectedItem = toSelect;

        UpdatePlaylistPlayingStates();
    }

    private void RefreshTracks(Playlist pl)
    {
        switch (pl.Kind)
        {
            case PlaylistKind.Liked:
                RebuildLikedTracks();
                break;
            case PlaylistKind.Saves:
                RebuildSavedTracks();
                break;
            case PlaylistKind.Normal:
                // Normal playlists are populated by PlaylistManager on disk; nothing to rebuild here.
                break;
        }

        TracksList.ItemsSource = null;
        TracksList.ItemsSource = pl.Tracks;
        TracksHeader.Text = $"{pl.Name} Playlist";

        SyncTrackLikedStates();
        LoadArtworkForTracks(pl);

        // After re-binding the tracks list, re-select the playing track if it
        // lives in this playlist. Defer to the next layout pass so the ListBox
        // has finished generating containers (otherwise ScrollIntoView noops).
        Dispatcher.BeginInvoke(new Action(HighlightCurrentlyPlayingTrack),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void RebuildLikedTracks() => RebuildPinnedTracks(App.LikedSongs, _likedPlaylist);
    private void RebuildSavedTracks() => RebuildPinnedTracks(App.SavedSongs, _savesPlaylist);

    private void SyncTrackLikedStates()
    {
        var liked = App.LikedSongs;
        void Sync(IEnumerable<Track> tracks)
        {
            foreach (var track in tracks)
            {
                track.IsLiked = liked.Contains(track.FilePath);
            }
        }

        Sync(_likedPlaylist.Tracks);
        Sync(_savesPlaylist.Tracks);
        foreach (var playlist in _playlistsCache)
        {
            Sync(playlist.Tracks);
        }
    }

    /// <summary>
    /// Repopulates the given pinned playlist's Tracks collection from the store's
    /// ordered list of paths, reusing existing Track instances from the loaded
    /// playlists where possible so artwork/title/artist already fetched survive
    /// across rebuilds.
    /// </summary>
    private void RebuildPinnedTracks(TrackListStore store, Playlist pinned)
    {
        var paths = store.GetAll();
        var index = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var pl in _playlistsCache)
        {
            foreach (var tr in pl.Tracks)
            {
                index[tr.FilePath] = tr;
            }
        }

        pinned.Tracks.Clear();
        foreach (var p in paths)
        {
            pinned.Tracks.Add(
                index.TryGetValue(p, out var existing) ? existing : new Track(p));
        }
    }

    private static void LoadArtworkForTracks(Playlist pl)
    {
        const int maxPerRefresh = 80;
        var loaded = 0;

        foreach (var track in pl.Tracks)
        {
            if (track.ArtworkSource is not null)
            {
                continue;
            }

            if (loaded >= maxPerRefresh)
            {
                break;
            }

            loaded++;
            var capture = track;
            _ = Task.Run(async () =>
            {
                try
                {
                    var art = await App.Artwork.GetArtworkAsync(capture).ConfigureAwait(false);
                    if (art is null) return;
                    UiDispatcher.BeginInvokeSafe(() =>
                    {
                        if (capture.ArtworkSource is null)
                        {
                            capture.ArtworkSource = art;
                        }
                    }, DispatcherPriority.Background);
                }
                catch
                {
                    // Best-effort: missing artwork falls back to initials.
                }
            });
        }
    }

    // ----- Playlists: row icon buttons -----

    private void PlaylistPlayButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: Playlist playlist })
        {
            return;
        }

        // Virtual playlists keep their track list dynamic; refresh before checking count.
        switch (playlist.Kind)
        {
            case PlaylistKind.Liked:
                RebuildLikedTracks();
                break;
            case PlaylistKind.Saves:
                RebuildSavedTracks();
                break;
        }

        if (playlist.Tracks.Count == 0)
        {
            return;
        }

        PlaylistsList.SelectedItem = playlist;
        RefreshTracks(playlist);

        if (IsPlaylistActive(playlist) && App.Player.CurrentTrack is not null)
        {
            App.Player.TogglePlayPause();
            return;
        }

        // Only persist as "current" for real, on-disk playlists.
        if (playlist.Kind == PlaylistKind.Normal)
        {
            App.Playlists.SetCurrentPlaylist(playlist.Name);
        }

        App.Player.SetQueue(playlist.Tracks, 0);
    }

    private void PlaylistAddButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: Playlist playlist })
        {
            return;
        }
        ShowAddMenuFor(playlist, (UIElement)sender);
    }

    private void ShowAddMenuFor(Playlist playlist, UIElement placementTarget)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        var addFiles = new MenuItem { Header = "Add files…" };
        addFiles.Click += (_, _) =>
        {
            menu.IsOpen = false;
            AddFilesToPlaylist(playlist);
        };

        var addFolder = new MenuItem { Header = "Add folder…" };
        addFolder.Click += (_, _) =>
        {
            menu.IsOpen = false;
            AddFolderToPlaylist(playlist);
        };

        var download = new MenuItem { Header = "Paste audio link…" };
        download.Click += (_, _) =>
        {
            menu.IsOpen = false;
            DownloadUrlIntoPlaylist(playlist);
        };

        menu.Items.Add(addFiles);
        menu.Items.Add(addFolder);
        menu.Items.Add(new Separator());
        menu.Items.Add(download);
        menu.IsOpen = true;
    }

    private void AddFilesToPlaylist(Playlist playlist)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = $"Add music to \"{playlist.Name}\"",
            Filter = "Audio files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aif;*.aiff;*.alac|All files|*.*",
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        if (dlg.FileNames.Length == 0)
        {
            return;
        }

        App.Playlists.AddTracksFromFiles(playlist.Name, dlg.FileNames);
        RefreshAll();
    }

    private void AddFolderToPlaylist(Playlist playlist)
    {
        var dlg = new OpenFolderDialog
        {
            Title = $"Pick a folder to import audio into \"{playlist.Name}\"",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        App.Playlists.AddTracksFromFolder(
            playlist.Name,
            dlg.FolderName,
            SearchOption.AllDirectories);
        RefreshAll();
    }

    private void DownloadUrlIntoPlaylist(Playlist playlist)
    {
        var window = new DownloadUrlWindow(playlist)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        window.ShowDialog();
        if (window.OpenYoutubeCookiesRequested)
        {
            ShowView(View.YoutubeCookies);
        }
    }

    private void RefreshAfterDownload(Playlist playlist)
    {
        App.Playlists.ReloadTracks(playlist);

        if (PlaylistsList.SelectedItem is Playlist selected
            && string.Equals(selected.Name, playlist.Name, StringComparison.OrdinalIgnoreCase))
        {
            RefreshTracks(playlist);
            return;
        }

        var cached = _playlistsCache?.FirstOrDefault(p =>
            string.Equals(p.Name, playlist.Name, StringComparison.OrdinalIgnoreCase));
        if (cached is not null
            && PlaylistsList.SelectedItem is Playlist selectedCached
            && string.Equals(selectedCached.Name, cached.Name, StringComparison.OrdinalIgnoreCase))
        {
            RefreshTracks(cached);
        }
    }

    private void OnBackgroundDownloadProgress(object? sender, BackgroundDownloadProgressEventArgs e)
    {
        if (!_isPanelActive)
        {
            return;
        }

        BackgroundDownloadBanner.Visibility = Visibility.Visible;
        BackgroundDownloadTitle.Text = $"Downloading to \"{e.PlaylistName}\"";

        var update = e.Update;
        if (update.Percent >= 0)
        {
            BackgroundDownloadProgress.Value = update.Percent;
            BackgroundDownloadPercent.Text = $"{update.Percent:0}%";
        }

        if (!string.IsNullOrWhiteSpace(update.CurrentSong))
        {
            BackgroundDownloadDetail.Text = update.CurrentSong;
        }
        else if (!string.IsNullOrWhiteSpace(update.Message))
        {
            BackgroundDownloadDetail.Text = update.Message;
        }
    }

    private void OnBackgroundDownloadCompleted(object? sender, BackgroundDownloadCompletedEventArgs e)
    {
        if (!_isPanelActive)
        {
            return;
        }

        if (e.Success)
        {
            BackgroundDownloadTitle.Text = "Download complete";
            BackgroundDownloadDetail.Text = e.DownloadedCount == 1
                ? "1 song added to your playlist."
                : $"{e.DownloadedCount} songs added to \"{e.PlaylistName}\".";
            BackgroundDownloadProgress.Value = 100;
            BackgroundDownloadPercent.Text = "100%";

            var playlist = FindPlaylistByName(e.PlaylistName)
                ?? App.Playlists.GetPlaylists()
                    .FirstOrDefault(p => string.Equals(p.Name, e.PlaylistName, StringComparison.OrdinalIgnoreCase));
            if (playlist is not null)
            {
                RefreshAfterDownload(playlist);
            }
        }
        else
        {
            BackgroundDownloadTitle.Text = "Download failed";
            BackgroundDownloadDetail.Text = e.Error ?? "Something went wrong.";
            BackgroundDownloadPercent.Text = string.Empty;
        }

        _backgroundDownloadHideTimer?.Stop();
        _backgroundDownloadHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _backgroundDownloadHideTimer.Tick += (_, _) =>
        {
            _backgroundDownloadHideTimer?.Stop();
            BackgroundDownloadBanner.Visibility = Visibility.Collapsed;
        };
        _backgroundDownloadHideTimer.Start();
    }

    private Playlist? FindPlaylistByName(string name) =>
        _playlistsCache.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private void PlaylistsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The divider sentinel isn't selectable: revert to the previous selection.
        if (PlaylistsList.SelectedItem is PlaylistDivider)
        {
            var previous = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            Dispatcher.BeginInvoke(() => PlaylistsList.SelectedItem = previous);
            return;
        }

        if (PlaylistsList.SelectedItem is Playlist pl)
        {
            RefreshTracks(pl);
        }
        else
        {
            TracksList.ItemsSource = null;
            TracksHeader.Text = "Songs";
        }
    }

    private void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForName("New playlist", "Name for the new playlist:", "My Playlist");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            App.Playlists.CreatePlaylist(name);
            RefreshAll();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning(ex.Message);
        }
    }

    private void PlaylistRenameButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: Playlist pl })
        {
            return;
        }

        var name = PromptForName("Rename playlist", "New name:", pl.Name);
        if (string.IsNullOrWhiteSpace(name) || name == pl.Name) return;
        try
        {
            var oldFolder = Path.Combine(App.Playlists.Root, pl.Name) + Path.DirectorySeparatorChar;
            App.Playlists.RenamePlaylist(pl.Name, name);
            var newFolder = Path.Combine(App.Playlists.Root, name) + Path.DirectorySeparatorChar;
            // Any liked tracks that lived in the old folder now live in the new folder.
            App.LikedSongs.ReplacePathPrefix(oldFolder, newFolder);
            RefreshAll();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning(ex.Message);
        }
    }

    private void PlaylistDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: Playlist pl })
        {
            return;
        }

        var ok = ModernMessageBox.ConfirmYesNo(
            $"Delete playlist \"{pl.Name}\" and all its files?",
            "Delete playlist",
            ModernMessageBox.Severity.Warning);
        if (!ok) return;
        try
        {
            var folder = Path.Combine(App.Playlists.Root, pl.Name) + Path.DirectorySeparatorChar;
            App.Playlists.DeletePlaylist(pl.Name);
            // The files are gone; prune any matching liked entries so we don't end up with
            // broken pointers in the Liked Songs list.
            App.LikedSongs.RemovePathsUnder(folder);
            RefreshAll();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning(ex.Message);
        }
    }

    // ----- Playlist right-click context menu -----

    /// <summary>
    /// Walks from a context-menu item up to its ContextMenu, then over to the
    /// PlacementTarget (the playlist row) and returns its bound <see cref="Playlist"/>.
    /// ContextMenus live in their own popup visual tree, so the usual
    /// <c>((FrameworkElement)sender).DataContext</c> trick doesn't reliably reach
    /// the row's DataContext on every WPF version.
    /// </summary>
    private static Playlist? PlaylistFromMenuItem(object sender)
    {
        if (sender is not MenuItem mi) return null;
        var cm = ItemsControl.ItemsControlFromItemContainer(mi) as ContextMenu
                 ?? mi.Parent as ContextMenu;
        if (cm?.PlacementTarget is FrameworkElement target)
        {
            return target.DataContext as Playlist;
        }
        return mi.DataContext as Playlist;
    }

    private void PlaylistContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        var pl = cm.PlacementTarget is FrameworkElement target
            ? target.DataContext as Playlist
            : null;

        bool isNormal = pl is not null && pl.Kind == PlaylistKind.Normal;

        foreach (var obj in cm.Items)
        {
            if (obj is not MenuItem mi) continue;
            var header = mi.Header as string ?? string.Empty;

            // Update the Pin item's label + icon to reflect the current state.
            if (header.StartsWith("Pin", StringComparison.OrdinalIgnoreCase) ||
                header.StartsWith("Unpin", StringComparison.OrdinalIgnoreCase))
            {
                bool pinned = pl?.IsUserPinned == true;
                mi.Header = pinned ? "Unpin playlist" : "Pin playlist";
                // Note: System.IO.Path is also `using`'d in this file, so we have
                // to spell out the WPF shape to disambiguate.
                if (mi.Icon is System.Windows.Shapes.Path p)
                {
                    p.Data = (Geometry)Application.Current.Resources[
                        pinned ? "Icon.PinOff" : "Icon.Pin"];
                }
                mi.IsEnabled = isNormal;
                continue;
            }

            // Items that only make sense for real (Normal) playlists. Play is always
            // enabled regardless of kind.
            switch (header)
            {
                case "Add files…":
                case "Add folder…":
                case "Paste audio link…":
                case "Open folder in File Explorer":
                case "Rename…":
                case "Delete playlist":
                    mi.IsEnabled = isNormal;
                    break;
            }
        }
    }

    private void PlaylistContextPlay_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null) return;

        PlaylistsList.SelectedItem = pl;
        PlaylistPlayButton_Click(new Button { Tag = pl }, e);
    }

    private void PlaylistContextPin_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;

        var pinned = App.Settings.Current.PinnedPlaylists ??= new List<string>();
        bool nowPinned = !pl.IsUserPinned;

        // Mirror the change into both the persisted list and the live model so
        // the row's badge updates immediately without waiting for RefreshAll.
        if (nowPinned)
        {
            if (!pinned.Contains(pl.Name, StringComparer.OrdinalIgnoreCase))
                pinned.Add(pl.Name);
        }
        else
        {
            pinned.RemoveAll(n => string.Equals(n, pl.Name, StringComparison.OrdinalIgnoreCase));
        }
        pl.IsUserPinned = nowPinned;

        App.Settings.Save();
        RefreshAll();
    }

    private void PlaylistContextAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;
        AddFilesToPlaylist(pl);
    }

    private void PlaylistContextAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;
        AddFolderToPlaylist(pl);
    }

    private void PlaylistContextDownload_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;
        DownloadUrlIntoPlaylist(pl);
    }

    private void PlaylistContextOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;

        var folder = Path.Combine(App.Playlists.Root, pl.Name);
        if (!Directory.Exists(folder))
        {
            ModernMessageBox.ShowWarning($"Folder \"{folder}\" doesn't exist.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning(ex.Message);
        }
    }

    private void PlaylistContextRename_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;

        // Reuse the same flow as the inline rename button by faking the Tag.
        PlaylistRenameButton_Click(new Button { Tag = pl }, e);
    }

    private void PlaylistContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var pl = PlaylistFromMenuItem(sender);
        if (pl is null || pl.Kind != PlaylistKind.Normal) return;

        PlaylistDeleteButton_Click(new Button { Tag = pl }, e);
    }

    // ----- Tracks -----

    private void InitializeTracksListZoom()
    {
        _tracksItemStyleBase ??= TracksList.ItemContainerStyle;
        var saved = App.Settings.Current.TracksListZoom;
        TracksZoom = Math.Clamp(saved > 0 ? saved : 1.0, TracksZoomMin, TracksZoomMax);
        ApplyTracksListItemStyle();
    }

    private void ApplyTracksListItemStyle()
    {
        if (_tracksItemStyleBase is null)
        {
            return;
        }

        var z = TracksZoom;
        var style = new Style(typeof(ListBoxItem), _tracksItemStyleBase);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8 * z, 4 * z, 8 * z, 4 * z)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12 * z));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1 * z, 0, 1 * z)));
        TracksList.ItemContainerStyle = style;
    }

    private void TracksList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _trackDragStart = e.GetPosition(null);
        _trackDragPending = true;
    }

    private void TracksList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _trackDragPending = false;
    }

    private void TracksList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_trackDragPending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _trackDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _trackDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _trackDragPending = false;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not Track track)
        {
            return;
        }

        try
        {
            DragDrop.DoDragDrop(TracksList, track, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "SettingsPanel.TracksList drag");
        }
    }

    private void TracksList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Track)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TracksList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(typeof(Track))) return;
            if (e.Data.GetData(typeof(Track)) is not Track dragged) return;
            if (GetActivePlaylist() is not Playlist pl) return;

            var tracks = pl.Tracks;
            var oldIndex = tracks.FindIndex(t =>
                string.Equals(t.FilePath, dragged.FilePath, StringComparison.OrdinalIgnoreCase));
            if (oldIndex < 0) return;

            var newIndex = GetTracksDropIndex(e.GetPosition(TracksList));
            newIndex = Math.Clamp(newIndex, 0, tracks.Count);
            if (newIndex > oldIndex) newIndex--;
            newIndex = Math.Clamp(newIndex, 0, Math.Max(0, tracks.Count - 1));
            if (oldIndex == newIndex) return;

            MoveTrackInPlaylist(pl, oldIndex, newIndex);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "SettingsPanel.TracksList drop");
        }
    }

    private int GetTracksDropIndex(Point position)
    {
        try
        {
            var hit = TracksList.InputHitTest(position) as DependencyObject;
            var item = FindAncestor<ListBoxItem>(hit);
            if (item is null)
            {
                return TracksList.Items.Count;
            }

            if (TracksList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                return TracksList.Items.Count;
            }

            var index = TracksList.ItemContainerGenerator.IndexFromContainer(item);
            if (index < 0)
            {
                return TracksList.Items.Count;
            }

            var itemTop = item.TranslatePoint(new Point(0, 0), TracksList).Y;
            var relativeY = position.Y - itemTop;
            var height = ((FrameworkElement)item).ActualHeight;
            if (height <= 0)
            {
                return index;
            }

            return relativeY > height / 2 ? index + 1 : index;
        }
        catch
        {
            return TracksList.Items.Count;
        }
    }

    private void MoveTrackInPlaylist(Playlist pl, int fromIndex, int toIndex)
    {
        var tracks = pl.Tracks;
        if (fromIndex < 0 || fromIndex >= tracks.Count) return;
        toIndex = Math.Clamp(toIndex, 0, tracks.Count - 1);

        var track = tracks[fromIndex];
        tracks.RemoveAt(fromIndex);
        tracks.Insert(toIndex, track);

        switch (pl.Kind)
        {
            case PlaylistKind.Normal:
                App.PlaylistOrders.SetOrder(pl.Name, tracks.Select(t => t.FilePath));
                break;
            case PlaylistKind.Liked:
                App.LikedSongs.Move(fromIndex, toIndex);
                break;
            case PlaylistKind.Saves:
                App.SavedSongs.Move(fromIndex, toIndex);
                break;
        }

        TracksList.ItemsSource = null;
        TracksList.ItemsSource = tracks;
        TracksList.SelectedItem = track;
        TracksList.ScrollIntoView(track);

        if (IsPlaylistActive(pl))
        {
            App.Player.UpdateQueueOrder(tracks);
        }
    }

    private void TracksList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        e.Handled = true;
        var step = e.Delta > 0 ? TracksZoomStep : -TracksZoomStep;
        TracksZoom = Math.Clamp(TracksZoom + step, TracksZoomMin, TracksZoomMax);
        App.Settings.Current.TracksListZoom = TracksZoom;
        App.Settings.Save();
    }

    private void TracksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        PlayTrack_Click(sender, new RoutedEventArgs());
    }

    private void TracksList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Select whichever row was right-clicked so the context menu acts on it.
        if (e.OriginalSource is DependencyObject src)
        {
            var item = FindAncestor<ListBoxItem>(src);
            if (item is not null)
            {
                item.IsSelected = true;
                item.Focus();
            }
            else
            {
                TracksList.SelectedItem = null;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private Track? GetContextTrack()
    {
        return TracksList.SelectedItem as Track;
    }

    private Playlist? GetActivePlaylist()
    {
        return PlaylistsList.SelectedItem as Playlist;
    }

    private void TrackContextPlay_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        if (t is null || GetActivePlaylist() is not Playlist pl) return;

        App.Playlists.SetCurrentPlaylist(pl.Name);
        var idx = pl.Tracks.IndexOf(t);
        if (idx < 0) idx = 0;
        App.Player.SetQueue(pl.Tracks, idx);
    }

    private void TrackContextShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        if (t is null) return;

        try
        {
            if (!File.Exists(t.FilePath))
            {
                ModernMessageBox.ShowInfo("This file no longer exists on disk.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{t.FilePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not open File Explorer: " + ex.Message);
        }
    }

    private void TrackContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        if (t is null) return;

        try
        {
            Clipboard.SetText(t.FilePath);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not access clipboard: " + ex.Message);
        }
    }

    private void TrackContextRename_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        var pl = GetActivePlaylist();
        if (t is null || pl is null) return;

        var newName = PromptForName(
            "Rename song",
            "Enter a new name for this song:",
            Path.GetFileNameWithoutExtension(t.FilePath));
        if (newName is null) return;

        var wasCurrent = string.Equals(App.Player.CurrentTrack?.FilePath, t.FilePath,
            StringComparison.OrdinalIgnoreCase);

        if (wasCurrent)
        {
            // Releases the file handle so File.Move can succeed.
            App.Player.Stop();
        }

        try
        {
            var oldPath = t.FilePath;
            string newPath;
            if (pl.Kind == PlaylistKind.Normal)
            {
                newPath = App.Playlists.RenameTrack(pl.Name, Path.GetFileName(oldPath), newName);
            }
            else
            {
                // Virtual playlist: rename at the original location.
                newPath = App.Playlists.RenameTrackAtPath(oldPath, newName);
            }

            // Keep Liked in sync regardless of which playlist we renamed from — the file may
            // be liked even if the user kicked off the rename from its home playlist.
            App.LikedSongs.ReplacePath(oldPath, newPath);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning(ex.Message);
        }
    }

    private void TrackContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        if (t is null) return;

        bool liked = App.LikedSongs.Contains(t.FilePath);
        MenuItemLike.Header = liked ? "Remove from Liked Songs" : "Add to Liked Songs";
        var fillKey = liked ? "Brush.TextDim" : null;
        MenuItemLikeIcon.Fill = fillKey is null
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            : (Brush)Application.Current.Resources[fillKey];

        // Tailor the rename/delete row to whichever playlist owns this menu invocation.
        var active = GetActivePlaylist();
        bool isVirtual = active is not null && active.Kind != PlaylistKind.Normal;
        bool isLiked = active is not null && active.Kind == PlaylistKind.Liked;

        SeparatorBeforeEdit.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
        MenuItemRename.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;

        if (isLiked)
        {
            MenuItemDelete.Header = "Remove from Liked Songs";
            MenuItemDeleteIcon.Data = (Geometry)Application.Current.Resources["Icon.Heart"];
            MenuItemDeleteIcon.Fill = (Brush)Application.Current.Resources["Brush.TextDim"];
            MenuItemDelete.Visibility = Visibility.Visible;
        }
        else if (isVirtual)
        {
            MenuItemDelete.Visibility = Visibility.Collapsed;
        }
        else
        {
            MenuItemDelete.Header = "Delete from playlist";
            MenuItemDeleteIcon.Data = (Geometry)Application.Current.Resources["Icon.Trash"];
            MenuItemDeleteIcon.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            MenuItemDelete.Visibility = Visibility.Visible;
        }
    }

    private void TrackContextLike_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        if (t is null) return;
        App.LikedSongs.Toggle(t.FilePath);
    }

    private void TrackContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var t = GetContextTrack();
        var pl = GetActivePlaylist();
        if (t is null || pl is null) return;

        if (pl.Kind == PlaylistKind.Liked)
        {
            // From the Liked virtual playlist, "delete" just unlikes — leave the file alone.
            App.LikedSongs.Remove(t.FilePath);
            return;
        }

        if (pl.Kind != PlaylistKind.Normal)
        {
            // Other virtual playlists don't support delete.
            return;
        }

        var confirm = ModernMessageBox.Confirm(
            $"Delete \"{t.DisplayName}\" from \"{pl.Name}\"?\n\nThe file will be removed from disk.",
            "Delete song",
            ModernMessageBox.Severity.Warning);
        if (!confirm) return;

        var wasCurrent = string.Equals(App.Player.CurrentTrack?.FilePath, t.FilePath,
            StringComparison.OrdinalIgnoreCase);

        if (wasCurrent)
        {
            App.Player.Stop();
        }

        try
        {
            App.Playlists.RemoveTrack(pl.Name, Path.GetFileName(t.FilePath));
            // Also drop the file from Liked so we don't leave dangling pointers.
            App.LikedSongs.Remove(t.FilePath);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not delete: " + ex.Message);
        }
    }

    private void PlayTrack_Click(object sender, RoutedEventArgs e)
    {
        var selectedTrack = TracksList.SelectedItem as Track;
        var currentTrack = App.Player.CurrentTrack;
        var hasDifferentSelection = selectedTrack is not null &&
            (currentTrack is null ||
             !string.Equals(currentTrack.FilePath, selectedTrack.FilePath, StringComparison.OrdinalIgnoreCase));

        if (!hasDifferentSelection && currentTrack is not null)
        {
            App.Player.TogglePlayPause();
            return;
        }

        if (PlaylistsList.SelectedItem is not Playlist pl) return;
        var t = selectedTrack;
        if (t is null)
        {
            if (pl.Tracks.Count == 0) return;
            t = pl.Tracks[0];
        }
        App.Playlists.SetCurrentPlaylist(pl.Name);
        var startIdx = pl.Tracks.IndexOf(t);
        if (startIdx < 0) startIdx = 0;
        App.Player.SetQueue(pl.Tracks, startIdx);
    }

    // ----- Minimize / Exit -----

    private void MinimizeWidget_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is WidgetWindow widget)
        {
            widget.MinimizeWidget();
        }
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        // Close every window first so each one's Closed handler runs cleanly,
        // then Shutdown() fires OnExit (Player.Dispose, settings save, mutex release).
        try
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
            {
                try { w.Close(); } catch { /* best-effort */ }
            }
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    // ----- Donate -----

    private async Task SyncUpdateButtonFromServiceAsync()
    {
        if (!_isPanelActive)
        {
            return;
        }

        if (App.Updates.IsAutoUpdateRunning)
        {
            SetUpdateButtonCheckingState();
            return;
        }

        if (App.Updates.LastCheckResult is not null)
        {
            _pendingUpdate = App.Updates.LastCheckResult;
            ApplyUpdateButtonState(_pendingUpdate);
            return;
        }

        await CheckForUpdatesAsync(showUpToDateMessage: false);
    }

    private async Task CheckForUpdatesAsync(bool showUpToDateMessage)
    {
        if (_updateCheckInFlight || _updateInstallInFlight || !_isPanelActive
            || App.Updates.IsAutoUpdateRunning)
        {
            return;
        }

        _updateCheckInFlight = true;
        SetUpdateButtonCheckingState();

        try
        {
            var result = await App.Updates.CheckForUpdateAsync().ConfigureAwait(true);
            if (!_isPanelActive)
            {
                return;
            }

            _pendingUpdate = result;
            ApplyUpdateButtonState(result);

            if (showUpToDateMessage)
            {
                if (result is null)
                {
                    ModernMessageBox.ShowWarning(
                        "Could not check for updates. Check your internet connection and try again.");
                }
                else if (result.IsUpdateAvailable)
                {
                    ModernMessageBox.ShowInfo(
                        $"Version {result.LatestVersion} is available. Click Update to download and install.");
                }
                else
                {
                    ModernMessageBox.ShowInfo(
                        $"You are on the latest version (v{App.Updates.CurrentVersion}).");
                }
            }
        }
        catch (Exception ex)
        {
            if (_isPanelActive)
            {
                ApplyUpdateButtonState(null);
                if (showUpToDateMessage)
                {
                    ModernMessageBox.ShowWarning("Could not check for updates: " + ex.Message);
                }
            }
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }

    private void SetUpdateButtonCheckingState()
    {
        if (UpdateButton is null)
        {
            return;
        }

        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = false;
        UpdateButton.Style = (Style)FindResource("FlatButton");
        UpdateButton.ToolTip = "Checking for updates...";
        if (UpdateButtonIcon is not null)
        {
            UpdateButtonIcon.Fill = (Brush)FindResource("Brush.TextDim");
        }
    }

    private void ApplyUpdateButtonState(UpdateCheckResult? result)
    {
        if (UpdateButton is null)
        {
            return;
        }

        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = !_updateInstallInFlight;

        if (result?.IsUpdateAvailable == true)
        {
            UpdateButton.Style = (Style)FindResource("PrimaryButton");
            UpdateButton.ToolTip = $"Version {result.LatestVersion} is available. Click to download and install.";
            if (UpdateButtonIcon is not null)
            {
                UpdateButtonIcon.Fill = Brushes.White;
            }
            return;
        }

        UpdateButton.Style = (Style)FindResource("FlatButton");
        UpdateButton.ToolTip = $"You are on the latest version (v{App.Updates.CurrentVersion}). Click to check again.";
        if (UpdateButtonIcon is not null)
        {
            UpdateButtonIcon.Fill = (Brush)FindResource("Brush.TextDim");
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInstallInFlight)
        {
            return;
        }

        if (_pendingUpdate?.IsUpdateAvailable == true)
        {
            await InstallPendingUpdateAsync().ConfigureAwait(true);
            return;
        }

        await CheckForUpdatesAsync(showUpToDateMessage: true).ConfigureAwait(true);
    }

    private async Task InstallPendingUpdateAsync()
    {
        var update = _pendingUpdate;
        if (update is null || !update.IsUpdateAvailable)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppBranding.GitHubReleasesUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                ModernMessageBox.ShowWarning("Could not open releases page: " + ex.Message);
            }

            return;
        }

        var confirmed = ModernMessageBox.ConfirmYesNo(
            $"Download and install Beats {update.LatestVersion}?\n\nBeats will close so the installer can update.",
            "Install update",
            ModernMessageBox.Severity.Question);
        if (!confirmed)
        {
            return;
        }

        _updateInstallInFlight = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.ToolTip = "Downloading update...";

        try
        {
            var progress = new Progress<double>(percent =>
            {
                UiDispatcher.BeginInvokeSafe(() =>
                {
                    UpdateButton.ToolTip = $"Downloading update... {percent * 100:0}%";
                });
            });

            if (!await App.Updates
                    .DownloadAndInstallAsync(update, progress, silentInstall: false)
                    .ConfigureAwait(true))
            {
                ModernMessageBox.ShowWarning("Could not download the update.");
                return;
            }

            ExitApp_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            _updateInstallInFlight = false;
            ApplyUpdateButtonState(_pendingUpdate);
            ModernMessageBox.ShowWarning("Could not download update: " + ex.Message);
        }
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DonationUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowWarning("Could not open browser: " + ex.Message);
        }
    }

    // ----- Liked / Saved Songs (virtual playlists) -----

    private void OnLikedSongsChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            if (!_isPanelActive) return;
            try
            {
                RebuildLikedTracks();
                SyncTrackLikedStates();
                UpdateFooterLikeIcon();
                if (PlaylistsList.SelectedItem is Playlist sel && sel.Kind == PlaylistKind.Liked)
                {
                    TracksList.ItemsSource = null;
                    TracksList.ItemsSource = _likedPlaylist.Tracks;
                    TracksHeader.Text = $"{_likedPlaylist.Name} Playlist";
                    LoadArtworkForTracks(_likedPlaylist);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "SettingsPanel.OnLikedSongsChanged");
            }
        });
    }

    private void OnSavedSongsChanged(object? sender, EventArgs e)
    {
        UiDispatcher.BeginInvokeSafe(() =>
        {
            if (!_isPanelActive) return;
            try
            {
                RebuildSavedTracks();
                UpdateFooterSaveIcon();
                if (PlaylistsList.SelectedItem is Playlist sel && sel.Kind == PlaylistKind.Saves)
                {
                    TracksList.ItemsSource = null;
                    TracksList.ItemsSource = _savesPlaylist.Tracks;
                    TracksHeader.Text = $"{_savesPlaylist.Name} Playlist";
                    LoadArtworkForTracks(_savesPlaylist);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "SettingsPanel.OnSavedSongsChanged");
            }
        });
    }

    // ----- Helpers -----

    private static string? PromptForName(string title, string prompt, string defaultValue)
    {
        var window = new NameInputWindow(title, prompt, defaultValue)
        {
            Owner = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive),
        };
        if (window.ShowDialog() == true)
        {
            return window.Value;
        }
        return null;
    }
}
