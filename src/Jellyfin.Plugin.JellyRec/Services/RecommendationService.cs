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
    private readonly RecommendationFolderManager _folderManager;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        SeerrApiClient seerrApiClient,
        TraktApiClient traktApiClient,
        RecommendationLibraryWriter writer,
        RecommendationFolderManager folderManager,
        ILogger<RecommendationService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _seerrApiClient = seerrApiClient;
        _traktApiClient = traktApiClient;
        _writer = writer;
        _folderManager = folderManager;
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

        var firstSeen = ApplyRecommendationLifecycle(config);

        // Single-pass gather of watched seeds and existing TMDb IDs in the library
        var (seeds, existing, history, discovered, skipped) = GetWatchedSeedsAndExistingItems(config.RecentlyWatchedLimit);

        if (config.SyncJellyfinHistoryToTrakt && HasTraktCredentials(config))
        {
            try
            {
                await SyncTraktHistoryAsync(config, history, discovered, skipped, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Jellyfin watch history to Trakt; the sync checkpoint was not advanced.");
            }
        }

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

                    // Calculate Time Decay (Recency Bias) factor
                    double decayFactor = 1.0;
                    if (seed.LastPlayedDate.HasValue)
                    {
                        var ageInDays = (DateTime.UtcNow - seed.LastPlayedDate.Value.ToUniversalTime()).TotalDays;
                        if (ageInDays > 0)
                        {
                            // Smoothly decays to 0.5 around 33 days, and 0.25 at 100 days
                            decayFactor = 1.0 / (1.0 + 0.03 * ageInDays);
                        }
                    }
                    else
                    {
                        // Baseline factor for rated/favorited items that haven't been played
                        decayFactor = 0.8;
                    }

                    // Boost weight if the seed item is highly rated or favorited by the user
                    double ratingBoost = seed.UserRating.HasValue ? seed.UserRating.Value : Math.Max(0, seed.PlayCount);
                    double finalBoost = ratingBoost * decayFactor;

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
                            Score = config.SeerrWeight + finalBoost,
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
        var dismissed = ParseDismissedItems(config.DismissedItems);
        var coolingDown = ParseCooldowns(config.ExpiredRecommendationCooldowns, DateTime.UtcNow).Keys.ToHashSet();
        var scored = candidates
            .Where(item => item.TmdbId > 0 &&
                           !existing.Contains((item.MediaType, item.TmdbId)) &&
                           !dismissed.Contains((item.MediaType, item.TmdbId)) &&
                           !coolingDown.Contains((item.MediaType, item.TmdbId)) &&
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
                best.FirstRecommendedAtUtc = firstSeen.TryGetValue((best.MediaType, best.TmdbId), out var seenAt)
                    ? seenAt
                    : DateTime.UtcNow;
                return best;
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .ToList();

        var ranked = Diversify(scored, Math.Max(1, config.MaxRecommendations), Math.Max(0, config.DiversityStrength));

        await _writer.WriteAsync(config, ranked, cancellationToken).ConfigureAwait(false);
        return ranked;
    }

    private Dictionary<(string MediaType, int TmdbId), DateTime> ApplyRecommendationLifecycle(PluginConfiguration config)
    {
        var now = DateTime.UtcNow;
        var retention = TimeSpan.FromDays(Math.Max(1, config.RecommendationRetentionDays));
        var current = _writer.ReadAll(config);
        var firstSeen = new Dictionary<(string MediaType, int TmdbId), DateTime>();
        var cooldowns = ParseCooldowns(config.ExpiredRecommendationCooldowns, now);
        var changed = cooldowns.Count != ParseCooldowns(config.ExpiredRecommendationCooldowns, DateTime.MinValue).Count;

        foreach (var item in current)
        {
            var key = (item.MediaType, item.TmdbId);
            var seenAt = item.FirstRecommendedAtUtc?.ToUniversalTime() ?? now;
            if (now - seenAt >= retention)
            {
                _writer.Remove(config, item);
                cooldowns[key] = now.Add(retention);
                changed = true;
                _logger.LogInformation("Expired recommendation '{Title}' after {Days} days.", item.Title, Math.Max(1, config.RecommendationRetentionDays));
            }
            else
            {
                firstSeen[key] = seenAt;
            }
        }

        if (changed)
        {
            config.ExpiredRecommendationCooldowns = SerializeCooldowns(cooldowns);
            Plugin.Instance.UpdateConfiguration(config);
        }

        return firstSeen;
    }

    private static Dictionary<(string MediaType, int TmdbId), DateTime> ParseCooldowns(string value, DateTime now)
    {
        var cooldowns = new Dictionary<(string MediaType, int TmdbId), DateTime>();
        foreach (var entry in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('|', 2);
            var keyParts = parts[0].Split(':', 2);
            if (parts.Length == 2 && keyParts.Length == 2 && int.TryParse(keyParts[1], out var tmdbId) &&
                DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var until) && until.ToUniversalTime() > now)
            {
                cooldowns[(NormalizeMediaType(keyParts[0], keyParts[0]), tmdbId)] = until.ToUniversalTime();
            }
        }

        return cooldowns;
    }

    private static string SerializeCooldowns(Dictionary<(string MediaType, int TmdbId), DateTime> cooldowns)
    {
        return string.Join(';', cooldowns
            .OrderBy(entry => entry.Key.MediaType)
            .ThenBy(entry => entry.Key.TmdbId)
            .Select(entry => $"{entry.Key.MediaType}:{entry.Key.TmdbId}|{entry.Value.ToUniversalTime():O}"));
    }

    private static List<RecommendationItem> Diversify(
        IReadOnlyCollection<RecommendationItem> candidates,
        int limit,
        double strength)
    {
        var remaining = candidates.ToList();
        var selected = new List<RecommendationItem>();
        while (remaining.Count > 0 && selected.Count < limit)
        {
            var next = remaining
                .Select(item => new
                {
                    Item = item,
                    AdjustedScore = item.Score - strength * (
                        (!string.Equals(item.SourceTitle, "Trakt", StringComparison.OrdinalIgnoreCase)
                            ? selected.Count(chosen => string.Equals(chosen.SourceTitle, item.SourceTitle, StringComparison.OrdinalIgnoreCase)) * 1.5
                            : 0) +
                        selected.Count(chosen => chosen.MediaType == item.MediaType) * 0.08 +
                        selected.Count(chosen => chosen.Year.HasValue && item.Year.HasValue && chosen.Year.Value / 10 == item.Year.Value / 10) * 0.15)
                })
                .OrderByDescending(candidate => candidate.AdjustedScore)
                .ThenByDescending(candidate => candidate.Item.Score)
                .ThenBy(candidate => candidate.Item.Title)
                .First().Item;

            selected.Add(next);
            remaining.Remove(next);
        }

        return selected;
    }

    private static HashSet<(string MediaType, int TmdbId)> ParseDismissedItems(string value)
    {
        var dismissed = new HashSet<(string MediaType, int TmdbId)>();
        foreach (var entry in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out var tmdbId))
            {
                dismissed.Add((NormalizeMediaType(parts[0], parts[0]), tmdbId));
            }
        }

        return dismissed;
    }

    public async Task<TraktHistorySyncResult> SyncAllTraktHistoryAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Config;
        if (!HasTraktCredentials(config))
        {
            throw new InvalidOperationException("Connect Trakt before syncing watch history.");
        }

        var (_, _, history, discovered, skipped) = GetWatchedSeedsAndExistingItems(config.RecentlyWatchedLimit);
        return await SyncTraktHistoryAsync(config, history, discovered, skipped, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TraktHistorySyncResult> SyncTraktHistoryAsync(
        PluginConfiguration config,
        IReadOnlyCollection<TraktHistoryItem> history,
        int discovered,
        int skipped,
        bool fullSync,
        CancellationToken cancellationToken)
    {
        var pending = history
            .Where(item => fullSync || !config.TraktHistoryLastSyncedAtUtc.HasValue ||
                item.WatchedAtUtc > config.TraktHistoryLastSyncedAtUtc.Value.ToUniversalTime())
            .OrderBy(item => item.WatchedAtUtc)
            .ToList();

        if (pending.Count == 0)
        {
            return new TraktHistorySyncResult
            {
                Discovered = discovered,
                Skipped = skipped,
                CheckpointUtc = config.TraktHistoryLastSyncedAtUtc
            };
        }

        var accepted = await _traktApiClient.AddToHistoryAsync(config, pending, cancellationToken).ConfigureAwait(false);
        config.TraktHistoryLastSyncedAtUtc = pending[^1].WatchedAtUtc;
        Plugin.Instance.UpdateConfiguration(config);
        var result = new TraktHistorySyncResult
        {
            Discovered = discovered,
            Submitted = pending.Count,
            Accepted = accepted,
            Skipped = skipped,
            CheckpointUtc = config.TraktHistoryLastSyncedAtUtc
        };
        _logger.LogInformation(
            "Trakt history sync discovered {Discovered}, submitted {Submitted}, accepted {Accepted}, and skipped {Skipped} items.",
            result.Discovered,
            result.Submitted,
            result.Accepted,
            result.Skipped);
        return result;
    }

    private (List<WatchedSeed> Seeds, HashSet<(string MediaType, int TmdbId)> Existing, List<TraktHistoryItem> History, int Discovered, int Skipped) GetWatchedSeedsAndExistingItems(int limit)
    {
        var config = Plugin.Config;
        var recFolder = _folderManager.ResolveRecommendationPath(config);

        var sourcePaths = config.SourceLibraryPaths
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var seeds = new List<WatchedSeed>();
        var existing = new HashSet<(string MediaType, int TmdbId)>();
        var history = new List<TraktHistoryItem>();
        var discovered = 0;
        var skipped = 0;

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
            Recursive = true
        });

        foreach (var item in items)
        {
            // Skip placeholders in the recommendations folder itself
            if (IsInRecommendationsFolder(item.Path, recFolder))
            {
                continue;
            }

            if (item is Episode episode)
            {
                if (sourcePaths.Length > 0 && !sourcePaths.Any(path => IsInPath(episode.Path, path)))
                {
                    continue;
                }

                var episodeData = GetMostRecentUserData(episode);
                if (episodeData is null || (!episodeData.Played && episodeData.PlayCount <= 0) || !episodeData.LastPlayedDate.HasValue)
                {
                    continue;
                }

                discovered++;
                var episodeTmdb = GetProviderId(episode, "Tmdb");
                var episodeTvdb = GetProviderId(episode, "Tvdb");
                var series = _libraryManager.GetItemById(episode.SeriesId) as Series;
                var seriesTmdb = series is null ? null : GetProviderId(series, "Tmdb");
                if ((!episodeTmdb.HasValue && !episodeTvdb.HasValue && !seriesTmdb.HasValue) ||
                    !episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
                {
                    skipped++;
                    continue;
                }

                history.Add(new TraktHistoryItem
                {
                    TmdbId = episodeTmdb,
                    TvdbId = episodeTvdb,
                    SeriesTmdbId = seriesTmdb,
                    MediaType = "episode",
                    WatchedAtUtc = episodeData.LastPlayedDate.Value.ToUniversalTime(),
                    SeasonNumber = episode.ParentIndexNumber.Value,
                    EpisodeNumber = episode.IndexNumber.Value
                });

                continue;
            }

            var tmdb = GetTmdbId(item);
            if (!tmdb.HasValue)
            {
                if (item is Movie)
                {
                    var movieData = GetMostRecentUserData(item);
                    if (movieData is not null && (movieData.Played || movieData.PlayCount > 0) && movieData.LastPlayedDate.HasValue)
                    {
                        discovered++;
                        skipped++;
                    }
                }

                continue;
            }

            var mediaType = item is Series ? "tv" : "movie";
            existing.Add((mediaType, tmdb.Value));

            // Check if source paths filter applies to seeds selection
            if (sourcePaths.Length > 0 && !sourcePaths.Any(path => IsInPath(item.Path, path)))
            {
                continue;
            }

            // Find watch history, rating status, and favorite status across users
            var bestData = GetMostRecentUserData(item);

            if (bestData is null)
            {
                continue;
            }

            if (item is Movie && (bestData.Played || bestData.PlayCount > 0) && bestData.LastPlayedDate.HasValue)
            {
                discovered++;
                history.Add(new TraktHistoryItem
                {
                    TmdbId = tmdb.Value,
                    MediaType = mediaType,
                    WatchedAtUtc = bestData.LastPlayedDate.Value.ToUniversalTime()
                });
            }

            // Extract rating and apply a boost if the item was favorited
            double? userRating = bestData.Rating;
            if (bestData.IsFavorite)
            {
                // Unrated favorites start at 8.0 + 2.0 = 10.0. Rated favorites get a +2.0 rating boost capped at 10.0.
                userRating = Math.Min(10.0, (userRating ?? 8.0) + 2.0);
            }

            seeds.Add(new WatchedSeed
            {
                TmdbId = tmdb.Value,
                MediaType = mediaType,
                Title = item.Name,
                LastPlayedDate = bestData.LastPlayedDate,
                PlayCount = bestData.PlayCount,
                UserRating = userRating
            });
        }

        var sortedSeeds = seeds
            .OrderByDescending(seed => seed.LastPlayedDate ?? DateTime.MinValue)
            .ThenByDescending(seed => seed.UserRating ?? 0.0)
            .ThenByDescending(seed => seed.PlayCount)
            .Take(Math.Max(1, limit))
            .ToList();

        return (sortedSeeds, existing, history, discovered, skipped);
    }

    private UserItemData? GetMostRecentUserData(BaseItem item)
    {
        return _userManager.Users
            .Select(user => _userDataManager.GetUserData(user, item))
            .Where(data => data is not null)
            .Cast<UserItemData>()
            .Where(data => data.Played || data.PlayCount > 0 || data.Rating.HasValue || data.IsFavorite)
            .OrderByDescending(data => data.LastPlayedDate)
            .FirstOrDefault();
    }

    private static bool IsInRecommendationsFolder(string? path, string recFolder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(recFolder))
        {
            return false;
        }
        try
        {
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(recFolder), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int? GetTmdbId(BaseItem item)
    {
        return GetProviderId(item, "Tmdb");
    }

    private static int? GetProviderId(BaseItem item, string provider)
    {
        return item.ProviderIds.TryGetValue(provider, out var value) && int.TryParse(value, out var id) ? id : null;
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
