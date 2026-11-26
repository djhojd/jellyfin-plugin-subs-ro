namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// A single subtitle item from subs.ro's API. Only the fields the plugin actually uses are
/// mapped; the API returns more (timestamps, poster, ids, ...) that aren't needed here.
/// </summary>
public sealed class SubsRoApiSubtitleItem
{
    /// <summary>
    /// Gets or sets the subtitle id, used to build the download URL.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the movie title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the uploader's free-text description (often names the release, e.g.
    /// "pentru WEB-DL").
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the translator/uploader credit.
    /// </summary>
    public string? Translator { get; set; }

    /// <summary>
    /// Gets or sets the content type: <c>movie</c> or <c>series</c>.
    /// </summary>
    public string? Type { get; set; }
}
