using System.Net;
using System.Net.Http;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Registers the subs.ro subtitle provider and its HTTP client with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<SubsRoClient>(client =>
        {
            client.BaseAddress = new Uri("https://subs.ro");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SubsRo/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        });

        serviceCollection.AddSingleton<ISubtitleProvider, SubsRoDownloader>();
    }
}
