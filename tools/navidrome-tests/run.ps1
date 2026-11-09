param([string]$CompilerPath = $env:SUBSONIC8_CSC)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Supply -CompilerPath or SUBSONIC8_CSC.' }
$outputDirectory = Join-Path $projectRoot '.tmp\navidrome-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse |
    Sort-Object FullName | Select-Object -First 1
if (-not $caliburn) { throw 'Restore Caliburn.Micro.Core first.' }
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @(
    'Client.Common\Models\Navidrome\NavidromeModels.cs',
    'Client.Common\Results\NavidromeResult.cs',
    'Client.Common\Services\Navidrome\NavidromeApi.cs',
    'Client.Common\Services\Navidrome\SubsonicXml.cs',
    'Client.Common\Services\Navidrome\LocalXml.cs',
    'Client.Common\Models\ListeningCounter.cs',
    'Client.Common\Models\PendingListening.cs',
    'Client.Common\Models\Navidrome\OfflineCatalog.cs',
    'Client.Common\Services\Navidrome\MediaDownload.cs',
    'tools\serializer-tests\VisualStateResultStub.cs',
    'tools\navidrome-tests\NavidromeTests.cs'
)
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'navidrome-tests.exe'
& $CompilerPath /nologo /main:NavidromeTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Navidrome production-source compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Navidrome protocol regressions failed.' }
