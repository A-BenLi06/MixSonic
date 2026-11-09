param([string]$FixtureState, [string]$CompilerPath = $env:SUBSONIC8_CSC)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $FixtureState) { $FixtureState = Join-Path $projectRoot '.tmp\navidrome-fixture\current.json' }
$FixtureState = (Resolve-Path -LiteralPath $FixtureState).Path
$state = Get-Content -LiteralPath $FixtureState -Raw | ConvertFrom-Json
$fixtureRoot = Join-Path $projectRoot '.tmp\navidrome-fixture'
if (-not $state.root.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Service mutations require the disposable project fixture.' }
$process = Get-Process -Id $state.processId -ErrorAction Stop
if ($process.ProcessName -ne 'navidrome') { throw 'Fixture process identity changed.' }
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
$outputDirectory = Join-Path $projectRoot '.tmp\navidrome-service-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$dataDirectory = Join-Path $outputDirectory ('run-' + [guid]::NewGuid().ToString('N'))
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse | Sort-Object FullName | Select-Object -First 1
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @(
    'Client.Common\Models\Navidrome\NavidromeModels.cs', 'Client.Common\Models\Navidrome\OfflineCatalog.cs',
    'Client.Common\Models\ListeningCounter.cs', 'Client.Common\Models\PendingListening.cs',
    'Client.Common\Results\NavidromeResult.cs', 'Client.Common\Services\Navidrome\NavidromeApi.cs',
    'Client.Common\Services\Navidrome\SubsonicXml.cs', 'Client.Common\Services\Navidrome\LocalXml.cs', 'Client.Common\Services\Navidrome\MediaDownload.cs',
    'Client\Navidrome\NavidromeSessionService.cs', 'Client\Navidrome\OfflineDownloadItem.cs', 'Client\Navidrome\OfflineLibraryService.cs',
    'Client\Navidrome\PlaybackActivityService.cs', 'Client\Navidrome\NavidromeItem.cs', 'Client\Navidrome\NavidromeText.cs',
    'tools\serializer-tests\VisualStateResultStub.cs', 'tools\navidrome-tests\StorageAdapters.cs', 'tools\navidrome-tests\ServiceTests.cs'
)
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'navidrome-services.exe'
& $CompilerPath /nologo /main:NavidromeServiceTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Production-service compilation failed.' }
& $executable $FixtureState $dataDirectory
if ($LASTEXITCODE -ne 0) { throw 'Production-service assertions failed.' }
