using System.IO;
using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.SubsRo;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

[Trait("Category", "Live")]
public class LiveSubsRoTests
{
    private static SubsRoDownloader BuildRealDownloader()
    {
        var handler = new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SubsRo/1.0");
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);
        return new SubsRoDownloader(NullLogger<SubsRoDownloader>.Instance, client);
    }

    [Fact]
    public async Task SearchAndDownload_RealSite_ReturnsValidSrt()
    {
        var downloader = BuildRealDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();
        Assert.NotEmpty(results);

        var response = await downloader.GetSubtitles(results[0].Id, CancellationToken.None);
        using var reader = new StreamReader(response.Stream);
        var text = reader.ReadToEnd();

        Assert.Equal("srt", response.Format);
        Assert.Contains("-->", text);
    }
}
