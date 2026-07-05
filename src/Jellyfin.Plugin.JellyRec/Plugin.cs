using Jellyfin.Plugin.JellyRec.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyRec;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("1d1af7ee-7c42-42a0-9f03-d202274e68d5");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin Instance { get; private set; } = null!;

    public override Guid Id => PluginId;

    public override string Name => "Jellyfin Recommendations";

    public static PluginConfiguration Config => Instance.Configuration ?? new PluginConfiguration();

    public static T GetConfigOrDefault<T>(Func<PluginConfiguration, T?> selector, T fallback)
        where T : notnull
    {
        var value = selector(Config);
        return value is null || value is string text && string.IsNullOrWhiteSpace(text) ? fallback : value;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.ConfigurationPage.html"
            }
        };
    }
}
