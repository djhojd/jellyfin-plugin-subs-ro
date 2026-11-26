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
               data-movie-name="First Knight">First Knight (1995)
              <img src="https://cdn.subs.ro/img/flags/flag-rom-big.png" alt="Subtitrare First Knight - ro" />
            </a>
          </div>
          <div data-subtitle-result="true">
            <a href="https://subs.ro/subtitrare/first-knight-1995-extended/129500"
               data-search-result-link="true"
               data-subtitle-id="129500"
               data-movie-name="First Knight">First Knight Extended
              <img src="https://cdn.subs.ro/img/flags/flag-rom-big.png" alt="Subtitrare First Knight - ro" />
            </a>
          </div>
          <div data-subtitle-result="true">
            <a href="https://subs.ro/subtitrare/first-knight-1995-eng/129600"
               data-search-result-link="true"
               data-subtitle-id="129600"
               data-movie-name="First Knight">First Knight (English)
              <img src="https://cdn.subs.ro/img/flags/flag-eng-big.png" alt="Subtitrare First Knight - en" />
            </a>
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
    public void Parse_ExcludesNonRomanianResults()
    {
        var results = SearchResultParser.Parse(TwoResults);

        Assert.DoesNotContain(results, r => r.Id == "129600");
    }

    [Fact]
    public void Parse_EmptyOrErrorState_ReturnsEmpty()
    {
        Assert.Empty(SearchResultParser.Parse(""));
        Assert.Empty(SearchResultParser.Parse(
            "<div data-search-results-state=\"error\"></div>"));
    }

    [Fact]
    public void Parse_MissingEnrichmentMarkup_LeavesFieldsNull()
    {
        var results = SearchResultParser.Parse(TwoResults);

        Assert.Null(results[0].Uploader);
        Assert.Null(results[0].Note);
        Assert.Null(results[0].DownloadCount);
    }

    private const string ResultWithEnrichment = """
        <div data-subtitle-result="true">
          <a href="https://subs.ro/subtitrare/vacation-friends-2021/102290"
             data-search-result-link="true"
             data-subtitle-id="102290"
             data-movie-name="Vacation Friends">Vacation Friends (2021)
            <img src="https://cdn.subs.ro/img/flags/flag-rom-big.png" alt="Subtitrare Vacation Friends - ro" />
          </a>
          <div class="flex items-start">
            <img src="https://cdn.subs.ro/img/icons/user.svg" />
            <span class="font-bold text-gray-600">
              <span class="font-medium text-gray-700">Traducător:</span>
              SubRip
            </span>
          </div>
          <div class="flex items-start">
            <img src="https://cdn.subs.ro/img/icons/cloud-upload.svg" />
            <span class="font-bold text-gray-600">
              <span class="font-medium text-gray-700">Uploader:</span>
              Anonim
            </span>
          </div>
          <div class="flex items-center">
            <img src="https://cdn.subs.ro/img/icons/download.svg" class="opacity-60" />
            <span class="text-sm font-semibold text-gray-700">456</span>
          </div>
          <a class="bg-green-600">
            <img src="https://cdn.subs.ro/img/icons/download.svg" class="brightness-0 invert" />
            <span>Descarcă</span>
          </a>
          <div class="text-sm text-gray-700 leading-relaxed overflow-auto">
            <span> Vacation Friends (2021),</span> <span> pentru</span> <span> WEB-DL.</span> <span> Enjoy!</span>
          </div>
        </div>
        """;

    [Fact]
    public void Parse_ExtractsUploaderNoteAndDownloadCount()
    {
        var results = SearchResultParser.Parse(ResultWithEnrichment);

        Assert.Single(results);
        Assert.Equal("Anonim", results[0].Uploader);
        Assert.Equal("Vacation Friends (2021), pentru WEB-DL. Enjoy!", results[0].Note);
        Assert.Equal(456, results[0].DownloadCount);
    }

    [Fact]
    public void Parse_DownloadCountWithThousandsSeparator_ParsesCorrectly()
    {
        var html = ResultWithEnrichment.Replace(">456<", ">1,208<", StringComparison.Ordinal);
        var results = SearchResultParser.Parse(html);

        Assert.Equal(1208, results[0].DownloadCount);
    }
}
