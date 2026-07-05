using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyRec.Models;

public sealed class RecommendationItem
{
    public int TmdbId { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string? PosterPath { get; set; }

    public string? ReleaseDate { get; set; }

    public string? FirstAirDate { get; set; }

    public double Score { get; set; }

    public string? SourceTitle { get; set; }

    [JsonIgnore]
    public int? Year
    {
        get
        {
            var date = MediaType == "tv" ? FirstAirDate : ReleaseDate;
            return DateTime.TryParse(date, out var parsed) ? parsed.Year : null;
        }
    }
}

