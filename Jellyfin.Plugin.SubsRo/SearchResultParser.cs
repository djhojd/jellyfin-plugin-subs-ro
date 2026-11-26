using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Parses the HTML fragment returned by subs.ro's search endpoint into structured results.
/// </summary>
public static class SearchResultParser
{
    /// <summary>
    /// Parses subtitle result cards from a subs.ro search HTML fragment.
    /// </summary>
    /// <param name="html">The HTML fragment.</param>
    /// <returns>The parsed results; empty when the fragment is empty or carries an error state.</returns>
    public static IReadOnlyList<SubsRoSearchResult> Parse(string html)
    {
        var results = new List<SubsRoSearchResult>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return results;
        }

        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(html);

        foreach (var card in doc.QuerySelectorAll("[data-subtitle-result]"))
        {
            if (!IsRomanian(card))
            {
                continue;
            }

            var link = card.QuerySelector("a[data-search-result-link]")
                ?? card.QuerySelector("a[href*='/subtitrare/']");
            if (link is null)
            {
                continue;
            }

            var href = link.GetAttribute("href") ?? string.Empty;
            var (parsedId, slug) = ParseHref(href);
            var id = link.GetAttribute("data-subtitle-id") ?? parsedId;
            var title = link.GetAttribute("data-movie-name") ?? string.Empty;

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(slug))
            {
                continue;
            }

            results.Add(new SubsRoSearchResult(
                id,
                slug,
                title,
                ExtractUploader(card),
                ExtractNote(card),
                ExtractDownloadCount(card)));
        }

        return results;
    }

    private static bool IsRomanian(IElement card)
        => card.QuerySelector("img[src*='flag-rom']") is not null;

    private static string? ExtractUploader(IElement card)
    {
        var label = card.QuerySelectorAll("span.font-medium.text-gray-700")
            .FirstOrDefault(s => s.TextContent.Trim() is "Uploader:");
        var value = label?.ParentElement?.TextContent;
        if (value is null)
        {
            return null;
        }

        var uploader = value.Replace("Uploader:", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(uploader) ? null : uploader;
    }

    private static string? ExtractNote(IElement card)
    {
        var note = card.QuerySelector("div.leading-relaxed")?.TextContent;
        if (string.IsNullOrEmpty(note))
        {
            return null;
        }

        var normalized = string.Join(' ', note.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static int? ExtractDownloadCount(IElement card)
    {
        var countText = card.QuerySelector("img.opacity-60[src$='download.svg']")
            ?.ParentElement
            ?.QuerySelector("span")
            ?.TextContent
            .Trim();

        var parsed = int.TryParse(
            countText,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var count);
        return parsed ? count : null;
    }

    private static (string? Id, string? Slug) ParseHref(string href)
    {
        const string Marker = "/subtitrare/";
        var idx = href.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return (null, null);
        }

        var tail = href[(idx + Marker.Length)..].Trim('/');
        var parts = tail.Split('/');
        if (parts.Length < 2)
        {
            return (null, null);
        }

        return (parts[^1], string.Join('/', parts[..^1]));
    }
}
