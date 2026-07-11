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
    private readonly RecommendationLibraryWriter _writer;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FavoriteRequestHostedService> _logger;
    private readonly object _configurationLock = new();

    public FavoriteRequestHostedService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        SeerrApiClient seerrApiClient,
        RecommendationFolderManager folderManager,
        RecommendationLibraryWriter writer,
        ILibraryManager libraryManager,
        ILogger<FavoriteRequestHostedService> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _seerrApiClient = seerrApiClient;
        _folderManager = folderManager;
        _writer = writer;
        _libraryManager = libraryManager;
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
        if (e.UserData is null ||
            e.Item is null ||
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

        // These are Jellyfin-native actions present in Web, Android and Android TV.
        // Dislike means Not Interested; rating or Mark Played means Already Watched.
        if (e.UserData.Likes == false)
        {
            MarkNotInterested(recommendation);
            return;
        }

        if (e.SaveReason == UserDataSaveReason.TogglePlayed && e.UserData.Played)
        {
            MarkWatched(recommendation, RatingToStars(e.UserData.Rating, 3));
            return;
        }

        if (e.SaveReason == UserDataSaveReason.UpdateUserRating && e.UserData.IsFavorite)
        {
            _ = RequestAsync(e.UserId, recommendation);
            return;
        }

        if (e.SaveReason == UserDataSaveReason.UpdateUserRating && e.UserData.Rating.HasValue)
        {
            MarkWatched(recommendation, RatingToStars(e.UserData.Rating, 3));
            return;
        }

    }

    private void MarkNotInterested(Models.RecommendationItem recommendation)
    {
        lock (_configurationLock)
        {
            var config = Plugin.Config;
            var dismissed = config.DismissedItems.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            dismissed.Add($"{recommendation.MediaType}:{recommendation.TmdbId}");
            config.DismissedItems = string.Join(';', dismissed.OrderBy(value => value));
            Plugin.Instance.UpdateConfiguration(config);
            _writer.Remove(config, recommendation);
        }

        _libraryManager.QueueLibraryScan();
        _logger.LogInformation("Native Dislike marked {Title} as not interested", recommendation.Title);
    }

    private void MarkWatched(Models.RecommendationItem recommendation, int stars)
    {
        lock (_configurationLock)
        {
            var config = Plugin.Config;
            var prefix = $"{recommendation.MediaType}:{recommendation.TmdbId}|";
            var entries = config.ManuallyWatchedItems.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(value => !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            entries.Add($"{recommendation.MediaType}:{recommendation.TmdbId}|{stars}|{Uri.EscapeDataString(recommendation.Title)}");
            config.ManuallyWatchedItems = string.Join(';', entries.OrderBy(value => value));
            Plugin.Instance.UpdateConfiguration(config);
            _writer.Remove(config, recommendation);
        }

        _libraryManager.QueueLibraryScan();
        _logger.LogInformation("Native rating marked {Title} watched with {Stars} stars", recommendation.Title, stars);
    }

    private static int RatingToStars(double? rating, int fallback) =>
        rating.HasValue ? Math.Clamp((int)Math.Round(rating.Value / 2.0, MidpointRounding.AwayFromZero), 1, 5) : fallback;

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
                if (Plugin.Config.RemoveAfterRequest && _writer.Remove(Plugin.Config, recommendation))
                {
                    _libraryManager.QueueLibraryScan();
                }
            }
        }
        catch (Exception ex)
        {
            var jellyfinUser = _userManager.GetUserById(jellyfinUserId);
            _logger.LogError(ex, "Failed to create Seerr request for {Title} by Jellyfin user {User}", recommendation.Title, jellyfinUser?.Username ?? jellyfinUserId.ToString());
        }
    }
}
