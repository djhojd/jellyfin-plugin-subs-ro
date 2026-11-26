using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubsRo.Configuration;

/// <summary>
/// Plugin configuration. The provider works anonymously by default; setting <see cref="ApiKey"/>
/// switches it to subs.ro's official API (<c>https://api.subs.ro</c>), falling back to the
/// anonymous flow if the API call fails or returns nothing.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the subs.ro API key (from a user's subs.ro account), if any. Optional —
    /// the plugin works anonymously without one.
    /// </summary>
    public string? ApiKey { get; set; }
}
