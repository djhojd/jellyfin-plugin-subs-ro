using System.Globalization;
using System.Text;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Jellyfin subtitle provider that downloads Romanian subtitles for movies from subs.ro.
/// </summary>
public class SubsRoDownloader : ISubtitleProvider
{
    private readonly ILogger<SubsRoDownloader> _logger;
    private readonly SubsRoClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoDownloader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="client">The subs.ro HTTP client.</param>
    public SubsRoDownloader(ILogger<SubsRoDownloader> logger, SubsRoClient client)
    {
        _logger = logger;
        _client = client;
    }

    /// <inheritdoc />
    public string Name => "subs.ro";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
        SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentType != VideoContentType.Movie
            || string.IsNullOrWhiteSpace(request.Name)
            || !IsRomanian(request))
        {
            return Array.Empty<RemoteSubtitleInfo>();
        }

        var html = await _client.SearchAsync(request.Name, request.ProductionYear, cancellationToken);
        var parsed = SearchResultParser.Parse(html);

        _logger.LogInformation(
            "subs.ro returned {Count} results for {Title} ({Year})",
            parsed.Count,
            request.Name,
            request.ProductionYear);

        return parsed.Select(r => new RemoteSubtitleInfo
        {
            Id = string.Format(CultureInfo.InvariantCulture, "{0}|{1}", r.Id, r.Slug),
            ProviderName = Name,
            Name = string.IsNullOrEmpty(r.Title) ? request.Name : r.Title,
            Format = "srt",
            ThreeLetterISOLanguageName = "ron",
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        var separator = id.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new ArgumentException($"Invalid subtitle id: {id}", nameof(id));
        }

        var subtitleId = id[..separator];
        var slug = id[(separator + 1)..];

        var archive = await _client.DownloadAsync(slug, subtitleId, cancellationToken);
        var srtBytes = ArchiveExtractor.ExtractLargestSrt(new MemoryStream(archive));
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

    private static bool IsRomanian(SubtitleSearchRequest request)
        => string.Equals(request.TwoLetterISOLanguageName, "ro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Language, "ron", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Language, "rum", StringComparison.OrdinalIgnoreCase);
}
