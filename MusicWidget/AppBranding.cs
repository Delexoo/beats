namespace MusicWidget;

/// <summary>Product display name and on-disk folder names for Beats.</summary>
public static class AppBranding
{
    public const string DisplayName = "Beats";
    public const string AppDataFolderName = "Beats";
    public const string LegacyAppDataFolderName = "MusicWidget";
    public const string DefaultPlaylistsFolderName = "Beats";
    public static string UserAgent
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "Beats/0.0.0" : $"Beats/{version.Major}.{version.Minor}.{version.Build}";
        }
    }
    public const string HelpPageUrl = "https://delexoo.github.io/beats/help.html";
    public const string PublisherName = "Delexo";
    public const string StoreUrl = "https://delexo.store";
    public const string GitHubOwner = "Delexoo";
    public const string GitHubRepo = "beats";
    public const string GitHubProfileUrl = "https://github.com/Delexoo";
    public const string GitHubRepoUrl = "https://github.com/Delexoo/beats";
    public const string GitHubReleasesUrl = "https://github.com/Delexoo/beats/releases";
    public const string GitHubIssuesUrl = "https://github.com/Delexoo/beats/issues";
}
