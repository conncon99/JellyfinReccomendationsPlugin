using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class TraktApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<TraktApiClient> _logger;

    public TraktApiClient(HttpClient httpClient, ILogger<TraktApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(config, HttpMethod.Get, "https://api.trakt.tv/users/settings");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<RecommendationItem>> GetPersonalRecommendationsAsync(PluginConfiguration config, int limit, CancellationToken cancellationToken)
    {
        var results = new List<RecommendationItem>();
        results.AddRange(await GetRecommendationBucketAsync(config, "movies", limit, cancellationToken).ConfigureAwait(false));
        results.AddRange(await GetRecommendationBucketAsync(config, "shows", limit, cancellationToken).ConfigureAwait(false));
        return results;
    }

    private async Task<List<RecommendationItem>> GetRecommendationBucketAsync(PluginConfiguration config, string bucket, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TraktClientId) || string.IsNullOrWhiteSpace(config.TraktAccessToken))
        {
            return new List<RecommendationItem>();
        }

        using var request = BuildRequest(config, HttpMethod.Get, $"https://api.trakt.tv/recommendations/{bucket}?limit={Math.Max(1, limit)}&extended=full");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Trakt recommendations returned {StatusCode} for {Bucket}", response.StatusCode, bucket);
            return new List<RecommendationItem>();
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var recommendations = JsonSerializer.Deserialize<List<JsonElement>>(content, JsonOptions);
        return recommendations?
            .Select(r => ToRecommendationItem(bucket, r))
            .Where(r => r is not null)
            .Cast<RecommendationItem>()
            .ToList() ?? new List<RecommendationItem>();
    }

    private static RecommendationItem? ToRecommendationItem(string bucket, JsonElement element)
    {
        var mediaElement = element;
        if (bucket == "movies" && element.TryGetProperty("movie", out var movieWrapper))
        {
            mediaElement = movieWrapper;
        }
        else if (bucket == "shows" && element.TryGetProperty("show", out var showWrapper))
        {
            mediaElement = showWrapper;
        }

        if (!mediaElement.TryGetProperty("ids", out var ids) ||
            !ids.TryGetProperty("tmdb", out var tmdbProperty) ||
            !tmdbProperty.TryGetInt32(out var tmdbId))
        {
            return null;
        }

        var title = mediaElement.TryGetProperty("title", out var titleProperty)
            ? titleProperty.GetString()
            : null;
        var overview = mediaElement.TryGetProperty("overview", out var overviewProperty)
            ? overviewProperty.GetString()
            : null;

        if (bucket == "movies")
        {
            return new RecommendationItem
            {
                TmdbId = tmdbId,
                MediaType = "movie",
                Title = title ?? "Untitled Movie",
                Overview = overview ?? string.Empty,
                Score = 1,
                SourceTitle = "Trakt"
            };
        }

        if (bucket == "shows")
        {
            return new RecommendationItem
            {
                TmdbId = tmdbId,
                MediaType = "tv",
                Title = title ?? "Untitled Show",
                Overview = overview ?? string.Empty,
                Score = 1,
                SourceTitle = "Trakt"
            };
        }

        return null;
    }

    private static HttpRequestMessage BuildRequest(PluginConfiguration config, HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("trakt-api-version", "2");
        request.Headers.TryAddWithoutValidation("trakt-api-key", config.TraktClientId);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.TraktAccessToken}");
        return request;
    }
}
