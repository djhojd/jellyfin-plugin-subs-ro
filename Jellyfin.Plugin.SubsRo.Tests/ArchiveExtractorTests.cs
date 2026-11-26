using System.IO;
using System.IO.Compression;
using System.Text;
using Jellyfin.Plugin.SubsRo;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class ArchiveExtractorTests
{
    private static MemoryStream BuildZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "readme.txt", "ignore me");
            WriteEntry(zip, "WEB-DL/small.srt", "1\n00:00:01,000 --> 00:00:02,000\nhi\n");
            WriteEntry(zip, "WEB-DL/main.srt",
                "1\n00:00:01,000 --> 00:00:02,000\n" + new string('x', 500) + "\n");
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    [Fact]
    public void ExtractBestSrt_NoHint_ReturnsLargestSrt_WalkingSubfolders()
    {
        using var zip = BuildZip();
        var result = ArchiveExtractor.ExtractBestSrt(zip);
        var text = Encoding.UTF8.GetString(result);
        Assert.Contains("xxxx", text);
    }

    [Fact]
    public void ExtractBestSrt_NoSrt_Throws()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "readme.txt", "no subs here");
        }

        ms.Position = 0;
        Assert.Throws<InvalidOperationException>(() => ArchiveExtractor.ExtractBestSrt(ms));
    }

    [Fact]
    public void ExtractBestSrt_WithHint_PrefersReleaseMatch_OverLargerNonMatch()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "Movie.2021.BluRay.x264-OtherGroup.srt",
                "1\n00:00:01,000 --> 00:00:02,000\n" + new string('x', 500) + "\n");
            WriteEntry(zip, "Movie.2021.WEB-DL.HEVC-TEPES.srt",
                "1\n00:00:01,000 --> 00:00:02,000\nmatching release\n");
        }

        ms.Position = 0;
        var result = ArchiveExtractor.ExtractBestSrt(ms, "Movie.2021.WEB-DL.HEVC-TEPES");
        var text = Encoding.UTF8.GetString(result);

        Assert.Contains("matching release", text);
    }

    [Fact]
    public void ExtractBestSrt_HintMatchesNothing_FallsBackToLargest()
    {
        using var zip = BuildZip();
        var result = ArchiveExtractor.ExtractBestSrt(zip, "Completely.Unrelated.Release.Name");
        var text = Encoding.UTF8.GetString(result);
        Assert.Contains("xxxx", text);
    }

    [Fact]
    public void ExtractBestSrt_RequireMatch_HintMatchesNothing_Throws()
    {
        using var zip = BuildZip();
        Assert.Throws<InvalidOperationException>(
            () => ArchiveExtractor.ExtractBestSrt(zip, "Completely.Unrelated.Release.Name", requireMatch: true));
    }

    [Fact]
    public void ExtractBestSrt_RequireMatch_NoHintAtAll_StillFallsBackToLargest()
    {
        using var zip = BuildZip();
        var result = ArchiveExtractor.ExtractBestSrt(zip, matchHint: null, requireMatch: true);
        var text = Encoding.UTF8.GetString(result);
        Assert.Contains("xxxx", text);
    }

    [Fact]
    public void ExtractBestSrt_RequireMatch_MissingEpisodeInSeasonArchive_ThrowsInsteadOfWrongEpisode()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "Show.S01E01.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode one\n");
            WriteEntry(zip, "Show.S01E02.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode two\n");
        }

        ms.Position = 0;

        // Season archive doesn't include the requested S01E03 (a partial-season upload).
        Assert.Throws<InvalidOperationException>(
            () => ArchiveExtractor.ExtractBestSrt(ms, "Show.S01E03.WEB-DL", requireMatch: true));
    }

    [Fact]
    public void ExtractBestSrt_RequireMatch_EpisodePresent_PicksCorrectEntry()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "Show.S01E01.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode one\n");
            WriteEntry(zip, "Show.S01E02.WEB-DL.srt", "1\n00:00:01,000 --> 00:00:02,000\nepisode two\n");
        }

        ms.Position = 0;
        var result = ArchiveExtractor.ExtractBestSrt(ms, "Show.S01E02.WEB-DL", requireMatch: true);
        var text = Encoding.UTF8.GetString(result);

        Assert.Contains("episode two", text);
    }
}
