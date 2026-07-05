using Jellyfin.Plugin.JellyRec.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class RecommendationFolderManager
{
    private const string DefaultFolderName = "JellyRec";
    private const string DefaultRecommendationsFolderName = "Recommendations";
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<RecommendationFolderManager> _logger;

    public RecommendationFolderManager(IApplicationPaths applicationPaths, ILogger<RecommendationFolderManager> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public string ResolveRecommendationPath(PluginConfiguration config)
    {
        var configuredPath = Environment.ExpandEnvironmentVariables(config.RecommendationLibraryPath ?? string.Empty).Trim();
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_applicationPaths.DataPath, DefaultFolderName, DefaultRecommendationsFolderName)
            : configuredPath;

        return Path.GetFullPath(path);
    }

    public async Task<string> EnsureRecommendationPathAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        var path = ResolveRecommendationPath(config);
        Directory.CreateDirectory(path);

        var probePath = Path.Combine(path, ".jellyrec-write-test");
        await File.WriteAllTextAsync(probePath, "ok", cancellationToken).ConfigureAwait(false);
        File.Delete(probePath);

        _logger.LogInformation("JellyRec recommendation folder is ready at {Path}", path);
        return path;
    }
}
