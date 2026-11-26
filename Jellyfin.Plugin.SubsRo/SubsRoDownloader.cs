using System.Globalization;
using System.Text;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Jellyfin subtitle provider that downloads Romanian subtitles for movies from subs.ro.
/// </summary>
public class SubsRoDownloader : ISubtitleProvider
{
    private const string ApiIdPrefix = "api|";
    private const string EpisodeIdMarker = "ep";

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub", ".vtt" };
    private static readonly string[] RomanianTags = { "ro", "ron", "rum", "rom", "romanian", "romana" };
    private static readonly char[] TagSeparators = { '.', '-', '_', ' ' };

    private readonly ILogger<SubsRoDownloader> _logger;
    private readonly SubsRoClient _client;
    private readonly SubsRoApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoDownloader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="client">The subs.ro anonymous public-site HTTP client.</param>
    /// <param name="apiClient">The subs.ro official-API HTTP client.</param>
    public SubsRoDownloader(ILogger<SubsRoDownloader> logger, SubsRoClient client, SubsRoApiClient apiClient)
    {
        _logger = logger;
        _client = client;
        _apiClient = apiClient;
    }

    /// <inheritdoc />
    public string Name => "subs.ro";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie, VideoContentType.Episode };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
        SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        // Jellyfin only calls Search for content types listed in SupportedMediaTypes (see
        // SubtitleManager.SearchSubtitles), so ContentType is always Movie or Episode here.
        var isEpisode = request.ContentType == VideoContentType.Episode;
        var searchTitle = isEpisode ? request.SeriesName : request.Name;

        if (string.IsNullOrWhiteSpace(searchTitle) || !IsRomanian(request))
        {
            return Array.Empty<RemoteSubtitleInfo>();
        }

        if (request.IsAutomated && HasUntaggedOrRomanianSidecar(request.MediaPath))
        {
            _logger.LogInformation(
                "Skipping automatic subs.ro search for {MediaPath}: an untagged or Romanian subtitle file is already next to it",
                request.MediaPath);
            return Array.Empty<RemoteSubtitleInfo>();
        }

        var apiKey = GetApiKey();
        var viaApi = false;
        IReadOnlyList<SubsRoSearchResult> parsed = Array.Empty<SubsRoSearchResult>();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            parsed = await SearchViaApiAsync(apiKey, request, searchTitle, cancellationToken);
            viaApi = parsed.Count > 0;
        }

        if (!viaApi)
        {
            parsed = await SearchViaScrapeAsync(request, searchTitle, isEpisode, cancellationToken);
        }

        _logger.LogInformation(
            "subs.ro returned {Count} results for {Title} ({Year}) via {Source}",
            parsed.Count,
            searchTitle,
            request.ProductionYear,
            viaApi ? "API" : "public site");

        var rawCount = parsed.Count;
        parsed = FilterByContentType(parsed, request.ContentType, searchTitle, request.ParentIndexNumber);

        if (isEpisode)
        {
            _logger.LogDebug(
                "Content-type/season filter kept {Kept}/{Raw} results for {Title} S{Season}",
                parsed.Count,
                rawCount,
                searchTitle,
                request.ParentIndexNumber);
        }

        var matchHint = string.IsNullOrEmpty(request.MediaPath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(request.MediaPath);

        var hintTokens = ReleaseMatcher.Tokenize(matchHint);

        // Jellyfin's automatic "download missing subtitles" task takes the first result we
        // return, so ranking by release match (falling back to popularity) here is what
        // actually decides which release gets auto-downloaded, not just the manual picker.
        var scoredAndRanked = parsed
            .Select(r => (Result: r, Score: ReleaseMatcher.Score(r.Note, hintTokens)))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Result.DownloadCount ?? 0)
            .ToList();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var r in scoredAndRanked.Take(3))
            {
                _logger.LogDebug(
                    "Ranked candidate id={Id} title={Title} score={Score} downloadCount={DownloadCount}",
                    r.Result.Id,
                    r.Result.Title,
                    r.Score,
                    r.Result.DownloadCount);
            }
        }

        var ranked = scoredAndRanked.Select(r => r.Result).ToList();

        return ranked.Select(r => new RemoteSubtitleInfo
        {
            Id = BuildId(viaApi, r, matchHint, isEpisode),
            ProviderName = Name,
            Name = string.IsNullOrEmpty(r.Title) ? searchTitle : r.Title,
            Format = "srt",
            ThreeLetterISOLanguageName = "ron",
            Author = r.Uploader,
            Comment = r.Note,
            DownloadCount = r.DownloadCount,
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetSubtitles called for id={Id}", id);

        if (id.StartsWith(ApiIdPrefix, StringComparison.Ordinal))
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "This subtitle was found via subs.ro's API, but no API key is configured anymore.");
            }

            var apiParts = id[ApiIdPrefix.Length..].Split('|');
            var apiSubtitleId = apiParts[0];
            var apiMatchHint = apiParts.Length >= 2 ? apiParts[1] : null;
            var apiIsEpisode = apiParts.Length >= 3 && apiParts[2] == EpisodeIdMarker;

            var apiArchive = await _apiClient.DownloadAsync(apiKey, apiSubtitleId, cancellationToken);
            return BuildResponse(id, apiArchive, apiMatchHint, apiIsEpisode);
        }

        var parts = id.Split('|');
        if (parts.Length < 2)
        {
            throw new ArgumentException($"Invalid subtitle id: {id}", nameof(id));
        }

        var subtitleId = parts[0];
        var slug = parts[1];
        var matchHint = parts.Length >= 3 ? parts[2] : null;
        var isEpisode = parts.Length >= 4 && parts[3] == EpisodeIdMarker;

        var archive = await _client.DownloadAsync(slug, subtitleId, cancellationToken);
        return BuildResponse(id, archive, matchHint, isEpisode);
    }

    /// <summary>
    /// Gets the configured subs.ro API key, if any. Virtual so tests can supply one without
    /// a live <see cref="Plugin.Instance"/>.
    /// </summary>
    /// <returns>The configured API key, or <see langword="null"/>.</returns>
    protected virtual string? GetApiKey() => Plugin.Instance?.Configuration.ApiKey;

    private async Task<IReadOnlyList<SubsRoSearchResult>> SearchViaApiAsync(
        string apiKey, SubtitleSearchRequest request, string searchTitle, CancellationToken cancellationToken)
    {
        var imdbId = request.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrEmpty(imdbId))
        {
            var byImdb = await _apiClient.SearchAsync(apiKey, "imdbid", imdbId, cancellationToken);
            if (byImdb.Count > 0)
            {
                return byImdb.Select(ToSearchResult).ToList();
            }
        }

        var tmdbId = request.GetProviderId(MetadataProvider.Tmdb);
        if (!string.IsNullOrEmpty(tmdbId))
        {
            var byTmdb = await _apiClient.SearchAsync(apiKey, "tmdbid", tmdbId, cancellationToken);
            if (byTmdb.Count > 0)
            {
                return byTmdb.Select(ToSearchResult).ToList();
            }
        }

        var byTitle = await _apiClient.SearchAsync(apiKey, "title", searchTitle, cancellationToken);
        return byTitle.Select(ToSearchResult).ToList();
    }

    private async Task<IReadOnlyList<SubsRoSearchResult>> SearchViaScrapeAsync(
        SubtitleSearchRequest request, string searchTitle, bool isEpisode, CancellationToken cancellationToken)
    {
        var externalId = ExtractExternalId(request);
        IReadOnlyList<SubsRoSearchResult> parsed = Array.Empty<SubsRoSearchResult>();

        if (externalId is not null)
        {
            var byIdHtml = await _client.SearchAsync(string.Empty, null, externalId, cancellationToken);
            parsed = SearchResultParser.Parse(byIdHtml);
        }

        if (parsed.Count == 0)
        {
            if (externalId is not null)
            {
                _logger.LogDebug(
                    "subs.ro returned no results for external id {ExternalId}; falling back to title search for {Title}",
                    externalId,
                    searchTitle);
            }

            // A series' ProductionYear on an Episode request is the individual episode's own
            // air year, not the show's premiere year subs.ro tags its season archives with —
            // passing it here would filter out otherwise-correct results, so it's omitted.
            var searchYear = isEpisode ? null : request.ProductionYear;
            var byTitleHtml = await _client.SearchAsync(searchTitle, searchYear, null, cancellationToken);
            parsed = SearchResultParser.Parse(byTitleHtml);
        }

        return parsed;
    }

    /// <summary>
    /// Hard-filters results by content type and, for episodes, by series name and season.
    /// subs.ro's title search is a loose/fuzzy match — searching "Breaking Bad" can return
    /// wholly unrelated movies whose own title merely contains the show's name too (e.g. a
    /// spin-off film titled "El Camino: A Breaking Bad Movie") alongside the real show — so an
    /// episode search requires the result's own title to *start with* the searched series name,
    /// matching subs.ro's real naming convention ("{Series}", "{Series} - Sezonul N",
    /// "{Series} - Sezoanele N-M"); a plain substring check is not strict enough, since a
    /// spin-off's title can legitimately contain the parent show's name anywhere but its start.
    /// Season text ("Sezonul 5"/"Sezoanele 1-2" — there is no structured season field, scrape
    /// or API) then narrows to the right season, or is skipped permissively when a result
    /// carries no season marker at all, since not every TV result is reliably tagged (a genuine
    /// single-episode upload may say nothing about its season). Rejecting an entire wrong season
    /// outright (rather than just ranking it lower) matters because picking the wrong season is
    /// worse than picking a suboptimal release within the right one.
    /// </summary>
    private static List<SubsRoSearchResult> FilterByContentType(
        IReadOnlyList<SubsRoSearchResult> results, VideoContentType contentType, string searchTitle, int? requestedSeason)
    {
        if (contentType == VideoContentType.Movie)
        {
            return results
                .Where(r => !string.Equals(r.Type, "series", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return results
            .Where(r =>
            {
                if (string.Equals(r.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!r.Title.StartsWith(searchTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (requestedSeason is null)
                {
                    return true;
                }

                var seasons = SeriesMatcher.ExtractSeasons($"{r.Title} {r.Note}");
                return seasons.Count == 0 || seasons.Contains(requestedSeason.Value);
            })
            .ToList();
    }

    private SubtitleResponse BuildResponse(string id, byte[] archive, string? matchHint, bool requireMatch)
    {
        byte[] srtBytes;
        try
        {
            srtBytes = ArchiveExtractor.ExtractBestSrt(new MemoryStream(archive), matchHint, requireMatch);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract a subtitle for id={Id} matchHint={MatchHint} requireMatch={RequireMatch}",
                id,
                matchHint,
                requireMatch);
            throw;
        }

        var utf8 = EncodingConverter.ToUtf8String(srtBytes);

        _logger.LogInformation(
            "Downloaded subtitle {Id} from subs.ro ({Bytes} bytes)",
            id,
            srtBytes.Length);

        return new SubtitleResponse
        {
            Format = "srt",
            Language = "ron",
            Stream = new MemoryStream(Encoding.UTF8.GetBytes(utf8)),
        };
    }

    private static string BuildId(bool viaApi, SubsRoSearchResult result, string matchHint, bool isEpisode)
    {
        var episodeSuffix = isEpisode && !string.IsNullOrEmpty(matchHint)
            ? string.Format(CultureInfo.InvariantCulture, "|{0}", EpisodeIdMarker)
            : string.Empty;

        if (viaApi)
        {
            return string.IsNullOrEmpty(matchHint)
                ? string.Format(CultureInfo.InvariantCulture, "{0}{1}", ApiIdPrefix, result.Id)
                : string.Format(CultureInfo.InvariantCulture, "{0}{1}|{2}{3}", ApiIdPrefix, result.Id, matchHint, episodeSuffix);
        }

        return string.IsNullOrEmpty(matchHint)
            ? string.Format(CultureInfo.InvariantCulture, "{0}|{1}", result.Id, result.Slug)
            : string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}{3}", result.Id, result.Slug, matchHint, episodeSuffix);
    }

    private static SubsRoSearchResult ToSearchResult(SubsRoApiSubtitleItem item) =>
        new(
            item.Id.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            item.Title ?? string.Empty,
            item.Translator,
            item.Description,
            Type: item.Type);

    /// <summary>
    /// Whether a subtitle file next to the video (named after it, like Jellyfin's own sidecar
    /// convention) is untagged or tagged Romanian. Jellyfin skips the automatic download only
    /// when a sidecar's language exactly equals the configured code, so an untagged sidecar, or
    /// one tagged <c>.rom</c> (which Jellyfin reads as Romani), would otherwise get a duplicate.
    /// </summary>
    private static bool HasUntaggedOrRomanianSidecar(string? mediaPath)
    {
        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var videoName = Path.GetFileNameWithoutExtension(mediaPath)!;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!SubtitleExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || !name.StartsWith(videoName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tag = name[videoName.Length..];
                if (tag.Length > 0 && Array.IndexOf(TagSeparators, tag[0]) < 0)
                {
                    continue; // a different video whose name merely starts with this one's
                }

                var tokens = tag.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0 || tokens.Any(t => RomanianTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder must not block downloads; fall through to a normal search.
        }

        return false;
    }

    private static bool IsRomanian(SubtitleSearchRequest request)
        => string.Equals(request.TwoLetterISOLanguageName, "ro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Language, "ron", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Language, "rum", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the bare numeric IMDb or TMDb id to search subs.ro's <c>external_id</c> field
    /// with (IMDb preferred), or <see langword="null"/> when neither is known. subs.ro
    /// rejects the "tt" IMDb prefix, so it is stripped here.
    /// </summary>
    private static string? ExtractExternalId(SubtitleSearchRequest request)
    {
        var imdbId = request.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrEmpty(imdbId))
        {
            return imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? imdbId[2..] : imdbId;
        }

        return request.GetProviderId(MetadataProvider.Tmdb);
    }
}
