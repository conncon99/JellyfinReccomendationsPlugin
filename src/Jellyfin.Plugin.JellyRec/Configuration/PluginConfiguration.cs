using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRec.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; }

    public string SeerrUrl { get; set; } = "http://seerr:5055";

    public string ApiKey { get; set; } = string.Empty;

    public string RecommendationLibraryPath { get; set; } = string.Empty;

    public string SourceLibraryPaths { get; set; } = string.Empty;

    public bool EnableSeerrRecommendations { get; set; } = true;

    public bool EnableTraktRecommendations { get; set; } = true;

    public bool SyncJellyfinHistoryToTrakt { get; set; }

    public DateTime? TraktHistoryLastSyncedAtUtc { get; set; }

    public string TraktClientId { get; set; } = string.Empty;

    public string TraktClientSecret { get; set; } = string.Empty;

    public string TraktAccessToken { get; set; } = string.Empty;

    public string TraktRefreshToken { get; set; } = string.Empty;

    public DateTime? TraktAccessTokenExpiresAtUtc { get; set; }

    public int TraktWeight { get; set; } = 3;

    public int SeerrWeight { get; set; } = 1;

    public int RecentlyWatchedLimit { get; set; } = 20;

    public int RecommendationsPerSeed { get; set; } = 8;

    public int MaxRecommendations { get; set; } = 100;

    public int RefreshIntervalHours { get; set; } = 24;

    public bool RequestOnlyFirstSeason { get; set; } = true;

    public bool RemoveAfterRequest { get; set; } = true;

    public double MinRating { get; set; } = 6.0;

    public double DiversityStrength { get; set; } = 1.0;

    public string DismissedItems { get; set; } = string.Empty;

    public string ManuallyWatchedItems { get; set; } = string.Empty;

    public int RecommendationRetentionDays { get; set; } = 7;

    public string ExpiredRecommendationCooldowns { get; set; } = string.Empty;
}
