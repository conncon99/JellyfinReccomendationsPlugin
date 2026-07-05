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
    private readonly ILogger<JellyRecController> _logger;

    public JellyRecController(
        SeerrApiClient seerrApiClient,
        TraktApiClient traktApiClient,
        RecommendationService recommendationService,
        RecommendationLibraryWriter writer,
        RecommendationFolderManager folderManager,
        ILogger<JellyRecController> logger)
    {
        _seerrApiClient = seerrApiClient;
        _traktApiClient = traktApiClient;
        _recommendationService = recommendationService;
        _writer = writer;
        _folderManager = folderManager;
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
        var path = await _folderManager.EnsureRecommendationPathAsync(config, cancellationToken).ConfigureAwait(false);
        return Ok(new { path });
    }

    [HttpPost("Refresh")]
    public async Task<ActionResult> Refresh(CancellationToken cancellationToken)
    {
        var recommendations = await _recommendationService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Manual JellyRec refresh wrote {Count} recommendations", recommendations.Count);
        return Ok(new { count = recommendations.Count, recommendations });
    }

    [HttpGet("Recommendations")]
    public ActionResult GetRecommendations()
    {
        var recommendations = _writer.ReadAll(Plugin.Config);
        return Ok(new { count = recommendations.Count, recommendations });
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
