using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Extracts subtitle content from a downloaded subs.ro archive (zip or rar).
/// </summary>
public static partial class ArchiveExtractor
{
    /// <summary>
    /// Opens the archive and returns the bytes of the <c>.srt</c> entry that best matches
    /// <paramref name="matchHint"/> (typically the video file's name), walking subfolders.
    /// Matching is by shared release-name tokens (resolution, source, codec, release group, ...);
    /// when no entry shares a token with the hint, or no hint is supplied, the largest <c>.srt</c>
    /// entry is returned instead, since size is otherwise the best available signal of completeness.
    /// </summary>
    /// <param name="archiveStream">The archive stream (zip or rar).</param>
    /// <param name="matchHint">The video file name (with or without extension) to match releases against, if known.</param>
    /// <param name="requireMatch">
    /// When <see langword="true"/> and <paramref name="matchHint"/> is non-empty, a shared-token
    /// match is mandatory rather than a preference — used for TV episodes, where a season
    /// archive can legitimately omit a specific episode (e.g. a partial-season upload), and
    /// silently falling back to the largest entry would serve the wrong episode's subtitle
    /// instead of failing loudly. Has no effect when there is no hint to match against.
    /// </param>
    /// <returns>The raw bytes of the chosen <c>.srt</c> entry.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the archive contains no <c>.srt</c> entry, or when <paramref name="requireMatch"/>
    /// is set and no entry shares a token with <paramref name="matchHint"/>.
    /// </exception>
    public static byte[] ExtractBestSrt(Stream archiveStream, string? matchHint = null, bool requireMatch = false)
    {
        using var archive = ArchiveFactory.Open(archiveStream);
        var candidates = archive.Entries
            .Where(e => !e.IsDirectory
                && e.Key is not null
                && e.Key.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            .Select(e => (Entry: e, Key: e.Key!))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Archive contains no .srt entry.");
        }

        var srt = SelectBest(candidates, matchHint, requireMatch);

        using var entryStream = srt.OpenEntryStream();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static SharpCompress.Archives.IArchiveEntry SelectBest(
        IReadOnlyList<(SharpCompress.Archives.IArchiveEntry Entry, string Key)> candidates,
        string? matchHint,
        bool requireMatch)
    {
        var hintTokens = ReleaseMatcher.Tokenize(matchHint);
        if (hintTokens.Count == 0)
        {
            return candidates.OrderByDescending(c => c.Entry.Size).First().Entry;
        }

        var pool = NarrowToRequestedEpisode(candidates, hintTokens, requireMatch);

        var scored = pool
            .Select(c => (c.Entry, Score: ReleaseMatcher.Score(c.Key, hintTokens)))
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Entry.Size)
            .First();

        if (requireMatch && scored.Score == 0 && !HasEpisodeMarker(hintTokens))
        {
            throw new InvalidOperationException(
                "No archive entry matches the requested episode; this archive likely doesn't include it.");
        }

        return scored.Entry;
    }

    /// <summary>
    /// A season archive's entries otherwise share almost every token (show name, release
    /// group, source, ...) for a given episode's several release variants, AND the exact same
    /// tokens repeat verbatim across every *other* episode too — so an overall release-match
    /// score alone can't reliably tell "right episode, so-so release" from "wrong episode,
    /// perfect release match" (they can score identically, or the wrong episode can even score
    /// higher). When the hint carries an episode marker (e.g. <c>S01E03</c>), candidates are
    /// narrowed to only entries carrying that same marker *before* scoring, so release-format
    /// ranking only ever competes within the correct episode's own release variants.
    /// </summary>
    private static IReadOnlyList<(SharpCompress.Archives.IArchiveEntry Entry, string Key)> NarrowToRequestedEpisode(
        IReadOnlyList<(SharpCompress.Archives.IArchiveEntry Entry, string Key)> candidates,
        IReadOnlySet<string> hintTokens,
        bool requireMatch)
    {
        var episodeMarkers = hintTokens.Where(t => EpisodeMarkerPattern().IsMatch(t)).ToList();
        if (episodeMarkers.Count == 0)
        {
            return candidates;
        }

        var narrowed = candidates.Where(c => ReleaseMatcher.Tokenize(c.Key).Overlaps(episodeMarkers)).ToList();
        if (narrowed.Count > 0)
        {
            return narrowed;
        }

        if (requireMatch)
        {
            throw new InvalidOperationException(
                "No archive entry matches the requested episode; this archive likely doesn't include it.");
        }

        return candidates;
    }

    private static bool HasEpisodeMarker(IReadOnlySet<string> hintTokens)
        => hintTokens.Any(t => EpisodeMarkerPattern().IsMatch(t));

    [GeneratedRegex(@"^S\d{1,2}E\d{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeMarkerPattern();
}
