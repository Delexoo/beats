using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MusicWidget.Models;

public sealed class Track : INotifyPropertyChanged
{
    private ImageSource? _artworkSource;
    private string? _title;
    private string? _artist;
    private bool _isLiked;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Human-friendly track label derived from the file name when tags are missing.</summary>
    public string FriendlyDisplayName => TrackNameFormatter.Beautify(DisplayName);

    public ImageSource? ArtworkSource
    {
        get => _artworkSource;
        set
        {
            if (!ReferenceEquals(_artworkSource, value))
            {
                _artworkSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasArtwork));
            }
        }
    }

    public bool HasArtwork => _artworkSource is not null;

    public string? Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Initials));
            }
        }
    }

    public string? Artist
    {
        get => _artist;
        set
        {
            if (_artist != value)
            {
                _artist = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Initials));
            }
        }
    }

    public string Initials => ComputeInitials(_title ?? FriendlyDisplayName);

    public bool IsLiked
    {
        get => _isLiked;
        set
        {
            if (_isLiked == value) return;
            _isLiked = value;
            OnPropertyChanged();
        }
    }

    public Track(string filePath)
    {
        FilePath = filePath;
    }

    public override string ToString() => DisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string ComputeInitials(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "??";
        }

        var cleaned = source
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "??";
        }

        if (parts.Length == 1)
        {
            var only = parts[0];
            if (only.Length >= 2)
            {
                return (char.ToUpper(only[0]).ToString() + char.ToUpper(only[1])).Trim();
            }
            return char.ToUpper(only[0]).ToString();
        }

        return (char.ToUpper(parts[0][0]).ToString() + char.ToUpper(parts[1][0])).Trim();
    }
}
