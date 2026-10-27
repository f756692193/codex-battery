$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $PSScriptRoot 'CoreTests.exe'
& $compiler /nologo /target:exe /utf8output ("/out:" + $testExe) /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $projectDir 'src\Core.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& $testExe (Join-Path $PSScriptRoot 'results')
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
$newsExe = Join-Path $PSScriptRoot 'NewsTests.exe'
& $compiler /nologo /target:exe /utf8output ("/out:" + $newsExe) /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Net.Http.dll (Join-Path $projectDir 'src\Core.cs') (Join-Path $projectDir 'src\News.cs') (Join-Path $PSScriptRoot 'NewsTests.cs')
if ($LASTEXITCODE -ne 0) { throw '消息测试编译失败' }
& $newsExe (Join-Path $PSScriptRoot 'fixtures\tibo-synthetic.xml')
if ($LASTEXITCODE -ne 0) { throw '消息测试失败' }
$uiExe = Join-Path $PSScriptRoot 'UiActionTests.exe'
& $compiler /nologo /target:exe /main:UiActionTests /utf8output ("/out:" + $uiExe) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Net.Http.dll (Join-Path $projectDir 'src\Core.cs') (Join-Path $projectDir 'src\News.cs') (Join-Path $projectDir 'src\App.cs') (Join-Path $PSScriptRoot 'UiActionTests.cs')
if ($LASTEXITCODE -ne 0) { throw '界面回归测试编译失败' }
& $uiExe (Join-Path $PSScriptRoot 'results-ui')
if ($LASTEXITCODE -ne 0) { throw '界面回归测试失败' }
