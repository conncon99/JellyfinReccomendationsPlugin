using Jellyfin.Plugin.JellyRec.Services;
using Jellyfin.Plugin.JellyRec.Tasks;
using Jellyfin.Plugin.JellyRec.Controllers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyRec.Services;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<SeerrApiClient>();
        serviceCollection.AddHttpClient<TraktApiClient>();
        serviceCollection.AddTransient<RecommendationFolderManager>();
        serviceCollection.AddTransient<RecommendationService>();
        serviceCollection.AddTransient<RecommendationLibraryWriter>();
        serviceCollection.AddTransient<RefreshRecommendationsTask>();
        serviceCollection.AddScoped<JellyRecController>();
        serviceCollection.AddHostedService<FavoriteRequestHostedService>();
    }
}
