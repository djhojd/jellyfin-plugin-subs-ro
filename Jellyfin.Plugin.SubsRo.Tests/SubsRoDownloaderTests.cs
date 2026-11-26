using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.SubsRo;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SubsRoDownloaderTests
{
    private const string CautarePage =
        "<input type=\"hidden\" name=\"antispam\" value=\"t\">";
    private const string RomanianFlag =
        "<img src=\"https://cdn.subs.ro/img/flags/flag-rom-big.png\" alt=\"Subtitrare First Knight - ro\" />";

    private static readonly string ResultsHtml =
        "<div data-subtitle-result=\"true\"><a data-search-result-link=\"true\" " +
        "href=\"https://subs.ro/subtitrare/first-knight-1995/129412\" " +
        $"data-subtitle-id=\"129412\" data-movie-name=\"First Knight\">First Knight{RomanianFlag}</a></div>";

    private static string MultiReleaseResultsHtml(string highDownloadNote, string lowDownloadNote) => $$"""
        <div data-subtitle-result="true">
          <a data-search-result-link="true" href="https://subs.ro/subtitrare/first-knight-1995/129412"
             data-subtitle-id="129412" data-movie-name="First Knight">First Knight{{RomanianFlag}}</a>
          <div class="flex items-center">
            <img src="https://cdn.subs.ro/img/icons/download.svg" class="opacity-60" />
            <span>900</span>
          </div>
          <div class="text-sm text-gray-700 leading-relaxed overflow-auto">{{highDownloadNote}}</div>
        </div>
        <div data-subtitle-result="true">
          <a data-search-result-link="true" href="https://subs.ro/subtitrare/first-knight-1995-alt/129500"
             data-subtitle-id="129500" data-movie-name="First Knight">First Knight{{RomanianFlag}}</a>
          <div class="flex items-center">
            <img src="https://cdn.subs.ro/img/icons/download.svg" class="opacity-60" />
            <span>10</span>
          </div>
          <div class="text-sm text-gray-700 leading-relaxed overflow-auto">{{lowDownloadNote}}</div>
        </div>
        """;

    private static byte[] BuildZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("First Knight.srt");
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nBună ziua\n");
            s.Write(bytes, 0, bytes.Length);
        }

        return ms.ToArray();
    }

    private static byte[] BuildZipWithMultipleReleases()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "First.Knight.1995.BluRay.x264-OtherGroup.srt",
                "1\n00:00:01,000 --> 00:00:02,000\n" + new string('x', 500) + "\n");
            WriteEntry(zip, "First.Knight.1995.WEB-DL.HEVC-TEPES.srt",
                "1\n00:00:01,000 --> 00:00:02,000\nmatching release\n");
        }

        return ms.ToArray();
    }

    private static byte[] BuildZipWithEpisodes()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "Show.S01E01.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode one\n");
            WriteEntry(zip, "Show.S01E02.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode two\n");
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// An <see cref="SubsRoApiClient"/> that throws if ever called — for tests exercising
    /// only the anonymous scrape path, so an accidental API-path call fails loudly.
    /// </summary>
    private static SubsRoApiClient BuildUnusedApiClient()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("SubsRoApiClient should not be called in this test."));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.subs.ro") };
        return new SubsRoApiClient(http, NullLogger<SubsRoApiClient>.Instance);
    }

    /// <summary>
    /// A <see cref="SubsRoClient"/> that throws if ever called — for tests exercising only
    /// the API path, so an accidental scrape-path call fails loudly.
    /// </summary>
    private static SubsRoClient BuildUnusedScrapeClient()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("SubsRoClient should not be called in this test."));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        return new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);
    }

    private static SubsRoApiClient BuildApiClient(
        out StubHttpMessageHandler handler, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        handler = new StubHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.subs.ro") };
        return new SubsRoApiClient(http, NullLogger<SubsRoApiClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly string EmptySearchJson =
        """{"status":200,"meta":{"requestId":"r"},"count":0,"items":[]}""";

    private static SubsRoDownloader BuildDownloader(byte[]? zipOverride = null, string? resultsHtmlOverride = null)
    {
        var zip = zipOverride ?? BuildZip();
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("descarca"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            }

            var body = path.Contains("cautare") ? CautarePage : resultsHtmlOverride ?? ResultsHtml;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);
        return new SubsRoDownloader(NullLogger<SubsRoDownloader>.Instance, client, BuildUnusedApiClient());
    }

    /// <summary>
    /// Builds a downloader whose <c>/ajax/search</c> stub returns different HTML depending
    /// on whether the posted body carries an <c>external_id</c> field, so id-based and
    /// title-based search attempts can be told apart.
    /// </summary>
    private static (SubsRoDownloader Downloader, StubHttpMessageHandler Handler) BuildDownloaderWithSeparateResponses(
        string externalIdResultsHtml, string titleResultsHtml)
    {
        StubHttpMessageHandler handler = null!;
        handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("cautare"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CautarePage, Encoding.UTF8, "text/html"),
                };
            }

            var body = handler.RequestBodies[^1];
            var responseBody = body.Contains("external_id=", StringComparison.Ordinal)
                ? externalIdResultsHtml
                : titleResultsHtml;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/html"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);
        return (new SubsRoDownloader(NullLogger<SubsRoDownloader>.Instance, client, BuildUnusedApiClient()), handler);
    }

    [Fact]
    public async Task Search_ReturnsRomanianResults_ForMovie()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Equal("129412|first-knight-1995", results[0].Id);
        Assert.Equal("ron", results[0].ThreeLetterISOLanguageName);
        Assert.Equal("srt", results[0].Format);
    }

    [Fact]
    public async Task Search_Episode_NoSeriesName_ReturnsEmpty()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            Name = "Pilot",
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        Assert.Empty(await downloader.Search(request, CancellationToken.None));
    }

    [Fact]
    public async Task Search_Episode_SearchesBySeriesNameNotEpisodeName()
    {
        var (downloader, handler) = BuildDownloaderWithSeparateResponses(
            externalIdResultsHtml: "<div data-search-results-state=\"error\"></div>",
            titleResultsHtml: ResultsHtml);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "First Knight",
            Name = "The Pilot Episode",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            ProductionYear = 2012,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Contains("titlu-film=First+Knight", handler.RequestBodies[^1]);

        // An episode's own ProductionYear is its air year, not the show's premiere year
        // subs.ro tags season archives with, so it must not be sent as the "an" filter.
        Assert.DoesNotContain("an=", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task Search_Episode_WrongSeasonInNote_IsRejected()
    {
        var html =
            "<div data-subtitle-result=\"true\">" +
            "<a data-search-result-link=\"true\" href=\"https://subs.ro/subtitrare/show-sezonul-2-2020/500\" " +
            $"data-subtitle-id=\"500\" data-movie-name=\"Show - Sezonul 2\">Show - Sezonul 2{RomanianFlag}</a>" +
            "</div>";
        var downloader = BuildDownloader(resultsHtmlOverride: html);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Show",
            Name = "Episode One",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        Assert.Empty(await downloader.Search(request, CancellationToken.None));
    }

    [Fact]
    public async Task Search_Episode_MatchingSeasonInNote_IsKept()
    {
        var html =
            "<div data-subtitle-result=\"true\">" +
            "<a data-search-result-link=\"true\" href=\"https://subs.ro/subtitrare/show-sezonul-1-2020/500\" " +
            $"data-subtitle-id=\"500\" data-movie-name=\"Show - Sezonul 1\">Show - Sezonul 1{RomanianFlag}</a>" +
            "</div>";
        var downloader = BuildDownloader(resultsHtmlOverride: html);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Show",
            Name = "Episode One",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_Episode_NoSeasonMarkerInNote_IsKeptPermissively()
    {
        // ResultsHtml's card ("First Knight", no season text at all) is ambiguous, not
        // known-wrong, so it should not be rejected outright.
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "First Knight",
            Name = "Episode One",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_Episode_WithMediaPath_MarksIdAsEpisode()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "First Knight",
            Name = "Episode One",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            MediaPath = "/tv/First.Knight.S01E01.1080p.WEB-DL.mkv",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Equal("129412|first-knight-1995|First.Knight.S01E01.1080p.WEB-DL|ep", results[0].Id);
    }

    [Fact]
    public async Task Search_NonRomanian_ReturnsEmpty()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            Language = "eng",
            TwoLetterISOLanguageName = "en",
        };

        Assert.Empty(await downloader.Search(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetSubtitles_DownloadsExtractsAndDecodes()
    {
        var downloader = BuildDownloader();

        var response = await downloader.GetSubtitles("129412|first-knight-1995", CancellationToken.None);

        Assert.Equal("srt", response.Format);
        Assert.Equal("ron", response.Language);
        using var reader = new StreamReader(response.Stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.Contains("Bună ziua", text);
    }

    [Fact]
    public async Task Search_WithMediaPath_AppendsMatchHintToId()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            MediaPath = "/movies/First.Knight.1995.WEB-DL.HEVC-TEPES.mkv",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Equal("129412|first-knight-1995|First.Knight.1995.WEB-DL.HEVC-TEPES", results[0].Id);
    }

    [Fact]
    public async Task GetSubtitles_EpisodeMarker_ArchiveMissingRequestedEpisode_Throws()
    {
        var zip = BuildZipWithEpisodes();
        var downloader = BuildDownloader(zip);

        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.GetSubtitles(
            "129412|first-knight-1995|Show.S01E03.WEB-DL|ep", CancellationToken.None));
    }

    [Fact]
    public async Task GetSubtitles_EpisodeMarker_ArchiveContainsRequestedEpisode_ReturnsIt()
    {
        var zip = BuildZipWithEpisodes();
        var downloader = BuildDownloader(zip);

        var response = await downloader.GetSubtitles(
            "129412|first-knight-1995|Show.S01E02.WEB-DL|ep", CancellationToken.None);

        using var reader = new StreamReader(response.Stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.Contains("episode two", text);
    }

    [Fact]
    public async Task GetSubtitles_NoEpisodeMarker_ArchiveMissingHint_FallsBackToLargest()
    {
        // Without the "|ep" marker (a movie id), a non-matching hint still falls back
        // instead of throwing.
        var zip = BuildZipWithEpisodes();
        var downloader = BuildDownloader(zip);

        var response = await downloader.GetSubtitles(
            "129412|first-knight-1995|Show.S01E03.WEB-DL", CancellationToken.None);

        using var reader = new StreamReader(response.Stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.True(text.Contains("episode one") || text.Contains("episode two"));
    }

    [Fact]
    public async Task GetSubtitles_MultipleReleasesInArchive_PicksMatchingRelease()
    {
        var downloader = BuildDownloader(BuildZipWithMultipleReleases());

        var response = await downloader.GetSubtitles(
            "129412|first-knight-1995|First.Knight.1995.WEB-DL.HEVC-TEPES", CancellationToken.None);

        using var reader = new StreamReader(response.Stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.Contains("matching release", text);
    }

    [Fact]
    public async Task Search_MultipleResults_RanksReleaseMatchAboveHigherDownloadCount()
    {
        var html = MultiReleaseResultsHtml(
            highDownloadNote: "First Knight (1995), BluRay release.",
            lowDownloadNote: "First Knight (1995), pentru WEB-DL. Enjoy!");
        var downloader = BuildDownloader(resultsHtmlOverride: html);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            MediaPath = "/movies/First.Knight.1995.WEB-DL.mkv",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("129500|first-knight-1995-alt|First.Knight.1995.WEB-DL", results[0].Id);
        Assert.Equal(10, results[0].DownloadCount);
        Assert.Equal("129412|first-knight-1995|First.Knight.1995.WEB-DL", results[1].Id);
    }

    [Fact]
    public async Task Search_MultipleResults_NoMediaPath_FallsBackToDownloadCount()
    {
        var html = MultiReleaseResultsHtml(
            highDownloadNote: "First Knight (1995), BluRay release.",
            lowDownloadNote: "First Knight (1995), pentru WEB-DL. Enjoy!");
        var downloader = BuildDownloader(resultsHtmlOverride: html);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(900, results[0].DownloadCount);
    }

    [Fact]
    public async Task Search_WithImdbProviderId_SearchesByExternalIdAndStripsTtPrefix()
    {
        var (downloader, handler) = BuildDownloaderWithSeparateResponses(
            externalIdResultsHtml: ResultsHtml,
            titleResultsHtml: "<div data-search-results-state=\"error\"></div>");
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            ProviderIds = { [MetadataProvider.Imdb.ToString()] = "tt3626476" },
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        var searchCalls = handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("ajax/search"));
        Assert.Equal(1, searchCalls);
        Assert.Contains("external_id=3626476", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task Search_WithTmdbProviderId_UsedWhenNoImdbId()
    {
        var (downloader, handler) = BuildDownloaderWithSeparateResponses(
            externalIdResultsHtml: ResultsHtml,
            titleResultsHtml: "<div data-search-results-state=\"error\"></div>");
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            ProviderIds = { [MetadataProvider.Tmdb.ToString()] = "653349" },
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Contains("external_id=653349", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task Search_ExternalIdReturnsNothing_FallsBackToTitleSearch()
    {
        var (downloader, handler) = BuildDownloaderWithSeparateResponses(
            externalIdResultsHtml: "<div data-search-results-state=\"error\"></div>",
            titleResultsHtml: ResultsHtml);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
            ProviderIds = { [MetadataProvider.Imdb.ToString()] = "tt3626476" },
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);

        // The external-id attempt's error state also triggers SubsRoClient's own
        // antispam-refresh retry (unrelated to this fallback), so that's 2 calls before
        // the title-search fallback makes a 3rd.
        var searchCalls = handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("ajax/search"));
        Assert.Equal(3, searchCalls);
        Assert.Contains("titlu-film=First+Knight", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task Search_NoProviderIds_SearchesByTitleOnly()
    {
        var (downloader, handler) = BuildDownloaderWithSeparateResponses(
            externalIdResultsHtml: "<div data-search-results-state=\"error\"></div>",
            titleResultsHtml: ResultsHtml);
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        var searchCalls = handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("ajax/search"));
        Assert.Equal(1, searchCalls);
    }

    [Fact]
    public async Task Search_ApiKeyConfigured_UsesApiResults_NeverTouchesScrapePath()
    {
        const string ApiJson = """
            {"status":200,"meta":{"requestId":"r"},"count":1,"items":[
              {"id":102290,"title":"Vacation Friends","description":"pentru WEB-DL","translator":"Anonim"}
            ]}
            """;
        var apiClient = BuildApiClient(out var apiHandler, req =>
            req.RequestUri!.AbsolutePath.Contains("imdbid") ? JsonResponse(ApiJson) : JsonResponse(EmptySearchJson));
        var downloader = new TestableSubsRoDownloader(
            NullLogger<SubsRoDownloader>.Instance, BuildUnusedScrapeClient(), apiClient, "my-api-key");
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

        Assert.Single(results);
        Assert.Equal("api|102290", results[0].Id);
        Assert.Equal("Anonim", results[0].Author);
        Assert.Equal("pentru WEB-DL", results[0].Comment);
        Assert.Contains(apiHandler.Requests, r => r.Headers.GetValues("X-Subs-Api-Key").Single() == "my-api-key");
    }

    [Fact]
    public async Task Search_ApiKeyConfigured_ApiReturnsNothing_FallsBackToScrape()
    {
        var apiClient = BuildApiClient(out _, _ => JsonResponse(EmptySearchJson));
        var scrapeHandler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("descarca"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BuildZip()) };
            }

            var body = path.Contains("cautare") ? CautarePage : ResultsHtml;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            };
        });
        var scrapeHttp = new HttpClient(scrapeHandler) { BaseAddress = new Uri("https://subs.ro") };
        var scrapeClient = new SubsRoClient(scrapeHttp, NullLogger<SubsRoClient>.Instance);
        var downloader = new TestableSubsRoDownloader(
            NullLogger<SubsRoDownloader>.Instance, scrapeClient, apiClient, "my-api-key");
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "First Knight",
            ProductionYear = 1995,
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        var results = (await downloader.Search(request, CancellationToken.None)).ToList();

        Assert.Single(results);
        Assert.Equal("129412|first-knight-1995", results[0].Id);
    }

    [Fact]
    public async Task GetSubtitles_ApiSourcedId_DownloadsViaApiClient()
    {
        var zip = BuildZip();
        var apiClient = BuildApiClient(out var apiHandler, req =>
            req.RequestUri!.AbsolutePath.Contains("download")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }
                : JsonResponse(EmptySearchJson));
        var downloader = new TestableSubsRoDownloader(
            NullLogger<SubsRoDownloader>.Instance, BuildUnusedScrapeClient(), apiClient, "my-api-key");

        var response = await downloader.GetSubtitles("api|102290", CancellationToken.None);

        using var reader = new StreamReader(response.Stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.Contains("Bună ziua", text);
        Assert.Contains(apiHandler.Requests, r => r.RequestUri!.AbsolutePath == "/v1.0/subtitle/102290/download");
    }

    [Fact]
    public async Task GetSubtitles_ApiSourcedId_NoApiKeyConfigured_Throws()
    {
        var downloader = new TestableSubsRoDownloader(
            NullLogger<SubsRoDownloader>.Instance, BuildUnusedScrapeClient(), BuildUnusedApiClient(), null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.GetSubtitles("api|102290", CancellationToken.None));
    }
}
