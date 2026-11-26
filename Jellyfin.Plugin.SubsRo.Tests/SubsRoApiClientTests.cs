using System.Net;
using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.SubsRo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SubsRoApiClientTests
{
    private const string SearchResponseJson = """
        {"status":200,"meta":{"requestId":"r1"},"count":1,"items":[
          {"id":102290,"title":"Vacation Friends","description":"pentru WEB-DL","translator":"Anonim","type":"movie"}
        ]}
        """;

    private static SubsRoApiClient BuildClient(
        out StubHttpMessageHandler handler, Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        handler = new StubHttpMessageHandler(responder ?? (_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SearchResponseJson, Encoding.UTF8, "application/json"),
            }));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.subs.ro") };
        return new SubsRoApiClient(http, NullLogger<SubsRoApiClient>.Instance);
    }

    [Fact]
    public async Task SearchAsync_SendsApiKeyHeaderAndCorrectUrl()
    {
        var client = BuildClient(out var handler);

        await client.SearchAsync("secret-key", "imdbid", "3626476", CancellationToken.None);

        var request = handler.Requests[^1];
        Assert.Equal("secret-key", request.Headers.GetValues("X-Subs-Api-Key").Single());
        Assert.Equal("/v1.0/search/imdbid/3626476", request.RequestUri!.AbsolutePath);
        Assert.Equal("language=ro", request.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task SearchAsync_ParsesItemsFromResponse()
    {
        var client = BuildClient(out _);

        var items = await client.SearchAsync("key", "imdbid", "3626476", CancellationToken.None);

        Assert.Single(items);
        Assert.Equal(102290, items[0].Id);
        Assert.Equal("Vacation Friends", items[0].Title);
        Assert.Equal("pentru WEB-DL", items[0].Description);
        Assert.Equal("Anonim", items[0].Translator);
        Assert.Equal("movie", items[0].Type);
    }

    [Fact]
    public async Task SearchAsync_ErrorStatus_ReturnsEmpty()
    {
        var client = BuildClient(out _, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"status":401,"meta":{"requestId":"r1"},"message":"Missing API key"}""", Encoding.UTF8, "application/json"),
        });

        var items = await client.SearchAsync("bad-key", "imdbid", "3626476", CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task SearchAsync_MalformedJson_ReturnsEmptyWithoutThrowing()
    {
        var client = BuildClient(out _, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json"),
        });

        var items = await client.SearchAsync("key", "imdbid", "3626476", CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task DownloadAsync_SendsApiKeyHeaderAndReturnsBytes()
    {
        var payload = new byte[] { 1, 2, 3 };
        var client = BuildClient(out var handler, _ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var bytes = await client.DownloadAsync("secret-key", "102290", CancellationToken.None);

        Assert.Equal(payload, bytes);
        var request = handler.Requests[^1];
        Assert.Equal("secret-key", request.Headers.GetValues("X-Subs-Api-Key").Single());
        Assert.Equal("/v1.0/subtitle/102290/download", request.RequestUri!.AbsolutePath);
    }
}
