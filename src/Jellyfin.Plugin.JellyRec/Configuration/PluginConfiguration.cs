using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRec.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; }

    public string SeerrUrl { get; set; } = "http://localhost:5055";

    public string ApiKey { get; set; } = string.Empty;

    public string RecommendationLibraryPath { get; set; } = "/data/JellyRec";

    public string SourceLibraryPaths { get; set; } = string.Empty;

    public bool EnableSeerrRecommendations { get; set; } = true;

    public bool EnableTraktRecommendations { get; set; } = true;

    public string TraktClientId { get; set; } = string.Empty;

    public string TraktAccessToken { get; set; } = string.Empty;

    public int TraktWeight { get; set; } = 3;

    public int SeerrWeight { get; set; } = 1;

    public int RecentlyWatchedLimit { get; set; } = 20;

    public int RecommendationsPerSeed { get; set; } = 8;

    public int MaxRecommendations { get; set; } = 100;

    public int RefreshIntervalHours { get; set; } = 24;

    public bool RequestOnlyFirstSeason { get; set; } = true;

    public bool RemoveAfterRequest { get; set; } = true;
}
