param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION 必须是三段式版本号。' }
& (Join-Path $repoRoot 'windows/tests/run.ps1')
& (Join-Path $repoRoot 'windows/build.ps1')
$releaseDir = Join-Path $repoRoot 'windows/dist'
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/VALIDATION.md') -Destination (Join-Path $releaseDir 'VALIDATION.md') -Force
$hash = (Get-FileHash -LiteralPath (Join-Path $releaseDir 'CodexQuotaManager.exe') -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256.txt') -Value ($hash + '  CodexQuotaManager.exe') -Encoding UTF8
$artifactDir = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
$archive = Join-Path $artifactDir ('CodexBattery-windows-' + $version + '.zip')
Compress-Archive -LiteralPath @((Join-Path $releaseDir 'CodexQuotaManager.exe'), (Join-Path $releaseDir 'README.txt'), (Join-Path $releaseDir 'VALIDATION.md'), (Join-Path $releaseDir 'SHA256.txt')) -DestinationPath $archive -Force
foreach ($oldPackage in Get-ChildItem -LiteralPath $artifactDir -Filter 'CodexBattery-windows-*.zip' -File) {
    if ($oldPackage.FullName -ne $archive) { Remove-Item -LiteralPath $oldPackage.FullName -Force }
}
Write-Output $archive
