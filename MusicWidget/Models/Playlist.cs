using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicWidget.Models;

public enum PlaylistKind
{
    /// <summary>A user playlist backed by a real folder on disk.</summary>
    Normal,
    /// <summary>Virtual playlist of liked songs, kept in <c>liked-songs.json</c>.</summary>
    Liked,
    /// <summary>Virtual placeholder playlist for saves / downloads.</summary>
    Saves,
}

public sealed class Playlist : INotifyPropertyChanged
{
    private bool _isPlayingNow;
    private bool _isActivePlaylist;
    private bool _isUserPinned;

    public string Name { get; }
    public string FolderPath { get; }
    public PlaylistKind Kind { get; }
    public string? IconKey { get; }

    /// <summary>
    /// User-controlled pin flag for Normal playlists. Liked/Saves are always pinned
    /// regardless of this value (see <see cref="IsPinned"/>).
    /// </summary>
    public bool IsUserPinned
    {
        get => _isUserPinned;
        set
        {
            if (_isUserPinned == value) return;
            _isUserPinned = value;
            OnPropertyChanged();
            // IsPinned is derived from IsUserPinned + Kind, so notify it too so
            // the pinned-badge data triggers re-evaluate.
            OnPropertyChanged(nameof(IsPinned));
        }
    }

    /// <summary>
    /// True for virtual playlists (Liked/Saves) and for any Normal playlist the
    /// user has pinned. Pinned rows render with a leading badge and bubble up
    /// above the divider in the playlist list.
    /// </summary>
    public bool IsPinned => Kind != PlaylistKind.Normal || _isUserPinned;

    public List<Track> Tracks { get; } = new();

    public bool IsPlayingNow
    {
        get => _isPlayingNow;
        set
        {
            if (_isPlayingNow != value)
            {
                _isPlayingNow = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True when this playlist is the current playback source (playing or paused).
    /// Drives play/pause toggle vs. play-from-start on the row play button.
    /// </summary>
    public bool IsActivePlaylist
    {
        get => _isActivePlaylist;
        set
        {
            if (_isActivePlaylist != value)
            {
                _isActivePlaylist = value;
                OnPropertyChanged();
            }
        }
    }

    public Playlist(string name, string folderPath, PlaylistKind kind = PlaylistKind.Normal, string? iconKey = null)
    {
        Name = name;
        FolderPath = folderPath;
        Kind = kind;
        IconKey = iconKey;
    }

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
