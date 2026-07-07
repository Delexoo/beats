using System.IO;
using System.Text.RegularExpressions;

namespace MusicWidget.Models;

public static class TrackNameFormatter
{
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Turns download-style names (Teminite_-_A_New_Dawn) into readable text (Teminite - A New Dawn).
    /// </summary>
    public static string Beautify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw.Trim().Replace('_', ' ');
        s = MultiSpaceRegex.Replace(s, " ");
        s = Regex.Replace(s, @"\s*-\s*", " - ");
        return s.Trim();
    }

    public static bool TryParseArtistTitle(string? source, out string artist, out string title)
    {
        artist = string.Empty;
        title = Beautify(source);

        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        artist = title[..separator].Trim();
        title = title[(separator + 3)..].Trim();
        return !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return Beautify(cleaned);
    }

    public static string BuildDisplayName(string? artist, string? title)
    {
        var cleanTitle = SanitizeFileName(title ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            return string.Empty;
        }

        var cleanArtist = SanitizeFileName(artist ?? string.Empty);
        return string.IsNullOrWhiteSpace(cleanArtist)
            ? cleanTitle
            : $"{cleanArtist} - {cleanTitle}";
    }
}
