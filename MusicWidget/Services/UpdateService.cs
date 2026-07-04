using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicWidget.Services;

public sealed class UpdateService
{
    public const string InstallerAssetName = "Beats-Setup-x64.exe";

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string CurrentVersion { get; } = GetCurrentVersion();

    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var url =
            $"https://api.github.com/repos/{AppBranding.GitHubOwner}/{AppBranding.GitHubRepo}/releases/latest";

        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        if (release?.TagName is not { Length: > 0 } tagName)
        {
            return null;
        }

        var latest = NormalizeVersion(tagName);
        var asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase));
        var downloadUrl = asset?.BrowserDownloadUrl;
        var isUpdateAvailable = IsNewerVersion(latest, CurrentVersion);

        return new UpdateCheckResult(latest, downloadUrl, isUpdateAvailable);
    }

    public async Task<string> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Beats-updates");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, InstallerAssetName);

        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            read += count;
            if (total > 0)
            {
                progress?.Report((double)read / total);
            }
        }

        return path;
    }

    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        });
    }

    public static bool IsNewerVersion(string candidate, string current)
    {
        var left = ParseVersionParts(candidate);
        var right = ParseVersionParts(current);
        for (var i = 0; i < 3; i++)
        {
            if (left[i] > right[i]) return true;
            if (left[i] < right[i]) return false;
        }

        return false;
    }

    private static int[] ParseVersionParts(string value)
    {
        return NormalizeVersion(value)
            .Split('.')
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .Concat(Enumerable.Repeat(0, 3))
            .Take(3)
            .ToArray();
    }

    private static string NormalizeVersion(string value)
    {
        return value.Trim().TrimStart('v', 'V');
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppBranding.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record UpdateCheckResult(
    string LatestVersion,
    string? DownloadUrl,
    bool IsUpdateAvailable);
