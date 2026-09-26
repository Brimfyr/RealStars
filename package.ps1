# Build Real Stars and produce an installable zip for players.
#
#   .\package.ps1                         # version 1.1.0, game build read from your settings.toml
#   .\package.ps1 -Version 1.1.0 -GameBuild v2026.9.22.5482
#
# Output: dist\RealStars-v<Version>-ksa<GameBuild>.zip (+ .sha256). The zip's root is a
# RealStars\ folder, because KSA uses the folder name as the mod id. Ships exactly the files in
# release-files.txt; StarMap.API/0Harmony are NOT included (the StarMap launcher provides them).
param(
    [string]$Version = "1.1.0",
    [string]$GameBuild = ""
)
$ErrorActionPreference = "Stop"

# The game's user-data folder (mods\, manifest.toml, settings.toml). Borea gives each instance its own,
# so prefer its active instance and fall back to the stock Documents location when Borea isn't installed.
function Get-KsaDataDir {
    $borea = Join-Path $env:LOCALAPPDATA "Borea"
    $active = Join-Path $borea "active-instance.toml"
    if (Test-Path $active) {
        $m = Select-String -Path $active -Pattern 'ActiveInstanceId\s*=\s*"([^"]+)"' | Select-Object -First 1
        if ($m) {
            $dir = Join-Path $borea (Join-Path "Instances" $m.Matches[0].Groups[1].Value)
            if (Test-Path $dir) { return $dir }
        }
    }
    return Join-Path ([Environment]::GetFolderPath("MyDocuments")) "My Games\Kitten Space Agency"
}
$proj = $PSScriptRoot

# The KSA build this release was tested on goes in the file name; default to the build you last ran.
if (-not $GameBuild) {
    $settings = Join-Path (Get-KsaDataDir) "settings.toml"
    if (Test-Path $settings) {
        $match = Select-String -Path $settings -Pattern '^lastVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
        if ($match) { $GameBuild = $match.Matches[0].Groups[1].Value }
    }
    if (-not $GameBuild) { throw "could not read lastVersion from settings.toml; pass -GameBuild" }
}

dotnet build "$proj\RealStars.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$assets = Get-Content "$proj\release-files.txt" |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith("#") }

$dist = Join-Path $proj "dist"
$stage = Join-Path $dist "RealStars"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "assets") | Out-Null

Copy-Item "$proj\bin\Release\RealStars.dll" $stage
Copy-Item "$proj\mod.toml" $stage
Copy-Item "$proj\README.md" $stage
Copy-Item "$proj\LICENSE" $stage
# The catalogue's licence and acknowledgements travel with it.
Copy-Item "$proj\CREDITS.md" $stage
foreach ($name in $assets) {
    $src = Join-Path "$proj\assets" $name
    if (-not (Test-Path $src)) { throw "release asset missing: assets\$name" }
    Copy-Item $src (Join-Path $stage "assets")
}

$zip = Join-Path $dist ("RealStars-v{0}-ksa{1}.zip" -f $Version, $GameBuild.TrimStart("v"))
if (Test-Path $zip) { Remove-Item $zip -Force }
# ZipFile rather than Compress-Archive: Windows PowerShell 5.1's Compress-Archive writes backslash entry
# names, which break extraction on Linux (KSA ships a Linux build).
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -Path "$zip.sha256" -Value ("{0}  {1}" -f $hash, (Split-Path $zip -Leaf)) -Encoding ascii

$sizeMb = (Get-Item $zip).Length / 1MB
Write-Host ("packaged {0} ({1:N1} MB, {2} assets) -> {3}" -f $Version, $sizeMb, $assets.Count, $zip)
Write-Host "sha256 $hash"
