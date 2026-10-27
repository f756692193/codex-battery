param()
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $projectDir
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION 必须是三段式版本号，如 1.0.0。' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x 编译器。' }
$dist = Join-Path $projectDir 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$output = Join-Path $dist 'CodexQuotaManager.exe'
$obj = Join-Path $projectDir 'obj'
New-Item -ItemType Directory -Path $obj -Force | Out-Null
$versionSource = Join-Path $obj 'VersionInfo.cs'
$versionCode = @"
using System.Reflection;
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
"@
[System.IO.File]::WriteAllText($versionSource, $versionCode, (New-Object System.Text.UTF8Encoding($false)))
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $projectDir 'src\app.ico'
$images = @()
foreach ($size in @(16, 32, 48)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $green = [System.Drawing.Color]::FromArgb(52, 190, 139)
    $pen = New-Object System.Drawing.Pen($green, [single]($size / 16))
    $brush = New-Object System.Drawing.SolidBrush($green)
    $graphics.DrawRectangle($pen, [single]($size * .08), [single]($size * .25), [single]($size * .76), [single]($size * .5))
    $graphics.FillRectangle($brush, [single]($size * .85), [single]($size * .4), [single]($size * .07), [single]($size * .2))
    $graphics.FillRectangle($brush, [single]($size * .18), [single]($size * .35), [single]($size * .48), [single]($size * .3))
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    $stream.Dispose(); $graphics.Dispose(); $pen.Dispose(); $brush.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]3)
    $offset = 6 + 16 * 3
    for ($i = 0; $i -lt 3; $i++) {
        $size = @(16, 32, 48)[$i]
        $writer.Write([byte]$size); $writer.Write([byte]$size); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ("/out:" + $output) ("/win32icon:" + $iconPath) ("/win32manifest:" + (Join-Path $projectDir 'src\app.manifest')) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Net.Http.dll (Join-Path $projectDir 'src\Core.cs') (Join-Path $projectDir 'src\News.cs') (Join-Path $projectDir 'src\App.cs') (Join-Path $projectDir 'src\AssemblyInfo.cs') $versionSource
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
$readme = [System.IO.File]::ReadAllText((Join-Path $projectDir 'README.txt')).Replace('{{VERSION}}', $version)
[System.IO.File]::WriteAllText((Join-Path $dist 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding($false)))
Write-Output $output
