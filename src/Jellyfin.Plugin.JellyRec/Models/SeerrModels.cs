using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyRec.Models;

public sealed class SeerrStatus
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class SeerrPagedResponse<T>
{
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

public sealed class SeerrMediaResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }
}

public sealed class SeerrUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("jellyfinUserGuid")]
    public string? JellyfinUserGuid { get; set; }
}

public sealed class SeerrRequestResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

