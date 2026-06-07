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
    public void ExtractLargestSrt_ReturnsLargestSrt_WalkingSubfolders()
    {
        using var zip = BuildZip();
        var result = ArchiveExtractor.ExtractLargestSrt(zip);
        var text = Encoding.UTF8.GetString(result);
        Assert.Contains("xxxx", text);
    }

    [Fact]
    public void ExtractLargestSrt_NoSrt_Throws()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "readme.txt", "no subs here");
        }

        ms.Position = 0;
        Assert.Throws<InvalidOperationException>(() => ArchiveExtractor.ExtractLargestSrt(ms));
    }
}
