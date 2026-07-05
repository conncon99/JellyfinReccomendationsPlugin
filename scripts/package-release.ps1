param(
    [string]$Version = "0.1.0.11.4",
    [string]$JellyfinVersion = "10.11.4",
    [string]$TargetAbi = "10.11.4.0",
    [string]$RepositoryOwner = "conncon99",
    [string]$RepositoryName = "JellyfinReccomendationsPlugin",
    [string]$ReleaseTag = "v0.1.0",
    [string]$Changelog = "Initial Trakt and Seerr recommendation test build"
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\Jellyfin.Plugin.JellyRec\JellyRec.csproj"
$releaseDir = Join-Path $root "release"
$manifestPath = Join-Path $root "manifest.json"

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

dotnet build $project --configuration Release -p:JellyfinVersion=$JellyfinVersion
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$outDir = Join-Path $root "src\Jellyfin.Plugin.JellyRec\bin\Release\net9.0"
$dllPath = Join-Path $outDir "JellyRec.dll"
if (-not (Test-Path $dllPath)) {
    throw "Expected DLL not found: $dllPath"
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$meta = @{
    guid = "1d1af7ee-7c42-42a0-9f03-d202274e68d5"
    name = "Jellyfin Recommendations"
    description = "Cross-client Jellyfin recommendations powered by Trakt and Seerr request handling."
    owner = $RepositoryOwner
    category = "Metadata"
    version = $Version
    changelog = $Changelog
    targetAbi = $TargetAbi
    timestamp = $timestamp
    status = "Active"
    autoUpdate = $true
    assemblies = @("JellyRec.dll")
} | ConvertTo-Json -Compress

$metaPath = Join-Path $outDir "meta.json"
Set-Content -Path $metaPath -Value $meta -NoNewline

$zipName = "JellyRec-$Version.zip"
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $dllPath, $metaPath -DestinationPath $zipPath -Force
$checksum = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash
$sourceUrl = "https://github.com/$RepositoryOwner/$RepositoryName/releases/download/$ReleaseTag/$zipName"
$repoUrl = "https://github.com/$RepositoryOwner/$RepositoryName"

$manifestJson = Get-Content $manifestPath -Raw
$manifest = $manifestJson | ConvertFrom-Json
$plugin = if ($manifest -is [array]) { $manifest[0] } else { $manifest }
$plugin.owner = $RepositoryOwner
$plugin.repositoryUrl = $repoUrl
$entry = [ordered]@{
    version = $Version
    changelog = $Changelog
    targetAbi = $TargetAbi
    sourceUrl = $sourceUrl
    checksum = $checksum
    timestamp = $timestamp
    dependencies = @()
}
$existingVersions = @($plugin.versions) | Where-Object { $_.version -ne $Version }
$plugin.versions = @($entry) + $existingVersions
$manifestOutput = "[`n" + ($plugin | ConvertTo-Json -Depth 10) + "`n]"
Set-Content $manifestPath -Value $manifestOutput -NoNewline

Write-Host "Created $zipPath"
Write-Host "Checksum $checksum"
Write-Host "Upload the ZIP to $sourceUrl"
Write-Host "Jellyfin repository URL: https://raw.githubusercontent.com/$RepositoryOwner/$RepositoryName/main/manifest.json"
