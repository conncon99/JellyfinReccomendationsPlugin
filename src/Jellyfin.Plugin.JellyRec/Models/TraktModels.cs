using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyRec.Models;

public sealed class TraktRecommendation
{
    [JsonPropertyName("movie")]
    public TraktMovie? Movie { get; set; }

    [JsonPropertyName("show")]
    public TraktShow? Show { get; set; }
}

public sealed class TraktMovie
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("ids")]
    public TraktIds? Ids { get; set; }
}

public sealed class TraktShow
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("ids")]
    public TraktIds? Ids { get; set; }
}

public sealed class TraktIds
{
    [JsonPropertyName("tmdb")]
    public int? Tmdb { get; set; }

    [JsonPropertyName("trakt")]
    public int? Trakt { get; set; }

    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }
}

public sealed class TraktHistoryItem
{
    public int? TmdbId { get; init; }

    public int? TvdbId { get; init; }

    public int? SeriesTmdbId { get; init; }

    public string MediaType { get; init; } = string.Empty;

    public DateTime WatchedAtUtc { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }
}

public sealed class TraktHistorySyncResult
{
    public int Discovered { get; init; }

    public int Submitted { get; init; }

    public int Accepted { get; init; }

    public int Skipped { get; init; }

    public DateTime? CheckpointUtc { get; init; }
}
