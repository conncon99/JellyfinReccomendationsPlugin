using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyRec.Controllers;

[ApiController]
[Route("JellyRec")]
public sealed class JellyRecController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SeerrApiClient _seerrApiClient;
    private readonly TraktApiClient _traktApiClient;
    private readonly RecommendationService _recommendationService;
    private readonly RecommendationLibraryWriter _writer;
    private readonly RecommendationFolderManager _folderManager;
    private readonly RecommendationHomeLibraryManager _homeLibraryManager;
    private readonly ILogger<JellyRecController> _logger;

    public JellyRecController(
        SeerrApiClient seerrApiClient,
        TraktApiClient traktApiClient,
        RecommendationService recommendationService,
        RecommendationLibraryWriter writer,
        RecommendationFolderManager folderManager,
        RecommendationHomeLibraryManager homeLibraryManager,
        ILogger<JellyRecController> logger)
    {
        _seerrApiClient = seerrApiClient;
        _traktApiClient = traktApiClient;
        _recommendationService = recommendationService;
        _writer = writer;
        _folderManager = folderManager;
        _homeLibraryManager = homeLibraryManager;
        _logger = logger;
    }

    [HttpPost("TestConnection")]
    public async Task<ActionResult> TestConnection([FromBody] PluginConfiguration config, CancellationToken cancellationToken)
    {
        await _seerrApiClient.TestConnectionAsync(config, cancellationToken).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("TestTrakt")]
    public async Task<ActionResult> TestTrakt([FromBody] PluginConfiguration config, CancellationToken cancellationToken)
    {
        return await _traktApiClient.TestConnectionAsync(config, cancellationToken).ConfigureAwait(false) ? Ok() : Unauthorized();
    }

    [HttpPost("Trakt/DeviceCode")]
    public async Task<ActionResult> BeginTraktDeviceAuthorization([FromBody] PluginConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _traktApiClient.BeginDeviceAuthorizationAsync(config, cancellationToken).ConfigureAwait(false);
            return JsonContent(new Dictionary<string, object?>
            {
                ["deviceCode"] = response.DeviceCode,
                ["userCode"] = response.UserCode,
                ["verificationUrl"] = response.VerificationUrl,
                ["expiresIn"] = response.ExpiresIn,
                ["interval"] = response.Interval
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Trakt device authorization");
            return JsonContent(new Dictionary<string, object?>
            {
                ["status"] = "failed",
                ["message"] = ex.Message
            });
        }
    }

    [HttpPost("Trakt/DeviceToken")]
    public async Task<ActionResult> PollTraktDeviceToken([FromBody] TraktDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _traktApiClient.PollDeviceTokenAsync(request.Configuration, request.DeviceCode, cancellationToken).ConfigureAwait(false);
            if (result.Token is null)
            {
                return JsonContent(new Dictionary<string, object?>
                {
                    ["status"] = result.Status,
                    ["message"] = result.Message
                });
            }

            return JsonContent(new Dictionary<string, object?>
            {
                ["status"] = result.Status,
                ["accessToken"] = result.Token.AccessToken,
                ["refreshToken"] = result.Token.RefreshToken,
                ["expiresAtUtc"] = DateTime.UtcNow.AddSeconds(result.Token.ExpiresIn)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll Trakt device authorization");
            return JsonContent(new Dictionary<string, object?>
            {
                ["status"] = "failed",
                ["message"] = ex.Message
            });
        }
    }

    [HttpPost("EnsureRecommendationFolder")]
    public async Task<ActionResult> EnsureRecommendationFolder([FromBody] PluginConfiguration config, CancellationToken cancellationToken)
    {
        await _homeLibraryManager.EnsureHomeLibraryAsync(config, cancellationToken).ConfigureAwait(false);
        var path = _folderManager.ResolveRecommendationPath(config);
        return Ok(new { path });
    }

    [HttpPost("Refresh")]
    public async Task<ActionResult> Refresh(CancellationToken cancellationToken)
    {
        var recommendations = await _recommendationService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await _homeLibraryManager.EnsureHomeLibraryAsync(Plugin.Config, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Manual JellyRec refresh wrote {Count} recommendations", recommendations.Count);
        return Ok(new { count = recommendations.Count, recommendations });
    }

    [HttpPost("Trakt/ResyncHistory")]
    public async Task<ActionResult> ResyncTraktHistory(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _recommendationService.SyncAllTraktHistoryAsync(cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Trakt history resync failed");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("Recommendations")]
    public ActionResult GetRecommendations()
    {
        var recommendations = _writer.ReadAll(Plugin.Config);
        return Ok(new { count = recommendations.Count, recommendations });
    }

    [HttpPost("NotInterested")]
    public ActionResult MarkNotInterested([FromBody] NotInterestedRequest request)
    {
        if (request.TmdbId <= 0 || (request.MediaType != "movie" && request.MediaType != "tv"))
        {
            return BadRequest();
        }

        var config = Plugin.Config;
        var key = $"{request.MediaType}:{request.TmdbId}";
        var dismissed = config.DismissedItems
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        dismissed.Add(key);
        config.DismissedItems = string.Join(';', dismissed.OrderBy(value => value));
        Plugin.Instance.UpdateConfiguration(config);

        var existing = _writer.ReadAll(config).FirstOrDefault(item => item.TmdbId == request.TmdbId && item.MediaType == request.MediaType);
        if (existing is not null)
        {
            _writer.Remove(config, existing);
        }

        _logger.LogInformation("Marked {MediaType} {TmdbId} as not interested", request.MediaType, request.TmdbId);
        return Ok();
    }

    [HttpPost("Watched")]
    public ActionResult MarkWatched([FromBody] WatchedRequest request)
    {
        if (request.TmdbId <= 0 || (request.MediaType != "movie" && request.MediaType != "tv") ||
            request.Rating < 1 || request.Rating > 5)
        {
            return BadRequest();
        }

        var config = Plugin.Config;
        var entries = config.ManuallyWatchedItems
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(value => !value.StartsWith($"{request.MediaType}:{request.TmdbId}|", StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Add($"{request.MediaType}:{request.TmdbId}|{request.Rating}|{Uri.EscapeDataString(request.Title ?? string.Empty)}");
        config.ManuallyWatchedItems = string.Join(';', entries.OrderBy(value => value));
        Plugin.Instance.UpdateConfiguration(config);

        var existing = _writer.ReadAll(config).FirstOrDefault(item => item.TmdbId == request.TmdbId && item.MediaType == request.MediaType);
        if (existing is not null)
        {
            _writer.Remove(config, existing);
        }

        _logger.LogInformation("Marked {MediaType} {TmdbId} watched with {Rating} stars", request.MediaType, request.TmdbId, request.Rating);
        return Ok();
    }

    private ContentResult JsonContent(Dictionary<string, object?> payload)
    {
        return Content(JsonSerializer.Serialize(payload, JsonOptions), "application/json");
    }
}

public sealed class TraktDeviceTokenRequest
{
    public PluginConfiguration Configuration { get; set; } = new();

    public string DeviceCode { get; set; } = string.Empty;
}

public sealed class NotInterestedRequest
{
    public int TmdbId { get; set; }

    public string MediaType { get; set; } = string.Empty;
}

public sealed class WatchedRequest
{
    public int TmdbId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int Rating { get; set; }
}
