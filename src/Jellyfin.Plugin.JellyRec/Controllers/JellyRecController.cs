using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Controllers;

[ApiController]
[Route("JellyRec")]
public sealed class JellyRecController : ControllerBase
{
    private readonly SeerrApiClient _seerrApiClient;
    private readonly TraktApiClient _traktApiClient;
    private readonly RecommendationService _recommendationService;
    private readonly ILogger<JellyRecController> _logger;

    public JellyRecController(
        SeerrApiClient seerrApiClient,
        TraktApiClient traktApiClient,
        RecommendationService recommendationService,
        ILogger<JellyRecController> logger)
    {
        _seerrApiClient = seerrApiClient;
        _traktApiClient = traktApiClient;
        _recommendationService = recommendationService;
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

    [HttpPost("Refresh")]
    public async Task<ActionResult> Refresh(CancellationToken cancellationToken)
    {
        var recommendations = await _recommendationService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Manual JellyRec refresh wrote {Count} recommendations", recommendations.Count);
        return Ok(new { count = recommendations.Count });
    }
}

