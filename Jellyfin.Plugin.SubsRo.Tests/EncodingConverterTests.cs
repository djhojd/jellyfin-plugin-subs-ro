using System.Text;
using Jellyfin.Plugin.SubsRo;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class EncodingConverterTests
{
    [Fact]
    public void ToUtf8String_DecodesIso88592_PreservingRomanianDiacritics()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string original = "Această replică are diacritice: ă â î";
        var iso = Encoding.GetEncoding("iso-8859-2");
        var bytes = iso.GetBytes(original);

        var result = EncodingConverter.ToUtf8String(bytes);

        Assert.Contains("diacritice", result);
        Assert.Contains("ă", result);
        Assert.Contains("â", result);
        Assert.Contains("î", result);
    }

    [Fact]
    public void ToUtf8String_DecodesUtf8Roundtrip()
    {
        var bytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nSalut, știi?\n");
        var result = EncodingConverter.ToUtf8String(bytes);
        Assert.Contains("știi", result);
    }
}
