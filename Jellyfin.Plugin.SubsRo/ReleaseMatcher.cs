namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Shared release-name token matching used to rank subs.ro search results and archive
/// entries against the local video file's name (resolution, source, codec, release group, ...).
/// </summary>
public static class ReleaseMatcher
{
    private static readonly char[] TokenSeparators = { '.', ' ', '-', '_', '[', ']', '(', ')' };

    /// <summary>
    /// Splits a release-style string into its normalized, case-insensitive tokens.
    /// </summary>
    /// <param name="value">The string to tokenize (a file name, or free-text release note).</param>
    /// <returns>The set of tokens; empty when <paramref name="value"/> is null or empty.</returns>
    public static HashSet<string> Tokenize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts how many tokens <paramref name="candidateText"/> shares with <paramref name="hintTokens"/>.
    /// </summary>
    /// <param name="candidateText">The candidate text to score (a file name, or free-text release note).</param>
    /// <param name="hintTokens">The tokenized hint to match against.</param>
    /// <returns>The number of shared tokens.</returns>
    public static int Score(string? candidateText, IReadOnlySet<string> hintTokens)
        => hintTokens.Count == 0 ? 0 : Tokenize(candidateText).Intersect(hintTokens).Count();
}
