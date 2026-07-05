using Jellyfin.Plugin.JellyRec.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class RecommendationHomeLibraryManager
{
    private const string LibraryName = "Recommended For You";
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
        var path = await _folderManager.EnsureRecommendationPathAsync(config, cancellationToken).ConfigureAwait(false);
        var normalizedPath = Path.GetFullPath(path);
        var existing = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(folder => string.Equals(folder.Name, LibraryName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (!existing.Locations.Any(location => SamePath(location, normalizedPath)))
            {
                _libraryManager.AddMediaPath(LibraryName, new MediaPathInfo(normalizedPath));
                _logger.LogInformation("Added JellyRec recommendation path {Path} to home library {LibraryName}", normalizedPath, LibraryName);
            }

            return;
        }

        var options = new LibraryOptions
        {
            PathInfos = new[] { new MediaPathInfo(normalizedPath) }
        };

        await _libraryManager.AddVirtualFolder(LibraryName, CollectionTypeOptions.mixed, options, true).ConfigureAwait(false);
        _logger.LogInformation("Created JellyRec home library {LibraryName} at {Path}", LibraryName, normalizedPath);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
