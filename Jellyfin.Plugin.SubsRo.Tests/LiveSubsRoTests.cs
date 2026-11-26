using System.IO;
using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.SubsRo;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

[Trait("Category", "Live")]
public class LiveSubsRoTests
{
    private static SubsRoApiClient BuildRealApiClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.subs.ro") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Plugin-SubsRo/1.0");
        return new SubsRoApiClient(http, NullLogger<SubsRoApiClient>.Instance);
    }

    private static SubsRoDownloader BuildRealDownloader(string? apiKey = null)
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
        return new TestableSubsRoDownloader(NullLogger<SubsRoDownloader>.Instance, client, BuildRealApiClient(), apiKey);
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

    [Fact]
    public async Task SearchAndDownload_RealSite_Episode_ReturnsValidSrtForRequestedEpisode()
    {
        var downloader = BuildRealDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Breaking Bad",
            Name = "Blood Money",
            ParentIndexNumber = 5,
            IndexNumber = 9,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            MediaPath = "/tv/Breaking.Bad.S05E09.720p.BluRay.x264-DEMAND.mkv",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();
        Assert.NotEmpty(results);
        Assert.Contains("|ep", results[0].Id);

        var response = await downloader.GetSubtitles(results[0].Id, CancellationToken.None);
        using var reader = new StreamReader(response.Stream);
        var text = reader.ReadToEnd();

        Assert.Equal("srt", response.Format);
        Assert.Contains("-->", text);
    }

    [Fact]
    public async Task Search_RealSite_ByImdbId_ReturnsResults()
    {
        var downloader = BuildRealDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "Vacation Friends",
            ProductionYear = 2021,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            ProviderIds = { [MetadataProvider.Imdb.ToString()] = "tt3626476" },
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.NotEmpty(results);
    }

    /// <summary>
    /// Exercises the real subs.ro API (search + download) end to end. Requires a real API
    /// key in the <c>SUBSRO_API_KEY</c> environment variable; skipped (auto-passes) when
    /// unset, since this key is a per-user secret never committed to the repo.
    /// </summary>
    [Fact]
    public async Task SearchAndDownload_RealApi_ByImdbId_ReturnsValidSrt()
    {
        var apiKey = Environment.GetEnvironmentVariable("SUBSRO_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            return;
        }

        var downloader = BuildRealDownloader(apiKey);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "Vacation Friends",
            ProductionYear = 2021,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            ProviderIds = { [MetadataProvider.Imdb.ToString()] = "tt3626476" },
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();
        Assert.NotEmpty(results);
        Assert.StartsWith("api|", results[0].Id, StringComparison.Ordinal);

        var response = await downloader.GetSubtitles(results[0].Id, CancellationToken.None);
        using var reader = new StreamReader(response.Stream);
        var text = reader.ReadToEnd();

        Assert.Equal("srt", response.Format);
        Assert.Contains("-->", text);
    }
}
