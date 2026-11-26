using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.SubsRo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class RequestPacerTests
{
    private static (HttpClient Client, StubHttpMessageHandler Stub) Build(
        RequestPacer pacer, Func<int, HttpResponseMessage> responder)
    {
        StubHttpMessageHandler? stub = null;
        stub = new StubHttpMessageHandler(_ => responder(stub!.Requests.Count));
        var handler = new PacingHandler(pacer, NullLogger<PacingHandler>.Instance) { InnerHandler = stub };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://subs.ro") }, stub);
    }

    private static HttpResponseMessage TooMany(TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    [Fact]
    public void RegisteredClients_ResolveWithTheirPacingHandlers()
    {
        var services = new ServiceCollection().AddLogging();
        new PluginServiceRegistrator().RegisterServices(services, null!);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<SubsRoClient>());
        Assert.NotNull(provider.GetRequiredService<SubsRoApiClient>());
    }

    [Fact]
    public async Task Requests_AreSpacedByTheInterval()
    {
        var (client, stub) = Build(new RequestPacer(TimeSpan.FromMilliseconds(300), TimeSpan.FromHours(1)), _ => new HttpResponseMessage(HttpStatusCode.OK));

        var watch = Stopwatch.StartNew();
        await client.GetAsync(new Uri("/a", UriKind.Relative));
        await client.GetAsync(new Uri("/b", UriKind.Relative));

        Assert.Equal(2, stub.Requests.Count);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task After429_LongPause_AnswersTooManyWithoutSending()
    {
        var (client, stub) = Build(new RequestPacer(TimeSpan.Zero, TimeSpan.FromHours(1)), _ => TooMany());

        await client.GetAsync(new Uri("/a", UriKind.Relative));
        using var second = await client.GetAsync(new Uri("/b", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task After429_RetryAfterOverridesTheCooldown()
    {
        var (client, stub) = Build(
            new RequestPacer(TimeSpan.Zero, TimeSpan.FromHours(1)),
            count => count == 1 ? TooMany(TimeSpan.FromMilliseconds(200)) : new HttpResponseMessage(HttpStatusCode.OK));

        await client.GetAsync(new Uri("/a", UriKind.Relative));
        using var second = await client.GetAsync(new Uri("/b", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }
}
