namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// A single subtitle result parsed from a subs.ro search response.
/// </summary>
/// <param name="Id">The subs.ro subtitle id.</param>
/// <param name="Slug">The URL slug used to build the download link.</param>
/// <param name="Title">The movie title as shown on subs.ro.</param>
public sealed record SubsRoSearchResult(string Id, string Slug, string Title);
