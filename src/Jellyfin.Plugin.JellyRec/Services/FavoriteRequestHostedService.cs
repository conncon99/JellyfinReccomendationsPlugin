using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class FavoriteRequestHostedService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly SeerrApiClient _seerrApiClient;
    private readonly RecommendationFolderManager _folderManager;
    private readonly ILogger<FavoriteRequestHostedService> _logger;

    public FavoriteRequestHostedService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        SeerrApiClient seerrApiClient,
        RecommendationFolderManager folderManager,
        ILogger<FavoriteRequestHostedService> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _seerrApiClient = seerrApiClient;
        _folderManager = folderManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.UpdateUserRating ||
            e.UserData is null ||
            e.Item is null ||
            !e.UserData.IsFavorite ||
            !Plugin.Config.Enabled)
        {
            return;
        }

        var recommendationRoot = _folderManager.ResolveRecommendationPath(Plugin.Config);
        var recommendation = RecommendationLibraryWriter.TryReadMetadataForPath(recommendationRoot, e.Item.Path);
        if (recommendation is null)
        {
            return;
        }

        _ = RequestAsync(e.UserId, recommendation);
    }

    private async Task RequestAsync(Guid jellyfinUserId, Models.RecommendationItem recommendation)
    {
        try
        {
            var users = await _seerrApiClient.GetUsersAsync(Plugin.Config, CancellationToken.None).ConfigureAwait(false);
            var seerrUserId = users.FirstOrDefault(user => string.Equals(user.JellyfinUserGuid, jellyfinUserId.ToString(), StringComparison.OrdinalIgnoreCase))?.Id;
            var request = await _seerrApiClient.CreateRequestAsync(Plugin.Config, recommendation.MediaType, recommendation.TmdbId, seerrUserId, CancellationToken.None).ConfigureAwait(false);

            if (request?.Id > 0)
            {
                _logger.LogInformation("Created Seerr request {RequestId} for {Title}", request.Id, recommendation.Title);
            }
        }
        catch (Exception ex)
        {
            var jellyfinUser = _userManager.GetUserById(jellyfinUserId);
            _logger.LogError(ex, "Failed to create Seerr request for {Title} by Jellyfin user {User}", recommendation.Title, jellyfinUser?.Username ?? jellyfinUserId.ToString());
        }
    }
}
