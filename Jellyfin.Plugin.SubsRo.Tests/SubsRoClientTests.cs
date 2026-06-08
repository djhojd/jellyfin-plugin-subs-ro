using System.Net;
using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.SubsRo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SubsRoClientTests
{
    private const string CautarePage =
        "<html><body><form><input type=\"hidden\" name=\"antispam\" value=\"tok123\"></form></body></html>";
    private const string ResultsHtml =
        "<div data-subtitle-result=\"true\"><a data-search-result-link=\"true\" " +
        "href=\"https://subs.ro/subtitrare/x-2020/77\" data-subtitle-id=\"77\" " +
        "data-movie-name=\"X\">X</a></div>";

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    [Fact]
    public async Task SearchAsync_BootstrapsTokenThenPostsSearch()
    {
        var handler = new StubHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("cautare") ? Ok(CautarePage) : Ok(ResultsHtml));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);

        var html = await client.SearchAsync("X", 2020, CancellationToken.None);

        Assert.Contains("data-subtitle-result", html);
        var postBody = handler.RequestBodies[^1];
        Assert.Contains("titlu-film=X", postBody);
        Assert.Contains("an=2020", postBody);
        Assert.Contains("type=subtitrari", postBody);
        Assert.Contains("antispam=tok123", postBody);
    }

    [Fact]
    public async Task DownloadAsync_GetsArchiveBytes()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") };
        var client = new SubsRoClient(http, NullLogger<SubsRoClient>.Instance);

        var bytes = await client.DownloadAsync("x-2020", "77", CancellationToken.None);

        Assert.Equal(payload, bytes);
        Assert.Contains("/subtitrare/descarca/x-2020/77", handler.Requests[^1].RequestUri!.AbsolutePath);
    }
}
