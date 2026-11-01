param(
    [string]$GameDirectory = $env:ADOFAI_GAME_DIR,
    [switch]$SkipTests,
    [switch]$IntegrationTest
)
$ErrorActionPreference = 'Stop'
if (-not $GameDirectory) {
    $GameDirectory = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\A Dance of Fire and Ice'
}
$managed = Join-Path $GameDirectory 'A Dance of Fire and Ice_Data\Managed'
$manager = Join-Path $managed 'UnityModManager'
if (-not (Test-Path -LiteralPath (Join-Path $manager 'UnityModManager.dll'))) {
    throw 'Game or UnityModManager not found. Pass -GameDirectory or set ADOFAI_GAME_DIR.'
}
$sdkLine = @(& dotnet --list-sdks) | Select-Object -Last 1
if ($LASTEXITCODE -ne 0 -or $sdkLine -notmatch '^([^ ]+) \[(.+)\]$') {
    throw 'Install .NET SDK 10 or newer to build and verify this project.'
}
$compiler = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
$buildDirectory = Join-Path $PSScriptRoot 'build'
$packageDirectory = Join-Path $PSScriptRoot 'dist\FriendsWine'
New-Item -ItemType Directory -Path $buildDirectory,$packageDirectory,(Join-Path $packageDirectory 'Audio') -Force | Out-Null
$arguments = @(
    '/nostdlib+', '/target:library', '/langversion:latest',
    '/optimize+', '/deterministic+', '/debug:portable', '/warn:4',
    ('/out:"' + (Join-Path $packageDirectory 'FriendsWine.dll') + '"'),
    ('/pathmap:"' + $PSScriptRoot + '=/_/FriendsWine"')
)
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','netstandard.dll','Newtonsoft.Json.dll','Unity.TextMeshPro.dll','DOTween.dll') |
    ForEach-Object { Join-Path $managed $_ }
$references += @(Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object { $_.FullName })
$references += @(Join-Path $manager 'UnityModManager.dll'; Join-Path $manager '0Harmony.dll')
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference" }
    $arguments += '/reference:"' + $reference + '"'
}
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -Filter '*.cs' |
    Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' })
if ($IntegrationTest) {
    $arguments += '/define:FRIENDSWINE_SELFTEST'
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'verification') -Filter '*.cs' |
        Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' })
}
$responseFile = Join-Path $buildDirectory 'compile.rsp'
[IO.File]::WriteAllLines($responseFile, $arguments, [Text.UTF8Encoding]::new($false))
& dotnet $compiler /noconfig ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Info.json') -Destination $packageDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config.default.json') -Destination (Join-Path $packageDirectory 'settings.json') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $packageDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERIFICATION.md') -Destination $packageDirectory -Force
$music = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.mp3' -File)
if ($music.Count -ne 1) { throw 'Expected exactly one source MP3 in the project root.' }
Copy-Item -LiteralPath $music[0].FullName -Destination (Join-Path $packageDirectory 'Audio\bgm.mp3') -Force
if (-not $SkipTests) {
    & dotnet run --project (Join-Path $PSScriptRoot 'tests\FriendsWine.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Core verification failed.' }
}
$modInfo = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Info.json') -Raw | ConvertFrom-Json
$archive = Join-Path $PSScriptRoot ('dist\FriendsWine-' + $modInfo.Version + '.zip')
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archive -Force
Write-Output "Built: $packageDirectory"
Write-Output "UMM archive: $archive"
