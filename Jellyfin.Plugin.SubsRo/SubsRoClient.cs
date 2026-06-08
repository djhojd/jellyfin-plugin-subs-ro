using System.Globalization;
using System.Net.Http;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Thin HTTP client for subs.ro: bootstraps the session/antispam token, runs searches,
/// and downloads subtitle archives.
/// </summary>
public sealed class SubsRoClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SubsRoClient> _logger;
    private string? _antispam;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client (base address must be subs.ro).</param>
    /// <param name="logger">The logger.</param>
    public SubsRoClient(HttpClient http, ILogger<SubsRoClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Searches subs.ro for subtitles matching the supplied title and year.
    /// </summary>
    /// <param name="title">The movie title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The raw HTML fragment of search results.</returns>
    public async Task<string> SearchAsync(string title, int? year, CancellationToken cancellationToken)
    {
        await EnsureTokenAsync(cancellationToken);
        var html = await PostSearchAsync(title, year, cancellationToken);

        if (IsErrorOrEmpty(html))
        {
            _logger.LogDebug("subs.ro search returned no usable state; refreshing token and retrying for {Title}", title);
            _antispam = null;
            await EnsureTokenAsync(cancellationToken);
            html = await PostSearchAsync(title, year, cancellationToken);
        }

        return html;
    }

    /// <summary>
    /// Downloads the subtitle archive for a given slug and id.
    /// </summary>
    /// <param name="slug">The subtitle URL slug.</param>
    /// <param name="id">The subtitle id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The archive bytes.</returns>
    public async Task<byte[]> DownloadAsync(string slug, string id, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, "/subtitrare/descarca/{0}/{1}", slug, id);
        using var response = await _http.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (_antispam is not null)
        {
            return;
        }

        using var response = await _http.GetAsync(new Uri("/cautare", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _antispam = ExtractAntispam(body) ?? string.Empty;
    }

    private async Task<string> PostSearchAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("titlu-film", title),
            new("type", "subtitrari"),
            new("antispam", _antispam ?? string.Empty),
        };

        if (year is > 0)
        {
            fields.Add(new("an", year.Value.ToString(CultureInfo.InvariantCulture)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ajax/search")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Referrer = new Uri("https://subs.ro/cautare");

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string? ExtractAntispam(string html)
    {
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(html);
        return doc.QuerySelector("input[name='antispam']")?.GetAttribute("value");
    }

    private static bool IsErrorOrEmpty(string html)
        => string.IsNullOrWhiteSpace(html)
            || html.Contains("data-search-results-state=\"error\"", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("data-subtitle-result", StringComparison.OrdinalIgnoreCase);
}
