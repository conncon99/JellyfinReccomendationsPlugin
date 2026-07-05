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

