using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyRec.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class RecommendationService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly SeerrApiClient _seerrApiClient;
    private readonly TraktApiClient _traktApiClient;
    private readonly RecommendationLibraryWriter _writer;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        SeerrApiClient seerrApiClient,
        TraktApiClient traktApiClient,
        RecommendationLibraryWriter writer,
        ILogger<RecommendationService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _seerrApiClient = seerrApiClient;
        _traktApiClient = traktApiClient;
        _writer = writer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RecommendationItem>> RefreshAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Config;
        if (!config.Enabled)
        {
            _logger.LogInformation("JellyRec is disabled; skipping refresh.");
            return Array.Empty<RecommendationItem>();
        }

        var seeds = GetWatchedSeeds(config.RecentlyWatchedLimit);
        var candidates = new List<RecommendationItem>();

        if (config.EnableTraktRecommendations)
        {
            var trakt = await _traktApiClient.GetPersonalRecommendationsAsync(config, config.MaxRecommendations, cancellationToken).ConfigureAwait(false);
            foreach (var item in trakt)
            {
                item.Score += config.TraktWeight;
            }

            candidates.AddRange(trakt);
        }

        if (config.EnableSeerrRecommendations)
        {
            foreach (var seed in seeds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seerrItems = await _seerrApiClient.GetRecommendationsAsync(config, seed.MediaType, seed.TmdbId, config.RecommendationsPerSeed, cancellationToken).ConfigureAwait(false);
                candidates.AddRange(seerrItems.Select(item => new RecommendationItem
                {
                    TmdbId = item.Id,
                    MediaType = NormalizeMediaType(item.MediaType, seed.MediaType),
                    Title = item.Title ?? item.Name ?? "Untitled",
                    Overview = item.Overview ?? string.Empty,
                    PosterPath = item.PosterPath,
                    ReleaseDate = item.ReleaseDate,
                    FirstAirDate = item.FirstAirDate,
                    Score = config.SeerrWeight + Math.Max(0, seed.PlayCount),
                    SourceTitle = seed.Title
                }));
            }
        }

        var existing = GetExistingTmdbIds();
        var ranked = candidates
            .Where(item => item.TmdbId > 0 && !existing.Contains((item.MediaType, item.TmdbId)))
            .GroupBy(item => (item.MediaType, item.TmdbId))
            .Select(group =>
            {
                var best = group.OrderByDescending(item => item.Score).First();
                best.Score = group.Sum(item => item.Score);
                return best;
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .Take(Math.Max(1, config.MaxRecommendations))
            .ToList();

        await _writer.WriteAsync(config, ranked, cancellationToken).ConfigureAwait(false);
        return ranked;
    }

    private List<WatchedSeed> GetWatchedSeeds(int limit)
    {
        var sourcePaths = Plugin.Config.SourceLibraryPaths
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var seeds = new List<WatchedSeed>();
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in items)
        {
            if (sourcePaths.Length > 0 && !sourcePaths.Any(path => IsInPath(item.Path, path)))
            {
                continue;
            }

            var tmdb = GetTmdbId(item);
            if (!tmdb.HasValue)
            {
                continue;
            }

            var bestData = _userManager.Users
                .Select(user => _userDataManager.GetUserData(user, item))
                .Where(data => data is not null)
                .Cast<UserItemData>()
                .Where(data => data.Played || data.PlayCount > 0)
                .OrderByDescending(data => data.LastPlayedDate)
                .FirstOrDefault();

            if (bestData is null)
            {
                continue;
            }

            seeds.Add(new WatchedSeed
            {
                TmdbId = tmdb.Value,
                MediaType = item is Series ? "tv" : "movie",
                Title = item.Name,
                LastPlayedDate = bestData.LastPlayedDate,
                PlayCount = bestData.PlayCount
            });
        }

        return seeds
            .OrderByDescending(seed => seed.LastPlayedDate ?? DateTime.MinValue)
            .ThenByDescending(seed => seed.PlayCount)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private HashSet<(string MediaType, int TmdbId)> GetExistingTmdbIds()
    {
        var existing = new HashSet<(string MediaType, int TmdbId)>();
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in items)
        {
            var tmdb = GetTmdbId(item);
            if (tmdb.HasValue)
            {
                existing.Add((item is Series ? "tv" : "movie", tmdb.Value));
            }
        }

        return existing;
    }

    private static int? GetTmdbId(BaseItem item)
    {
        return item.ProviderIds.TryGetValue("Tmdb", out var value) && int.TryParse(value, out var id) ? id : null;
    }

    private static string NormalizeMediaType(string? mediaType, string fallback)
    {
        return string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" :
            string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase) ? "movie" :
            fallback;
    }

    private static bool IsInPath(string? itemPath, string libraryPath)
    {
        return !string.IsNullOrWhiteSpace(itemPath) &&
            Path.GetFullPath(itemPath).StartsWith(Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase);
    }
}
