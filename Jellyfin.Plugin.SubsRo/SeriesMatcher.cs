using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Extracts season numbers from subs.ro's free-text titles/notes for TV series. subs.ro has
/// no structured season field (scrape or official API) — a series result is tagged only via
/// text like "Sezonul 5" (single season) or "Sezoanele 1-2" (season range, including a
/// still-ongoing show bundled up to its latest complete season).
/// </summary>
public static partial class SeriesMatcher
{
    /// <summary>
    /// Extracts every season number mentioned in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Free text to scan (typically a result's title plus its note/description).</param>
    /// <returns>
    /// The season numbers found. Empty means no season marker was found at all — this is
    /// "unknown", not "no seasons", and callers should treat it permissively rather than as a
    /// rejection, since subs.ro doesn't tag every series result the same way.
    /// </returns>
    public static IReadOnlySet<int> ExtractSeasons(string? text)
    {
        var seasons = new HashSet<int>();
        if (string.IsNullOrEmpty(text))
        {
            return seasons;
        }

        foreach (Match match in SingleSeasonPattern().Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var season))
            {
                seasons.Add(season);
            }
        }

        foreach (Match match in SeasonRangePattern().Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var start)
                && int.TryParse(match.Groups[2].Value, out var end)
                && start <= end)
            {
                for (var season = start; season <= end; season++)
                {
                    seasons.Add(season);
                }
            }
        }

        return seasons;
    }

    [GeneratedRegex(@"\bSezonul\s+(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SingleSeasonPattern();

    [GeneratedRegex(@"\bSezoanele\s+(\d{1,2})\s*-\s*(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonRangePattern();
}
