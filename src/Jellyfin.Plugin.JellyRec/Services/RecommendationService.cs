using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        // Single-pass gather of watched seeds and existing TMDb IDs in the library
        var (seeds, existing) = GetWatchedSeedsAndExistingItems(config.RecentlyWatchedLimit);

        // Fetch already-requested items from Seerr to filter them out of recommendations
        if (HasSeerrCredentials(config))
        {
            var requestedTmdbIds = await _seerrApiClient.GetRequestedTmdbIdsAsync(config, cancellationToken).ConfigureAwait(false);
            foreach (var req in requestedTmdbIds)
            {
                existing.Add(req);
            }
            _logger.LogInformation("Excluded {Count} already-requested Seerr items from recommendations.", requestedTmdbIds.Count);
        }

        var candidates = new List<RecommendationItem>();

        // Trigger Trakt recommendations task (runs in parallel with Seerr requests)
        Task<List<RecommendationItem>>? traktTask = null;
        if (config.EnableTraktRecommendations && HasTraktCredentials(config))
        {
            traktTask = _traktApiClient.GetPersonalRecommendationsAsync(config, config.MaxRecommendations, cancellationToken);
        }

        // Trigger Seerr recommendations in parallel
        var seerrTasks = new List<(WatchedSeed Seed, Task<List<SeerrMediaResult>> Task)>();
        if (config.EnableSeerrRecommendations && HasSeerrCredentials(config))
        {
            foreach (var seed in seeds)
            {
                // Quality threshold: skip seeds that the user actively disliked (rated < 5.0 / 10, or 2.5 stars)
                if (seed.UserRating.HasValue && seed.UserRating.Value < 5.0)
                {
                    _logger.LogInformation("Skipping seed '{Title}' as recommendation source due to low user rating ({Rating})", seed.Title, seed.UserRating.Value);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var task = _seerrApiClient.GetRecommendationsAsync(config, seed.MediaType, seed.TmdbId, config.RecommendationsPerSeed, cancellationToken);
                seerrTasks.Add((seed, task));
            }
        }

        // Await Trakt recommendations if enabled
        if (traktTask is not null)
        {
            try
            {
                var trakt = await traktTask.ConfigureAwait(false);
                foreach (var item in trakt)
                {
                    item.Score += config.TraktWeight;
                    candidates.Add(item);
                }
                _logger.LogInformation("Fetched {Count} Trakt recommendations.", trakt.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Trakt recommendations.");
            }
        }

        // Await all parallel Seerr recommendation tasks
        if (seerrTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(seerrTasks.Select(x => x.Task)).ConfigureAwait(false);
                foreach (var item in seerrTasks)
                {
                    var seed = item.Seed;
                    var seerrItems = await item.Task.ConfigureAwait(false);

                    // Boost weight if the seed item is highly rated by the user
                    double ratingBoost = seed.UserRating.HasValue ? seed.UserRating.Value : Math.Max(0, seed.PlayCount);

                    foreach (var rec in seerrItems)
                    {
                        candidates.Add(new RecommendationItem
                        {
                            TmdbId = rec.Id,
                            MediaType = NormalizeMediaType(rec.MediaType, seed.MediaType),
                            Title = rec.Title ?? rec.Name ?? "Untitled",
                            Overview = rec.Overview ?? string.Empty,
                            PosterPath = rec.PosterPath,
                            ReleaseDate = rec.ReleaseDate,
                            FirstAirDate = rec.FirstAirDate,
                            Rating = rec.VoteAverage,
                            Score = config.SeerrWeight + ratingBoost,
                            SourceTitle = seed.Title
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Seerr recommendations.");
            }
        }

        // Process, filter, and rank the candidates
        var ranked = candidates
            .Where(item => item.TmdbId > 0 &&
                           !existing.Contains((item.MediaType, item.TmdbId)) &&
                           (item.Rating >= config.MinRating || item.Rating == 0.0)) // Filter out low-rated items (allow 0.0 for unrated/missing info)
            .GroupBy(item => (item.MediaType, item.TmdbId))
            .Select(group =>
            {
                var best = group.OrderByDescending(item => item.Score).First();
                // Aggregate score for items recommended by multiple seeds
                best.Score = group.Sum(item => item.Score);
                // Apply small extra score boost for higher quality TMDb/Trakt ratings (e.g. +0.8 for 8.0 rating)
                if (best.Rating > 0)
                {
                    best.Score += best.Rating * 0.1;
                }
                return best;
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .Take(Math.Max(1, config.MaxRecommendations))
            .ToList();

        await _writer.WriteAsync(config, ranked, cancellationToken).ConfigureAwait(false);
        return ranked;
    }

    private (List<WatchedSeed> Seeds, HashSet<(string MediaType, int TmdbId)> Existing) GetWatchedSeedsAndExistingItems(int limit)
    {
        var sourcePaths = Plugin.Config.SourceLibraryPaths
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var seeds = new List<WatchedSeed>();
        var existing = new HashSet<(string MediaType, int TmdbId)>();

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in items)
        {
            var tmdb = GetTmdbId(item);
            if (!tmdb.HasValue)
            {
                continue;
            }

            var mediaType = item is Series ? "tv" : "movie";
            existing.Add((mediaType, tmdb.Value));

            // Check if source paths filter applies to seeds selection
            if (sourcePaths.Length > 0 && !sourcePaths.Any(path => IsInPath(item.Path, path)))
            {
                continue;
            }

            // Find watch history and rating status across users
            var bestData = _userManager.Users
                .Select(user => _userDataManager.GetUserData(user, item))
                .Where(data => data is not null)
                .Cast<UserItemData>()
                .Where(data => data.Played || data.PlayCount > 0 || data.Rating.HasValue)
                .OrderByDescending(data => data.LastPlayedDate)
                .FirstOrDefault();

            if (bestData is null)
            {
                continue;
            }

            seeds.Add(new WatchedSeed
            {
                TmdbId = tmdb.Value,
                MediaType = mediaType,
                Title = item.Name,
                LastPlayedDate = bestData.LastPlayedDate,
                PlayCount = bestData.PlayCount,
                UserRating = bestData.Rating
            });
        }

        var sortedSeeds = seeds
            .OrderByDescending(seed => seed.LastPlayedDate ?? DateTime.MinValue)
            .ThenByDescending(seed => seed.UserRating ?? 0.0)
            .ThenByDescending(seed => seed.PlayCount)
            .Take(Math.Max(1, limit))
            .ToList();

        return (sortedSeeds, existing);
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

    private static bool HasTraktCredentials(PluginConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.TraktClientId) &&
            !string.IsNullOrWhiteSpace(config.TraktAccessToken);
    }

    private static bool HasSeerrCredentials(PluginConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.SeerrUrl) &&
            !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    private static bool IsInPath(string? itemPath, string libraryPath)
    {
        return !string.IsNullOrWhiteSpace(itemPath) &&
            Path.GetFullPath(itemPath).StartsWith(Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase);
    }
}
