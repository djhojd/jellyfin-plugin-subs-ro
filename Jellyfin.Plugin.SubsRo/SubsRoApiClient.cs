using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Thin HTTP client for subs.ro's official API (<c>https://api.subs.ro</c>), used when the
/// plugin is configured with an API key. Structured JSON, no antispam/session bootstrap
/// needed — unlike <see cref="SubsRoClient"/>, which scrapes the anonymous public site.
/// </summary>
public sealed class SubsRoApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<SubsRoApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoApiClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client (base address must be subs.ro's API host).</param>
    /// <param name="logger">The logger.</param>
    public SubsRoApiClient(HttpClient http, ILogger<SubsRoApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Searches subs.ro's API for Romanian subtitles matching the given field/value.
    /// </summary>
    /// <param name="apiKey">The caller's subs.ro API key.</param>
    /// <param name="searchField">One of <c>imdbid</c>, <c>tmdbid</c>, or <c>title</c>.</param>
    /// <param name="value">The search value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching items; empty on any error (invalid key, quota exceeded, no results, ...).</returns>
    public async Task<IReadOnlyList<SubsRoApiSubtitleItem>> SearchAsync(
        string apiKey, string searchField, string value, CancellationToken cancellationToken)
    {
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "/v1.0/search/{0}/{1}?language=ro",
            searchField,
            Uri.EscapeDataString(value));

        using var response = await SendAsync(apiKey, url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "subs.ro API search failed with status {StatusCode} for {Field}={Value}",
                (int)response.StatusCode,
                searchField,
                value);
            return Array.Empty<SubsRoApiSubtitleItem>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var parsed = JsonSerializer.Deserialize<SubsRoApiSearchResponse>(json, JsonOptions);
            return parsed?.Items ?? Array.Empty<SubsRoApiSubtitleItem>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse subs.ro API search response for {Field}={Value}", searchField, value);
            return Array.Empty<SubsRoApiSubtitleItem>();
        }
    }

    /// <summary>
    /// Downloads a subtitle archive by numeric id via subs.ro's API.
    /// </summary>
    /// <param name="apiKey">The caller's subs.ro API key.</param>
    /// <param name="id">The subtitle id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The archive bytes.</returns>
    public async Task<byte[]> DownloadAsync(string apiKey, string id, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, "/v1.0/subtitle/{0}/download", id);
        using var response = await SendAsync(apiKey, url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string apiKey, string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Add("X-Subs-Api-Key", apiKey);
        return await _http.SendAsync(request, cancellationToken);
    }
}
