using System.Net;
using System.Net.Http;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Registers the subs.ro subtitle provider and its HTTP client with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // The site's FAQ asks automated clients to wait a minute after a 429; the API's 429 means
        // the key's daily quota is spent, so that pause is long and the site serves meanwhile.
        var sitePacer = new RequestPacer(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
        var apiPacer = new RequestPacer(TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));

        serviceCollection.AddHttpClient<SubsRoClient>(client =>
        {
            client.BaseAddress = new Uri("https://subs.ro");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SubsRo/1.0");
        })
        .AddHttpMessageHandler(sp => new PacingHandler(sitePacer, sp.GetRequiredService<ILogger<PacingHandler>>()))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        });

        serviceCollection.AddHttpClient<SubsRoApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.subs.ro");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SubsRo/1.0");
        })
        .AddHttpMessageHandler(sp => new PacingHandler(apiPacer, sp.GetRequiredService<ILogger<PacingHandler>>()));

        serviceCollection.AddSingleton<ISubtitleProvider, SubsRoDownloader>();
    }
}
