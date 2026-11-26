namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// The envelope subs.ro's API wraps <c>/search/...</c> results in.
/// </summary>
public sealed class SubsRoApiSearchResponse
{
    /// <summary>
    /// Gets or sets the matching subtitle items.
    /// </summary>
    public IReadOnlyList<SubsRoApiSubtitleItem> Items { get; set; } = Array.Empty<SubsRoApiSubtitleItem>();
}
