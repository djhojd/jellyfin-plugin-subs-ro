using Jellyfin.Plugin.SubsRo;
using Xunit;

namespace Jellyfin.Plugin.SubsRo.Tests;

public class SeriesMatcherTests
{
    [Fact]
    public void ExtractSeasons_SingleSeason_ReturnsThatSeason()
    {
        var seasons = SeriesMatcher.ExtractSeasons("Breaking Bad - Sezonul 5");

        Assert.Equal(new HashSet<int> { 5 }, seasons);
    }

    [Fact]
    public void ExtractSeasons_SeasonRange_ReturnsAllSeasonsInRange()
    {
        var seasons = SeriesMatcher.ExtractSeasons("The Pitt - Sezoanele 1-2 complete, 30 episoade");

        Assert.Equal(new HashSet<int> { 1, 2 }, seasons);
    }

    [Fact]
    public void ExtractSeasons_PartialSeasonNote_StillReturnsSeasonNumber()
    {
        var seasons = SeriesMatcher.ExtractSeasons("Sezonul 5, episoadele 9-13, 15-16, pentru WEB-DL");

        Assert.Equal(new HashSet<int> { 5 }, seasons);
    }

    [Fact]
    public void ExtractSeasons_NoSeasonMarker_ReturnsEmpty()
    {
        var seasons = SeriesMatcher.ExtractSeasons("Severance DVDRIP XVID");

        Assert.Empty(seasons);
    }

    [Fact]
    public void ExtractSeasons_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(SeriesMatcher.ExtractSeasons(null));
        Assert.Empty(SeriesMatcher.ExtractSeasons(string.Empty));
    }

    [Fact]
    public void ExtractSeasons_IsCaseInsensitive()
    {
        var seasons = SeriesMatcher.ExtractSeasons("Show - sezonul 3");

        Assert.Equal(new HashSet<int> { 3 }, seasons);
    }

    [Fact]
    public void ExtractSeasons_CombinesMarkersFromTitleAndNote()
    {
        var seasons = SeriesMatcher.ExtractSeasons("Show - Sezonul 2 Sezoanele 4-5");

        Assert.Equal(new HashSet<int> { 2, 4, 5 }, seasons);
    }
}
