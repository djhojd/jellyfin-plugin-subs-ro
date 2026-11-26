using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Paces requests to one subs.ro host, shared by every <see cref="PacingHandler"/> built for it:
/// one request at a time, a fixed interval apart, and a pause after a 429. subs.ro publishes no
/// per-second limit, only a daily API quota and "wait a minute" after a 429 on the public site.
/// </summary>
[SuppressMessage("Reliability", "CA1001", Justification = "Lives for the whole process; the semaphore never allocates its wait handle.")]
public sealed class RequestPacer
{
    // Pauses up to this long are waited out; longer ones (the API's daily quota) fail fast
    // instead of stalling Jellyfin's scheduled task for hours.
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval;
    private readonly TimeSpan _cooldown;
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestPacer"/> class.
    /// </summary>
    /// <param name="interval">The minimum time between two requests.</param>
    /// <param name="cooldown">How long to pause after a 429 that carries no <c>Retry-After</c>.</param>
    public RequestPacer(TimeSpan interval, TimeSpan cooldown)
    {
        _interval = interval;
        _cooldown = cooldown;
    }

    /// <summary>
    /// Sends a request once the pacer allows it, or answers 429 without sending while paused
    /// for longer than a minute.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="send">Sends the request over the network.</param>
    /// <param name="logger">Logs the start of a pause.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(logger);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _nextAllowed - DateTimeOffset.UtcNow;
            if (wait > MaxWait)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            var response = await send(request, cancellationToken);
            var pause = _interval;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                pause = GetRetryAfter(response) ?? _cooldown;
                logger.LogWarning(
                    "subs.ro ({Host}) answered 429 Too Many Requests; pausing requests to it for {Pause}",
                    request?.RequestUri?.Host,
                    pause);
            }

            _nextAllowed = DateTimeOffset.UtcNow + pause;
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        return retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
    }
}
