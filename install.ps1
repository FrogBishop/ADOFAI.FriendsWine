param([string]$GameDirectory = $env:ADOFAI_GAME_DIR)
$ErrorActionPreference = 'Stop'
if (-not $GameDirectory) {
    $GameDirectory = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\A Dance of Fire and Ice'
}
$package = Join-Path $PSScriptRoot 'dist\FriendsWine'
if (-not (Test-Path -LiteralPath (Join-Path $package 'FriendsWine.dll'))) {
    throw 'Run build.ps1 first.'
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory 'A Dance of Fire and Ice.exe'))) {
    throw 'Not an ADOFAI game directory.'
}
if (Get-Process -Name 'A Dance of Fire and Ice' -ErrorAction SilentlyContinue) {
    throw 'Close the game before replacing the mod DLL.'
}
$destination = Join-Path $GameDirectory 'Mods\FriendsWine'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $package) {
    if ($item.Name -eq 'settings.json' -and (Test-Path -LiteralPath (Join-Path $destination 'settings.json'))) {
        continue
    }
    Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
}
Write-Output "Installed: $destination"
Write-Output 'Existing settings.json was preserved. Launch the game and press F8.'
