using System;
using System.IO;
using MusicWidget.Models;
using TagLib;

namespace MusicWidget.Services;

public static class TrackTagService
{
    public static (string Artist, string Title) ReadDisplayMetadata(string filePath, Models.Track? track = null)
    {
        var artist = track?.Artist ?? string.Empty;
        var title = track?.Title ?? string.Empty;

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
            {
                title = TrackNameFormatter.Beautify(tagFile.Tag.Title);
            }

            var performer = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist;
            if (!string.IsNullOrWhiteSpace(performer))
            {
                artist = TrackNameFormatter.Beautify(performer);
            }
        }
        catch
        {
            // Tag reads are best-effort; fall back to the file name.
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (TrackNameFormatter.TryParseArtistTitle(fileName, out var parsedArtist, out var parsedTitle))
            {
                if (string.IsNullOrWhiteSpace(artist))
                {
                    artist = parsedArtist;
                }

                title = parsedTitle;
            }
            else
            {
                title = TrackNameFormatter.Beautify(fileName);
            }
        }

        return (artist, title);
    }

    public static byte[]? ReadEmbeddedCover(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var pics = tagFile.Tag.Pictures;
            if (pics is null || pics.Length == 0)
            {
                return null;
            }

            IPicture? best = null;
            foreach (var pic in pics)
            {
                if (pic.Type == PictureType.FrontCover)
                {
                    return pic.Data?.Data;
                }

                best ??= pic;
            }

            return best?.Data?.Data;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteMetadata(string filePath, string artist, string title, string? coverImagePath)
    {
        using var tagFile = TagLib.File.Create(filePath);
        var cleanTitle = TrackNameFormatter.SanitizeFileName(title);
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            throw new ArgumentException("Song name cannot be empty.");
        }

        tagFile.Tag.Title = cleanTitle;

        var cleanArtist = TrackNameFormatter.SanitizeFileName(artist);
        tagFile.Tag.Performers = string.IsNullOrWhiteSpace(cleanArtist)
            ? Array.Empty<string>()
            : [cleanArtist];

        if (coverImagePath is not null)
        {
            var bytes = System.IO.File.ReadAllBytes(coverImagePath);
            tagFile.Tag.Pictures =
            [
                new Picture
                {
                    Type = PictureType.FrontCover,
                    MimeType = GetMimeType(coverImagePath),
                    Data = new ByteVector(bytes),
                },
            ];
        }

        tagFile.Save();
    }

    private static string GetMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
}
