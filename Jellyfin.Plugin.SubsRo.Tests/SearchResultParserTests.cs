using Jellyfin.Plugin.SubsRo;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SearchResultParserTests
{
    private const string TwoResults = """
        <div id="search-results">
          <div data-subtitle-result="true">
            <a href="https://subs.ro/subtitrare/first-knight-1995/129412"
               data-search-result-link="true"
               data-subtitle-id="129412"
               data-movie-name="First Knight">First Knight (1995)</a>
          </div>
          <div data-subtitle-result="true">
            <a href="https://subs.ro/subtitrare/first-knight-1995-extended/129500"
               data-search-result-link="true"
               data-subtitle-id="129500"
               data-movie-name="First Knight">First Knight Extended</a>
          </div>
        </div>
        """;

    [Fact]
    public void Parse_ExtractsIdSlugAndTitle()
    {
        var results = SearchResultParser.Parse(TwoResults);

        Assert.Equal(2, results.Count);
        Assert.Equal("129412", results[0].Id);
        Assert.Equal("first-knight-1995", results[0].Slug);
        Assert.Equal("First Knight", results[0].Title);
    }

    [Fact]
    public void Parse_EmptyOrErrorState_ReturnsEmpty()
    {
        Assert.Empty(SearchResultParser.Parse(""));
        Assert.Empty(SearchResultParser.Parse(
            "<div data-search-results-state=\"error\"></div>"));
    }
}
