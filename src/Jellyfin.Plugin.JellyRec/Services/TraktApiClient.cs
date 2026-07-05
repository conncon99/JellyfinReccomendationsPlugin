using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class TraktApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string UserAgent = "JellyRec/0.1 (+https://github.com/conncon99/JellyfinReccomendationsPlugin)";
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

    public async Task<TraktDeviceCodeResponse> BeginDeviceAuthorizationAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TraktClientId))
        {
            throw new InvalidOperationException("Trakt client ID is required.");
        }

        using var request = BuildTraktJsonRequest(
            config,
            "https://api.trakt.tv/oauth/device/code",
            new { client_id = config.TraktClientId });
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Trakt device authorization failed ({(int)response.StatusCode} {response.StatusCode}): {content}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TraktDeviceCodeResponse>(responseJson, JsonOptions);
        if (result is null || string.IsNullOrWhiteSpace(result.DeviceCode) || string.IsNullOrWhiteSpace(result.UserCode))
        {
            throw new InvalidOperationException($"Trakt returned a device authorization response without a device code: {responseJson}");
        }

        return result;
    }

    public async Task<TraktTokenPollResult> PollDeviceTokenAsync(PluginConfiguration config, string deviceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TraktClientId) || string.IsNullOrWhiteSpace(config.TraktClientSecret))
        {
            throw new InvalidOperationException("Trakt client ID and client secret are required.");
        }

        using var request = BuildTraktJsonRequest(
            config,
            "https://api.trakt.tv/oauth/device/token",
            new
            {
                code = deviceCode,
                client_id = config.TraktClientId,
                client_secret = config.TraktClientSecret
            });
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var token = await response.Content.ReadFromJsonAsync<TraktTokenResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return TraktTokenPollResult.Approved(token ?? throw new InvalidOperationException("Trakt returned an empty token response."));
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (content.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.Pending();
        }

        if (content.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.SlowDown();
        }

        if (content.Contains("expired_token", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.Expired();
        }

        if (content.Contains("access_denied", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.Denied();
        }

        if (content.Contains("invalid_client", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.InvalidClient();
        }

        if (content.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
        {
            return TraktTokenPollResult.InvalidGrant();
        }

        _logger.LogWarning("Trakt device token poll failed with {StatusCode}: {Response}", response.StatusCode, content);
        return TraktTokenPollResult.Failed();
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
        AddCommonHeaders(request);
        return request;
    }

    private static HttpRequestMessage BuildTraktJsonRequest<T>(PluginConfiguration config, string url, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("trakt-api-version", "2");
        request.Headers.TryAddWithoutValidation("trakt-api-key", config.TraktClientId);
        AddCommonHeaders(request);
        return request;
    }

    private static void AddCommonHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}

public sealed class TraktDeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_url")]
    public string VerificationUrl { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;
}

public sealed class TraktTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

public sealed class TraktTokenPollResult
{
    private TraktTokenPollResult(string status, TraktTokenResponse? token = null)
    {
        Status = status;
        Token = token;
    }

    public string Status { get; }

    public TraktTokenResponse? Token { get; }

    public static TraktTokenPollResult Approved(TraktTokenResponse token) => new("approved", token);

    public static TraktTokenPollResult Pending() => new("pending");

    public static TraktTokenPollResult SlowDown() => new("slow_down");

    public static TraktTokenPollResult Expired() => new("expired");

    public static TraktTokenPollResult Denied() => new("denied");

    public static TraktTokenPollResult InvalidClient() => new("invalid_client");

    public static TraktTokenPollResult InvalidGrant() => new("invalid_grant");

    public static TraktTokenPollResult Failed() => new("failed");
}
