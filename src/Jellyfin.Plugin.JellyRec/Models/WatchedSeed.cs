namespace Jellyfin.Plugin.JellyRec.Models;

public sealed class WatchedSeed
{
    public int TmdbId { get; init; }

    public string MediaType { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DateTime? LastPlayedDate { get; init; }

    public int PlayCount { get; init; }

    public double? UserRating { get; init; }
}

