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

            await File.WriteAllTextAsync(
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
        var folder = Path.GetFullPath(GetItemFolder(libraryRoot, item));
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
        await File.WriteAllTextAsync(Path.Combine(folder, $"{safeTitle}.strm"), "jellyrec://request", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "movie.nfo"), BuildMovieNfo(item), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteShowAsync(string folder, RecommendationItem item, CancellationToken cancellationToken)
    {
        var seasonFolder = Path.Combine(folder, "Season 00");
        Directory.CreateDirectory(seasonFolder);
        await File.WriteAllTextAsync(Path.Combine(seasonFolder, "S00E9999.strm"), "jellyrec://request", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "tvshow.nfo"), BuildShowNfo(item), cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMovieNfo(RecommendationItem item)
    {
        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <movie>
          <title>{Escape(item.Title)}</title>
          <plot>{Escape(item.Overview)}</plot>
          <year>{item.Year?.ToString() ?? string.Empty}</year>
          <tmdbid>{item.TmdbId}</tmdbid>
          <uniqueid type="tmdb" default="true">{item.TmdbId}</uniqueid>
        </movie>
        """;
    }

    private static string BuildShowNfo(RecommendationItem item)
    {
        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <tvshow>
          <title>{Escape(item.Title)}</title>
          <plot>{Escape(item.Overview)}</plot>
          <year>{item.Year?.ToString() ?? string.Empty}</year>
          <tmdbid>{item.TmdbId}</tmdbid>
          <uniqueid type="tmdb" default="true">{item.TmdbId}</uniqueid>
        </tvshow>
        """;
    }

    private static string GetItemFolder(string root, RecommendationItem item)
    {
        var year = item.Year.HasValue ? $" ({item.Year})" : string.Empty;
        return Path.Combine(root, $"{SafeFileName(item.Title)}{year} [{item.MediaType}-{item.TmdbId}]");
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
}
