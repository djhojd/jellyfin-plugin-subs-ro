using Jellyfin.Plugin.SubsRo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo.Tests;

/// <summary>
/// A <see cref="SubsRoDownloader"/> with a fixed API key, so tests can exercise the
/// API-backed search/download path without a live <see cref="Plugin.Instance"/>.
/// </summary>
public sealed class TestableSubsRoDownloader : SubsRoDownloader
{
    private readonly string? _apiKey;

    public TestableSubsRoDownloader(
        ILogger<SubsRoDownloader> logger, SubsRoClient client, SubsRoApiClient apiClient, string? apiKey)
        : base(logger, client, apiClient)
    {
        _apiKey = apiKey;
    }

    protected override string? GetApiKey() => _apiKey;
}
