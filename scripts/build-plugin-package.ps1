param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$rootPath = Split-Path -Parent $PSScriptRoot
$webUiPath = Join-Path $rootPath "webui"
$projectPath = Join-Path $rootPath "Jellyfin.Plugin.SubtitlesTools\Jellyfin.Plugin.SubtitlesTools.csproj"
$artifactRoot = Join-Path $rootPath "artifacts"
$packageRoot = Join-Path $artifactRoot "package"

[xml]$projectFile = Get-Content -Path $projectPath -Encoding utf8
$version = $projectFile.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version was not found in the plugin csproj."
}

Push-Location $webUiPath
try {
    npm ci
    npm run lint
    npm run typecheck
    npm run test -- --run
    npm run build
}
finally {
    Pop-Location
}

dotnet build $projectPath -c $Configuration

if (Test-Path $packageRoot) {
    Remove-Item -Path $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$buildOutputRoot = Join-Path $rootPath "Jellyfin.Plugin.SubtitlesTools\bin\$Configuration\net9.0"
$packageFiles = @(
    "Jellyfin.Plugin.SubtitlesTools.deps.json",
    "Jellyfin.Plugin.SubtitlesTools.dll",
    "Jellyfin.Plugin.SubtitlesTools.pdb",
    "Jellyfin.Plugin.SubtitlesTools.xml"
)

foreach ($packageFile in $packageFiles) {
    Copy-Item -Path (Join-Path $buildOutputRoot $packageFile) -Destination $packageRoot
}

$zipPath = Join-Path $artifactRoot "Jellyfin.Plugin.SubtitlesTools_$version.zip"
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath
$hash = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToUpperInvariant()

Write-Host "Local package created: $zipPath"
Write-Host "MD5: $hash"
