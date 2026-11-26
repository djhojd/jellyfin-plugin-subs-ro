namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// A single subtitle result parsed from a subs.ro search response.
/// </summary>
/// <param name="Id">The subs.ro subtitle id.</param>
/// <param name="Slug">The URL slug used to build the download link.</param>
/// <param name="Title">The movie title as shown on subs.ro.</param>
/// <param name="Uploader">The uploader's display name, if shown.</param>
/// <param name="Note">The uploader's free-text note (often names the release, e.g. "WEB-DL"), if shown.</param>
/// <param name="DownloadCount">The result's download count, if shown.</param>
/// <param name="Type">
/// The content type (<c>movie</c> or <c>series</c>), when known. Only the official API
/// reports this; scrape-path results leave it <see langword="null"/> (season text in
/// <paramref name="Title"/>/<paramref name="Note"/> is the only signal there).
/// </param>
public sealed record SubsRoSearchResult(
    string Id,
    string Slug,
    string Title,
    string? Uploader = null,
    string? Note = null,
    int? DownloadCount = null,
    string? Type = null);
