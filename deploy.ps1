# Build Real Stars and deploy it to the game's user-writable mods folder (dev loop).
# No elevation needed: everything lives under the active instance's mods folder.
# Installs exactly what package.ps1 ships (release-files.txt) and removes stale files left by
# earlier deploys, so the local install always matches what players get.
$ErrorActionPreference = "Stop"

$proj = $PSScriptRoot
dotnet build "$proj\RealStars.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }

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
$gameDir = Get-KsaDataDir
Write-Host "game data dir: $gameDir"
$dest = Join-Path $gameDir "mods\RealStars"
$destAssets = Join-Path $dest "assets"
New-Item -ItemType Directory -Force $destAssets | Out-Null

$assets = Get-Content "$proj\release-files.txt" |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith("#") }

Copy-Item "$proj\bin\Release\RealStars.dll" $dest -Force
Copy-Item "$proj\mod.toml" $dest -Force
Copy-Item "$proj\README.md" $dest -Force
Copy-Item "$proj\LICENSE" $dest -Force
Copy-Item "$proj\CREDITS.md" $dest -Force
foreach ($name in $assets) {
    $src = Join-Path "$proj\assets" $name
    if (-not (Test-Path $src)) { throw "release asset missing: assets\$name" }
    Copy-Item $src $destAssets -Force
}

# Prune assets that are no longer shipped. ShadowContent\ is regenerated on every launch and is left alone.
Get-ChildItem $destAssets -File | Where-Object { $assets -notcontains $_.Name } | ForEach-Object {
    Remove-Item $_.FullName -Force
    Write-Host "removed stale assets\$($_.Name)"
}

# The game auto-adds new mod folders to its local manifest DISABLED; for the dev loop, register it
# enabled if it isn't listed yet (an existing entry, enabled or not, is left as the user set it).
$manifest = Join-Path $gameDir "manifest.toml"
if (-not (Test-Path $manifest)) { throw "local manifest not found: $manifest (run the game once first)" }
if ((Get-Content $manifest -Raw) -notmatch 'id = "RealStars"') {
    Add-Content $manifest "`n[[mods]]`nid = `"RealStars`"`nenabled = true" -Encoding utf8
    Write-Host "added RealStars to $manifest"
}

Write-Host "deployed -> $dest"
