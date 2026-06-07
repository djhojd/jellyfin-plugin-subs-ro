using SharpCompress.Archives;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Extracts subtitle content from a downloaded subs.ro archive (zip or rar).
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// Opens the archive and returns the bytes of the largest <c>.srt</c> entry, walking subfolders.
    /// </summary>
    /// <param name="archiveStream">The archive stream (zip or rar).</param>
    /// <returns>The raw bytes of the chosen <c>.srt</c> entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the archive contains no <c>.srt</c> entry.</exception>
    public static byte[] ExtractLargestSrt(Stream archiveStream)
    {
        using var archive = ArchiveFactory.Open(archiveStream);
        var srt = archive.Entries
            .Where(e => !e.IsDirectory
                && e.Key is not null
                && e.Key.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Size)
            .FirstOrDefault();

        if (srt is null)
        {
            throw new InvalidOperationException("Archive contains no .srt entry.");
        }

        using var entryStream = srt.OpenEntryStream();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }
}
