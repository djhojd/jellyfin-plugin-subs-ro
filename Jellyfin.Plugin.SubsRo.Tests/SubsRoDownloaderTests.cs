using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.SubsRo;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SubsRoDownloaderTests
{
    private const string CautarePage =
        "<input type=\"hidden\" name=\"antispam\" value=\"t\">";
    private const string ResultsHtml =
        "<div data-subtitle-result=\"true\"><a data-search-result-link=\"true\" " +
        "href=\"https://subs.ro/subtitrare/first-knight-1995/129412\" " +
        "data-subtitle-id=\"129412\" data-movie-name=\"First Knight\">First Knight</a></div>";

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

    private static SubsRoDownloader BuildDownloader()
    {
        var zip = BuildZip();
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("descarca"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            }

            var body = path.Contains("cautare") ? CautarePage : ResultsHtml;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);
        return new SubsRoDownloader(NullLogger<SubsRoDownloader>.Instance, client);
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
    public async Task Search_NonMovie_ReturnsEmpty()
    {
        var downloader = BuildDownloader();
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            Name = "Whatever",
            Language = "ron",
            TwoLetterISOLanguageName = "ro",
        };

        Assert.Empty(await downloader.Search(request, CancellationToken.None));
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
}
