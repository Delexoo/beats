using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicWidget.Models;

using MusicWidget;

namespace MusicWidget.Services;

public sealed class ArtworkService
{
    private static readonly HttpClient _http = CreateClient();
    private static readonly SemaphoreSlim _loadConcurrency = new(6, 6);
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _memCache = new();

    public ArtworkService(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    public Task<ImageSource?> GetArtworkAsync(Track track, CancellationToken ct = default)
    {
        var key = NormalizeKey(track.FilePath);
        if (_memCache.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached);
        }

        return _inflight.GetOrAdd(key, _ => LoadAsync(track, ct));
    }

    private async Task<ImageSource?> LoadAsync(Track track, CancellationToken ct)
    {
        await _loadConcurrency.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = NormalizeKey(track.FilePath);
            var cachePath = Path.Combine(_cacheDir, key + ".png");
            if (File.Exists(cachePath))
            {
                var fromDisk = LoadBitmap(cachePath);
                _memCache[key] = fromDisk;
                return fromDisk;
            }

            string? title = null;
            string? artist = null;
            byte[]? embedded = null;

            try
            {
                using var tagFile = TagLib.File.Create(track.FilePath);
                title = tagFile.Tag.Title;
                artist = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist;
                var pics = tagFile.Tag.Pictures;
                if (pics is not null && pics.Length > 0)
                {
                    embedded = pics[0].Data?.Data;
                }
            }
            catch
            {
                // Reading tags is best-effort; some files don't have any.
            }

            UpdateTrackTagsOnUi(track, title, artist);

            byte[]? bytes = embedded;
            if (bytes is null || bytes.Length == 0)
            {
                bytes = await TryFetchOnlineAsync(track, title, artist, ct).ConfigureAwait(false);
            }

            if (bytes is null || bytes.Length == 0)
            {
                _memCache[key] = null;
                return null;
            }

            await File.WriteAllBytesAsync(cachePath, bytes, ct).ConfigureAwait(false);
            var bmp = LoadBitmap(cachePath);
            _memCache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            _loadConcurrency.Release();
            _inflight.TryRemove(NormalizeKey(track.FilePath), out _);
        }
    }

    private static void UpdateTrackTagsOnUi(Track track, string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        void Apply()
        {
            if (!string.IsNullOrWhiteSpace(title)) track.Title = title;
            if (!string.IsNullOrWhiteSpace(artist)) track.Artist = artist;
        }

        if (dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(Apply);
    }

    private static async Task<byte[]?> TryFetchOnlineAsync(
        Track track, string? title, string? artist, CancellationToken ct)
    {
        var query = BuildQuery(track, title, artist);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        try
        {
            var url = "https://itunes.apple.com/search?term=" + Uri.EscapeDataString(query) +
                      "&entity=song&limit=1";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (!first.TryGetProperty("artworkUrl100", out var artProp))
            {
                return null;
            }

            var artUrl = artProp.GetString();
            if (string.IsNullOrEmpty(artUrl)) return null;

            artUrl = artUrl.Replace("100x100bb", "300x300bb");
            return await _http.GetByteArrayAsync(artUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildQuery(Track track, string? title, string? artist)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(artist)) sb.Append(artist).Append(' ');
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append(title);
        }
        else
        {
            sb.Append(SanitizeFilename(track.DisplayName));
        }
        return sb.ToString().Trim();
    }

    private static string SanitizeFilename(string name)
    {
        var trimmed = name
            .Replace('_', ' ')
            .Replace('.', ' ');

        return trimmed;
    }

    private static ImageSource? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 120;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeKey(string filePath)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(AppBranding.UserAgent);
        return c;
    }
}
