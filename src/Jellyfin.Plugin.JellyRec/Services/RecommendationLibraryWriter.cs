using System.Security;
using System.Text.Json;
using Jellyfin.Plugin.JellyRec.Configuration;
using Jellyfin.Plugin.JellyRec.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class RecommendationLibraryWriter
{
    public const string MetadataFileName = "jellyrec.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly RecommendationFolderManager _folderManager;
    private readonly ILogger<RecommendationLibraryWriter> _logger;

    public RecommendationLibraryWriter(RecommendationFolderManager folderManager, ILogger<RecommendationLibraryWriter> logger)
    {
        _folderManager = folderManager;
        _logger = logger;
    }

    public async Task WriteAsync(PluginConfiguration config, IReadOnlyCollection<RecommendationItem> recommendations, CancellationToken cancellationToken)
    {
        var libraryRoot = await _folderManager.EnsureRecommendationPathAsync(config, cancellationToken).ConfigureAwait(false);

        foreach (var item in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = GetItemFolder(libraryRoot, item);
            Directory.CreateDirectory(folder);

            await WriteIfChangedAsync(
                Path.Combine(folder, MetadataFileName),
                JsonSerializer.Serialize(item, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            if (item.MediaType == "tv")
            {
                await WriteShowAsync(folder, item, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteMovieAsync(folder, item, cancellationToken).ConfigureAwait(false);
            }
        }

        RemoveStaleFolders(libraryRoot, recommendations);

        _logger.LogInformation("Wrote {Count} recommendation placeholders to {Path}", recommendations.Count, libraryRoot);
    }

    public IReadOnlyList<RecommendationItem> ReadAll(PluginConfiguration config)
    {
        var libraryRoot = _folderManager.ResolveRecommendationPath(config);
        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
        {
            return Array.Empty<RecommendationItem>();
        }

        var recommendations = new List<RecommendationItem>();
        foreach (var metadataPath in Directory.EnumerateFiles(libraryRoot, MetadataFileName, SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(metadataPath);
                var item = JsonSerializer.Deserialize<RecommendationItem>(json, JsonOptions);
                if (item is not null && item.TmdbId > 0 && !string.IsNullOrWhiteSpace(item.Title))
                {
                    recommendations.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read JellyRec recommendation metadata at {Path}", metadataPath);
            }
        }

        return recommendations
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .ToList();
    }

    public bool Remove(PluginConfiguration config, RecommendationItem item)
    {
        var libraryRoot = Path.GetFullPath(_folderManager.ResolveRecommendationPath(config));
        var metadataPath = Directory.EnumerateFiles(libraryRoot, MetadataFileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => MetadataMatches(path, item));
        if (metadataPath is null)
        {
            return false;
        }

        var folder = Path.GetFullPath(Path.GetDirectoryName(metadataPath)!);
        if (!folder.StartsWith(libraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder))
        {
            return false;
        }

        Directory.Delete(folder, true);
        return true;
    }

    public static RecommendationItem? TryReadMetadataForPath(string libraryRoot, string? itemPath)
    {
        if (string.IsNullOrWhiteSpace(itemPath) || string.IsNullOrWhiteSpace(libraryRoot))
        {
            return null;
        }

        // Fast string check first to avoid expensive path resolution or disk checks for regular library items
        if (!itemPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase) &&
            !itemPath.Contains("JellyRec", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = Path.GetFullPath(libraryRoot);
        var current = Directory.Exists(itemPath) ? itemPath : Path.GetDirectoryName(itemPath);
        while (!string.IsNullOrWhiteSpace(current) && Path.GetFullPath(current).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var metadataPath = Path.Combine(current, MetadataFileName);
            if (File.Exists(metadataPath))
            {
                var json = File.ReadAllText(metadataPath);
                return JsonSerializer.Deserialize<RecommendationItem>(json, JsonOptions);
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static async Task WriteMovieAsync(string folder, RecommendationItem item, CancellationToken cancellationToken)
    {
        var safeTitle = SafeFileName(item.Title);
        await WriteIfChangedAsync(Path.Combine(folder, $"{safeTitle}.strm"), "jellyrec://request", cancellationToken).ConfigureAwait(false);
        await WriteIfChangedAsync(Path.Combine(folder, "movie.nfo"), BuildMovieNfo(item), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteShowAsync(string folder, RecommendationItem item, CancellationToken cancellationToken)
    {
        // Jellyfin's TV home shelf only renders episodes.  The old placeholder therefore
        // appeared as the fake special S00E9999.  Recommendations are intentionally stored
        // as movie-shaped cards (while jellyrec.json retains MediaType=tv) so the shelf shows
        // the recommended series itself and never invents an episode.
        var legacySeasonFolder = Path.Combine(folder, "Season 00");
        if (Directory.Exists(legacySeasonFolder))
        {
            Directory.Delete(legacySeasonFolder, true);
        }

        var legacyNfo = Path.Combine(folder, "tvshow.nfo");
        if (File.Exists(legacyNfo))
        {
            File.Delete(legacyNfo);
        }

        await WriteMovieAsync(folder, item, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMovieNfo(RecommendationItem item)
    {
        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <movie>
          <title>{Escape(item.Title)}</title>
          <plot>{Escape(ActionHelp(item.Overview))}</plot>
          <year>{item.Year?.ToString() ?? string.Empty}</year>
          <tmdbid>{item.TmdbId}</tmdbid>
          <uniqueid type="tmdb" default="true">{item.TmdbId}</uniqueid>
          {PosterElement(item)}
        </movie>
        """;
    }

    private static string GetItemFolder(string root, RecommendationItem item)
    {
        var year = item.Year.HasValue ? $" ({item.Year})" : string.Empty;
        var mediaFolder = item.MediaType == "tv" ? RecommendationFolderManager.SeriesFolderName : RecommendationFolderManager.MoviesFolderName;
        return Path.Combine(root, mediaFolder, $"{SafeFileName(item.Title)}{year} [{item.MediaType}-{item.TmdbId}]");
    }

    private static string ActionHelp(string overview) =>
        "JellyRec: Dislike = Not interested • Mark played / Rate = Already seen • Favorite = Download\n\n" + overview;

    private static string PosterElement(RecommendationItem item) => string.IsNullOrWhiteSpace(item.PosterPath)
        ? string.Empty
        : $"<thumb aspect=\"poster\">https://image.tmdb.org/t/p/w500{Escape(item.PosterPath)}</thumb>";

    private static bool MetadataMatches(string path, RecommendationItem expected)
    {
        try
        {
            var item = JsonSerializer.Deserialize<RecommendationItem>(File.ReadAllText(path), JsonOptions);
            return item?.TmdbId == expected.TmdbId && item.MediaType == expected.MediaType;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveStaleFolders(string root, IReadOnlyCollection<RecommendationItem> current)
    {
        var active = current.Select(item => (item.MediaType, item.TmdbId)).ToHashSet();
        foreach (var metadataPath in Directory.EnumerateFiles(root, MetadataFileName, SearchOption.AllDirectories).ToList())
        {
            try
            {
                var item = JsonSerializer.Deserialize<RecommendationItem>(File.ReadAllText(metadataPath), JsonOptions);
                var itemFolder = Path.GetDirectoryName(metadataPath)!;
                var isLegacyRootItem = string.Equals(Path.GetDirectoryName(itemFolder), root, StringComparison.OrdinalIgnoreCase);
                if (item is not null && (isLegacyRootItem || !active.Contains((item.MediaType, item.TmdbId))))
                {
                    Directory.Delete(itemFolder, true);
                }
            }
            catch
            {
                // Leave unreadable folders in place; ReadAll logs the actionable diagnostic.
            }
        }
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    private static async Task WriteIfChangedAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && string.Equals(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), content, StringComparison.Ordinal))
        {
            return;
        }

        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }
}
