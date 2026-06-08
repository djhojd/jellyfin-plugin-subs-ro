#requires -Version 7
<#
.SYNOPSIS
    Builds and packages the subs.ro Jellyfin plugin into an installable .zip.
.DESCRIPTION
    Uses jprm (run via uvx) to build the plugin in Release and produce a versioned
    plugin archive containing the plugin DLL, its dependencies, and a generated meta.json.
    Falls back to a manual dotnet publish + zip if jprm is unavailable.
#>
[CmdletBinding()]
param(
    [string]$Output = "artifacts"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$outDir = Join-Path $root $Output
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Invoke-JprmBuild {
    Write-Host "Packaging with jprm (via uvx)..."
    uvx --from jprm jprm plugin build $root --output $outDir
    return $LASTEXITCODE -eq 0
}

function Invoke-ManualPackage {
    Write-Host "jprm unavailable; falling back to manual publish + zip..."
    $publish = Join-Path $root "publish"
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

    dotnet publish (Join-Path $root "Jellyfin.Plugin.SubsRo/Jellyfin.Plugin.SubsRo.csproj") `
        -c Release -o $publish

    # Ship the plugin DLL and third-party dependencies, but never the Jellyfin.* assemblies
    # (the server provides those at runtime).
    $dlls = Get-ChildItem $publish -Filter *.dll |
        Where-Object { $_.Name -notlike "Jellyfin.*" -and $_.Name -notlike "MediaBrowser.*" }

    $zip = Join-Path $outDir "subs.ro_1.0.0.0.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path $dlls.FullName -DestinationPath $zip
    Write-Host "Created $zip (manual package; meta.json NOT generated - add manually if installing)."
}

Write-Host "Building plugin (Release)..."
dotnet build (Join-Path $root "Jellyfin.Plugin.SubsRo/Jellyfin.Plugin.SubsRo.csproj") -c Release

$jprmOk = $false
try { $jprmOk = Invoke-JprmBuild } catch { $jprmOk = $false }
if (-not $jprmOk) { Invoke-ManualPackage }

Write-Host "Done. Artifact(s) in ${outDir}:"
Get-ChildItem $outDir -Filter *.zip | Select-Object Name, Length
