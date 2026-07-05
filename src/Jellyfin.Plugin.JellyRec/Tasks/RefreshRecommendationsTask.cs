using Jellyfin.Plugin.JellyRec.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Tasks;

public sealed class RefreshRecommendationsTask : IScheduledTask
{
    private readonly RecommendationService _recommendationService;
    private readonly RecommendationHomeLibraryManager _homeLibraryManager;
    private readonly ILogger<RefreshRecommendationsTask> _logger;

    public RefreshRecommendationsTask(
        RecommendationService recommendationService,
        RecommendationHomeLibraryManager homeLibraryManager,
        ILogger<RefreshRecommendationsTask> logger)
    {
        _recommendationService = recommendationService;
        _homeLibraryManager = homeLibraryManager;
        _logger = logger;
    }

    public string Name => "Refresh Jellyfin Recommendations";

    public string Key => "JellyRecRefreshRecommendations";

    public string Description => "Refreshes Jellyfin recommendation placeholders using Trakt and Seerr.";

    public string Category => "Jellyfin Recommendations";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(5);
        var recommendations = await _recommendationService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await _homeLibraryManager.EnsureHomeLibraryAsync(Plugin.Config, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
        _logger.LogInformation("Scheduled JellyRec refresh wrote {Count} recommendations", recommendations.Count);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        if (!Plugin.Config.Enabled)
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(Math.Max(1, Plugin.Config.RefreshIntervalHours)).Ticks
            }
        };
    }
}
