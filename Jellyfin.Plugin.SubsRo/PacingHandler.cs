using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Routes a client's requests through a shared <see cref="RequestPacer"/>. The HTTP client
/// factory rebuilds handlers periodically, so the pacing state lives in the pacer, not here.
/// </summary>
public sealed class PacingHandler : DelegatingHandler
{
    private readonly RequestPacer _pacer;
    private readonly ILogger<PacingHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PacingHandler"/> class.
    /// </summary>
    /// <param name="pacer">The pacer shared by all handlers for the same host.</param>
    /// <param name="logger">The logger.</param>
    public PacingHandler(RequestPacer pacer, ILogger<PacingHandler> logger)
    {
        _pacer = pacer;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _pacer.SendAsync(request, base.SendAsync, _logger, cancellationToken);
}
