# Jellyfin subs.ro Subtitle Provider

A [Jellyfin](https://jellyfin.org) subtitle provider plugin that downloads Romanian
subtitles from [subs.ro](https://subs.ro) for movies and TV episodes that are missing them.

The plugin registers as a standard Jellyfin subtitle provider, so Jellyfin's own
"download missing subtitles" logic decides *when* to call it — you simply add
**Romanian** as a subtitle download language on your movie library.

## Features

- Works for **movies and TV episodes**.
- Searches subs.ro by **IMDb/TMDb id** when Jellyfin knows one, falling back to
  **title + year** (movies) or **series name** (episodes).
- For episodes, rejects results for the wrong season outright (subs.ro tags TV results
  per-season, in free text only) and requires the archive to actually contain the
  requested episode rather than guessing.
- Ranks multiple results by how well their release notes match the video's own file name
  (resolution, source, codec, release group, ...), not just by download count.
- Downloads the subtitle archive (`.zip`/`.rar`), extracts the best-matching `.srt` (same
  release-matching logic, falling back to the largest `.srt` when nothing matches or there's
  only one), and re-encodes it to **UTF-8** so Romanian diacritics (ă â î ș ț) render
  correctly on all clients.
- Works **anonymously** by default — no subs.ro account required. Optionally, set a subs.ro
  **API key** in the plugin's configuration page to search/download via subs.ro's official
  API instead of the public site; the anonymous flow is still used as a fallback if the API
  call fails or returns nothing.

## Requirements

- Jellyfin server **12.4** or newer (.NET 10). For 10.11.x you would need to retarget to
  `net9.0` / `Jellyfin.Controller` 10.11.x.

## Build

The plugin builds with the .NET 10 SDK. Packaging uses
[`jprm`](https://github.com/oddstr13/jellyfin-plugin-repository-manager), run via
[`uvx`](https://docs.astral.sh/uv/) so nothing is installed globally.

```powershell
pwsh ./build.ps1
```

This produces `artifacts/subs-ro_<version>.zip` containing the plugin DLL, its
dependencies (`AngleSharp`, `SharpCompress`), and a generated `meta.json`. If `jprm` is
unavailable the script falls back to a manual `dotnet publish` + zip (without a
`meta.json`).

## Install

### From the plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories** (or **Catalog → ⚙**) and add a
   repository with this URL:
   `https://github.com/djhojd/jellyfin-plugin-subs-ro/releases/latest/download/manifest.json`
2. Open the **Catalog**, find **subs.ro** under *Subtitles*, and install it.
3. Restart the Jellyfin server. Updates then show up in the catalog like any other plugin.

### Manually

1. Download `subs-ro_<version>.zip` from the
   [latest release](https://github.com/djhojd/jellyfin-plugin-subs-ro/releases/latest), or
   run `pwsh ./build.ps1` and locate `artifacts/subs-ro_<version>.zip`.
2. Create a folder for the plugin inside your Jellyfin `plugins` directory, e.g.
   `plugins/subs.ro_1.0.0.0/`, and extract the zip's contents into it.
   - Linux: `/var/lib/jellyfin/plugins/`
   - Windows (service): `%ProgramData%\Jellyfin\Server\plugins\`
   - Windows (tray/standalone): `%LocalAppData%\jellyfin\plugins\`
3. Restart the Jellyfin server. The plugin appears under **Dashboard → Plugins**.

## Enable

1. Go to **Dashboard → Libraries**, edit your movie library.
2. Under **Subtitle Downloads**, add **Romanian** to the download languages and enable
   subtitle downloading.
3. New movies fetch subtitles on import. For existing movies, run
   **Dashboard → Scheduled Tasks → Download missing subtitles** (or use the per-item
   subtitle search, where **subs.ro** appears as a source).
4. *(Optional)* Go to **Dashboard → Plugins → subs.ro** and paste a subs.ro API key to use
   the official API instead of the anonymous public site. Generate a key from your subs.ro
   account. Leave blank to keep working anonymously.

## How matching works

- Matching prefers the **IMDb id**, then **TMDb id** (both via subs.ro), falling back to
  **title + year** (movies) or **series name** (episodes) if neither id gets a match.
- Language is fixed to **Romanian**; the provider returns no results for other languages.
- When there are multiple results, or a result's archive contains multiple `.srt` files
  (multi-CD or several release variants), the entry whose release name best matches the
  video's own file name wins; ties (or no match at all) fall back to download count, then
  file size.
- For episodes, results are additionally required to actually be the searched series (not
  just a title that happens to contain its name) and, when a season can be determined from
  a result's title/note, to be for the requested season. Within a season archive, the
  entry must carry the requested episode's own `SxxExx` marker — a partial-season upload
  that doesn't include the requested episode fails with a clear error instead of silently
  downloading the wrong one.

## Tests

```powershell
# Unit tests (no network) — these run in CI
dotnet test --filter "Category!=Live"

# Live integration test — hits real subs.ro (search + download)
dotnet test --filter "Category=Live"
```

The live test is intentionally excluded from the default unit run. If it fails after
passing before, subs.ro likely changed its markup or endpoints — update the fixtures in
the unit tests first, then the production code, then re-verify.

## Releasing

1. Set the new version (four parts, e.g. `1.1.0.0`) in both `Directory.Build.props` and
   `build.yaml`, and replace `changelog` in `build.yaml` with this release's changes.
2. Merge that to `main`, then tag and push: `git tag v1.1.0.0 && git push origin v1.1.0.0`.
3. The release workflow checks the tag matches both versions, runs the unit tests, builds
   the zip, and publishes a GitHub release with the zip and an updated `manifest.json` that
   still lists earlier versions.

## License

GPL-3.0
