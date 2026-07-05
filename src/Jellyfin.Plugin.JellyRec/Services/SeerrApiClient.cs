using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class SeerrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<SeerrApiClient> _logger;

    public SeerrApiClient(HttpClient httpClient, ILogger<SeerrApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SeerrStatus?> TestConnectionAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        using var statusRequest = BuildRequest(config, HttpMethod.Get, "/api/v1/status");
        using var statusResponse = await _httpClient.SendAsync(statusRequest, cancellationToken).ConfigureAwait(false);
        statusResponse.EnsureSuccessStatusCode();

        using var authRequest = BuildRequest(config, HttpMethod.Get, "/api/v1/auth/me");
        using var authResponse = await _httpClient.SendAsync(authRequest, cancellationToken).ConfigureAwait(false);
        authResponse.EnsureSuccessStatusCode();

        return await statusResponse.Content.ReadFromJsonAsync<SeerrStatus>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<SeerrMediaResult>> GetRecommendationsAsync(PluginConfiguration config, string mediaType, int tmdbId, int take, CancellationToken cancellationToken)
    {
        var endpoint = mediaType == "tv"
            ? $"/api/v1/tv/{tmdbId}/recommendations"
            : $"/api/v1/movie/{tmdbId}/recommendations";

        using var request = BuildRequest(config, HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Seerr recommendations endpoint returned {StatusCode} for {MediaType} {TmdbId}", response.StatusCode, mediaType, tmdbId);
            return new List<SeerrMediaResult>();
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var paged = JsonSerializer.Deserialize<SeerrPagedResponse<SeerrMediaResult>>(content, JsonOptions);
        if (paged?.Results is { Count: > 0 })
        {
            return paged.Results.Take(take).ToList();
        }

        var array = JsonSerializer.Deserialize<List<SeerrMediaResult>>(content, JsonOptions);
        return array?.Take(take).ToList() ?? new List<SeerrMediaResult>();
    }

    public async Task<List<SeerrUser>> GetUsersAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(config, HttpMethod.Get, "/api/v1/user?take=2147483647");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var paged = JsonSerializer.Deserialize<SeerrPagedResponse<SeerrUser>>(content, JsonOptions);
        return paged?.Results ?? new List<SeerrUser>();
    }

    public async Task<SeerrRequestResponse?> CreateRequestAsync(PluginConfiguration config, string mediaType, int tmdbId, int? userId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["mediaType"] = mediaType,
            ["mediaId"] = tmdbId,
            ["is4k"] = false
        };

        if (mediaType == "tv")
        {
            body["seasons"] = config.RequestOnlyFirstSeason ? new[] { 1 } : "all";
        }

        if (userId.HasValue)
        {
            body["userId"] = userId.Value;
        }

        using var request = BuildRequest(config, HttpMethod.Post, "/api/v1/request");
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SeerrRequestResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(PluginConfiguration config, HttpMethod method, string path)
    {
        var baseUrl = config.SeerrUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, baseUrl + path);
        request.Headers.TryAddWithoutValidation("X-Api-Key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }
}

