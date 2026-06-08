# Jellyfin subs.ro Subtitle Provider

A [Jellyfin](https://jellyfin.org) subtitle provider plugin that downloads Romanian
subtitles from [subs.ro](https://subs.ro) for movies that are missing them.

The plugin registers as a standard Jellyfin subtitle provider, so Jellyfin's own
"download missing subtitles" logic decides *when* to call it — you simply add
**Romanian** as a subtitle download language on your movie library.

## Features

- Searches subs.ro by **title + year** (movies only).
- Downloads the subtitle archive (`.zip`/`.rar`), extracts the largest `.srt`, and
  re-encodes it to **UTF-8** so Romanian diacritics (ă â î ș ț) render correctly on all
  clients.
- Works **anonymously** — no subs.ro account required.

## Requirements

- Jellyfin server **10.11.x** (.NET 9). For the 10.10 line you would need to retarget to
  `net8.0` / `Jellyfin.Controller` 10.10.x.

## Build

The plugin builds with the .NET 9 SDK. Packaging uses
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

1. Run `pwsh ./build.ps1` and locate `artifacts/subs-ro_<version>.zip`.
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

## How matching works

- Matching is by **title + year** only. subs.ro's IMDB/TMDB id search is non-functional,
  so it is not used.
- Language is fixed to **Romanian**; the provider returns no results for other languages.
- When a subtitle archive contains multiple `.srt` files (multi-CD or several release
  variants), the **largest** `.srt` is returned.

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

## License

GPL-3.0
