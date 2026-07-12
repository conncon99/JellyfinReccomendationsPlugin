using Jellyfin.Plugin.JellyRec.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class RecommendationHomeLibraryManager
{
    private const string LegacyLibraryName = "Recommended For You";
    private const string MovieLibraryName = "Recommended Movies";
    private const string SeriesLibraryName = "Recommended Series";
    private readonly ILibraryManager _libraryManager;
    private readonly RecommendationFolderManager _folderManager;
    private readonly ILogger<RecommendationHomeLibraryManager> _logger;

    public RecommendationHomeLibraryManager(
        ILibraryManager libraryManager,
        RecommendationFolderManager folderManager,
        ILogger<RecommendationHomeLibraryManager> logger)
    {
        _libraryManager = libraryManager;
        _folderManager = folderManager;
        _logger = logger;
    }

    public async Task EnsureHomeLibraryAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        await _folderManager.EnsureRecommendationPathAsync(config, cancellationToken).ConfigureAwait(false);
        await EnsureLibraryAsync(MovieLibraryName, _folderManager.GetMediaPath(config, "movie"), CollectionTypeOptions.movies).ConfigureAwait(false);
        // A TV library's home carousel is episode-only. Use movie-shaped cards for series
        // recommendations so Jellyfin displays titles, not fabricated specials.
        await EnsureLibraryAsync(SeriesLibraryName, _folderManager.GetMediaPath(config, "tv"), CollectionTypeOptions.movies).ConfigureAwait(false);

        // v0.1.31 and earlier used one mixed library. Remove it after the two focused
        // libraries exist so upgrades do not leave a duplicate home shelf behind.
        if (_libraryManager.GetVirtualFolders().Any(folder => string.Equals(folder.Name, LegacyLibraryName, StringComparison.OrdinalIgnoreCase)))
        {
            await _libraryManager.RemoveVirtualFolder(LegacyLibraryName, false).ConfigureAwait(false);
            _logger.LogInformation("Removed legacy JellyRec library {LibraryName}", LegacyLibraryName);
        }

        _libraryManager.QueueLibraryScan();
    }

    private async Task EnsureLibraryAsync(string libraryName, string path, CollectionTypeOptions collectionType)
    {
        var normalizedPath = Path.GetFullPath(path);
        var existing = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(folder => string.Equals(folder.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.CollectionType != collectionType)
        {
            await _libraryManager.RemoveVirtualFolder(libraryName, false).ConfigureAwait(false);
            existing = null;
            _logger.LogInformation("Recreated JellyRec library {LibraryName} with card-oriented type {CollectionType}", libraryName, collectionType);
        }

        if (existing is null)
        {
            var options = new LibraryOptions { PathInfos = new[] { new MediaPathInfo(normalizedPath) } };
            await _libraryManager.AddVirtualFolder(libraryName, collectionType, options, false).ConfigureAwait(false);
            _logger.LogInformation("Created JellyRec home library {LibraryName} at {Path}", libraryName, normalizedPath);
            return;
        }

        if (!existing.Locations.Any(location => SamePath(location, normalizedPath)))
        {
            _libraryManager.AddMediaPath(libraryName, new MediaPathInfo(normalizedPath));
            _logger.LogInformation("Added JellyRec path {Path} to home library {LibraryName}", normalizedPath, libraryName);
        }
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
